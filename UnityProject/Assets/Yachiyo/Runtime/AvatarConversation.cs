using UnityEngine;

namespace Yachiyo {
 public partial class AvatarMotion {
  bool speechActive;
  float speechBlend,speechLevel,speechClock,speechAt,gestureClock,gestureDuration,gestureBlend,gestureStrength;
  float headClock,headDuration,headBlend,headStrength;
  bool timedGesture,timedHead;float gestureStart,headStart,gesturePrepare,gestureRelease,headPrepare,headRelease;
  string gesture="",gestureSide="right";
  string headGesture="",headSide="right";
  public float SpeechPlaybackWeight=>speechBlend;
  public bool ConversationGestureActive=>gestureBlend>.01f||headBlend>.01f;
  public string ConversationGesture=>string.IsNullOrEmpty(gesture)?headGesture:gesture;

  public void SpeechAudio(bool active){
   if(active&&!speechActive){speechClock=0;speechLevel=0;}
   speechActive=active;speechAt=Time.unscaledTime;
  }
  public void SpeechFrame(float time,float level){speechClock=Mathf.Max(0,time);speechLevel=Mathf.Clamp01(level);speechAt=Time.unscaledTime;}
  public void Gesture(string type,float duration,float strength,string side){
   if(type=="head_tilt")return;
   if(type=="nod"||type=="shake_head"){
    timedHead=false;
    headGesture=type;headClock=0;headDuration=Mathf.Clamp(duration,.35f,15f);headStrength=Mathf.Clamp01(strength);headSide=side;return;
   }
   timedGesture=false;
   gesture=type;gestureClock=0;gestureDuration=Mathf.Clamp(duration,.35f,15f);gestureStrength=Mathf.Clamp01(strength);gestureSide=side;
  }
  public void GestureTimed(string type,float start,float duration,float prepare,float release,float strength,string side){
   Gesture(type,duration,strength,side);
   if(type=="head_tilt")return;
   if(type=="nod"||type=="shake_head"){timedHead=true;headStart=start;headPrepare=prepare;headRelease=release;}
   else{timedGesture=true;gestureStart=start;gesturePrepare=prepare;gestureRelease=release;}
  }
  static float SpeechEnvelope(float clock,float length,float prepare,float release){
   if(clock<0||clock>=length)return 0;
   return Mathf.SmoothStep(0,1,Mathf.Clamp01(clock/Mathf.Max(.05f,prepare)))*Mathf.SmoothStep(0,1,Mathf.Clamp01((length-clock)/Mathf.Max(.05f,release)));
  }
  public void SetConversationEmotion(string emotion){if(!string.IsNullOrEmpty(emotion))stateEmotion=emotion.ToLowerInvariant();}
  public void EndConversationGesture(){gestureDuration=0;headDuration=0;}
  public void ResetConversation(){SpeechAudio(false);EndConversationGesture();stateMode="idle";stateUntil=0;}

  void ApplyConversation(float dt,bool modelPlaying,bool localPlaying){
   stateClock+=dt;
   if(Time.unscaledTime>stateUntil)stateMode="idle";
   if(speechActive&&Time.unscaledTime-speechAt>.8f)speechActive=false;
   // A real playback frame owns this layer. Short sentences start immediately;
   // stalls, pause and cancel release it without queuing a delayed body action.
   speechBlend=Mathf.MoveTowards(speechBlend,speechActive&&!modelPlaying&&!localPlaying?1:0,dt/(speechActive ? .2f : .28f));
   gestureClock=timedGesture?speechClock-gestureStart:gestureClock+dt;
   float desired=timedGesture?SpeechEnvelope(gestureClock,gestureDuration,gesturePrepare,gestureRelease):gestureClock<gestureDuration?ActionEnvelope(gestureClock,gestureDuration):0;
   gestureBlend=Mathf.MoveTowards(gestureBlend,desired,dt/.18f);
   float g=gestureBlend*gestureStrength;
   headClock=timedHead?speechClock-headStart:headClock+dt;
   headBlend=Mathf.MoveTowards(headBlend,timedHead?SpeechEnvelope(headClock,headDuration,headPrepare,headRelease):headClock<headDuration?ActionEnvelope(headClock,headDuration):0,dt/.18f);

   // Playback and UI states do not invent gestures. Only an explicit AI cue
   // controls this layer, and a generated full-body pose owns all its bones.
   if(modelPlaying||localPlaying)return;
   float h=headBlend*headStrength,headBeat=Mathf.Sin(headClock*5);
   switch(headGesture){
    case "nod":RotateLocal(HumanBodyBones.Neck,new Vector3(5*headBeat*h,0,0));RotateLocal(HumanBodyBones.Head,new Vector3(6*headBeat*h,0,0));break;
    case "shake_head":RotateLocal(HumanBodyBones.Neck,new Vector3(0,6*headBeat*h,0));RotateLocal(HumanBodyBones.Head,new Vector3(0,7*headBeat*h,0));break;
   }
   if(g<=.001f||modelPlaying||localPlaying)return;
   float sign=gestureSide=="left"?-1:1,osc=Mathf.Sin(gestureClock*5);
   switch(gesture){
    case "explain":case "emphasis":ArmGesture(sign<0,g,gestureClock,false);RotateLocal(HumanBodyBones.Chest,new Vector3(2*osc*g,sign*3*g,0));break;
    case "open_palms":ArmGesture(true,g,gestureClock,true);ArmGesture(false,g,gestureClock,true);break;
    case "lean_in":RotateLocal(HumanBodyBones.Spine,new Vector3(5*g,0,0));RotateLocal(HumanBodyBones.Chest,new Vector3(3*g,0,0));break;
    case "lean_left":case "lean_right":RotateLocal(HumanBodyBones.Spine,new Vector3(0,0,(gesture=="lean_left"?-1:1)*5*g));break;
    case "sway":RotateLocal(HumanBodyBones.Spine,new Vector3(0,0,Mathf.Sin(gestureClock*2.3f)*3*g));break;
    case "breathe":RotateLocal(HumanBodyBones.Chest,new Vector3(Mathf.Sin(gestureClock*1.8f)*1.5f*g,0,0));break;
   }
  }
  void ArmGesture(bool left,float amount,float time,bool open){
   float side=left?-1:1,beat=.5f+.5f*Mathf.Sin(time*2.2f);
   var upper=left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm;
   var lower=left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm;
   var hand=left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand;
   RotateWorld(upper,Vector3.forward,side*(open?15:9)*amount);
   RotateWorld(upper,Vector3.right,-(8+3*beat)*amount);
   RotateWorld(lower,Vector3.right,-(30+16*beat)*amount);
   RotateWorld(hand,Vector3.forward,side*(open?15:6)*amount);
  }
 }
}
