using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yachiyo {
 public enum FaceEmotion { Neutral, Happy, Surprised, Sad, Thinking, Angry, Warm, ConcernedWarm, Shy, Playful, Serious }

 // Owns every facial blend-shape write. AvatarMotion must not write blink/smile
 // while this component is present; the controller blends emotions, blinking,
 // talking/visemes and optional eye look into one final weight set.
 [DefaultExecutionOrder(200)]
 public sealed class YachiyoFaceController : MonoBehaviour {
  public AvatarMotion avatar;
  public float blinkIntervalMin=2.8f,blinkIntervalMax=5.2f,blinkDuration=.18f;
  readonly List<SkinnedMeshRenderer> renderers=new List<SkinnedMeshRenderer>();
  readonly Dictionary<string,List<(SkinnedMeshRenderer renderer,int index)>> shapes=new Dictionary<string,List<(SkinnedMeshRenderer renderer,int index)>>();
  readonly Dictionary<string,float> current=new Dictionary<string,float>(),target=new Dictionary<string,float>();
  readonly HashSet<string> managed=new HashSet<string>();
  FaceEmotion emotion=FaceEmotion.Neutral;float intensity=1,fade=.22f;
  float blinkTimer=3,blinkClock=-1,forceBlink=-1,activeBlinkDuration=.18f;bool doubleBlink;
  bool talking;float talkClock;string viseme=null;float visemeWeight;
  bool externalMouth;float mouthExpires,openAmount;readonly float[] vowels=new float[5];
  Transform leftEye,rightEye,lookTarget,head;
  Quaternion leftEyeRest,rightEyeRest;Vector3 headForward,headRight,headUp;
  float lookYaw,lookPitch,microYaw,microPitch;
  public int BlinkCount {get;private set;}
  public float BlinkWeight {get;private set;}
  public Vector2 EyeMicroOffset=>new Vector2(microYaw,microPitch);
  bool ready;

  static readonly Dictionary<FaceEmotion,(string,float)[]> Presets=new Dictionary<FaceEmotion,(string,float)[]>{
   {FaceEmotion.Neutral,new (string,float)[0]},
   {FaceEmotion.Warm,new[]{("_209_FC_mouth_smile",40f),("_301_FC_eyebrow_uresii",10f),("_301_EB_eyebrow_uresii",10f)}},
   {FaceEmotion.ConcernedWarm,new[]{("_209_FC_mouth_smile",25f),("_302_FC_eyebrow_kanasii",24f),("_302_EB_eyebrow_kanasii",24f)}},
   {FaceEmotion.Shy,new[]{("_209_FC_mouth_smile",60f),("_102_FC_eye_smile_left",25f),("_102_FC_eye_smile_right",25f),("_102_EL_eye_smile_left",25f),("_102_EL_eye_smile_right",25f)}},
   {FaceEmotion.Playful,new[]{("_209_FC_mouth_smile",65f),("_301_FC_eyebrow_uresii",30f),("_301_EB_eyebrow_uresii",30f),("_102_FC_eye_smile_left",20f),("_102_EL_eye_smile_left",20f)}},
   {FaceEmotion.Serious,new[]{("_304_FC_eyebrow_down",22f),("_304_EB_eyebrow_down",22f),("_104_FC_eye_kiri",12f),("_104_EL_eye_kiri",12f)}},
   {FaceEmotion.Happy,new[]{
    ("_209_FC_mouth_smile",75f),("_102_FC_eye_smile_left",55f),("_102_FC_eye_smile_right",55f),
    ("_102_EL_eye_smile_left",55f),("_102_EL_eye_smile_right",55f),("_301_FC_eyebrow_uresii",15f),("_301_EB_eyebrow_uresii",15f)}},
   {FaceEmotion.Surprised,new[]{
    ("_103_FC_eye_surprise",100f),("_103_EL_eye_surprise",100f),("_303_FC_eyebrow_up",100f),("_303_EB_eyebrow_up",100f),("JawOpen",30f),("_216_FC_mouth_ahiru_open",30f)}},
   {FaceEmotion.Sad,new[]{
    ("_105_FC_eye_kanasii",70f),("_105_EL_eye_kanasii",70f),("_302_FC_eyebrow_kanasii",55f),("_302_EB_eyebrow_kanasii",55f),
    ("_210_FC_mouth_kanasii",45f),("MouthSad",35f)}},
   {FaceEmotion.Thinking,new[]{
    ("_104_FC_eye_kiri",65f),("_104_EL_eye_kiri",65f),("_304_FC_eyebrow_down",55f),("_304_EB_eyebrow_down",55f),("MouthCornerSlant",35f),("EyeLookUpLeft",35f),("EyeLookUpRight",35f)}},
   {FaceEmotion.Angry,new[]{
    ("_208_FC_mouth_ikari",55f),("BrowLowerer",40f),("_104_FC_eye_kiri",50f),("_104_EL_eye_kiri",50f)}},
  };
  static readonly Dictionary<string,string> Visemes=new Dictionary<string,string>{
   {"aa","vrc.v_aa"},{"a","vrc.v_aa"},{"e","vrc.v_e"},{"ih","vrc.v_ih"},{"i","vrc.v_ih"},
   {"oh","vrc.v_oh"},{"o","vrc.v_oh"},{"ou","vrc.v_ou"},{"u","vrc.v_ou"}
  };

  public FaceEmotion Emotion=>emotion;
  public bool Talking=>talking;
  public int ManagedShapeCount=>managed.Count;
  public int BoundShapeCount {get{int n=0;foreach(var key in managed)if(shapes.ContainsKey(key))n++;return n;}}
  public bool HasAudioVisemes=>HasShape("vrc.v_aa")&&HasShape("vrc.v_ih")&&HasShape("vrc.v_ou")&&HasShape("vrc.v_e")&&HasShape("vrc.v_oh");
  public float AudioMouthOpen=>externalMouth&&Time.unscaledTime<mouthExpires?openAmount:0;

  void Awake()=>Initialize();
  void Start()=>Initialize();
  public void Initialize(){
   if(ready||avatar==null)return;
   foreach(var r in GetComponentsInChildren<SkinnedMeshRenderer>(true)){
    if(!r.sharedMesh)continue;renderers.Add(r);
    for(int i=0;i<r.sharedMesh.blendShapeCount;i++){
     string n=r.sharedMesh.GetBlendShapeName(i);
     if(!shapes.TryGetValue(n,out var list)){list=new List<(SkinnedMeshRenderer,int)>();shapes[n]=list;}
     list.Add((r,i));
    }
   }
   foreach(var preset in Presets.Values)foreach(var pair in preset)managed.Add(pair.Item1);
   foreach(var n in new[]{"_101_FC_eye_close_left","_101_FC_eye_close_right","_101_EL_eye_close_left","_101_EL_eye_close_right","JawOpen","MouthClosed"})managed.Add(n);
   foreach(var n in Visemes.Values)managed.Add(n);
   foreach(var n in managed)current[n]=0f;
   if(avatar&&avatar.animator){avatar.Initialize();leftEye=avatar.animator.GetBoneTransform(HumanBodyBones.LeftEye);rightEye=avatar.animator.GetBoneTransform(HumanBodyBones.RightEye);head=avatar.animator.GetBoneTransform(HumanBodyBones.Head);leftEyeRest=avatar.RestBoneRotation(HumanBodyBones.LeftEye);rightEyeRest=avatar.RestBoneRotation(HumanBodyBones.RightEye);headForward=head.InverseTransformDirection(avatar.transform.forward);headRight=head.InverseTransformDirection(avatar.transform.right);headUp=head.InverseTransformDirection(avatar.transform.up);}
   blinkTimer=UnityEngine.Random.Range(blinkIntervalMin,blinkIntervalMax);ready=true;
   var names=new List<string>(shapes.Keys);names.Sort();
   Debug.Log("FACE_CONTROLLER renderers="+renderers.Count+" shapes="+shapes.Count+" managed="+managed.Count+" smile="+HasShape("_209_FC_mouth_smile")+" blink="+HasShape("_101_FC_eye_close_left")+" jaw="+HasShape("JawOpen"));
  }
  bool HasShape(string n)=>shapes.ContainsKey(n);

  public void SetEmotion(FaceEmotion e,float strength=1f,float transition=.22f){
   Initialize();emotion=e;intensity=Mathf.Clamp01(strength);fade=Mathf.Max(.01f,transition);
  }
  public void ForceBlink(float duration=.18f){Initialize();forceBlink=Mathf.Max(.05f,duration);}
  public void SetTalking(bool value){Initialize();talking=value;if(value)externalMouth=false;if(!value)talkClock=0;}
  public void SetViseme(string name,float weight=1f){Initialize();viseme=name;visemeWeight=Mathf.Clamp01(weight);}
  public void SetLookTarget(Transform target){lookTarget=target;}
  public void SetAudioMouth(float value,float a,float i,float u,float e,float o,bool silence){
   Initialize();externalMouth=true;talking=false;viseme=null;mouthExpires=Time.unscaledTime+.25f;
   openAmount=silence?0:Mathf.Clamp01(value);var input=new[]{a,i,u,e,o};float sum=0;
   for(int n=0;n<5;n++){vowels[n]=Mathf.Clamp01(input[n]);sum+=vowels[n];}
   if(sum>1)for(int n=0;n<5;n++)vowels[n]/=sum;
  }
  public void CloseAudioMouth(){externalMouth=true;talking=false;viseme=null;openAmount=0;mouthExpires=Time.unscaledTime;Array.Clear(vowels,0,5);}

  void LateUpdate(){
   Initialize();if(!ready)return;float dt=Time.deltaTime;
   // Build this frame's goal from the current expression preset.
   target.Clear();
   if(Presets.TryGetValue(emotion,out var preset))foreach(var pair in preset)Add(pair.Item1,pair.Item2*intensity);
   // Auto blink plus one-shot forced blink.
   blinkTimer-=dt;
   if(forceBlink>0){blinkClock=0;activeBlinkDuration=forceBlink;forceBlink=-1;BlinkCount++;}
   if(blinkClock<0&&blinkTimer<=0){blinkClock=0;activeBlinkDuration=UnityEngine.Random.Range(.16f,.20f);BlinkCount++;}
   float blink=0f;
   if(blinkClock>=0){blinkClock+=dt;float t=blinkClock/activeBlinkDuration;blink=(t<.28f?Mathf.SmoothStep(0,1,t/.28f):1-Mathf.SmoothStep(0,1,(t-.28f)/.72f))*100;
    if(t>=1){blinkClock=-1;bool again=!doubleBlink&&UnityEngine.Random.value<.16f;blinkTimer=again?UnityEngine.Random.Range(.10f,.18f):UnityEngine.Random.Range(blinkIntervalMin*.65f,blinkIntervalMax*1.3f);doubleBlink=again;}}
   BlinkWeight=blink;
   if(blink>0){Add("_101_FC_eye_close_left",blink);Add("_101_FC_eye_close_right",blink);Add("_101_EL_eye_close_left",blink);Add("_101_EL_eye_close_right",blink);}
   if(blink>0){Add("_304_FC_eyebrow_down",blink*.025f);Add("_304_EB_eyebrow_down",blink*.025f);}
   // Procedural talking. A viseme, when supplied by a future audio/TTS layer,
   // takes priority and can be blended over the generic open/close motion.
   if(externalMouth){
    float amount=Time.unscaledTime<mouthExpires?openAmount:0;
    // Visemes already include jaw deformation. Do not add JawOpen on top.
    target.Remove("JawOpen");target.Remove("_216_FC_mouth_ahiru_open");
    string[] names={"vrc.v_aa","vrc.v_ih","vrc.v_ou","vrc.v_e","vrc.v_oh"};float sum=0;
    for(int n=0;n<5;n++){sum+=vowels[n];Add(names[n],vowels[n]*amount*85);}
    if(sum<.01f)Add("JawOpen",amount*50);
   }
   else if(talking){
    talkClock+=dt;float open=(.5f+.5f*Mathf.Sin(talkClock*11.5f))*(.55f+.25f*Mathf.Sin(talkClock*3.7f));
    Add("JawOpen",open*55f);Add("MouthClosed",(1f-open)*20f);
    if(string.IsNullOrEmpty(viseme)){if(Mathf.Sin(talkClock*5.3f)>0)Add("vrc.v_aa",open*34f);else Add("vrc.v_oh",open*26f);}
   }
   if(!string.IsNullOrEmpty(viseme)&&Visemes.TryGetValue(viseme,out var shape)){Add(shape,visemeWeight*70f);Add("JawOpen",visemeWeight*35f);}
   // Apply with a short transition so emotion changes do not pop.
   float step=dt/Mathf.Max(.001f,fade)*100f;
   foreach(string name in managed){
    float goal=target.TryGetValue(name,out var g)?Mathf.Clamp(g,0f,100f):0f;
    float now=name.StartsWith("_101_")?goal:Mathf.MoveTowards(current.TryGetValue(name,out var c)?c:0f,goal,step);
    current[name]=now;
    if(shapes.TryGetValue(name,out var list))foreach(var pair in list)pair.renderer.SetBlendShapeWeight(pair.index,now);
   }
   ApplyEyeAttention(dt);
  }
  void ApplyEyeAttention(float dt){
   if(!head||!leftEye||!rightEye)return;
   var forward=head.TransformDirection(headForward);var right=head.TransformDirection(headRight);var up=head.TransformDirection(headUp);float yaw=0,pitch=0;
   if(lookTarget){var d=(lookTarget.position-head.position).normalized;yaw=Mathf.Clamp(Mathf.Atan2(Vector3.Dot(d,right),Vector3.Dot(d,forward))*Mathf.Rad2Deg,-24,24);pitch=Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(Vector3.Dot(d,up),-1,1))*Mathf.Rad2Deg,-15,15);}
   lookYaw=Mathf.Lerp(lookYaw,yaw,1-Mathf.Exp(-dt*12));lookPitch=Mathf.Lerp(lookPitch,pitch,1-Mathf.Exp(-dt*12));
   float gain=avatar.PoseMode=="idle"?1:.35f;
   microYaw=Mathf.MoveTowards(microYaw,avatar.IdleGazeYaw*gain,dt*180);microPitch=Mathf.MoveTowards(microPitch,avatar.IdleGazePitch*gain,dt*140);
   var offset=Quaternion.AngleAxis(lookYaw+microYaw,up)*Quaternion.AngleAxis(lookPitch+microPitch,right);
   leftEye.rotation=offset*leftEye.parent.rotation*leftEyeRest;rightEye.rotation=offset*rightEye.parent.rotation*rightEyeRest;
  }
  void Add(string name,float weight){
   if(!managed.Contains(name))managed.Add(name);
   target[name]=Mathf.Max(target.TryGetValue(name,out var v)?v:0f,weight);
  }
 }
}
