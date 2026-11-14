using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Yachiyo {
 // Network tasks only move UTF-8 messages. All avatar/Unity API calls run in Update.
 public sealed partial class DirectorBridge : MonoBehaviour {
  public StudioUI ui;public AvatarMotion avatar;public YachiyoFaceController face;
  public string endpoint="ws://127.0.0.1:3289/avatar?role=unity";
  public string Status {get;private set;}="导演台未连接";
  public bool Connected=>transportConnected;
  public int ReceivedPerformances {get;private set;}
  public int ReceivedMouthFrames {get;private set;}
  public string LastReceipt {get;private set;}="";
  public string ActiveUtterance=>utterance;
  readonly ConcurrentQueue<string> incoming=new ConcurrentQueue<string>(),outgoing=new ConcurrentQueue<string>();
  readonly SemaphoreSlim outgoingSignal=new SemaphoreSlim(0);
  readonly Queue<Message> performances=new Queue<Message>();
  readonly HashSet<string> seen=new HashSet<string>();
  readonly Dictionary<Renderer,bool> hiddenRenderers=new Dictionary<Renderer,bool>();
  readonly string[] actions={"wave","bow","nod","turn","shake_head","blink","smile","smirk","look_at_chat","look_away","walk","clap","dance","explain","open_palms","emphasis","lean_in","lean_left","lean_right","sway","breathe"};
  CancellationTokenSource lifetime;Task network;volatile bool transportConnected;bool wasConnected;
  string session="",utterance="";int epoch=-1,generation;long lastSeq;int mouthClock=-1;
  Message active;Coroutine execution;float emotionUntil,gazeUntil;Transform gaze;
  bool showSettings;string editEndpoint;Font font;int cameraRevision=-1;float nextCameraStatus;
  [Serializable]public class Message {
   public string type,kind,sessionId,commandId,turnId,utteranceId,state,reason;public int epoch;public long seq;public Payload payload;
  }
  [Serializable]public class Payload {
   public MotionClip motion;public bool neuralMotion,continueMotion;
   public PerformancePlan performance;
   public string emotion,mode,interruptPolicy,@event,source,bodyMode,action,text;public float intensity,durationMs,holdMs,value,a,i,u,e,o;public int priority,audioTimeMs;public bool silence;public Behavior[] actions;
  }
  [Serializable]public class Behavior {public string type,side,target;public float durationMs,delayMs,intensity;}
  [Serializable]class Ready {public string type="ready";public Capabilities capabilities;}
  [Serializable]class Capabilities {public string protocol="yachiyo-avatar/1";public bool humanoid,audioVisemes,serviceConnected;public int shapeCount;public string selectedBackend;public float cameraX,cameraY,cameraDistance,cameraYaw,cameraPitch;public bool cameraLocked;public string[] actions,emotions;public string motionBackend="model: wave/bow/turn/walk/clap/dance via existing Windows/Ubuntu service; local: explicit preview; explicit AI nod/shake/arm cues use local gestures; speech: StreamTalk audio-conditioned neural motion with media clock";}

  void Start(){
   avatar.followPointer=false;
   font=Resources.Load<Font>("NotoSansCJKsc-Regular");
   endpoint=PlayerPrefs.GetString("yachiyo.director.endpoint",endpoint);
   var args=Environment.GetCommandLineArgs();int p=Array.IndexOf(args,"--director-url");if(p>=0&&p+1<args.Length)endpoint=args[p+1];
   editEndpoint=endpoint;ui.LocalStopRequested+=LocalStop;Connect();
   if(Array.IndexOf(args,"--verify-director")>=0)gameObject.AddComponent<DirectorVerification>().bridge=this;
  }
  bool ValidEndpoint(string text)=>Uri.TryCreate(text,UriKind.Absolute,out var uri)&&uri.Scheme=="ws"&&(uri.Host=="127.0.0.1"||uri.Host=="localhost")&&uri.AbsolutePath=="/avatar"&&uri.Query=="?role=unity";
  void Connect(){
   lifetime?.Cancel();Clear("reconnect");
   if(!ValidEndpoint(endpoint)){Status="地址须为本机 ws://127.0.0.1:端口/avatar?role=unity";return;}
   lifetime=new CancellationTokenSource();var stop=lifetime.Token;network=Task.Run(()=>Run(stop));
  }
  async Task Run(CancellationToken stop){
   while(!stop.IsCancellationRequested){
    using(var ws=new ClientWebSocket())using(var link=CancellationTokenSource.CreateLinkedTokenSource(stop)){
     try{
      await ws.ConnectAsync(new Uri(endpoint),stop);
      while(outgoing.TryDequeue(out _)){}transportConnected=true;
      var read=Read(ws,link.Token);var write=Write(ws,link.Token);
      await Task.WhenAny(read,write);link.Cancel();ws.Abort();
      try{await Task.WhenAll(read,write);}catch(OperationCanceledException){}catch(WebSocketException){}
     }catch(OperationCanceledException){}catch(Exception){/* Reconnect without logging endpoints or audio metadata. */}
     finally{transportConnected=false;}
    }
    try{await Task.Delay(1500,stop);}catch(OperationCanceledException){}
   }
  }
  async Task Read(ClientWebSocket ws,CancellationToken stop){
   byte[] buffer=new byte[8192];
   while(ws.State==WebSocketState.Open&&!stop.IsCancellationRequested){
    using(var stream=new System.IO.MemoryStream()){
     WebSocketReceiveResult part;
     do{part=await ws.ReceiveAsync(new ArraySegment<byte>(buffer),stop);if(part.MessageType==WebSocketMessageType.Close)return;if(part.MessageType!=WebSocketMessageType.Text)throw new FormatException("JSON text required");stream.Write(buffer,0,part.Count);if(stream.Length>65536)throw new FormatException("Frame too large");}while(!part.EndOfMessage);
     if(incoming.Count>=256)throw new FormatException("Receiver queue full");incoming.Enqueue(Encoding.UTF8.GetString(stream.ToArray()));
    }
   }
  }
  async Task Write(ClientWebSocket ws,CancellationToken stop){
   while(!stop.IsCancellationRequested){await outgoingSignal.WaitAsync(stop);while(outgoing.TryDequeue(out var text))await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)),WebSocketMessageType.Text,true,stop);}
  }
  void Send(object message){if(!transportConnected)return;outgoing.Enqueue(JsonUtility.ToJson(message));outgoingSignal.Release();}
  void LocalStop(){Send(new Message{type="interrupt"});Clear("local-stop");}
  void Update(){
   if(!UnifiedDirectorHost.IsBundled&&Input.GetKeyDown(KeyCode.F12))showSettings=!showSettings;
   if(transportConnected!=wasConnected){
    wasConnected=transportConnected;
    if(wasConnected){SendReady();Status="导演台已连接";}
    else{Clear("disconnected");Status="等待导演台 · F12 设置";}
   }
   for(int n=0;n<160&&incoming.TryDequeue(out var text);n++){
    try{var msg=JsonUtility.FromJson<Message>(text);if(msg.type=="ping"){Send(new Message{type="ping"});SendReady();continue;}Handle(msg);}catch(Exception e){Debug.LogWarning("DIRECTOR_FRAME_REJECTED "+e.GetType().Name);}
   }
   if(transportConnected&&ui.CameraRevision!=cameraRevision&&Time.unscaledTime>nextCameraStatus){cameraRevision=ui.CameraRevision;nextCameraStatus=Time.unscaledTime+.12f;SendReady();}
   if(emotionUntil>0&&Time.unscaledTime>emotionUntil){RestoreAffect();emotionUntil=0;}
   if(gazeUntil>0&&Time.unscaledTime>gazeUntil){face.SetLookTarget(null);gazeUntil=0;}
   UpdatePerformanceGaze();
   if(execution==null&&performances.Count>0){active=performances.Dequeue();execution=StartCoroutine(Execute(active,generation));}
  }
  public bool AwaitingContinuation {get;private set;}
  void Handle(Message msg){
   if(msg.type!="command")return;
   if(msg.kind=="cancel"){
    if(msg.sessionId==session&&msg.epoch<epoch)return;
    Clear("cancelled");session=msg.sessionId;epoch=msg.epoch;lastSeq=0;return;
   }
   if(msg.sessionId!=session||msg.epoch!=epoch||msg.seq<=lastSeq||msg.payload==null)return;
   lastSeq=msg.seq;var p=msg.payload;
   switch(msg.kind){
    case "control":
     if(p.action=="visibility"){
      if(p.value<.5f&&hiddenRenderers.Count==0){foreach(var r in avatar.GetComponentsInChildren<Renderer>()){hiddenRenderers[r]=r.enabled;r.enabled=false;}}
      else if(p.value>.5f){foreach(var pair in hiddenRenderers)if(pair.Key)pair.Key.enabled=pair.Value;hiddenRenderers.Clear();}
     }
     if(p.action=="camera_lock")ui.DirectorCameraLocked=p.value>.5f;
     if(p.action=="camera_reset")ui.ResetDirectorCamera();
     if(p.action=="camera_x")ui.SetDirectorFraming(p.value,ui.DirectorFraming.y);
     if(p.action=="camera_y")ui.SetDirectorFraming(ui.DirectorFraming.x,p.value);
     if(p.action=="camera_zoom")ui.ZoomDirectorCamera(p.value);
     if(p.action=="backend"&&StudioUI.IsMotionBackend(p.text))ui.SelectBackend(p.text);
     if(p.action=="reconnect")ui.ReconnectDirectorService();
     SendReady();
     break;
    case "perform":
     if(avatar.NeuralSpeechMotion&&p.actions!=null&&p.actions.Length>0){Receipt(msg,"unsupported","StreamTalk owns the body during speech");return;}
     if(string.IsNullOrEmpty(msg.commandId)||!seen.Add(msg.commandId))return;
     ReceivedPerformances++;
     if(seen.Count>1024)seen.Clear();
     if(p.actions!=null&&p.actions.Length>8){Receipt(msg,"failed","At most eight actions per performance");return;}
     int fullBody=0;if(p.actions!=null)foreach(var action in p.actions)if(IsRemote(action.type,p.bodyMode))fullBody++;
     if(fullBody>1){Receipt(msg,"unsupported","Split full-body model actions into separate performances");return;}
     if(p.actions!=null)foreach(var action in p.actions)if(Array.IndexOf(actions,action.type)<0){Receipt(msg,"unsupported","Unsupported action: "+action.type);return;}
     if(performances.Count>=16){Receipt(msg,"failed","Unity queue full");return;}
     if(active!=null&&(p.interruptPolicy=="replace"||p.priority>active.payload.priority))CancelPerformances("replaced");
     Receipt(msg,"accepted");performances.Enqueue(msg);break;
    case "state":
     avatar.SetCharacterState(p.mode,p.emotion,p.holdMs);
     if(active==null&&speechPlan==null){if(!string.IsNullOrEmpty(p.emotion))Emotion(p.emotion,.65f,p.holdMs);else if(p.mode=="idle"||p.mode=="listening"){RestoreAffect();emotionUntil=0;}}
     break;
    case "audio":
     if(p.@event=="start")AwaitingContinuation=false;
     else if(msg.utteranceId==utterance&&p.@event!="pause"&&p.@event!="resume")AwaitingContinuation=p.@event=="end"&&p.continueMotion;
     Debug.Log("DIRECTOR_AUDIO "+p.@event+" seq="+msg.seq+" current="+(msg.utteranceId==utterance));
     if(p.@event=="start"&&!string.IsNullOrEmpty(msg.utteranceId)){utterance=msg.utteranceId;mouthClock=-1;face.CloseAudioMouth();avatar.SpeechAudio(p.source=="speech");if(p.source=="speech"){if(p.neuralMotion){CancelPerformances("neural-speech",true);avatar.BeginSpeechMotion();}avatar.SetCharacterState("speaking",p.emotion,15000);StartPerformance(p.performance);}}
     else if(msg.utteranceId==utterance&&(p.@event=="pause"||p.@event=="resume")){face.CloseAudioMouth();avatar.SpeechAudio(p.@event=="resume");avatar.SpeechFrame(p.audioTimeMs/1000f,0);}
     else if(msg.utteranceId==utterance){CancelSpeechPerformances(utterance);utterance="";face.CloseAudioMouth();avatar.SpeechAudio(false);avatar.EndSpeechMotion();EndPerformance();}
     break;
    case "motion":
     if(string.IsNullOrEmpty(utterance)||msg.utteranceId!=utterance||p.motion==null)return;
     avatar.AddSpeechMotion(p.motion);break;
    case "mouth":
     if(string.IsNullOrEmpty(utterance)||msg.utteranceId!=utterance||p.audioTimeMs<mouthClock)return;
     mouthClock=p.audioTimeMs;avatar.SpeechFrame(p.audioTimeMs/1000f,p.value);AdvancePerformance(p.audioTimeMs);ReceivedMouthFrames++;face.SetAudioMouth(p.value,p.a,p.i,p.u,p.e,p.o,p.silence);break;
   }
  }
  IEnumerator Execute(Message msg,int version){
   var p=msg.payload;float duration=Mathf.Clamp(p.durationMs,260,15000)/1000;
   bool started=true;if(p.actions!=null)foreach(var a in p.actions)if(IsRemote(a.type,p.bodyMode))started=false;
   Emotion(p.emotion,p.intensity,duration*1000);if(started)Receipt(msg,"started");
   float began=Time.unscaledTime;bool body=false;
   if(p.actions!=null)foreach(var action in p.actions){
    float at=Mathf.Clamp(action.delayMs,0,12000)/1000;
    while(Time.unscaledTime-began<at){if(version!=generation)yield break;yield return null;}
    if(version!=generation)yield break;
    bool remote=IsRemote(action.type,p.bodyMode);
    if(remote){
     ui.PrepareNextMotion();int revision=avatar.MotionRevision;string prompt=action.type=="walk"?"A person walks forward slowly.":action.type=="clap"?"A person claps their hands.":action.type=="dance"?"A person dances gently in place.":action.type=="bow"?"A person bows politely.":action.type=="turn"?"A person turns around once in place.":"A person waves hello with their right hand.";
     ui.RequestMotion(prompt);float deadline=Time.unscaledTime+45;
     while(avatar.MotionRevision==revision&&Time.unscaledTime<deadline){if(version!=generation)yield break;yield return null;if(!ui.MotionRequestBusy&&avatar.MotionRevision==revision)break;}
     if(avatar.MotionRevision==revision){string reason=ui.MotionStatus;ui.StopMotion(true);Receipt(msg,"failed","Motion service did not start: "+reason);Finish();yield break;}
     if(!started){Receipt(msg,"started");started=true;}
     body=true;began=Time.unscaledTime;
    }else switch(action.type){
     case "wave":case "bow":case "turn":ui.CancelMotionRequest();avatar.Act(action.type,action.type);body=true;break;
     case "nod":case "shake_head":case "explain":case "open_palms":case "emphasis":case "lean_in":case "lean_left":case "lean_right":case "sway":case "breathe":
      avatar.Gesture(action.type,Mathf.Clamp(action.durationMs>0?action.durationMs:duration*1000,350,15000)/1000,action.intensity>0?action.intensity:Mathf.Max(.4f,p.intensity),action.side);break;
     case "blink":face.ForceBlink();break;
     case "smile":case "smirk":Emotion("happy",Mathf.Max(.5f,action.intensity),duration*1000);break;
     case "look_at_chat":face.SetLookTarget(ui.view.transform);gazeUntil=Time.unscaledTime+duration;break;
     case "look_away":
      if(!gaze)gaze=new GameObject("Director eye target").transform;
      gaze.position=ui.view.transform.position+ui.view.transform.right*(action.side=="left"?-1:1);face.SetLookTarget(gaze);gazeUntil=Time.unscaledTime+duration;break;
    }
   }
   while(Time.unscaledTime-began<duration){if(version!=generation)yield break;yield return null;}
   if(body)ui.FinishMotion();
   Receipt(msg,"completed");Finish();
  }
  void Finish(){active=null;execution=null;}
  static bool IsRemote(string type,string mode)=>type=="walk"||type=="clap"||type=="dance"||(mode=="model"&&(type=="wave"||type=="bow"||type=="turn"));
  void SendReady(){face.Initialize();Send(new Ready{capabilities=new Capabilities{humanoid=avatar.animator&&avatar.animator.avatar.isHuman,audioVisemes=face.HasAudioVisemes,shapeCount=face.BoundShapeCount,actions=actions,selectedBackend=ui.SelectedBackend,serviceConnected=ui.ServiceConnected,cameraX=ui.DirectorFraming.x,cameraY=ui.DirectorFraming.y,cameraDistance=ui.DirectorCameraDistance,cameraYaw=ui.DirectorCameraYaw,cameraPitch=ui.DirectorCameraPitch,cameraLocked=ui.DirectorCameraLocked,emotions=new[]{"neutral","happy","surprised","sad","thinking","angry"}}});}
  void Emotion(string name,float amount,float duration){
   avatar.SetConversationEmotion(name);
   string n=(name??"").ToLowerInvariant();FaceEmotion e=FaceEmotion.Neutral;
   if(n.Contains("happy")||n.Contains("smil")||n.Contains("joy")||n.Contains("smirk")||n.Contains("excited"))e=FaceEmotion.Happy;
   else if(n=="concerned_warm")e=FaceEmotion.ConcernedWarm;
   else if(n=="warm")e=FaceEmotion.Warm;
   else if(n=="shy")e=FaceEmotion.Shy;
   else if(n=="playful")e=FaceEmotion.Playful;
   else if(n=="serious")e=FaceEmotion.Serious;
   else if(n.Contains("surpris")||n.Contains("shock"))e=FaceEmotion.Surprised;
   else if(n.Contains("sad")||n.Contains("cry")||n.Contains("worried"))e=FaceEmotion.Sad;
   else if(n.Contains("think")||n.Contains("curious"))e=FaceEmotion.Thinking;
   else if(n.Contains("angry")||n.Contains("annoy"))e=FaceEmotion.Angry;
   face.SetEmotion(e,Mathf.Clamp01(amount));emotionUntil=Time.unscaledTime+Mathf.Clamp(duration/1000,.26f,15);
  }
  void Receipt(Message msg,string state,string reason=""){
   LastReceipt=state;Send(new Message{type="receipt",sessionId=msg.sessionId,epoch=msg.epoch,commandId=msg.commandId,state=state,reason=reason});
   Debug.Log("DIRECTOR_RECEIPT "+state+(string.IsNullOrEmpty(reason)?"":" "+reason));
  }
  void CancelSpeechPerformances(string id){
   if(string.IsNullOrEmpty(id))return;
   if(active!=null&&active.utteranceId==id){generation++;if(execution!=null)StopCoroutine(execution);execution=null;Receipt(active,"cancelled","speech-ended");active=null;ui.StopMotion(true);}
   int count=performances.Count;
   for(int i=0;i<count;i++){var queued=performances.Dequeue();if(queued.utteranceId==id)Receipt(queued,"cancelled","speech-ended");else performances.Enqueue(queued);}
   avatar.EndConversationGesture();
  }
  void CancelPerformances(string reason,bool preserveMotion=false){
   generation++;if(execution!=null)StopCoroutine(execution);execution=null;
   if(active!=null)Receipt(active,"cancelled",reason);active=null;
   while(performances.Count>0)Receipt(performances.Dequeue(),"cancelled",reason);
   if(avatar)avatar.EndConversationGesture();
   if(ui&&avatar){if(preserveMotion)ui.CancelMotionRequest();else ui.StopMotion(true);}
  }
  void Clear(string reason){AwaitingContinuation=false;CancelPerformances(reason);if(avatar)avatar.ResetConversation();utterance="";mouthClock=-1;seen.Clear();emotionUntil=0;gazeUntil=0;if(face){face.CloseAudioMouth();EndPerformance();face.SetLookTarget(null);}}
  void OnDestroy(){if(ui)ui.LocalStopRequested-=LocalStop;lifetime?.Cancel();Clear("destroyed");if(gaze)Destroy(gaze.gameObject);}
  void OnGUI(){
   if(!font)return;float scale=Mathf.Min(Screen.width/1280f,Screen.height/800f);var old=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
   var style=new GUIStyle(GUI.skin.label){font=font,fontSize=12,normal={textColor=Connected?new Color(.45f,.88f,.76f):Color.gray}};
   var buttonStyle=new GUIStyle(GUI.skin.button){font=font,fontSize=12};
   if(UnifiedDirectorHost.IsBundled)GUI.Label(new Rect(350,51,500,22),Status,style);else if(GUI.Button(new Rect(350,51,500,22),Status,buttonStyle))showSettings=!showSettings;
   if(showSettings){GUI.Box(new Rect(410,140,650,180),"");GUI.Label(new Rect(430,155,600,28),"直播导演台 · 本机控制通道（与动作服务独立）",style);editEndpoint=GUI.TextField(new Rect(430,198,600,30),editEndpoint);if(GUI.Button(new Rect(430,244,180,32),"保存并重新连接")){endpoint=editEndpoint.Trim();if(ValidEndpoint(endpoint)){PlayerPrefs.SetString("yachiyo.director.endpoint",endpoint);PlayerPrefs.Save();Connect();showSettings=false;}else Status="仅支持本机导演台地址";}GUI.Label(new Rect(625,247,400,32),"开发模式端口 5174，桌面模式 3289",style);}
   GUI.matrix=old;
  }
 }
}
