using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Built with the Windows .NET Framework compiler; no SDK or Node install is
// required on the end user's computer. The Unity process owns this child.
sealed class WebHost : Form {
 [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child,IntPtr parent);
 [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out RECT rect);
 [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr window,int x,int y,int w,int h,bool redraw);
 [StructLayout(LayoutKind.Sequential)] struct RECT {public int Left,Top,Right,Bottom;}
 readonly Process owner;readonly IntPtr parentWindow;readonly string url;readonly bool verify;
 readonly NamedPipeClientStream pipe;readonly StreamWriter writer;
 readonly JavaScriptSerializer json=new JavaScriptSerializer();readonly Timer watch=new Timer();
 readonly WebView2 web=new WebView2();Dictionary<string,object> layout;
 bool closing;Size lastSize;
 static string Arg(string[] args,string key){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
 [STAThread] static void Main(string[] args){
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  try{var owner=Process.GetProcessById(int.Parse(Arg(args,"--parent")));var deadline=DateTime.UtcNow.AddSeconds(15);
   while(owner.MainWindowHandle==IntPtr.Zero&&!owner.HasExited&&DateTime.UtcNow<deadline){System.Threading.Thread.Sleep(100);owner.Refresh();}
   if(owner.MainWindowHandle==IntPtr.Zero)throw new Exception("Unity window not found");
   Application.Run(new WebHost(owner,Arg(args,"--pipe"),Arg(args,"--url"),Array.IndexOf(args,"--verify")>=0));
  }catch(Exception error){File.AppendAllText(Path.Combine(Path.GetTempPath(),"yachiyo-webhost-error.log"),DateTime.Now+" "+error+Environment.NewLine);}
 }
 WebHost(Process process,string pipeName,string address,bool verification){
  owner=process;parentWindow=process.MainWindowHandle;url=address;verify=verification;
  pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);pipe.Connect(15000);
  writer=new StreamWriter(pipe,new UTF8Encoding(false)){AutoFlush=true};
  FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopLevel=false;AutoScaleMode=AutoScaleMode.None;
  SetParent(Handle,parentWindow);web.Dock=DockStyle.Fill;Controls.Add(web);
  watch.Interval=50;watch.Tick+=delegate{if(owner.HasExited){Close();return;}ResizeToParent();};watch.Start();
  Shown+=async delegate{await Initialize();};ResizeToParent();
  Task.Run((Action)ReadCommands);
 }
 void Send(string type,string value){try{lock(writer)writer.WriteLine(type+"\t"+value);}catch(IOException){if(!closing)BeginInvoke((Action)Close);}}
 void ReadCommands(){
  try{var reader=new StreamReader(pipe,Encoding.UTF8);string line;
   while((line=reader.ReadLine())!=null){var parts=line.Split(new[]{'\t'},2);if(parts[0]=="close"){BeginInvoke((Action)Close);return;}
    if(parts[0]=="eval"&&parts.Length==2){string code=Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));BeginInvoke((Action)(async delegate{if(web.CoreWebView2!=null)await web.CoreWebView2.ExecuteScriptAsync(code);}));}
   }
  }catch(IOException){}catch(ObjectDisposedException){}finally{if(!closing&&IsHandleCreated)BeginInvoke((Action)Close);}
 }
 async Task Initialize(){
  try{
   string profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"YachiyoUnityDirector",verify?"webview-verify-"+owner.Id:"webview2");
   var options=new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
   var environment=await CoreWebView2Environment.CreateAsync(null,profile,options);await web.EnsureCoreWebView2Async(environment);
   web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;web.CoreWebView2.Settings.AreDevToolsEnabled=verify;
   web.CoreWebView2.Settings.IsZoomControlEnabled=false;web.ZoomFactor=1;
   string origin=new Uri(url).GetLeftPart(UriPartial.Authority);
   web.CoreWebView2.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){Uri target;if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out target)||target.GetLeftPart(UriPartial.Authority)!=origin)e.Cancel=true;};
   web.CoreWebView2.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;Uri target;if(e.IsUserInitiated&&Uri.TryCreate(e.Uri,UriKind.Absolute,out target)&&target.Scheme=="https")Process.Start(new ProcessStartInfo(e.Uri){UseShellExecute=true});};
   web.CoreWebView2.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e){
    if(new Uri(e.Source).GetLeftPart(UriPartial.Authority)!=origin)return;
    var message=json.Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);string type=Convert.ToString(message["type"]);string data=json.Serialize(message["data"]);
    if(type=="layout"){layout=(Dictionary<string,object>)message["data"];UpdateRegion();Send("layout",data);}else if(type=="result"&&verify&&data.Length<65536)Send("result",data);
   };
   web.CoreWebView2.ProcessFailed+=delegate(object sender,CoreWebView2ProcessFailedEventArgs e){Send("error","Windows 界面已退出："+e.ProcessFailedKind);};
   await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.yachiyoDesktop=Object.freeze({embeddedMemory:true,platform:'win32',unified:true});window.webkit={messageHandlers:{yachiyoLayout:{postMessage:data=>chrome.webview.postMessage({type:'layout',data})},yachiyoVerification:{postMessage:data=>chrome.webview.postMessage({type:'result',data})}}};");
   web.CoreWebView2.Navigate(url);
  }catch(Exception error){Send("error","Windows 界面启动失败，请安装 Microsoft Edge WebView2 Runtime。"+error.Message);}
 }
 double Number(string key){object value;return layout!=null&&layout.TryGetValue(key,out value)?Convert.ToDouble(value):0;}
 void ResizeToParent(){RECT r;if(!GetClientRect(parentWindow,out r))return;Size size=new Size(r.Right,r.Bottom);if(size!=lastSize){lastSize=size;MoveWindow(Handle,0,0,size.Width,size.Height,true);UpdateRegion();}}
 void UpdateRegion(){
  var region=new Region(new Rectangle(Point.Empty,lastSize));
  if(layout!=null&&Convert.ToBoolean(layout["visible"])&&Number("viewportWidth")>0&&Number("viewportHeight")>0){
   double sx=lastSize.Width/Number("viewportWidth"),sy=lastSize.Height/Number("viewportHeight");
   region.Exclude(new Rectangle((int)Math.Round(Number("x")*sx),(int)Math.Round(Number("y")*sy),(int)Math.Round(Number("width")*sx),(int)Math.Round(Number("height")*sy)));
  }
  var old=Region;Region=region;if(old!=null)old.Dispose();
 }
 protected override void OnFormClosed(FormClosedEventArgs e){closing=true;watch.Stop();web.Dispose();pipe.Dispose();base.OnFormClosed(e);}
}
