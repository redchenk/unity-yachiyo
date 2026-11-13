using System.Collections.Generic;
using UnityEngine;

namespace Yachiyo {
 public partial class AvatarMotion {
  readonly Dictionary<HumanBodyBones,Quaternion> renderedPose=new Dictionary<HumanBodyBones,Quaternion>();
  readonly Dictionary<HumanBodyBones,Vector3> poseVelocity=new Dictionary<HumanBodyBones,Vector3>(),exitVelocity=new Dictionary<HumanBodyBones,Vector3>();
  Vector3 renderedHips,rootVelocity,exitRootVelocity;
  Vector3 motionOrigin,idleOrigin,restRoot;
  float blendDuration=.5f,holdClock,holdLimit;
  public bool HoldingMotion {get;private set;}
  public int MotionRevision {get;private set;}
  public string PoseMode=>HoldingMotion?"joining":clip!=null?"model":localAction!="idle"?"action":"idle";
  public float TransitionProgress=>Mathf.Clamp01(blendClock/blendDuration);
  public Vector3 CameraAnchor=>clip!=null?bones[HumanBodyBones.Hips].position:transform.TransformPoint(restRoot+idleOrigin);
  static float Smooth5(float t){t=Mathf.Clamp01(t);return t*t*t*(t*(t*6-15)+10);}
  static Quaternion Travel(Quaternion pose,Vector3 velocity,float seconds){float speed=velocity.magnitude;return speed<.001f?pose:Quaternion.AngleAxis(speed*seconds,velocity/speed)*pose;}
  void CaptureTransition(float duration=.5f){
   foreach(var p in bones){transition[p.Key]=p.Value.localRotation;exitVelocity[p.Key]=poseVelocity.TryGetValue(p.Key,out var v)?v:Vector3.zero;}
   transitionHips=bones[HumanBodyBones.Hips].localPosition;exitRootVelocity=rootVelocity;blendClock=0;blendDuration=duration;
  }
  void RememberPose(float dt){
   foreach(var p in bones){
    Vector3 velocity=Vector3.zero;
    if(dt>.001f&&dt<.1f&&renderedPose.TryGetValue(p.Key,out var previous)){
     var delta=p.Value.localRotation*Quaternion.Inverse(previous);delta.ToAngleAxis(out float angle,out var axis);if(angle>180)angle-=360;
     if(float.IsFinite(axis.x)&&Mathf.Abs(angle)>.001f)velocity=Vector3.ClampMagnitude(axis*angle/dt,180);
    }
    poseVelocity[p.Key]=velocity;renderedPose[p.Key]=p.Value.localRotation;
   }
   var root=bones[HumanBodyBones.Hips].localPosition;rootVelocity=dt>.001f&&dt<.1f?Vector3.ClampMagnitude((root-renderedHips)/dt,.6f):Vector3.zero;renderedHips=root;
  }
  void ApplyTransition(){
   float blend=Smooth5(blendClock/blendDuration);if(blend>=1)return;
   // Preserve the outgoing velocity at t=0, then decay it. Quintic blending
   // reaches the moving destination with zero added velocity at either end.
   float travel=.10f*(1-Mathf.Exp(-blendClock/.10f));
   foreach(var p in transition)bones[p.Key].localRotation=Quaternion.Slerp(Travel(p.Value,exitVelocity[p.Key],travel),bones[p.Key].localRotation,blend);
   bones[HumanBodyBones.Hips].localPosition=Vector3.Lerp(transitionHips+exitRootVelocity*travel,bones[HumanBodyBones.Hips].localPosition,blend);
  }
  void KeepRootOrigin(){var p=transform.InverseTransformPoint(bones[HumanBodyBones.Hips].position)-restRoot;idleOrigin=new Vector3(p.x,0,p.z);}
  public void HoldForNextMotion(float timeout=12){
   Initialize();CaptureTransition();if(clip!=null)KeepRootOrigin();clip=null;NeuralSpeechMotion=false;Streaming=false;streamQueue.Clear();
   HoldingMotion=true;holdClock=0;holdLimit=timeout;heldBreath=Breath(0);localAction="idle";CurrentAction="动作衔接";
  }
  void ApplyHeldPose(float dt){
   holdClock+=dt;float travel=.10f*(1-Mathf.Exp(-holdClock/.10f));
   foreach(var p in transition)bones[p.Key].localRotation=Travel(p.Value,exitVelocity[p.Key],travel);
   bones[HumanBodyBones.Hips].localPosition=transitionHips+exitRootVelocity*travel;
   // A waiting hand pose retains a very small chest breath, not an idle reset.
   RotateLocal(HumanBodyBones.Chest,new Vector3((Breath(0)-heldBreath)*.18f,0,0));
  }
  float heldBreath;
 }
}
