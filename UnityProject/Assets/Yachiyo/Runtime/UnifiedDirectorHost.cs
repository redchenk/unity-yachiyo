using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

namespace Yachiyo {
 // One user-facing application. Its private local service is owned by this process.
 public sealed class UnifiedDirectorHost : MonoBehaviour {
  public bool Active {get;private set;}
  public string Status {get;private set;}="正在启动直播导演…";
  public static Rect StageViewport {get;private set;}=new Rect(.1f,.15f,.55f,.65f);
  string lastLayout;GameObject backdrop;Mesh backdropMesh;Camera stageCamera;Texture2D background;
  Process service;string stateDir,token,origin;bool quitting;Font font;
  [Serializable]class State {public string origin,token;public int pid;}
  [Serializable]class Shutdown {public string token;}
  [Serializable]class Layout {public bool visible;public float x,y,width,height,stageX,stageY,stageWidth,stageHeight,viewportWidth,viewportHeight;}
  [DllImport("YachiyoWebView")]static extern void YachiyoWeb_Open(string url);
  [DllImport("YachiyoWebView")]static extern void YachiyoWeb_Layout(float width,float header);
  [DllImport("YachiyoWebView")]static extern void YachiyoWeb_Close();
  [DllImport("YachiyoWebView")]static extern void YachiyoWeb_Evaluate(string script);
  [DllImport("YachiyoWebView")]static extern void YachiyoWeb_EnableVerification();
  [DllImport("YachiyoWebView")]static extern IntPtr YachiyoWeb_Verification();
  [DllImport("YachiyoWebView")]static extern IntPtr YachiyoWeb_Viewport();
  [DllImport("YachiyoWebView")]static extern void YachiyoWeb_CameraInput([Out]float[] values);
  static readonly float[] cameraInput=new float[4];
  public static float PixelScale {get;private set;}=2;
  static bool Windows=>Application.platform==RuntimePlatform.WindowsPlayer;
  static string DirectorRoot=>Windows?Path.Combine(Application.dataPath,"Director"):Path.Combine(Application.dataPath,"Resources","Director");
  static string Node=>Path.Combine(DirectorRoot,"runtime",Windows?"node.exe":"node");
  public static float[] ConsumeCameraInput(){if(Windows)UnifiedWindowsWebView.CameraInput(cameraInput);else YachiyoWeb_CameraInput(cameraInput);return cameraInput;}
  public static bool IsBundled=>(Windows||Application.platform==RuntimePlatform.OSXPlayer)&&Array.IndexOf(Environment.GetCommandLineArgs(),"--director-url")<0&&File.Exists(Node);
  static string Quote(string text)=>"\""+(Windows?text:text.Replace("\\","\\\\")).Replace("\"","\\\"")+"\"";
  void Evaluate(string script){if(Windows)UnifiedWindowsWebView.Evaluate(script);else YachiyoWeb_Evaluate(script);}
  IEnumerator Start(){
   font=Resources.Load<Font>("NotoSansCJKsc-Regular");
   if(!IsBundled){enabled=false;yield break;}
   // Override a previous Windows release's saved 1280x800 window on every boot.
   if(Windows)Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
   string root=DirectorRoot;
   stateDir=Path.Combine(Path.GetTempPath(),"yachiyo-director-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stateDir);
   string state=Path.Combine(stateDir,"ready.json");
   var start=new ProcessStartInfo(Node,Quote(Path.Combine(root,"tools","desktop","headless.mjs"))+" --parent-pid "+Process.GetCurrentProcess().Id+" --state "+Quote(state)) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root};
   start.EnvironmentVariables["YACHIYO_SQLITE_DRIVER"]="node";
   try{service=Process.Start(start);}catch(Exception e){Status="导演服务未能启动："+e.Message;yield break;}
   float deadline=Time.realtimeSinceStartup+25;
   while(!File.Exists(state)&&Time.realtimeSinceStartup<deadline){if(service.HasExited){Status="导演服务已退出，请检查端口 3289 和日志。";yield break;}yield return null;}
   if(!File.Exists(state)){Status="导演服务启动超时";yield break;}
   var ready=JsonUtility.FromJson<State>(File.ReadAllText(state));origin=ready.origin;token=ready.token;
   var args=Environment.GetCommandLineArgs();int verification=Array.IndexOf(args,"--verify-web-script");
   try{if(Windows)UnifiedWindowsWebView.Open(root,origin+"/live2d-studio/?unified=1",verification>=0);else{if(verification>=0)YachiyoWeb_EnableVerification();YachiyoWeb_Open(origin+"/live2d-studio/?unified=1");}Active=true;Status="直播导演 · 同窗控制";}catch(Exception e){Status="导演面板未能打开："+e.Message;}
   if(Active&&verification>=0&&verification+1<args.Length)StartCoroutine(VerifyWeb(args[verification+1]));
  }
  IEnumerator VerifyWeb(string script){
   float deadline=Time.realtimeSinceStartup+25;
   while(!GetComponent<DirectorBridge>().Connected&&Time.realtimeSinceStartup<deadline)yield return null;
   yield return new WaitForSeconds(3);Evaluate(File.ReadAllText(script));deadline=Time.realtimeSinceStartup+70;
   while(Time.realtimeSinceStartup<deadline){
    string result=Windows?UnifiedWindowsWebView.Result:Marshal.PtrToStringAnsi(YachiyoWeb_Verification());
    File.WriteAllText(script+".progress.json",result);
    if(result.Contains("\"done\":true")){File.WriteAllText(script+".result.json",result);yield return new WaitForSeconds(.5f);Application.Quit();yield break;}
    yield return new WaitForSeconds(.15f);
   }
   File.WriteAllText(script+".result.json","{\"done\":true,\"error\":\"WebKit verification timed out\"}");Application.Quit();
  }
  void Update(){
   if(!Active)return;if(!Windows)YachiyoWeb_Layout(1,0);
   if(Windows&&!string.IsNullOrEmpty(UnifiedWindowsWebView.Error)){Status=UnifiedWindowsWebView.Error;Active=false;return;}
   string value=Windows?UnifiedWindowsWebView.Layout:Marshal.PtrToStringAnsi(YachiyoWeb_Viewport());if(value==lastLayout)return;lastLayout=value;
   var layout=JsonUtility.FromJson<Layout>(value);if(layout.viewportWidth<=0||layout.viewportHeight<=0||layout.width<=0||layout.height<=0)return;
   StageViewport=new Rect(layout.x/layout.viewportWidth,1-(layout.y+layout.height)/layout.viewportHeight,layout.width/layout.viewportWidth,layout.height/layout.viewportHeight);
   PixelScale=Screen.width/layout.viewportWidth;
   SetupBackground(layout);
  }
  void SetupBackground(Layout layout){
   if(!stageCamera)stageCamera=GetComponent<StudioUI>().view;
   if(!backdrop){
    background=Resources.Load<Texture2D>("DirectorStage");if(!background)return;
    backdrop=new GameObject("Director stage background");backdrop.transform.SetParent(stageCamera.transform,false);backdrop.transform.localPosition=new Vector3(0,0,12);
    backdropMesh=new Mesh();backdropMesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};backdropMesh.triangles=new[]{0,2,1,0,3,2};
    backdrop.AddComponent<MeshFilter>().sharedMesh=backdropMesh;var material=new Material(Shader.Find("Yachiyo/StageBackdrop"));material.mainTexture=background;backdrop.AddComponent<MeshRenderer>().sharedMaterial=material;
    foreach(string name in new[]{"Stage","Stage ring"}){var item=GameObject.Find(name);if(item&&item.GetComponent<Renderer>())item.GetComponent<Renderer>().enabled=false;}
   }
   float halfHeight=12*Mathf.Tan(stageCamera.fieldOfView*Mathf.Deg2Rad*.5f);backdrop.transform.localScale=new Vector3(halfHeight*layout.width/layout.height,halfHeight,1);
   float imageAspect=(float)background.width/background.height,stageAspect=layout.stageWidth/layout.stageHeight;
   float us=imageAspect>stageAspect?stageAspect/imageAspect:1,vs=imageAspect>stageAspect?1:imageAspect/stageAspect;
   float u0=(1-us)/2+(layout.x-layout.stageX)/layout.stageWidth*us,u1=u0+layout.width/layout.stageWidth*us;
   float v1=(1-vs)/2+(1-(layout.y-layout.stageY)/layout.stageHeight)*vs,v0=v1-layout.height/layout.stageHeight*vs;
   backdropMesh.uv=new[]{new Vector2(u0,v0),new Vector2(u1,v0),new Vector2(u1,v1),new Vector2(u0,v1)};
  }
  public void StopAudio(){if(Active)Evaluate("window.dispatchEvent(new CustomEvent('yachiyo:avatar-stop'));");}
  void OnApplicationQuit(){
   if(quitting)return;quitting=true;
   if(Windows)UnifiedWindowsWebView.Close();else if(Active)YachiyoWeb_Close();
   if(service!=null&&!service.HasExited){
    // Never block Unity's quit callback waiting for its own network player loop.
    try{if(Windows){var request=(System.Net.HttpWebRequest)System.Net.WebRequest.Create(origin+"/api/app/shutdown");request.Proxy=null;request.Method="POST";request.ContentType="application/json";request.Timeout=500;var body=System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Shutdown{token=token}));using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);request.GetResponse().Close();}else Process.Start(new ProcessStartInfo("/bin/kill","-TERM "+service.Id){UseShellExecute=false,CreateNoWindow=true});}catch{}
    // The Node parent watchdog also closes services after a crash/forced quit.
   }
  }
  void OnGUI(){
   if(Active||!IsBundled||!font)return;float scale=Mathf.Min(Screen.width/1280f,Screen.height/800f);var previous=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
   GUI.Label(new Rect(350,25,500,22),Status,new GUIStyle(GUI.skin.label){font=font,fontSize=14,normal={textColor=new Color(.45f,.88f,.76f)}});GUI.matrix=previous;
  }
 }
}
