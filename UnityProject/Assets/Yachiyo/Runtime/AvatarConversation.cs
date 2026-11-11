using UnityEngine;

namespace Yachiyo {
 public partial class AvatarMotion {
  bool speechActive;
  float speechBlend,speechLevel,speechClock,speechAt,gestureClock,gestureDuration,gestureBlend,gestureStrength;
  float headClock,headDuration,headBlend,headStrength;
  int speechPhrase;
  string gesture="",gestureSide="right";
  string headGesture="",headSide="right";
  public float SpeechBodyWeight=>speechBlend;
  public bool ConversationGestureActive=>gestureBlend>.01f||headBlend>.01f;
  public string ConversationGesture=>string.IsNullOrEmpty(gesture)?headGesture:gesture;

  public void SpeechAudio(bool active){
   if(active&&!speechActive){speechPhrase++;speechClock=0;speechLevel=0;}
   speechActive=active;speechAt=Time.unscaledTime;
  }
  public void SpeechFrame(float time,float level){speechClock=Mathf.Max(0,time);speechLevel=Mathf.Clamp01(level);speechAt=Time.unscaledTime;}
  public void Gesture(string type,float duration,float strength,string side){
   if(type=="nod"||type=="head_tilt"||type=="shake_head"){
    headGesture=type;headClock=0;headDuration=Mathf.Clamp(duration,.35f,15f);headStrength=Mathf.Clamp01(strength);headSide=side;return;
   }
   gesture=type;gestureClock=0;gestureDuration=Mathf.Clamp(duration,.35f,15f);gestureStrength=Mathf.Clamp01(strength);gestureSide=side;
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
   gestureClock+=dt;
   float desired=gestureClock<gestureDuration?ActionEnvelope(gestureClock,gestureDuration):0;
   gestureBlend=Mathf.MoveTowards(gestureBlend,desired,dt/.18f);
   float g=gestureBlend*gestureStrength;
   headClock+=dt;
   headBlend=Mathf.MoveTowards(headBlend,headClock<headDuration?ActionEnvelope(headClock,headDuration):0,dt/.18f);

   if(!modelPlaying&&!localPlaying){
    if(speechBlend>.001f){
     float energy=stateEmotion.Contains("sad") ? .55f : stateEmotion.Contains("happy")?1.1f:1;
     float amount=speechBlend*energy*(.55f+speechLevel*.7f);
     float beat=Mathf.Sin(speechClock*3.2f),side=(speechPhrase%2==0?-1:1);
     float swap=Mathf.Sin(speechClock*.95f+speechPhrase*.8f);
     float armShare=(gesture=="explain"||gesture=="emphasis"||gesture=="open_palms")?1-g:1;
     ArmGesture(false,armShare*amount*(.65f-.25f*swap),speechClock, false);
     ArmGesture(true,armShare*amount*(.65f+.25f*swap),speechClock+.7f, false);
     RotateLocal(HumanBodyBones.Chest,new Vector3(beat*1.3f*amount,side*amount*2.8f,swap*amount*1.4f));
     RotateLocal(HumanBodyBones.Neck,new Vector3(beat*1.5f*amount,0,side*amount*1.3f));
     RotateLocal(HumanBodyBones.Head,new Vector3(beat*1.8f*amount,-side*amount*1.5f,0));
    } else if(stateMode=="thinking"){
     float ease=Mathf.SmoothStep(0,1,Mathf.Clamp01(stateClock/.45f));
     RotateLocal(HumanBodyBones.Chest,new Vector3(0,3*ease,0));
     RotateLocal(HumanBodyBones.Neck,new Vector3(-3*ease,0,5*ease));
     RotateLocal(HumanBodyBones.Head,new Vector3(-2*ease,0,5*ease));
    } else if(stateMode=="listening"){
     float nod=Mathf.Sin(stateClock*1.5f);
     RotateLocal(HumanBodyBones.Chest,new Vector3(nod*.6f,0,0));
     RotateLocal(HumanBodyBones.Neck,new Vector3(1+nod,0,0));
    }
   }
   // Head acknowledgements coexist with an arm gesture or a generated clip.
   float h=headBlend*headStrength,headSign=headSide=="left"?-1:1,headBeat=Mathf.Sin(headClock*5);
   switch(headGesture){
    case "nod":RotateLocal(HumanBodyBones.Neck,new Vector3(5*headBeat*h,0,0));RotateLocal(HumanBodyBones.Head,new Vector3(6*headBeat*h,0,0));break;
    case "head_tilt":RotateLocal(HumanBodyBones.Neck,new Vector3(0,0,6*headSign*h));RotateLocal(HumanBodyBones.Head,new Vector3(0,0,7*headSign*h));break;
    case "shake_head":RotateLocal(HumanBodyBones.Neck,new Vector3(0,6*headBeat*h,0));RotateLocal(HumanBodyBones.Head,new Vector3(0,7*headBeat*h,0));break;
   }
   if(g<=.001f||modelPlaying||localPlaying)return;
   float sign=gestureSide=="left"?-1:1,osc=Mathf.Sin(gestureClock*5);
   switch(gesture){
    case "explain":case "emphasis":ArmGesture(sign<0,g,gestureClock,false);RotateLocal(HumanBodyBones.Chest,new Vector3(2*osc*g,sign*3*g,0));break;
    case "open_palms":ArmGesture(true,g,gestureClock,true);ArmGesture(false,g,gestureClock,true);RotateLocal(HumanBodyBones.Head,new Vector3(-2*g,0,3*g));break;
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
