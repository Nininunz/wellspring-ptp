// SPDX-License-Identifier: GPL-2.0-only
// Wire structures follow WellspringPTP Public.h, live frame version 3.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
class PressureBridge {
 internal class Latch {
  readonly int threshold,release; bool armed; int above,below; long last=-1000;
  internal Latch(int t) { threshold=t;release=t*7/10; }
  internal void Reset() { armed=false;above=below=0; }
  internal bool Sample(int pressure,bool valid,bool button,long ms) {
   if(!valid || pressure<=release) { above=0;if(++below>=2) armed=true; }
   else { below=0;if(button && pressure>=threshold) above++;else above=0; }
   if(valid && armed && above>=2 && ms-last>=250) { armed=false;above=0;last=ms;return true; }
   return false;
  }
 }
 internal static int SelfTest() {
  Latch l=new Latch(265);int t=0;
  if(l.Sample(300,true,true,t+=8) || l.Sample(300,true,true,t+=8)) throw new Exception("Must observe release first");
  l.Sample(0,false,false,t+=8);l.Sample(0,false,false,t+=8);
  if(l.Sample(300,true,true,t+=8) || !l.Sample(300,true,true,t+=8)) throw new Exception("Threshold debounce failed");
  for(int i=0;i<100;i++) if(l.Sample(300,true,true,t+=8)) throw new Exception("Repeated while held");
  l.Sample(100,true,true,t+=8);l.Sample(100,true,true,t+=8);
  if(l.Sample(300,true,true,t+=8) || !l.Sample(300,true,true,t+=8)) throw new Exception("Rearm failed");
  l.Reset();for(int i=0;i<5;i++) if(l.Sample(300,true,true,t+=8)) throw new Exception("Stale reset failed");
  l.Sample(0,false,false,t+=8);l.Sample(0,false,false,t+=8);
  for(int i=0;i<100;i++) if(l.Sample(200,true,true,t+=8)) throw new Exception("Below-threshold trigger");
  for(int i=0;i<10;i++) if(l.Sample(300,true,false,t+=8)) throw new Exception("Trigger without physical click");
  Console.WriteLine("PASS: threshold, debounce, held suppression, hysteresis rearm, stale reset, button gate.");return 0;
 }
 [StructLayout(LayoutKind.Sequential)] struct Contact {
  public uint Id; public ushort X,Y; public uint Phase; public byte Confident,Palm;
  public ushort Reserved; public short RawX,RawY; public ushort Major,Minor,Pressure,Orientation;
 }
 [StructLayout(LayoutKind.Sequential)] struct Frame {
  public uint Version,Sequence; public long Timestamp;
  public byte Count,RawCount,Blanked,Button,ForceClick,ClickReport; public ushort Reserved;
  [MarshalAs(UnmanagedType.ByValArray,SizeConst=5)] public Contact[] Contacts;
 }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string p,uint a,uint s,IntPtr sec,uint disp,uint flags,IntPtr t);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle h,uint code,IntPtr input,uint inputSize,IntPtr output,uint outputSize,out uint n,IntPtr ov);
 static bool SetLive(SafeFileHandle h,IntPtr input,bool enabled) {
  Marshal.WriteInt32(input,enabled?1:0); uint n;
  return DeviceIoControl(h,0x22a410,input,4,IntPtr.Zero,0,out n,IntPtr.Zero);
 }
 public static int Run(SafeFileHandle actuator,bool monitor) {
  HelperControl.Load();
  string folder=HelperControl.Folder;
  string stop=Path.Combine(folder,"pressure-stop.txt");
  int threshold=HelperControl.Threshold,release=185;
  string config=Path.Combine(folder,"pressure-threshold.txt");
  if(File.Exists(config) && (!int.TryParse(File.ReadAllText(config).Trim(),out threshold) || threshold<50 || threshold>1000)) throw new Exception("Invalid threshold (50..1000).");
  release=threshold*7/10;
  using(Mutex mutex=new Mutex(false,"Local\\T2PressureHapticBridge")) {
   if(!mutex.WaitOne(0)) { Console.WriteLine("A pressure bridge is already running."); return 10; }
   try { using(SafeFileHandle feed=CreateFile(@"\\.\AmtPtpDeviceUsbKm",0xc0000000,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
    if(feed.IsInvalid) { Console.WriteLine("Wellspring pressure feed unavailable: "+Marshal.GetLastWin32Error()); return 11; }
    IntPtr input=Marshal.AllocHGlobal(4), data=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Frame)));
    bool live=false; int peak=0,clicks=0,frames=0; uint seq=0;
    Latch latch=new Latch(threshold);bool previousEnabled=HelperControl.Enabled!=0;
    var serverCancel=new CancellationTokenSource();Task server=null;
    DateTime began=DateTime.UtcNow,lastFrame=began,lastStatus=began;
    System.Diagnostics.Stopwatch timer=System.Diagnostics.Stopwatch.StartNew();
    try {
     live=SetLive(feed,input,true); if(!live) throw new Exception("Cannot enable pressure feed.");
     server=HelperControl.Serve(serverCancel.Token);
     Console.WriteLine("Pressure bridge started; monitor="+monitor+" threshold="+threshold+" release="+release);
     Console.Out.Flush();
     while(!HelperControl.Stop && !File.Exists(stop) && (!monitor || (DateTime.UtcNow-began).TotalSeconds<25)) {
      if(threshold!=HelperControl.Threshold || previousEnabled!=(HelperControl.Enabled!=0)) { threshold=HelperControl.Threshold;latch=new Latch(threshold);previousEnabled=HelperControl.Enabled!=0; }
      int test=Interlocked.Exchange(ref HelperControl.PendingTest,-1);
      if(test>=0) { Marshal.WriteInt32(input,test);uint n;DeviceIoControl(actuator,0x22a000,input,4,IntPtr.Zero,0,out n,IntPtr.Zero); }
      uint count; bool ok=DeviceIoControl(feed,0x226414,IntPtr.Zero,0,data,(uint)Marshal.SizeOf(typeof(Frame)),out count,IntPtr.Zero);
      if(!ok) throw new Exception("Pressure feed failed: "+Marshal.GetLastWin32Error());
      if(count!=(uint)Marshal.SizeOf(typeof(Frame))) throw new Exception("Pressure frame size mismatch.");
      Frame f=(Frame)Marshal.PtrToStructure(data,typeof(Frame)); if(f.Version!=3 || f.Count>5) throw new Exception("Unsupported pressure frame.");
      DateTime now=DateTime.UtcNow;
      if(f.Sequence==seq) { if((now-lastFrame).TotalMilliseconds>250) { latch.Reset();HelperControl.Pressure=0; } Thread.Sleep(8); continue; }
      seq=f.Sequence;lastFrame=now;frames++;
      bool valid=f.Count==1 && f.RawCount==1 && f.Blanked==0 && f.Contacts[0].Confident!=0 && f.Contacts[0].Palm==0;
      int pressure=valid?f.Contacts[0].Pressure:0; if(pressure>peak) peak=pressure;
      HelperControl.Pressure=pressure;
      if(latch.Sample(pressure,valid,f.Button!=0,timer.ElapsedMilliseconds) && HelperControl.Enabled!=0) {
       if(!monitor) {
        Marshal.WriteInt32(input,HelperControl.Waveform);
        if(!DeviceIoControl(actuator,0x22a000,input,4,IntPtr.Zero,0,out count,IntPtr.Zero)) throw new Exception("Actuator failed: "+Marshal.GetLastWin32Error());
       }
       clicks++; Console.WriteLine((monitor?"Threshold detected":"Haptic click")+" #"+clicks+" pressure="+pressure); Console.Out.Flush();
       HelperControl.Clicks=clicks;
      }
      if((now-lastStatus).TotalSeconds>=5) { Console.WriteLine("Status: frames="+frames+" peak="+peak+" clicks="+clicks);Console.Out.Flush();lastStatus=now; }
      Thread.Sleep(8);
     }
     return 0;
    } finally {
     serverCancel.Cancel();serverCancel.Dispose();
     if(live) SetLive(feed,input,false);
     Marshal.FreeHGlobal(input);Marshal.FreeHGlobal(data);
     Console.WriteLine("Stopped; frames="+frames+" peak="+peak+" crossings="+clicks);Console.Out.Flush();
    }
   }} finally { mutex.ReleaseMutex(); }
  }
 }
}
