using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yachiyo {
 [DefaultExecutionOrder(-100)]
 public partial class AvatarMotion : MonoBehaviour {
  public Animator animator;
  public bool followPointer=false;
  public string CurrentAction {get;private set;}="待机";
  public string MotionSource {get;private set;}="本地动作";
  public float Progress => clip==null?0:Mathf.Clamp01(clock/Mathf.Max(0.01f,clip.Duration));
  public bool Playing => clip!=null;
  public bool LocalActionActive=>(clip==null&&localAction!="idle")||ConversationGestureActive;
  public bool Streaming {get;private set;}
  public int CurrentSequence=>clip==null?0:clip.sequence;
  public float BufferedSeconds {get{float seconds=clip==null?0:Mathf.Max(0,clip.frames.Length/clip.fps-clock);foreach(var c in streamQueue)seconds+=c.frames.Length/c.fps;return seconds;}}
  public int StreamChunks {get;private set;}
  public int StreamUnderruns {get;private set;}
  readonly Queue<MotionClip> streamQueue=new Queue<MotionClip>();
  public bool NeuralSpeechMotion {get;private set;}
  public int SpeechMotionFrames {get;private set;}
  public int SpeechMotionUnderruns {get;private set;}
  public float SpeechMotionClock=>NeuralSpeechMotion?speechClock:0;
  public float SpeechRenderClock {get;private set;}
  float speechChunkStart;bool speechUnderrun;
  public float expression;
  YachiyoFaceController faceController;
  readonly Dictionary<HumanBodyBones,Transform> bones=new Dictionary<HumanBodyBones,Transform>();
  readonly Dictionary<HumanBodyBones,Quaternion> restLocal=new Dictionary<HumanBodyBones,Quaternion>();
  readonly Dictionary<HumanBodyBones,Quaternion> restGlobal=new Dictionary<HumanBodyBones,Quaternion>();
  readonly Dictionary<HumanBodyBones,Quaternion> transition=new Dictionary<HumanBodyBones,Quaternion>();
  SkinnedMeshRenderer[] meshes;
  readonly List<(SkinnedMeshRenderer mesh,int index)> blink=new List<(SkinnedMeshRenderer,int)>();
  readonly List<(SkinnedMeshRenderer mesh,int index)> smile=new List<(SkinnedMeshRenderer,int)>();
  MotionClip clip;
  HumanBodyBones[] mapping;
  Vector3 hipsRest, startRoot, transitionHips;
  float hipHeight, clock, blendClock=1, actionTime, nextBlink=3, blinkClock=-1;
  string localAction="idle";
  string stateMode="idle",stateEmotion="";
  float stateUntil,stateClock;
  bool initialized;
  public void Initialize() {
   if(initialized)return;
   animator=GetComponent<Animator>();
   if(!animator||!animator.avatar||!animator.avatar.isHuman)throw new InvalidOperationException("Humanoid Avatar required");
   animator.enabled=false;
   for(int i=0;i<(int)HumanBodyBones.LastBone;i++) {
    var b=(HumanBodyBones)i;var t=animator.GetBoneTransform(b);if(!t)continue;
    bones[b]=t;restLocal[b]=t.localRotation;restGlobal[b]=Quaternion.Inverse(transform.rotation)*t.rotation;
   }
   hipsRest=bones[HumanBodyBones.Hips].localPosition;
   hipHeight=transform.InverseTransformPoint(bones[HumanBodyBones.Hips].position).y;
   meshes=GetComponentsInChildren<SkinnedMeshRenderer>();
   foreach(var m in meshes){
    bool nativeBlink=false;
    foreach(string name in new[]{"_101_FC_eye_close_left","_101_FC_eye_close_right","_101_EL_eye_close_left","_101_EL_eye_close_right"}){int index=m.sharedMesh.GetBlendShapeIndex(name);if(index>=0){blink.Add((m,index));nativeBlink=true;}}
    if(!nativeBlink){
     int combined=m.sharedMesh.GetBlendShapeIndex("まばたき");
     if(combined>=0)blink.Add((m,combined));
     else foreach(string name in new[]{"EyeClosedLeft","EyeClosedRight"}){int index=m.sharedMesh.GetBlendShapeIndex(name);if(index>=0)blink.Add((m,index));}
    }
    int nativeSmile=m.sharedMesh.GetBlendShapeIndex("_209_FC_mouth_smile");
    if(nativeSmile>=0)smile.Add((m,nativeSmile));
    else foreach(string name in new[]{"MouthSmileLeft","MouthSmileRight"}){int index=m.sharedMesh.GetBlendShapeIndex(name);if(index>=0)smile.Add((m,index));}
   }
   InitializeIdle();initialized=true;
  }
  void Start()=>Initialize();
  public void SetCharacterState(string mode,string emotion,float holdMs){
   Initialize();string m=(mode??"").ToLowerInvariant();if(m=="acting")m="speaking";
   if(m!=stateMode){stateMode=m;stateClock=0f;}
   SetConversationEmotion(emotion);stateUntil=Time.unscaledTime+Mathf.Clamp(holdMs/1000f,1.2f,30f);
  }
  public string CharacterState=>stateMode;
  public Quaternion RestBoneRotation(HumanBodyBones bone){Initialize();return restLocal.TryGetValue(bone,out var q)?q:Quaternion.identity;}
  public void Play(MotionClip motion,string label) {
   NeuralSpeechMotion=false;
   Streaming=false;streamQueue.Clear();Initialize(); motion.Validate();CaptureTransition();
   // An idle weight shift is not locomotion. Anchor the new clip to the planted
   // stance so repeated idle/action cycles cannot accumulate that small offset.
   var origin=clip!=null?transform.InverseTransformPoint(bones[HumanBodyBones.Hips].position):restRoot+idleOrigin;
   motionOrigin=new Vector3(origin.x,0,origin.z);
   HoldingMotion=false;MotionRevision++;clip=motion;clock=0;CurrentAction=label;MotionSource=motion.source;
   mapping=new HumanBodyBones[motion.bones.Length];for(int i=0;i<mapping.Length;i++) mapping[i]=Enum.TryParse(motion.bones[i],out HumanBodyBones b)?b:HumanBodyBones.LastBone;
   startRoot=motion.frames[0].root;
  }
  public void Enqueue(MotionClip motion,string label){
   motion.Validate();
   if(!Streaming){Play(motion,label);Streaming=true;StreamChunks=1;StreamUnderruns=0;}
   else {if(motion.bones.Length!=clip.bones.Length)throw new ArgumentException("Stream skeleton changed");for(int i=0;i<motion.bones.Length;i++)if(motion.bones[i]!=clip.bones[i])throw new ArgumentException("Stream bone order changed");streamQueue.Enqueue(motion);StreamChunks++;}
  }
  public void DiscardFuture()=>streamQueue.Clear();
  public void BeginSpeechMotion(){
   HoldForNextMotion(15);NeuralSpeechMotion=true;SpeechMotionFrames=0;SpeechMotionUnderruns=0;SpeechRenderClock=0;speechChunkStart=0;speechUnderrun=false;
  }
  public void AddSpeechMotion(MotionClip motion){
   if(!NeuralSpeechMotion)return;
   motion.Validate();
   if(motion.startFrame!=SpeechMotionFrames||SpeechMotionFrames+motion.frames.Length>1800||Mathf.Abs(motion.fps-30)>.001f)throw new ArgumentException("Invalid speech frame sequence");
   if(clip==null){
    Play(motion,"StreamTalk 语音动作");NeuralSpeechMotion=true;Streaming=true;speechChunkStart=motion.startFrame/30f;
   }else{
    if(motion.bones.Length!=clip.bones.Length)throw new ArgumentException("Speech skeleton changed");
    for(int i=0;i<motion.bones.Length;i++)if(motion.bones[i]!=clip.bones[i])throw new ArgumentException("Speech bone order changed");
    streamQueue.Enqueue(motion);
   }
   SpeechMotionFrames+=motion.frames.Length;
  }
  public void EndSpeechMotion(bool continuation=false){if(NeuralSpeechMotion)HoldForNextMotion(continuation?12:.65f);}
  public void Act(string action,string label) {Initialize();if(clip!=null)KeepRootOrigin();CaptureTransition(action=="idle"?1.15f:.5f);HoldingMotion=false;NeuralSpeechMotion=false;Streaming=false;streamQueue.Clear();clip=null;localAction=action;actionTime=0;CurrentAction=label;MotionSource="本地动作";}
  public void Tick(float dt) {
   Initialize();actionTime+=dt;blendClock+=dt;AdvanceLife(dt);
   if(HoldingMotion){if(holdClock>=holdLimit)Act("idle","待机");else{ApplyHeldPose(dt);RememberPose(dt);return;}}
   foreach(var p in bones)p.Value.localRotation=restLocal[p.Key];
   bones[HumanBodyBones.Hips].localPosition=hipsRest;
   bones[HumanBodyBones.Hips].position+=transform.TransformVector(idleOrigin);
   if(clip!=null) {
    if(NeuralSpeechMotion){
     // Web audio reports at about 30 Hz. Interpolate the already generated
     // poses at render cadence between reports instead of holding alternate
     // display frames. Bound prediction to 50 ms; stale audio stops advancing.
     float predicted=speechClock+(speechActive?Mathf.Clamp(Time.unscaledTime-speechAt,0,.05f):0);
     if(speechActive)SpeechRenderClock=Mathf.Max(SpeechRenderClock,predicted);
     float renderTime=Mathf.Min(SpeechRenderClock,Mathf.Max(0,(SpeechMotionFrames-1)/30f));
     clock=Mathf.Max(0,renderTime-speechChunkStart);
     while(streamQueue.Count>0&&clock>=clip.frames.Length/clip.fps){
      speechChunkStart+=clip.frames.Length/clip.fps;clip=streamQueue.Dequeue();clock=Mathf.Max(0,renderTime-speechChunkStart);
     }
     bool underrun=speechClock>SpeechMotionFrames/30f+.05f;
     if(underrun&&!speechUnderrun)SpeechMotionUnderruns++;speechUnderrun=underrun;
    }else clock+=dt;
    if(!NeuralSpeechMotion&&Streaming&&clock>=clip.frames.Length/clip.fps){
     if(streamQueue.Count>0){clock-=clip.frames.Length/clip.fps;clip=streamQueue.Dequeue();CurrentAction=clip.prompt;MotionSource=clip.source;}
     else {if(clock-dt<clip.frames.Length/clip.fps)StreamUnderruns++;clock=Mathf.Min(clock,clip.frames.Length/clip.fps);}
    }
    float pos=Mathf.Clamp(clock*clip.fps,0,clip.frames.Length-1);int a=(int)pos,b=Mathf.Min(a+1,clip.frames.Length-1);float mix=pos-a;
    var first=clip.frames[a];var second=clip.frames[b];
    if(Streaming&&streamQueue.Count>0&&clock>=clip.Duration){first=clip.frames[clip.frames.Length-1];second=streamQueue.Peek().frames[0];mix=Mathf.Clamp01((clock-clip.Duration)*clip.fps);}
    // Global T-pose deltas avoid assuming that SOMA and FBX local bone axes match.
    for(int j=0;j<mapping.Length;j++)if(bones.TryGetValue(mapping[j],out var t)) {
     var q=Quaternion.Slerp(first.rotations[j],second.rotations[j],mix);
     t.rotation=transform.rotation*q*restGlobal[mapping[j]];
    }
    var root=Vector3.Lerp(first.root,second.root,mix);
    root.x-=startRoot.x;root.z-=startRoot.z;
    root*=hipHeight/clip.referenceHipHeight;
    bones[HumanBodyBones.Hips].position=transform.TransformPoint(root+motionOrigin);
    if(!Streaming&&clock>clip.Duration+0.15f)Act("idle","待机");
   } else {
    RotateWorld(HumanBodyBones.LeftUpperArm,Vector3.forward,70);
    RotateWorld(HumanBodyBones.RightUpperArm,Vector3.forward,-70);
    float actionDuration=ActionDuration(localAction);
    float envelope=localAction=="idle"?0f:ActionEnvelope(actionTime,actionDuration);
    if(localAction=="wave"){
     float swing=Mathf.Sin(actionTime*7f);
     RotateWorld(HumanBodyBones.RightUpperArm,Vector3.forward,30f*envelope);
     RotateWorld(HumanBodyBones.RightLowerArm,Vector3.forward,28f*envelope+12f*swing*envelope);
     RotateLocal(HumanBodyBones.RightHand,new Vector3(0,0,10f*swing*envelope));
     RotateLocal(HumanBodyBones.Chest,new Vector3(0,-6f*envelope,0));
     RotateLocal(HumanBodyBones.Neck,new Vector3(0,5f*envelope,0));
     RotateLocal(HumanBodyBones.Head,new Vector3(0,4f*envelope,6f*envelope));
     RotateLocal(HumanBodyBones.Hips,new Vector3(0,3f*envelope,0));
    }
    if(localAction=="bow"){
     RotateLocal(HumanBodyBones.Spine,new Vector3(22f*envelope,0,0));
     RotateLocal(HumanBodyBones.Chest,new Vector3(10f*envelope,0,0));
     RotateLocal(HumanBodyBones.Neck,new Vector3(8f*envelope,0,0));
     RotateLocal(HumanBodyBones.Head,new Vector3(5f*envelope,0,0));
     RotateLocal(HumanBodyBones.Hips,new Vector3(-4f*envelope,0,0));
    }
    if(localAction=="nod"){
     float nod=Mathf.Sin(Mathf.Clamp01(actionTime/Mathf.Max(.2f,actionDuration))*Mathf.PI*2f)*envelope;
     RotateLocal(HumanBodyBones.Neck,new Vector3(7f*nod,0,0));
     RotateLocal(HumanBodyBones.Head,new Vector3(5f*nod,0,0));
     RotateLocal(HumanBodyBones.Chest,new Vector3(2f*nod,0,0));
    }
    if(localAction=="shake_head"){
     float shake=Mathf.Sin(actionTime*8f)*envelope;
     RotateLocal(HumanBodyBones.Neck,new Vector3(0,6f*shake,0));
     RotateLocal(HumanBodyBones.Head,new Vector3(0,6f*shake,0));
    }
    if(localAction=="turn"){
     float turn=Mathf.SmoothStep(0,360,Mathf.Clamp01(actionTime/Mathf.Max(.2f,actionDuration)));
     RotateWorld(HumanBodyBones.Hips,Vector3.up,turn);
     RotateLocal(HumanBodyBones.Chest,new Vector3(0,4f*Mathf.Sin(turn*Mathf.Deg2Rad),0));
     RotateLocal(HumanBodyBones.Head,new Vector3(0,6f*Mathf.Sin(turn*Mathf.Deg2Rad),0));
    }
    if(localAction=="idle")ApplyNaturalIdle(dt);
    if(localAction!="idle"&&actionTime>actionDuration+.55f)Act("idle","待机");
   }
   ApplyConversation(dt,clip!=null,localAction!="idle");
   ApplyTransition();
   if(followPointer&&clip==null&&localAction=="idle"&&!speechActive&&!ConversationGestureActive) {
    float x=Mathf.Clamp((Input.mousePosition.x/Mathf.Max(1,Screen.width)-0.5f)*2,-1,1),y=Mathf.Clamp((Input.mousePosition.y/Mathf.Max(1,Screen.height)-0.5f)*2,-1,1);
    RotateLocal(HumanBodyBones.Head,new Vector3(-y*7,x*12,0));
   }
   if(faceController==null)faceController=GetComponent<YachiyoFaceController>();
   if(faceController==null){
    nextBlink-=dt;
    if(nextBlink<0){blinkClock=0;nextBlink=UnityEngine.Random.Range(2.8f,5.3f);}
    float weight=0;if(blinkClock>=0){blinkClock+=dt;weight=Mathf.Sin(Mathf.Clamp01(blinkClock/0.18f)*Mathf.PI)*100;if(blinkClock>=0.18f)blinkClock=-1;}
    foreach(var s in blink)s.mesh.SetBlendShapeWeight(s.index,weight);
    foreach(var s in smile)s.mesh.SetBlendShapeWeight(s.index,expression*70);
   }
   RememberPose(dt);
  }
  static float ActionDuration(string action){
   switch(action){case "nod":return 1.6f;case "head_tilt":return 2.4f;case "shake_head":return 1.8f;case "bow":return 2.8f;case "turn":return 3.0f;default:return 3.2f;}
  }
  static float ActionEnvelope(float time,float duration){
   float fade=Mathf.Min(.38f,duration*.30f);
   return Mathf.SmoothStep(0f,1f,Mathf.Clamp01(time/fade))*Mathf.SmoothStep(0f,1f,Mathf.Clamp01((duration-time)/fade));
  }
  // Native sleeve constraints evaluate after this pose and before late cloth solving.
  void Update()=>Tick(Time.deltaTime);
  void RotateLocal(HumanBodyBones b,Vector3 angle){if(bones.TryGetValue(b,out var t))t.localRotation*=Quaternion.Euler(angle);}
  void RotateWorld(HumanBodyBones b,Vector3 axis,float angle){if(bones.TryGetValue(b,out var t))t.rotation=Quaternion.AngleAxis(angle,transform.TransformDirection(axis))*t.rotation;}
 }
}
