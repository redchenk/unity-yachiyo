using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Yachiyo {
 public partial class StudioUI {
  public string SelectedBackend {get;private set;}="ubuntu";
  public string BackendLabel=>SelectedBackend=="desktop"?"台式机 Windows":SelectedBackend=="windows"?"笔记本 Windows":"笔记本 Ubuntu";
  public bool ServiceConnected {get;private set;}
  public bool ConnectingBackend {get;private set;}
  public string LastHealthBackend {get;private set;}="";
  public string ServiceStatus=>serviceStatus;
  int connectionVersion;
  bool advancedService,serviceFocusRequested;
  public static bool IsMotionBackend(string backend)=>backend=="windows"||backend=="ubuntu"||backend=="desktop";
  static string DefaultServiceUrl(string backend)=>backend=="desktop"?"http://127.0.0.1:28787":backend=="windows"?"http://127.0.0.1:18787":"http://127.0.0.1:8787";
  static string ProfileKey(string backend)=>"motion-service-"+backend;
  static string ConnectionState(string backend)=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),"Library","Application Support","Tsukuyomi","MotionConnections",backend+".status");
  void InitializeServiceProfiles(string[] args){
   string legacy=PlayerPrefs.GetString("motion-service",serviceUrl);
   SelectedBackend=PlayerPrefs.GetString("motion-backend",legacy.Contains(":18787")?"windows":"ubuntu");
   if(!IsMotionBackend(SelectedBackend))SelectedBackend="ubuntu";
   serviceUrl=PlayerPrefs.GetString(ProfileKey(SelectedBackend),PlayerPrefs.HasKey("motion-backend")?DefaultServiceUrl(SelectedBackend):legacy);
   int platform=Array.IndexOf(args,"--motion-platform");if(platform>=0&&platform+1<args.Length){SelectedBackend=IsMotionBackend(args[platform+1])?args[platform+1]:"ubuntu";serviceUrl=PlayerPrefs.GetString(ProfileKey(SelectedBackend),DefaultServiceUrl(SelectedBackend));}
   int endpoint=Array.IndexOf(args,"--service-url");if(endpoint>=0&&endpoint+1<args.Length)serviceUrl=args[endpoint+1].TrimEnd('/');
   if(Array.IndexOf(args,"--show-service-settings")>=0){settings=true;serviceFocusRequested=true;}
   if(Array.IndexOf(args,"--no-auto-connect")<0&&endpoint<0)StartCoroutine(ConnectSelectedService());
  }
  public void SelectBackend(string backend,bool connect=true){
   if(!IsMotionBackend(backend))throw new ArgumentException("Unknown motion backend");
   // StopMotion invalidates generation, pending session creation and queued poses
   // before the endpoint changes. CloseStream retains the old endpoint for DELETE.
   StopMotion();connectionVersion++;ConnectingBackend=false;ServiceConnected=false;LastHealthBackend="";
   SelectedBackend=backend;serviceUrl=PlayerPrefs.GetString(ProfileKey(backend),DefaultServiceUrl(backend));hostedArdy=false;
   PlayerPrefs.SetString("motion-backend",backend);PlayerPrefs.SetString("motion-service",serviceUrl);PlayerPrefs.Save();
   serviceStatus="已切换到 "+BackendLabel+"，正在等待连接。";status="动作服务已切换到 "+BackendLabel+"。";
   if(connect)StartCoroutine(ConnectSelectedService());
  }
  IEnumerator ProbeService(int version,string endpoint,bool showFailure){
   string backend=SelectedBackend;
   using(var req=UnityWebRequest.Get(endpoint+(hostedArdy?"/ardy":"")+"/health")){
    req.timeout=2;yield return req.SendWebRequest();
    if(version!=connectionVersion||endpoint!=serviceUrl||backend!=SelectedBackend)yield break;
    ServiceConnected=false;
    if(req.result==UnityWebRequest.Result.Success){
     ServiceHealth health=null;try{health=JsonUtility.FromJson<ServiceHealth>(req.downloadHandler.text);}catch{}
     if(health!=null&&!string.IsNullOrEmpty(health.provider)){
      ServiceConnected=true;LastHealthBackend=backend;serviceStatus=BackendLabel+" 已连接 · "+(health.streaming?"连续实时动作":"动作片段")+(health.state=="unloaded"?" · 首次生成会加载模型":"");
      PlayerPrefs.SetString(ProfileKey(backend),endpoint);PlayerPrefs.SetString("motion-service",endpoint);PlayerPrefs.Save();yield break;
     }
    }
    if(showFailure)serviceStatus=BackendLabel+" 未连接，请确认目标电脑及动作服务已开启。";
   }
  }
  IEnumerator CheckService(){if(!ValidUrl())yield break;int version=++connectionVersion;ConnectingBackend=false;yield return ProbeService(version,serviceUrl,true);}
  public IEnumerator ConnectSelectedService(bool allowHelper=true){
   if(!ValidUrl())yield break;int version=++connectionVersion;string endpoint=serviceUrl,backend=SelectedBackend;
   ConnectingBackend=true;ServiceConnected=false;serviceStatus="正在连接 "+BackendLabel+"…";
   yield return ProbeService(version,endpoint,false);
   if(version!=connectionVersion)yield break;
   if(ServiceConnected){ConnectingBackend=false;yield break;}
   if(!allowHelper||endpoint!=DefaultServiceUrl(backend)||Application.platform!=RuntimePlatform.OSXPlayer){ConnectingBackend=false;serviceStatus=BackendLabel+" 未连接，请确认目标电脑及动作服务已开启。";yield break;}
   string helper=Path.Combine(Application.streamingAssetsPath,"ServiceTools",backend+".command");
   string state=ConnectionState(backend);
   try{
    Directory.CreateDirectory(Path.GetDirectoryName(state));if(File.Exists(state))File.Delete(state);
    if(!File.Exists(helper))throw new FileNotFoundException("Connection helper missing");
    using(var chmod=Process.Start(new ProcessStartInfo("/bin/chmod","+x "+QuoteArgument(helper)){UseShellExecute=false,CreateNoWindow=true}))chmod.WaitForExit();
    Process.Start(new ProcessStartInfo("/usr/bin/open","-a Terminal "+QuoteArgument(helper)){UseShellExecute=false,CreateNoWindow=true});
   }catch(Exception error){ConnectingBackend=false;serviceStatus="连接入口暂不可用："+error.GetType().Name;yield break;}
   serviceStatus="正在连接 "+BackendLabel+"，请在终端完成登录。";
   float deadline=Time.realtimeSinceStartup+120;
   while(version==connectionVersion&&Time.realtimeSinceStartup<deadline){
    yield return new WaitForSeconds(1);if(version!=connectionVersion)yield break;
    yield return ProbeService(version,endpoint,false);if(version!=connectionVersion)yield break;
    if(ServiceConnected){ConnectingBackend=false;yield break;}
    string result="";try{if(File.Exists(state))result=File.ReadAllText(state).Trim();}catch(IOException){}
    if(result=="service_off"){ConnectingBackend=false;serviceStatus=BackendLabel+" 通道已连接，请先在目标电脑启动动作服务，然后点「重连」。";yield break;}
    if(result=="failed"){ConnectingBackend=false;serviceStatus=BackendLabel+" 连接失败，请确认目标电脑和系统，并完成终端登录。";yield break;}
   }
   if(version==connectionVersion){ConnectingBackend=false;serviceStatus=BackendLabel+" 尚未连接。完成终端登录后可点击「重连」。";}
  }
  static string QuoteArgument(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
  Rect ServicePanel(float w,float h)=>new Rect(w*.34f+28,h-(advancedService?482:440),w*.66f-56,advancedService?392:350);
  void DrawServiceSettings(float w,float h){
   var panel=ServicePanel(w,h);float x=panel.x,y=panel.y,pw=panel.width;Box(panel,new Color(.085f,.12f,.15f,.98f));
   Text(x+18,y+14,pw-180,26,"动作服务 · "+BackendLabel,label);
   if(Button(new Rect(x+pw-116,y+12,98,28),advancedService?"收起地址":"高级地址"))advancedService=!advancedService;
   if(Button(new Rect(x+18,y+52,136,34),(SelectedBackend=="windows"?"✓  ":"")+"Windows  1",SelectedBackend=="windows"))SelectBackend("windows");
   if(Button(new Rect(x+166,y+52,136,34),(SelectedBackend=="ubuntu"?"✓  ":"")+"Ubuntu  2",SelectedBackend=="ubuntu"))SelectBackend("ubuntu");
   if(Button(new Rect(x+314,y+52,136,34),(SelectedBackend=="desktop"?"✓  ":"")+"台式机  3",SelectedBackend=="desktop"))SelectBackend("desktop");
   if(Button(new Rect(x+pw-166,y+52,148,34),ConnectingBackend?"重新连接":"重连"))StartCoroutine(ConnectSelectedService());
   Text(x+18,y+96,pw-36,38,serviceStatus,small);
   float shift=advancedService?42:0;
   if(advancedService){GUI.SetNextControlName("service-url");string edited=GUI.TextField(new Rect(x+18,y+136,pw-176,32),serviceUrl,200,input);if(edited!=serviceUrl){StopMotion();connectionVersion++;ConnectingBackend=false;ServiceConnected=false;serviceUrl=edited;serviceStatus="地址已更改，请检查连接。";}if(Button(new Rect(x+pw-140,y+136,122,32),"检查连接"))StartCoroutine(CheckService());}
   if(Button(new Rect(x+18,y+140+shift,207,32),hostedArdy?"ARDY 在线片段":"DiP 连续实时")){StopMotion();connectionVersion++;hostedArdy=!hostedArdy;ServiceConnected=false;StartCoroutine(CheckService());}
   Text(x+240,y+144+shift,pw-260,30,hostedArdy?"公共在线生成，需等待片段。":"根据动作指令在所选电脑持续生成。",small);
   Text(x+18,y+184+shift,90,26,"微笑",small);avatar.expression=GUI.HorizontalSlider(new Rect(x+100,y+192+shift,pw-130,20),avatar.expression,0,1);
   if(clothing){
    if(Button(new Rect(x+18,y+222+shift,182,32),clothing.simulationEnabled?"布料：开  F7":"布料：关  F7"))clothing.SetSimulation(!clothing.simulationEnabled);
    if(Button(new Rect(x+211,y+222+shift,150,32),clothing.highQuality?"精度：精细":"精度：标准"))clothing.highQuality=!clothing.highQuality;
    if(Button(new Rect(x+372,y+222+shift,130,32),"复位衣服"))clothing.ResetSimulation();
    Text(x+18,y+269+shift,90,26,"风力",small);clothing.wind=GUI.HorizontalSlider(new Rect(x+100,y+277+shift,pw-180,20),clothing.wind,0,1);Text(x+pw-65,y+269+shift,50,26,Mathf.RoundToInt(clothing.wind*100)+"%",small);
    Text(x+18,y+308+shift,pw-36,26,"切换服务会停止旧动作 · 记住本次选择",small);
   }
  }
 }
}
