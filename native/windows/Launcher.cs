using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Collections.Generic;

sealed class Launcher : Form {
 public sealed class Config {public string motionRoot="",motionUrl="http://127.0.0.1:8787",backend="desktop";public bool startMotion=true,stopOwnedMotion=true;}
 readonly Label status=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Text="正在启动八千代…"};
 readonly string root=AppDomain.CurrentDomain.BaseDirectory;readonly string[] args;
 Process motion,client;Config config;bool running,closing;string log;
 static Mutex single;
 [STAThread] static void Main(string[] args){
  bool first;single=new Mutex(true,"Local\\YachiyoMotionStudioLauncher",out first);
  if(!first){MessageBox.Show("八千代已经在运行，请切回现有窗口。","八千代");return;}
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new Launcher(args));single.ReleaseMutex();
 }
 Launcher(string[] arguments){args=arguments;Text="八千代 · 启动中";ClientSize=new Size(460,140);StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;Controls.Add(status);Shown+=async delegate{await Start();};}
 void Note(string message){status.Text=message;File.AppendAllText(log,DateTime.Now.ToString("s")+" "+message+Environment.NewLine,Encoding.UTF8);}
 static string Q(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
 bool Healthy(){try{var request=(HttpWebRequest)WebRequest.Create(config.motionUrl.TrimEnd('/')+"/health");request.Proxy=null;request.Timeout=1500;using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){string data=reader.ReadToEnd();return data.Contains("\"provider\"")&&data.Contains("dip");}}catch{return false;}}
 bool SpeechWarming(){try{var request=(HttpWebRequest)WebRequest.Create(config.motionUrl.TrimEnd('/')+"/speech/health");request.Proxy=null;request.Timeout=1500;using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){var health=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());object state,alignment;return (health.TryGetValue("state",out state)&&Convert.ToString(state)=="loading")||(health.TryGetValue("alignment",out alignment)&&Convert.ToString(alignment)=="loading");}}catch{return false;}}
 async Task Start(){
  try{
   string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"YachiyoUnityDirector");Directory.CreateDirectory(data);log=Path.Combine(data,"launcher.log");
   string file=Path.Combine(root,"启动设置.json");config=File.Exists(file)?new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(file,Encoding.UTF8)):new Config();
   Uri endpoint;if(!Uri.TryCreate(config.motionUrl,UriKind.Absolute,out endpoint)||(endpoint.Scheme!="http"&&endpoint.Scheme!="https"))throw new Exception("启动设置.json 中的 motionUrl 无效。");
   if(config.backend!="desktop"&&config.backend!="windows"&&config.backend!="ubuntu")throw new Exception("backend 应为 desktop、windows 或 ubuntu。");
   if(!File.Exists(Path.Combine(root,"Yachiyo.exe")))throw new Exception("请先完整解压启动包，再运行启动程序。");
   // Never replace an existing listener or touch another Python installation.
   if(config.startMotion&&endpoint.IsLoopback&&!await Task.Run((Func<bool>)Healthy)){
    string serviceRoot=config.motionRoot;
    if(string.IsNullOrWhiteSpace(serviceRoot))foreach(string drive in new[]{"D:\\","E:\\","F:\\","C:\\"}){string candidate=Path.Combine(drive,"YachiyoMotion");if(File.Exists(Path.Combine(candidate,"state","installed.json"))){serviceRoot=candidate;break;}}
    if(string.IsNullOrWhiteSpace(serviceRoot))throw new Exception("没有找到已安装的动作模型。请在启动设置.json 中填写 motionRoot，或将 startMotion 改为 false 仅打开客户端。");
    string python=Path.Combine(serviceRoot,"runtime","python.exe"),stream=Path.Combine(serviceRoot,"runtime-streamtalk","python.exe");
    if(File.Exists(Path.Combine(serviceRoot,"state","streamtalk-installed.json"))&&File.Exists(stream))python=stream;
    string entry=Path.Combine(serviceRoot,"windows","run_service.py");if(!File.Exists(python)||!File.Exists(entry))throw new Exception("motionRoot 中缺少动作运行环境，请填写现有 YachiyoMotion 目录。");
    if(endpoint.Port!=8787)throw new Exception("自动启动的本地动作服务使用 8787 端口，请检查 motionUrl。");
    foreach(var listener in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())if(listener.Port==8787)throw new Exception("8787 端口被其他程序占用，已保留原程序，请检查后重试。");
    Note("正在加载本机动作模型，首次启动可能需要一两分钟…");
    var start=new ProcessStartInfo(python,"-u "+Q(entry)){WorkingDirectory=serviceRoot,UseShellExecute=false,CreateNoWindow=true};start.EnvironmentVariables["PYTHONUTF8"]="1";start.EnvironmentVariables["PYTHONNOUSERSITE"]="1";
    motion=Process.Start(start);DateTime deadline=DateTime.UtcNow.AddMinutes(3);bool ready=false;
    while(!closing&&DateTime.UtcNow<deadline){if(motion.HasExited)throw new Exception("动作服务启动失败，请查看 "+Path.Combine(serviceRoot,"logs","service-error.log"));if(await Task.Run((Func<bool>)Healthy)){ready=true;break;}await Task.Delay(500);}
    if(closing)return;if(!ready)throw new Exception("动作模型加载超时，请查看动作服务日志。");
    // DiP can be ready before StreamTalk and the aligner. Do not expose a cold
    // first line while those known installed models are still warming up.
    Note("正在预热语音动作与语义对齐…");deadline=DateTime.UtcNow.AddSeconds(150);
    while(!closing&&await Task.Run((Func<bool>)SpeechWarming)){if(DateTime.UtcNow>deadline)throw new Exception("语音动作模型预热超时，请查看动作服务日志。");if(motion.HasExited)throw new Exception("动作服务在预热时退出。");await Task.Delay(500);}
   }
   if(closing)return;Note("正在打开角色和直播界面…");
   var launch=new ProcessStartInfo(Path.Combine(root,"Yachiyo.exe"),string.Join(" ",Array.ConvertAll(args,Q))){WorkingDirectory=root,UseShellExecute=false};
   launch.EnvironmentVariables["YACHIYO_LOCAL_MOTION_BACKEND"]=config.backend;launch.EnvironmentVariables["YACHIYO_LOCAL_MOTION_URL"]=config.motionUrl.TrimEnd('/');
   client=Process.Start(launch);running=true;Hide();await Task.Run((Action)client.WaitForExit);running=false;
   if(client.ExitCode!=0)MessageBox.Show("八千代异常退出，请查看 %USERPROFILE%\\AppData\\LocalLow\\Tsukuyomi\\Yachiyo Motion Studio\\Player.log。","八千代");
  }catch(Exception error){if(log!=null)Note(error.Message);MessageBox.Show(error.Message,"八千代启动失败",MessageBoxButtons.OK,MessageBoxIcon.Error);}
  finally{StopOwned();Close();}
 }
 void StopOwned(){if(motion!=null&&config.stopOwnedMotion){try{if(!motion.HasExited){motion.Kill();motion.WaitForExit(10000);}}catch{}}}
 protected override void OnFormClosing(FormClosingEventArgs e){closing=true;if(!running)StopOwned();base.OnFormClosing(e);}
}
