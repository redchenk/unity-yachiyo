using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Yachiyo {
 public partial class StudioUI : MonoBehaviour {
  public AvatarMotion avatar;
  public Camera view;
  public string serviceUrl="http://127.0.0.1:8787";
  string prompt="向我挥挥手", status="准备好了。试着和我打个招呼吧。", serviceStatus="未连接";
  bool busy,settings,hostedArdy,cameraSettings;
  GarmentPhysics clothing;
  YachiyoFaceController face;
  int requestVersion;
  string streamSession,streamEndpoint;
  int streamSequence;
  public int RemoteChunks {get;private set;}
  public float LastGenerationSeconds {get;private set;}
  float yaw=0, pitch=8, distance=3.4f;
  Vector3 cameraOffset=new Vector3(0,.85f,0),previousPointer;
  int cameraDragButton=-1;
  bool cameraPan,traceCamera,cameraFocusRequested,textFieldFocused;
  Font font;GUIStyle label,title,small,button,input;
  Color ink=new Color(.86f,.9f,.93f),muted=new Color(.47f,.56f,.63f),accent=new Color(.45f,.88f,.76f);
  string[] localIds={"wave","bow","nod","turn"};string[] localNames={"挥手","鞠躬","点头","转一圈"};
  void Start(){clothing=avatar.GetComponent<GarmentPhysics>();face=avatar.GetComponent<YachiyoFaceController>()??avatar.gameObject.AddComponent<YachiyoFaceController>();face.avatar=avatar;var director=gameObject.AddComponent<DirectorBridge>();director.ui=this;director.avatar=avatar;director.face=face;if(UnifiedDirectorHost.IsBundled)gameObject.AddComponent<UnifiedDirectorHost>();font=Resources.Load<Font>("NotoSansCJKsc-Regular");var args=Environment.GetCommandLineArgs();traceCamera=Array.IndexOf(args,"--trace-camera-input")>=0;InitializeServiceProfiles(args);StartCoroutine(AutoCapture());StartCoroutine(VerifyRemote(args));StartCoroutine(ClothVerification.Run(this,avatar,clothing,args));StartCoroutine(FaceVerification.Run(avatar,args));StartCoroutine(StreamVerification.Run(this,avatar,args));StartCoroutine(BackendVerification.Run(this,avatar,args));StartCoroutine(VerifyAll(args));}
  IEnumerator VerifyAll(string[] args){
   int p=Array.IndexOf(args,"--verify-all");if(p<0||p+1>=args.Length)yield break;string prefix=args[p+1];yield return null;
   yield return ClothVerification.Run(this,avatar,clothing,new[]{"--verify-cloth",prefix+"-cloth"});
   yield return StreamVerification.Run(this,avatar,new[]{"--verify-stream",prefix+"-stream"});
   yield return VerifyRemote(new[]{"--verify-ardy",prefix+"-ardy"});
   yield return FaceVerification.Run(avatar,new[]{"--verify-face",prefix+"-face"});
   hostedArdy=false;prompt="向我挥挥手";avatar.followPointer=true;status="准备好了，可以发送实时动作指令。";Debug.Log("ALL_VERIFICATION_DONE "+prefix);
  }
  IEnumerator VerifyRemote(string[] args){
   int p=Array.IndexOf(args,"--verify-remote");bool online=Array.IndexOf(args,"--verify-ardy")>=0;if(online){p=Array.IndexOf(args,"--verify-ardy");hostedArdy=true;}
   if(p<0||p+1>=args.Length)yield break;yield return null;requestVersion++;
   yield return Generate(online?"挥手":"跳舞",requestVersion,serviceUrl+(online?"/ardy":""));
   bool played=avatar.Playing;string source=avatar.MotionSource;
   yield return new WaitForSeconds(1.4f);ScreenCapture.CaptureScreenshot(args[p+1]+".png");yield return new WaitForEndOfFrame();StopMotion();
   File.WriteAllText(args[p+1]+".json",JsonUtility.ToJson(new RemoteReport{remote_motion_loaded=played,stop_worked=!avatar.Playing,source=source}));
  }
  [Serializable]class RemoteReport{public bool remote_motion_loaded,stop_worked;public string source;}
  IEnumerator AutoCapture(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--capture");if(i>=0&&i+1<args.Length){yield return new WaitForSeconds(2);ScreenCapture.CaptureScreenshot(args[i+1]);}}
  void Update(){
   UpdateCameraInput();
   if(Input.GetKeyDown(KeyCode.Escape))StopMotion();
   if(UnifiedDirectorHost.IsBundled)return;
   for(int i=0;i<4;i++)if(Input.GetKeyDown(KeyCode.F1+i)){StopMotion();avatar.Act(localIds[i],localNames[i]);status="正在"+localNames[i]+"。";}
   if(Input.GetKeyDown(KeyCode.F5))PlaySample();
   if(Input.GetKeyDown(KeyCode.F6)){settings=!settings;cameraSettings=false;serviceFocusRequested=settings;}
   if(settings&&!textFieldFocused){if(Input.GetKeyDown(KeyCode.Alpha1))SelectBackend("windows");if(Input.GetKeyDown(KeyCode.Alpha2))SelectBackend("ubuntu");}
   if(Input.GetKeyDown(KeyCode.F7)&&clothing)clothing.SetSimulation(!clothing.simulationEnabled);
   if(Input.GetKeyDown(KeyCode.F8)){cameraSettings=!cameraSettings;settings=false;cameraFocusRequested=cameraSettings;}
   if(Input.GetKeyDown(KeyCode.F9))SetCameraPreset(0);
   if(Input.GetKeyDown(KeyCode.F10)&&face)face.ForceBlink();
   if(Input.GetKeyDown(KeyCode.F11)&&face)face.SetTalking(!face.Talking);
   if(Input.GetKeyDown(KeyCode.Return)&&(Input.GetKey(KeyCode.LeftCommand)||Input.GetKey(KeyCode.RightCommand)||Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl)))SendPrompt();
  }
  void LateUpdate(){
   // Positive pitch raises the camera; rotating +Z with positive Euler X lowers it.
   var hips=avatar.animator.GetBoneTransform(HumanBodyBones.Hips);
   var target=new Vector3(hips.position.x,0,hips.position.z)+cameraOffset;
   view.transform.position=target+Quaternion.Euler(-pitch,yaw,0)*new Vector3(0,0,distance);
   view.transform.LookAt(target);view.rect=UnifiedDirectorHost.IsBundled?UnifiedDirectorHost.StageViewport:new Rect(.34f,.085f,.66f,.81f);
  }
  Rect CameraPanel(float w){return new Rect(w-370,174,342,418);}
  bool InCameraArea(Vector3 pointer){
   if(!view.pixelRect.Contains(pointer))return false;
   float scale=Mathf.Min(Screen.width/1280f,Screen.height/800f),w=Screen.width/scale,h=Screen.height/scale;
   var point=new Vector2(pointer.x/scale,(Screen.height-pointer.y)/scale);
   if(new Rect(w-200,90,172,36).Contains(point))return false;
   if(cameraSettings&&CameraPanel(w).Contains(point))return false;
   if(settings&&ServicePanel(w,h).Contains(point))return false;
   return true;
  }
  void UpdateCameraInput(){
   if(UnifiedDirectorHost.IsBundled){
    var input=UnifiedDirectorHost.ConsumeCameraInput();if(DirectorCameraLocked)return;
    float dx=input[0]*UnifiedDirectorHost.PixelScale,dy=input[1]*UnifiedDirectorHost.PixelScale;
    if(input[3]>.5f){float step=2*distance*Mathf.Tan(view.fieldOfView*Mathf.Deg2Rad*.5f)/Mathf.Max(view.pixelHeight,1);cameraOffset+=(-view.transform.right*dx+view.transform.up*dy)*step;}
    else{float sensitivity=180f/Mathf.Max(view.pixelHeight,1);yaw=Mathf.Repeat(yaw-dx*sensitivity+180,360)-180;pitch=Mathf.Clamp(pitch+dy*sensitivity,-80,80);}
    distance=Mathf.Clamp(distance*Mathf.Exp(-input[2]*.03f),.35f,8);return;
   }
   if(DirectorCameraLocked){cameraDragButton=-1;return;}
   var pointer=Input.mousePosition;bool overView=InCameraArea(pointer);
   if(traceCamera)for(int i=0;i<3;i++)if(Input.GetMouseButtonDown(i)||Input.GetMouseButtonUp(i))Debug.Log("CAMERA_POINTER button="+i+" down="+Input.GetMouseButtonDown(i)+" position="+pointer+" overView="+overView+" yaw="+yaw+" pitch="+pitch+" offset="+cameraOffset+" distance="+distance);
   if(cameraDragButton<0&&overView){
    for(int i=0;i<3;i++)if(Input.GetMouseButtonDown(i)){
     cameraDragButton=i;cameraPan=i==2||Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
     previousPointer=pointer;break;
    }
   }
   if(cameraDragButton>=0){
    if(!Application.isFocused||!Input.GetMouseButton(cameraDragButton))cameraDragButton=-1;
    else{
     var delta=pointer-previousPointer;
     if(cameraPan){
      // Pan at the focus depth so the scene follows the pointer at any zoom level.
      cameraOffset+=view.ScreenToWorldPoint(new Vector3(previousPointer.x,previousPointer.y,distance))-view.ScreenToWorldPoint(new Vector3(pointer.x,pointer.y,distance));
     }else{float sensitivity=180f/Mathf.Max(view.pixelHeight,1);yaw=Mathf.Repeat(yaw-delta.x*sensitivity+180,360)-180;pitch=Mathf.Clamp(pitch-delta.y*sensitivity,-80,80);}
     previousPointer=pointer;
    }
   }
   if(overView)distance=Mathf.Clamp(distance*Mathf.Exp(-Input.mouseScrollDelta.y*.08f),.35f,8);
   if(cameraSettings&&!textFieldFocused){
    if(Input.GetKeyDown(KeyCode.Alpha1))SetCameraPreset(0);
    if(Input.GetKeyDown(KeyCode.Alpha2))SetCameraPreset(1);
    if(Input.GetKeyDown(KeyCode.Alpha3))SetCameraPreset(2);
    float horizontal=(Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.LeftArrow)?1:0);
    float vertical=(Input.GetKey(KeyCode.UpArrow)?1:0)-(Input.GetKey(KeyCode.DownArrow)?1:0);
    float step=Mathf.Min(Time.unscaledDeltaTime,.05f);
    if(Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift))cameraOffset+=(view.transform.right*horizontal+view.transform.up*vertical)*distance*.35f*step;
    else{yaw=Mathf.Repeat(yaw+horizontal*65*step+180,360)-180;pitch=Mathf.Clamp(pitch+vertical*65*step,-80,80);}
    float zoom=(Input.GetKey(KeyCode.Equals)||Input.GetKey(KeyCode.Plus)||Input.GetKey(KeyCode.KeypadPlus)?1:0)-(Input.GetKey(KeyCode.Minus)||Input.GetKey(KeyCode.KeypadMinus)?1:0);
    distance=Mathf.Clamp(distance*Mathf.Exp(-zoom*step),.35f,8);
   }
  }
  void SetCameraPreset(int preset){
   cameraDragButton=-1;yaw=0;
   var head=avatar.animator.GetBoneTransform(HumanBodyBones.Head);
   if(preset==1){pitch=3;distance=1.8f;cameraOffset=new Vector3(0,head.position.y-.16f,0);}
   else if(preset==2){pitch=0;distance=.72f;cameraOffset=new Vector3(0,head.position.y+.065f,0);}
   else{pitch=8;distance=3.4f;cameraOffset=new Vector3(0,.85f,0);}
  }
  void Styles(){if(label!=null)return;label=new GUIStyle(GUI.skin.label){font=font,fontSize=16,wordWrap=true};label.normal.textColor=ink;title=new GUIStyle(label){fontSize=30,fontStyle=FontStyle.Bold};small=new GUIStyle(label){fontSize=12};small.normal.textColor=muted;
   button=new GUIStyle(GUI.skin.button){font=font,fontSize=16,alignment=TextAnchor.MiddleCenter};button.normal.textColor=ink;button.hover.textColor=accent;
   input=new GUIStyle(GUI.skin.textArea){font=font,fontSize=17,wordWrap=true,padding=new RectOffset(14,14,12,12)};
  }
  void Box(Rect r,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
  void Text(float x,float y,float w,float h,string s,GUIStyle style=null){GUI.Label(new Rect(x,y,w,h),new GUIContent(s),style??label);}
  bool Button(Rect r,string s,bool primary=false){var old=GUI.backgroundColor;GUI.backgroundColor=primary?new Color(.21f,.62f,.50f):new Color(.20f,.26f,.31f);bool b=GUI.Button(r,new GUIContent(s),button);GUI.backgroundColor=old;return b;}
  void OnGUI(){if(UnifiedDirectorHost.IsBundled)return;Styles();float scale=Mathf.Min(Screen.width/1280f,Screen.height/800f);GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));float w=Screen.width/scale,h=Screen.height/scale;
   if(cameraFocusRequested){GUI.FocusControl(null);cameraFocusRequested=false;textFieldFocused=false;}
   if(serviceFocusRequested){GUI.FocusControl(null);serviceFocusRequested=false;textFieldFocused=false;}
   if(cameraSettings&&!textFieldFocused&&Event.current.type==EventType.KeyDown){
    var key=Event.current.keyCode;
    if(key==KeyCode.LeftArrow||key==KeyCode.RightArrow||key==KeyCode.UpArrow||key==KeyCode.DownArrow||key==KeyCode.Alpha1||key==KeyCode.Alpha2||key==KeyCode.Alpha3||key==KeyCode.Equals||key==KeyCode.Plus||key==KeyCode.Minus||key==KeyCode.KeypadPlus||key==KeyCode.KeypadMinus)Event.current.Use();
   }
   if(traceCamera&&(Event.current.type==EventType.MouseDown||Event.current.type==EventType.MouseUp))Debug.Log("CAMERA_GUI "+Event.current.type+" position="+Event.current.mousePosition+" screen="+Screen.width+"x"+Screen.height+" scale="+scale);
   Box(new Rect(0,0,w,76),new Color(.055f,.075f,.095f));Box(new Rect(0,76,w*.34f,h-76),new Color(.067f,.089f,.11f));Box(new Rect(w*.34f,h-64,w*.66f,64),new Color(.055f,.075f,.095f));
   Text(28,18,290,40,"YACHIYO   /   八千代",new GUIStyle(title){fontSize=23});if(!UnifiedDirectorHost.IsBundled)Text(350,28,450,24,"MOTION STUDIO     ·     实时互动原型",small);
   Text(w-230,26,210,30,avatar.Streaming?"●  实时动作中":busy?"●  正在准备动作":"●  可交互",new GUIStyle(label){normal={textColor=accent}});
   float panel=w*.34f-56;
   Text(28,108,panel,48,"一起动起来",title);Text(28,166,panel,42,"用一个动作，开启这次见面。",small);
   Text(28,206,panel,24,"想让八千代做什么？",label);
   GUI.SetNextControlName("motion-prompt");prompt=GUI.TextArea(new Rect(28,244,panel,88),prompt,500,input);
   if(Button(new Rect(28,346,panel,45),busy?"更新动作指令":"发送动作指令",true))SendPrompt();
   Text(28,414,panel,24,"本地动作预览",small);
   for(int i=0;i<4;i++){int col=i%2,row=i/2;if(Button(new Rect(28+col*(panel/2+5),450+row*48,panel/2-5,39),localNames[i]+"  F"+(i+1))){StopMotion();avatar.Act(localIds[i],localNames[i]);status="正在"+localNames[i]+"。";}}
   if(Button(new Rect(28,559,panel,34),"播放 Kimodo 官方样例  F5"))PlaySample();
   Text(28,597,panel,18,"表情",small);
   string[] faceNames={"平静","开心","惊讶","难过","思考"};
   float fw=(panel-16)/5f;
   for(int i=0;i<5;i++)if(Button(new Rect(28+i*(fw+4),616,fw,28),faceNames[i]))face.SetEmotion((FaceEmotion)i);
   if(Button(new Rect(28,650,panel/2-4,28),face!=null&&face.Talking?"停止说话":"说话  F11")){if(face)face.SetTalking(!face.Talking);}
   if(Button(new Rect(28+panel/2+4,650,panel/2-4,28),"眨眼  F10")){if(face)face.ForceBlink();}
   Text(28,682,panel,20,status,small);
   if(Button(new Rect(28,h-94,160,32),avatar.followPointer?"视线跟随：开":"视线跟随：关"))avatar.followPointer=!avatar.followPointer;
   if(Button(new Rect(203,h-94,panel-175,32),settings?"收起设置":"场景设置")){settings=!settings;cameraSettings=false;serviceFocusRequested=settings;}
   float right=w*(UnifiedDirectorHost.IsBundled?.52f:.34f)+28;
   Text(right,92,520,22,"TSUKUYOMI  /  月见一万代  ·  "+BackendLabel+" 动作服务",small);
   if(clothing)Text(right,119,300,22,clothing.simulationEnabled?"衣袖 · 裙摆   /   Unity Cloth 已开启":"实时布料已关闭  /  F7 开启",small);
   Text(right,h-56,440,26,avatar.CurrentAction+"  ·  "+avatar.MotionSource,small);
   if(Button(new Rect(w-150,h-53,120,34),"停止  ESC"))StopMotion();
   if(Button(new Rect(w-200,90,172,36),cameraSettings?"收起视角  F8":"调整视角  F8")){cameraSettings=!cameraSettings;settings=false;cameraFocusRequested=cameraSettings;}
   Text(right,145,w*.66f-56,24,"拖动旋转  ·  Shift + 拖动平移  ·  滚轮缩放  ·  F9 复位",small);
   if(avatar.Playing){Box(new Rect(right,h-76,w*.66f-56,3),new Color(.16f,.23f,.26f));Box(new Rect(right,h-76,(w*.66f-56)*avatar.Progress,3),accent);}
   if(settings)DrawServiceSettings(w,h);
   if(cameraSettings)DrawCameraPanel(w);
   textFieldFocused=GUI.GetNameOfFocusedControl()=="motion-prompt"||GUI.GetNameOfFocusedControl()=="service-url";
  }
  void DrawCameraPanel(float w){
   var r=CameraPanel(w);float x=r.x,y=r.y;Box(r,new Color(.085f,.12f,.15f,.98f));
   Text(x+16,y+12,310,28,"自由视角",label);
   string[] presets={"全身","半身","面部"};for(int i=0;i<3;i++)if(Button(new Rect(x+16+i*105,y+48,99,32),presets[i]))SetCameraPreset(i);
   cameraOffset.y=CameraSlider(x,y+96,"取景高度",cameraOffset.y,-.5f,3.5f,cameraOffset.y.ToString("0.00")+" m");
   pitch=CameraSlider(x,y+146,"俯仰角度",pitch,-80,80,Mathf.RoundToInt(pitch)+"°");
   yaw=CameraSlider(x,y+196,"环绕角度",yaw,-180,180,Mathf.RoundToInt(yaw)+"°");
   distance=CameraSlider(x,y+246,"镜头距离",distance,.35f,8,distance.ToString("0.00")+" m");
   if(Button(new Rect(x+16,y+310,310,34),"复位到默认视角  F9"))SetCameraPreset(0);
   Text(x+16,y+357,310,50,"方向键旋转 · Shift + 方向键平移\n+ / − 缩放 · 1 / 2 / 3 切换预设",small);
  }
  float CameraSlider(float x,float y,string name,float value,float min,float max,string formatted){
   Text(x+16,y,160,23,name,small);Text(x+240,y,86,23,formatted,small);
   return GUI.HorizontalSlider(new Rect(x+16,y+28,310,20),value,min,max);
  }
  public bool DirectorCameraLocked {get;set;}
  public void ResetDirectorCamera()=>SetCameraPreset(0);
  public void ReconnectDirectorService()=>StartCoroutine(ConnectSelectedService());
  public event Action LocalStopRequested;
  public bool MotionRequestBusy=>busy;
  public string MotionStatus=>status;
  public void StopMotion(bool fromDirector=false){if(!fromDirector)LocalStopRequested?.Invoke();CloseStream();requestVersion++;busy=false;avatar.Act("idle","待机");status="已停止，正在等你。";}
  void PlaySample(){CloseStream();requestVersion++;busy=false;try{var clip=JsonUtility.FromJson<MotionClip>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Motions","kimodo-official.json")));avatar.Play(clip,"官方动作样例");status="正在播放官方随仓库提供的动作，非本次生成。";}catch(Exception e){status="样例加载失败："+e.Message;}}
  public void RequestMotion(string text){prompt=text;SendPrompt();}
  void SendPrompt(){if(string.IsNullOrWhiteSpace(prompt))return;string s=prompt.Trim();if(s=="停止"){StopMotion();return;}
   requestVersion++;avatar.DiscardFuture();streamSequence=avatar.Streaming?avatar.CurrentSequence:0;
   StartCoroutine(RouteMotion(s,requestVersion));
  }
  IEnumerator RouteMotion(string text,int version){
   if(!ValidUrl())yield break;busy=true;status="正在连接动作服务…";
   using(var req=UnityWebRequest.Get(serviceUrl+(hostedArdy?"/ardy":"")+"/health")){req.timeout=8;yield return req.SendWebRequest();if(version!=requestVersion)yield break;
    if(req.result!=UnityWebRequest.Result.Success){ServiceConnected=false;serviceStatus=BackendLabel+" 连接中断，请检查系统和动作服务。";Fail(serviceStatus,version);yield break;}
    var health=JsonUtility.FromJson<ServiceHealth>(req.downloadHandler.text);
    if(health.streaming){yield return StreamMotion(text,version);yield break;}
   }
   yield return Generate(text,version,hostedArdy?serviceUrl+"/ardy":serviceUrl);
  }
  UnityWebRequest Post(string url,string json){var req=new UnityWebRequest(url,"POST");req.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));req.downloadHandler=new DownloadHandlerBuffer();req.SetRequestHeader("Content-Type","application/json");req.timeout=180;return req;}
  void CloseStream(){if(!string.IsNullOrEmpty(streamSession))StartCoroutine(DeleteStream(streamEndpoint,streamSession));streamSession=null;streamSequence=0;}
  IEnumerator DeleteStream(string endpoint,string session){using(var req=UnityWebRequest.Delete(endpoint+"/streams/"+session)){req.timeout=5;yield return req.SendWebRequest();}}
  IEnumerator StreamMotion(string text,int version){
   string endpoint=serviceUrl;
   if(string.IsNullOrEmpty(streamSession)||streamEndpoint!=endpoint){
    CloseStream();
    using(var req=Post(endpoint+"/streams","{\"seed\":37}")){yield return req.SendWebRequest();
     if(req.result!=UnityWebRequest.Result.Success){Fail("实时动作会话创建失败。",version);yield break;}
     var session=JsonUtility.FromJson<MotionSession>(req.downloadHandler.text);
     if(version!=requestVersion){yield return DeleteStream(endpoint,session.session);yield break;}
     streamSession=session.session;streamEndpoint=endpoint;streamSequence=0;
    }
   }
   string sessionId=streamSession;bool first=true;
   while(version==requestVersion&&streamSession==sessionId){
    if(!first&&avatar.BufferedSeconds>.65f){yield return null;continue;}first=false;
    var data=new StreamRequest{prompt=text,sequence=streamSequence,revision=version};
    using(var req=Post(endpoint+"/streams/"+sessionId+"/step",JsonUtility.ToJson(data))){
     status=avatar.Streaming?"正在连续生成："+text:"正在准备首段动作，首次启动需要加载模型…";
     yield return req.SendWebRequest();if(version!=requestVersion)yield break;
     if(req.result!=UnityWebRequest.Result.Success){CloseStream();avatar.Act("idle","待机");Fail("实时动作连接中断，请检查笔记本后重新发送。",version);yield break;}
     try{var motion=JsonUtility.FromJson<MotionClip>(req.downloadHandler.text);if(motion.revision!=version)yield break;
      avatar.Enqueue(motion,text);streamSequence=motion.sequence;RemoteChunks++;LastGenerationSeconds=motion.generationSeconds;
      busy=false;status="连续生成中 · 每段 2 秒 · 本次 "+Mathf.RoundToInt(motion.generationSeconds*1000)+" 毫秒";
     }catch(Exception error){CloseStream();avatar.Act("idle","待机");Fail("动作数据无效："+error.Message,version);yield break;}
    }
   }
  }
  bool ValidUrl(){if(Uri.TryCreate(serviceUrl,UriKind.Absolute,out var uri)&&(uri.Scheme=="http"||uri.Scheme=="https")){serviceUrl=serviceUrl.TrimEnd('/');return true;}status="请输入有效的动作服务地址。";return false;}
  IEnumerator Generate(string text,int version,string overrideEndpoint=null){if(!ValidUrl())yield break;string endpoint=overrideEndpoint??serviceUrl;busy=true;status="正在请求动作模型…";
   var data=new GenerationRequest{prompt=text};GenerationJob job;
   using(var req=new UnityWebRequest(endpoint+"/jobs","POST")){req.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));req.downloadHandler=new DownloadHandlerBuffer();req.SetRequestHeader("Content-Type","application/json");req.timeout=10;yield return req.SendWebRequest();if(version!=requestVersion)yield break;if(req.result!=UnityWebRequest.Result.Success){string message="动作服务尚未就绪，请在连接设置中检查地址。";if(req.responseCode==400)message="请使用英文动作描述，或中文预设：跳舞、伸懒腰、向前走。";if(req.responseCode==429)message="动作服务忙碌，请稍后再试。";Fail(message,version);yield break;}job=JsonUtility.FromJson<GenerationJob>(req.downloadHandler.text);}
   float deadline=Time.realtimeSinceStartup+600;
   while(version==requestVersion&&Time.realtimeSinceStartup<deadline){
    using(var req=UnityWebRequest.Get(endpoint+"/jobs/"+job.id)){req.timeout=10;yield return req.SendWebRequest();if(version!=requestVersion)yield break;if(req.result!=UnityWebRequest.Result.Success){Fail("动作服务连接中断。",version);yield break;}job=JsonUtility.FromJson<GenerationJob>(req.downloadHandler.text);}
    if(job.status=="failed"){Fail(job.error,version);yield break;}
    if(job.status=="done")break;
    status=job.status=="running"?"正在生成动作…":"动作已排队…";yield return new WaitForSeconds(.4f);
   }
   if(version!=requestVersion)yield break;if(job.status!="done"){Fail("动作生成超时，请检查服务。",version);yield break;}
   using(var req=UnityWebRequest.Get(endpoint+"/jobs/"+job.id+"/motion")){req.timeout=30;yield return req.SendWebRequest();if(version!=requestVersion)yield break;if(req.result!=UnityWebRequest.Result.Success){Fail("动作下载失败。",version);yield break;}
    try{var clip=JsonUtility.FromJson<MotionClip>(req.downloadHandler.text);if(job.cached)clip.source+=" · 缓存";avatar.Play(clip,text);status="动作已就绪"+(job.cached?"（缓存）":"")+"。";busy=false;}catch(Exception e){Fail("动作数据无效："+e.Message,version);}}
  }
  void Fail(string message,int version){if(version!=requestVersion)return;busy=false;status=message;}
 }
}
