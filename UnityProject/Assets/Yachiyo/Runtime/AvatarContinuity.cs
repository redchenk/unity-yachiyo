using System.Collections.Generic;
using UnityEngine;

namespace Yachiyo {
 public partial class AvatarMotion {
  readonly Dictionary<HumanBodyBones,Quaternion> renderedPose=new Dictionary<HumanBodyBones,Quaternion>();
  readonly Dictionary<HumanBodyBones,Vector3> poseVelocity=new Dictionary<HumanBodyBones,Vector3>(),exitVelocity=new Dictionary<HumanBodyBones,Vector3>();
  readonly Dictionary<HumanBodyBones,Quaternion> incomingPose=new Dictionary<HumanBodyBones,Quaternion>();
  readonly Dictionary<HumanBodyBones,Vector3> rotationOffset=new Dictionary<HumanBodyBones,Vector3>(),velocityOffset=new Dictionary<HumanBodyBones,Vector3>();
  Vector3 renderedHips,rootVelocity,exitRootVelocity,positionOffset,positionVelocityOffset;
  Vector3 motionOrigin,idleOrigin,restRoot;
  float blendDuration=.5f;
  bool transitionPending,streamRecovering;
  public int MotionRevision {get;private set;}
  public bool SettlingToIdle=>(clip==null||streamRecovering)&&localAction=="idle"&&TransitionProgress<1;
  public string PoseMode=>SettlingToIdle?"settling":clip!=null&&!streamRecovering?"model":localAction!="idle"?"action":"idle";
  public float TransitionProgress=>transitionPending?0:Mathf.Clamp01(blendClock/blendDuration);
  public Vector3 CameraAnchor=>clip!=null&&!streamRecovering?bones[HumanBodyBones.Hips].position:transform.TransformPoint(restRoot+idleOrigin);
  static float Smooth5(float t){t=Mathf.Clamp01(t);return t*t*t*(t*(t*6-15)+10);}
  static Vector3 RotationVector(Quaternion q){
   q.ToAngleAxis(out float angle,out var axis);if(angle>180)angle-=360;
   return float.IsFinite(axis.x)&&float.IsFinite(axis.y)&&float.IsFinite(axis.z)&&Mathf.Abs(angle)>.00001f?axis*angle:Vector3.zero;
  }
  static Quaternion Travel(Quaternion pose,Vector3 velocity,float seconds){float speed=velocity.magnitude;return speed<.00001f?pose:Quaternion.AngleAxis(speed*seconds,velocity/speed)*pose;}
  void CaptureTransition(float duration=.5f){
   foreach(var p in bones){transition[p.Key]=p.Value.localRotation;exitVelocity[p.Key]=poseVelocity.TryGetValue(p.Key,out var v)?v:Vector3.zero;}
   transitionHips=bones[HumanBodyBones.Hips].localPosition;exitRootVelocity=rootVelocity;blendClock=0;blendDuration=duration;transitionPending=true;
  }
  void RememberPose(float dt){
   foreach(var p in bones){
    var velocity=dt>.001f&&dt<.1f&&renderedPose.TryGetValue(p.Key,out var previous)?Vector3.ClampMagnitude(RotationVector(p.Value.localRotation*Quaternion.Inverse(previous))/dt,360):Vector3.zero;
    poseVelocity[p.Key]=velocity;renderedPose[p.Key]=p.Value.localRotation;
   }
   var root=bones[HumanBodyBones.Hips].localPosition;rootVelocity=dt>.001f&&dt<.1f?Vector3.ClampMagnitude((root-renderedHips)/dt,.6f):Vector3.zero;renderedHips=root;
  }
  void ApplyTransition(float dt){
   if(transitionPending){
    // Estimate the incoming velocity from the model itself, not from the old
    // clip. Only transition setup samples twice; normal frames sample once.
    foreach(var p in bones)incomingPose[p.Key]=p.Value.localRotation;
    var targetRoot=bones[HumanBodyBones.Hips].localPosition;
    const float sample=.008f;
    bool model=clip!=null&&!streamRecovering;
    if(model)ApplyModelPose(clock+sample);
    float step=NeuralSpeechMotion&&!speechActive?0:Mathf.Clamp(dt,0,.05f);
    foreach(var p in bones){
     var target=incomingPose[p.Key];
     var incomingVelocity=model?Vector3.ClampMagnitude(RotationVector(p.Value.localRotation*Quaternion.Inverse(target))/sample,360):Vector3.zero;
     rotationOffset[p.Key]=RotationVector(Travel(transition[p.Key],exitVelocity[p.Key],step)*Quaternion.Inverse(target));
     velocityOffset[p.Key]=exitVelocity[p.Key]-incomingVelocity;
     p.Value.localRotation=target;
    }
    var incomingRootVelocity=model?Vector3.ClampMagnitude((bones[HumanBodyBones.Hips].localPosition-targetRoot)/sample,.6f):Vector3.zero;
    positionOffset=transitionHips+exitRootVelocity*step-targetRoot;positionVelocityOffset=exitRootVelocity-incomingRootVelocity;
    bones[HumanBodyBones.Hips].localPosition=targetRoot;transitionPending=false;blendClock=0;
   }
   float t=Mathf.Clamp01(blendClock/blendDuration);if(t>=1)return;
   // Quintic inertialization: remove the pose AND velocity difference while
   // the destination continues playing. At the end both corrections are zero.
   float t2=t*t,t3=t2*t,t4=t3*t,t5=t4*t;
   float position=1-10*t3+15*t4-6*t5,velocity=(t-6*t3+8*t4-3*t5)*blendDuration;
   foreach(var p in bones)p.Value.localRotation=Travel(p.Value.localRotation,rotationOffset[p.Key]*position+velocityOffset[p.Key]*velocity,1);
   bones[HumanBodyBones.Hips].localPosition+=positionOffset*position+positionVelocityOffset*velocity;
  }
  void KeepRootOrigin(){var p=transform.InverseTransformPoint(bones[HumanBodyBones.Hips].position)-restRoot;idleOrigin=new Vector3(p.x,0,p.z);}
  public void ReleaseMotion(){
   Initialize();
   // Repeated prepare/end/cancel messages must not restart the same recovery.
   if((clip!=null&&!streamRecovering)||localAction!="idle"){
    if(clip!=null)KeepRootOrigin();CaptureTransition(.8f);
   }
   clip=null;NeuralSpeechMotion=false;Streaming=false;streamRecovering=false;streamQueue.Clear();
   localAction="idle";actionTime=0;CurrentAction="待机";MotionSource="本地动作";
  }
  bool BoundaryNeedsTransition(){
   if(streamQueue.Count==0)return false;
   var last=clip.frames[clip.frames.Length-1];var next=streamQueue.Peek().frames[0];
   if(Vector3.Distance(last.root,next.root)*hipHeight/clip.referenceHipHeight>.04f)return true;
   for(int i=0;i<last.rotations.Length;i++)if(Quaternion.Angle(last.rotations[i],next.rotations[i])>12)return true;
   return false;
  }
  void AdvanceMotionClock(float dt){
   if(clip==null){if(localAction!="idle"&&actionTime>=ActionDuration(localAction))ReleaseMotion();return;}
   bool starved=false;
   if(NeuralSpeechMotion){
    float predicted=speechClock+(speechActive?Mathf.Clamp(Time.unscaledTime-speechAt,0,.05f):0);
    if(speechActive)SpeechRenderClock=Mathf.Max(SpeechRenderClock,predicted);
    float renderTime=Mathf.Min(SpeechRenderClock,Mathf.Max(0,(SpeechMotionFrames-1)/30f));
    clock=Mathf.Max(0,renderTime-speechChunkStart);
    while(streamQueue.Count>0&&clock>=clip.frames.Length/clip.fps){
     if(BoundaryNeedsTransition())CaptureTransition(.3f);
     speechChunkStart+=clip.frames.Length/clip.fps;clip=streamQueue.Dequeue();clock=Mathf.Max(0,renderTime-speechChunkStart);
    }
    starved=speechActive&&SpeechRenderClock>SpeechMotionFrames/30f+.05f;
    if(starved&&!speechUnderrun)SpeechMotionUnderruns++;speechUnderrun=starved;
   }else{
    clock+=dt;
    if(Streaming){
     while(streamQueue.Count>0&&clock>=clip.frames.Length/clip.fps){
      if(BoundaryNeedsTransition())CaptureTransition(.3f);
      clock-=clip.frames.Length/clip.fps;clip=streamQueue.Dequeue();CurrentAction=clip.prompt;MotionSource=clip.source;
     }
     starved=streamQueue.Count==0&&clock>=clip.frames.Length/clip.fps;
     if(starved){if(!streamRecovering)StreamUnderruns++;clock=clip.frames.Length/clip.fps;}
    }else if(clock>=clip.Duration){ReleaseMotion();return;}
   }
   if(starved&&!streamRecovering){KeepRootOrigin();CaptureTransition(.8f);localAction="idle";streamRecovering=true;}
   else if(!starved&&streamRecovering){CaptureTransition(.4f);streamRecovering=false;}
  }
  void ApplyModelPose(float time){
   float pos=Mathf.Clamp(time*clip.fps,0,clip.frames.Length-1);int a=(int)pos,b=Mathf.Min(a+1,clip.frames.Length-1);float mix=pos-a;
   var first=clip.frames[a];var second=clip.frames[b];
   // Continuous packets interpolate normally. A discontinuity is bridged on
   // dequeue, never spread over a single frame at the end of the old packet.
   if(Streaming&&streamQueue.Count>0&&time>=clip.Duration&&!BoundaryNeedsTransition()){
    first=clip.frames[clip.frames.Length-1];second=streamQueue.Peek().frames[0];mix=Mathf.Clamp01((time-clip.Duration)*clip.fps);
   }
   for(int j=0;j<mapping.Length;j++)if(bones.TryGetValue(mapping[j],out var bone))bone.rotation=transform.rotation*Quaternion.Slerp(first.rotations[j],second.rotations[j],mix)*restGlobal[mapping[j]];
   var root=Vector3.Lerp(first.root,second.root,mix);root.x-=startRoot.x;root.z-=startRoot.z;root*=hipHeight/clip.referenceHipHeight;
   bones[HumanBodyBones.Hips].position=transform.TransformPoint(root+motionOrigin);
  }
 }
}
