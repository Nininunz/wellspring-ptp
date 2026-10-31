using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace AmtPtpConfigGui {
 public partial class HapticsPanel : System.Windows.Controls.UserControl {
  readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(200)};
  bool busy, initialized;
  string Helper=>Path.Combine(AppContext.BaseDirectory,"WellspringHaptics.exe");
  public HapticsPanel() {
   InitializeComponent(); ThresholdValue.Text="Threshold: 265 · release at 185";timer.Tick+=async(_,_)=>await Refresh();
   Loaded+=async(_,_)=>{timer.Start();Startup.IsChecked=await HasStartup();await Refresh();};
   Unloaded+=(_,_)=>timer.Stop();
  }
  static async Task<string> Send(string command) {
   using var timeout=new CancellationTokenSource(1200);
   using var pipe=new NamedPipeClientStream(".","WellspringPTP_Haptics_v1",PipeDirection.InOut,PipeOptions.Asynchronous);
   await pipe.ConnectAsync(timeout.Token);
   using var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,false,1024,true);
   using var writer=new StreamWriter(pipe,System.Text.Encoding.UTF8,1024,true){AutoFlush=true};
   await writer.WriteLineAsync(command.AsMemory(),timeout.Token);
   return await reader.ReadLineAsync(timeout.Token)??"ERROR";
  }
  async Task Refresh() {
   if(busy)return;busy=true;
   try {
    var p=(await Send("GET")).Split('|');if(p.Length!=5)throw new IOException("Invalid helper reply.");
    if(!initialized) {Enabled.IsChecked=p[0]=="1";Threshold.Value=int.Parse(p[1]);Waveform.SelectedIndex=int.Parse(p[2]);initialized=true;}
    int pressure=int.Parse(p[3]);PressureMeter.Value=Math.Min(pressure,1000);PressureValue.Text=$"{pressure} · {p[4]} haptic clicks";
    Status.Text=p[0]=="1"?"Connected · second click is enabled":"Connected · second click is paused";SettingsControls.IsEnabled=true;
   } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or OperationCanceledException or TimeoutException or FormatException) {Status.Text="Helper is not running or could not be reached. Start it to enable pressure haptics.";SettingsControls.IsEnabled=false;initialized=false;PressureMeter.Value=0;}
   finally {busy=false;}
  }
  void Threshold_Changed(object sender,RoutedPropertyChangedEventArgs<double> e) {if(ThresholdValue!=null)ThresholdValue.Text=$"Threshold: {(int)e.NewValue} · release at {(int)e.NewValue*7/10}";}
  async Task RunHelper(string argument,bool wait) {
   if(!File.Exists(Helper))throw new FileNotFoundException("WellspringHaptics.exe is missing beside the control panel.");
   using var process=Process.Start(new ProcessStartInfo(Helper,argument){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden});
   if(wait&&process!=null) {await process.WaitForExitAsync();if(process.ExitCode!=0)throw new IOException("The helper operation did not complete.");}
  }
  async void Start_Click(object sender,RoutedEventArgs e) {try {await RunHelper("--watch",false);await Task.Delay(700);await Refresh();}catch(Exception ex){ShowError(ex);}}
  async void Stop_Click(object sender,RoutedEventArgs e) {try {await Send("STOP");await Task.Delay(300);await Refresh();}catch(Exception ex){ShowError(ex);}}
  async void Apply_Click(object sender,RoutedEventArgs e) {try {if(await Send($"SET|{(Enabled.IsChecked==true?1:0)}|{(int)Threshold.Value}|{Waveform.SelectedIndex}")!="OK")throw new IOException("Settings were rejected.");await Refresh();}catch(Exception ex){ShowError(ex);}}
  async void Test_Click(object sender,RoutedEventArgs e) {try {if(await Send("TEST|"+((System.Windows.Controls.Button)sender).Tag)!="OK")throw new IOException("Test was rejected.");}catch(Exception ex){ShowError(ex);}}
  static async Task<bool> HasStartup() {
   using var p=Process.Start(new ProcessStartInfo("schtasks.exe","/Query /TN \"Wellspring Pressure Haptics\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true});
   if(p==null)return false;await p.WaitForExitAsync();return p.ExitCode==0;
  }
  async void Startup_Click(object sender,RoutedEventArgs e) {try {await RunHelper(Startup.IsChecked==true?"--startup-on":"--startup-off",true);}catch(Exception ex){ShowError(ex);}finally {Startup.IsChecked=await HasStartup();}}
  static void ShowError(Exception ex)=>System.Windows.MessageBox.Show(ex.Message,"Haptic controls",MessageBoxButton.OK,MessageBoxImage.Information);
 }
}
