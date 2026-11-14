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
  bool transitionPending,streamRecovering,streamUnderrun;
  public int MotionRevision {get;private set;}
  public bool SettlingToIdle=>(clip==null||streamRecovering)&&localAction=="idle"&&TransitionProgress<1;
  public string PoseMode=>streamPredicting?"predicting":SettlingToIdle?"settling":clip!=null&&!streamRecovering?"model":localAction!="idle"?"action":"idle";
  public float TransitionProgress=>transitionPending?0:Mathf.Clamp01(blendClock/blendDuration);
  public Vector3 CameraAnchor=>clip!=null&&!streamRecovering?bones[HumanBodyBones.Hips].position:transform.TransformPoint(restRoot+idleOrigin);
  static float Smooth5(float t){t=Mathf.Clamp01(t);return t*t*t*(t*(t*6-15)+10);}
  static Vector3 RotationVector(Quaternion q){
   var axis=new Vector3(q.x,q.y,q.z);if(q.w<0)axis=-axis;
   float length=axis.magnitude;if(!float.IsFinite(length))return Vector3.zero;
   // atan2 retains precision for sub-degree display-frame rotations.
   return length<.000001f?axis*(2*Mathf.Rad2Deg):axis*(2*Mathf.Atan2(length,Mathf.Abs(q.w))*Mathf.Rad2Deg/length);
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
   clip=null;NeuralSpeechMotion=false;Streaming=false;streamRecovering=false;streamPredicting=false;previousModelFrame=null;streamQueue.Clear();
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
   if(NeuralSpeechMotion){
    // Keep a continuous render clock between media reports. Correct small
    // clock errors through playback speed instead of holding then jumping.
    if(speechActive)speechReportAge+=dt;
    float desired=speechClock+Mathf.Clamp(speechReportAge,0,.25f);
    if(speechActive){
     float error=desired-SpeechRenderClock;
     if(error>.25f){CaptureTransition(.3f);SpeechRenderClock=desired;}
     else SpeechRenderClock=Mathf.Max(SpeechRenderClock,Mathf.Min(desired+.035f,SpeechRenderClock+dt*(1+Mathf.Clamp((error-dt)*5,-.12f,.12f))));
    }
    clock=Mathf.Max(0,SpeechRenderClock-speechChunkStart);
   }else clock+=dt;
   if(Streaming){
    // A long outage has already settled. Restart the incoming non-audio clip
    // at its beginning; short gaps consume the predicted part of its timeline.
    if(streamRecovering&&!NeuralSpeechMotion&&streamQueue.Count>0)clock=clip.frames.Length/clip.fps;
    while(streamQueue.Count>0&&clock+.00001f>=clip.frames.Length/clip.fps){
     bool discontinuous=BoundaryNeedsTransition();if(discontinuous)CaptureTransition(.3f);
     previousModelFrame=discontinuous?null:clip.frames[clip.frames.Length-1];
     float duration=clip.frames.Length/clip.fps;clock=Mathf.Max(0,clock-duration);if(NeuralSpeechMotion)speechChunkStart+=duration;
     clip=streamQueue.Dequeue();CurrentAction=clip.prompt;MotionSource=clip.source;
    }
   }else if(clock>=clip.Duration){ReleaseMotion();return;}
   float gap=Streaming&&(streamQueue.Count==0||BoundaryNeedsTransition())?Mathf.Max(0,clock-clip.Duration):0;
   bool starved=gap>1/clip.fps+.05f;
   if(NeuralSpeechMotion){if(starved&&!speechUnderrun)SpeechMotionUnderruns++;speechUnderrun=starved;}
   else {if(starved&&!streamUnderrun)StreamUnderruns++;streamUnderrun=starved;}
   bool predicting=gap>0&&gap<=PredictionHorizon;
   if(gap>PredictionHorizon&&!streamRecovering){KeepRootOrigin();CaptureTransition(.8f);localAction="idle";streamRecovering=true;}
   else if(gap<=0&&(streamRecovering||streamPredicting)){CaptureTransition(streamRecovering?.4f:.25f);streamRecovering=false;}
   streamPredicting=predicting&&!streamRecovering;
  }
 }
}
