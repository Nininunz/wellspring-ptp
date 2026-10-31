// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
class Client {
 [StructLayout(LayoutKind.Sequential)] struct Interface { public int size; public Guid guid; public int flags; public IntPtr reserved; }
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid g,string e,IntPtr p,uint f);
 [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s,IntPtr d,ref Guid g,uint i,ref Interface f);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s,ref Interface f,IntPtr b,uint n,out uint needed,IntPtr d);
 [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string p,uint a,uint share,IntPtr sec,uint disp,uint flags,IntPtr t);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle h,uint code,ref uint input,uint inputSize,IntPtr output,uint outputSize,out uint returned,IntPtr ov);
 static int Main(string[] args) {
  if(args.Length==1 && (args[0]=="--startup-on" || args[0]=="--startup-off")) return HelperControl.Startup(args[0]=="--startup-on");
  if(args.Length==1 && args[0]=="--selftest") return PressureBridge.SelfTest();
  bool watch=args.Length==1 && (args[0]=="--watch" || args[0]=="--monitor");
  bool fire=args.Length==1 && (args[0]=="--light" || args[0]=="--firm" || watch);
  if(args.Length>1 || (args.Length==1 && !fire && args[0]!="--list")) return 2;
  Guid guid=new Guid("dbe6fbda-5109-42ea-9497-881d31f09720");
  IntPtr set=SetupDiGetClassDevs(ref guid,null,IntPtr.Zero,18);
  try {
   Interface f=new Interface(); f.size=Marshal.SizeOf(typeof(Interface));
   if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,0,ref f)) { Console.WriteLine("Actuator transport not installed/bound."); return 1; }
   Interface extra=new Interface(); extra.size=f.size;
   if(SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,1,ref extra)) { Console.Error.WriteLine("Multiple transports; refusing write."); return 3; }
   uint needed; SetupDiGetDeviceInterfaceDetail(set,ref f,IntPtr.Zero,0,out needed,IntPtr.Zero);
   IntPtr b=Marshal.AllocHGlobal((int)needed); string path;
   try { Marshal.WriteInt32(b,IntPtr.Size==8?8:6); if(!SetupDiGetDeviceInterfaceDetail(set,ref f,b,needed,out needed,IntPtr.Zero)) return 4; path=Marshal.PtrToStringUni(IntPtr.Add(b,4)); } finally { Marshal.FreeHGlobal(b); }
   Console.WriteLine("Transport: "+path); if(!fire) return 0;
   using(SafeFileHandle h=CreateFile(path,0x40000000,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
    if(h.IsInvalid) { Console.Error.WriteLine("Open error "+Marshal.GetLastWin32Error()); return 5; }
    if(watch) return PressureBridge.Run(h,args[0]=="--monitor");
    uint strength=args[0]=="--firm"?1u:0u, returned;
    bool ok=DeviceIoControl(h,0x0022a000,ref strength,4,IntPtr.Zero,0,out returned,IntPtr.Zero);
    Console.WriteLine("Exact SET_REPORT completed="+ok+" error="+(ok?0:Marshal.GetLastWin32Error())); return ok?0:6;
   }
  } finally { SetupDiDestroyDeviceInfoList(set); }
 }
}
