// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Diagnostics;
using System.Security.Principal;
using System.Security;
using System.Security.AccessControl;
class HelperControl {
 internal static int Enabled=1,Threshold=265,Waveform=1,Pressure,Clicks,PendingTest=-1;
 internal static volatile bool Stop;
 internal const string PipeName="WellspringPTP_Haptics_v1";
 internal static string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WellspringPTP","Haptics");
 static string SettingsPath=Path.Combine(Folder,"settings.txt");
 internal static void Load() {
  if(!File.Exists(SettingsPath))return;
  try { var p=File.ReadAllText(SettingsPath).Trim().Split('|');
   int e,t,w; if(p.Length==3 && int.TryParse(p[0],out e) && int.TryParse(p[1],out t) && int.TryParse(p[2],out w) && e>=0&&e<=1&&t>=50&&t<=1000&&w>=0&&w<=1) { Enabled=e;Threshold=t;Waveform=w; }
  } catch(IOException) { }
 }
 static void Save() {
  Directory.CreateDirectory(Folder);string temp=SettingsPath+".tmp";
  File.WriteAllText(temp,Enabled+"|"+Threshold+"|"+Waveform);File.Move(temp,SettingsPath,true);
 }
 internal static Task Serve(CancellationToken token) { return Task.Run(async ()=> {
  while(!token.IsCancellationRequested) {
   try {
    var security=new PipeSecurity();
    security.SetSecurityDescriptorSddlForm("D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;"+WindowsIdentity.GetCurrent().User.Value+")");
    using(var pipe=NamedPipeServerStreamAcl.Create(PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security)) {
     await pipe.WaitForConnectionAsync(token);
     using(var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,false,1024,true))
     using(var writer=new StreamWriter(pipe,System.Text.Encoding.UTF8,1024,true) {AutoFlush=true}) {
      string line=await reader.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(2),token);
      string result="ERROR";
      if(line=="GET") result=Enabled+"|"+Threshold+"|"+Waveform+"|"+Pressure+"|"+Clicks;
      else if(line!=null && line.Length<100 && line.StartsWith("SET|")) {
       var p=line.Split('|');int e,t,w;
       if(p.Length==4&&int.TryParse(p[1],out e)&&int.TryParse(p[2],out t)&&int.TryParse(p[3],out w)&&e>=0&&e<=1&&t>=50&&t<=1000&&w>=0&&w<=1) { Enabled=e;Threshold=t;Waveform=w;Save();result="OK"; }
      } else if(line=="TEST|0" || line=="TEST|1") { Interlocked.Exchange(ref PendingTest,line.EndsWith("1")?1:0);result="OK"; }
      else if(line=="STOP") { Stop=true;result="OK"; }
      await writer.WriteLineAsync(result);
     }
    }
   } catch(OperationCanceledException) { break; } catch(Exception ex) when(ex is IOException || ex is TimeoutException || ex is UnauthorizedAccessException) { Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"control-error.txt"),ex.Message);await Task.Delay(250,token); }
  }
 },token); }
 internal static int Startup(bool enable) {
  const string name="Wellspring Pressure Haptics";
  var info=new ProcessStartInfo("schtasks.exe") {UseShellExecute=false,CreateNoWindow=true};
  if(enable) {
   Directory.CreateDirectory(Folder);
   string exe=Environment.ProcessPath;
   string sid=WindowsIdentity.GetCurrent().User.Value;
   string xml="<?xml version=\"1.0\"?><Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Triggers><LogonTrigger><Enabled>true</Enabled><UserId>"+sid+"</UserId></LogonTrigger></Triggers><Principals><Principal id=\"Author\"><UserId>"+sid+"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals><Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings><Actions Context=\"Author\"><Exec><Command>"+SecurityElement.Escape(exe)+"</Command><Arguments>--watch</Arguments></Exec></Actions></Task>";
   string path=Path.Combine(Folder,"startup-task.xml");File.WriteAllText(path,xml);
   foreach(string a in new[]{"/Create","/TN",name,"/XML",path,"/F"})info.ArgumentList.Add(a);
  } else foreach(string a in new[]{"/Delete","/TN",name,"/F"})info.ArgumentList.Add(a);
  using(var process=Process.Start(info)) {process.WaitForExit();return process.ExitCode;}
 }
}
