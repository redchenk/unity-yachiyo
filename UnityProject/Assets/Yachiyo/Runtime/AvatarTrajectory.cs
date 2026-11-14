using UnityEngine;

namespace Yachiyo {
 public partial class AvatarMotion {
  // Prediction only bridges missing samples of the current AI trajectory. It
  // cannot create a new semantic action and ends at a bounded, resting velocity.
  const float PredictionHorizon=1.1f;
  MotionFrame previousModelFrame;
  bool streamPredicting;
  public bool PredictingMotion=>streamPredicting;
  public float PredictionGap=>streamPredicting?Mathf.Max(0,clock-clip.Duration):0;
  MotionFrame FrameAt(int index){
   if(index<0)return previousModelFrame??clip.frames[0];
   if(index<clip.frames.Length)return clip.frames[index];
   if(streamQueue.Count>0&&!BoundaryNeedsTransition()){var next=streamQueue.Peek();return next.frames[Mathf.Min(index-clip.frames.Length,next.frames.Length-1)];}
   return clip.frames[clip.frames.Length-1];
  }
  static float Tangent(float before,float after){
   if(before*after<=0)return 0;
   return Mathf.Sign(after)*Mathf.Min(Mathf.Abs((before+after)*.5f),3*Mathf.Min(Mathf.Abs(before),Mathf.Abs(after)));
  }
  static Vector3 Tangent(Vector3 before,Vector3 after)=>new Vector3(Tangent(before.x,after.x),Tangent(before.y,after.y),Tangent(before.z,after.z));
  static Quaternion RotationCurve(Quaternion first,Quaternion second,Vector3 start,Vector3 end,float t){
   var a=Travel(first,start,1f/3);var b=Travel(second,end,-1f/3);
   var left=Quaternion.Slerp(first,a,t);var middle=Quaternion.Slerp(a,b,t);var right=Quaternion.Slerp(b,second,t);
   return Quaternion.Slerp(Quaternion.Slerp(left,middle,t),Quaternion.Slerp(middle,right,t),t);
  }
  static Vector3 PositionCurve(Vector3 first,Vector3 second,Vector3 start,Vector3 end,float t){
   float t2=t*t,t3=t2*t;return (2*t3-3*t2+1)*first+(t3-2*t2+t)*start+(-2*t3+3*t2)*second+(t3-t2)*end;
  }
  Vector3 TailAngularVelocity(int bone){
   int last=clip.frames.Length-1;var a=FrameAt(last-2).rotations[bone];var b=FrameAt(last-1).rotations[bone];var c=FrameAt(last).rotations[bone];
   return Vector3.ClampMagnitude((RotationVector(c*Quaternion.Inverse(b))*.75f+RotationVector(b*Quaternion.Inverse(a))*.25f)*clip.fps,240);
  }
  Vector3 TailRootVelocity(){int n=clip.frames.Length-1;return Vector3.ClampMagnitude(((FrameAt(n).root-FrameAt(n-1).root)*.75f+(FrameAt(n-1).root-FrameAt(n-2).root)*.25f)*clip.fps,.6f);}
  static Vector3 PredictDisplacement(Vector3 velocity,Vector3 acceleration,float seconds,float maxDistance){
   // Integrals of a quintic velocity decay and a bounded acceleration lobe.
   // Position and velocity agree at t=0; velocity/acceleration vanish at T.
   float horizon=Mathf.Min(PredictionHorizon,maxDistance/Mathf.Max(.001f,velocity.magnitude*.5f+acceleration.magnitude*.05f*PredictionHorizon));
   horizon=Mathf.Max(.05f,horizon);float u=Mathf.Clamp01(seconds/horizon),u2=u*u,u3=u2*u,u4=u3*u,u5=u4*u,u6=u5*u;
   return velocity*(horizon*(u-2.5f*u4+3*u5-u6))+acceleration*(horizon*horizon*(.5f*u2-u3+.75f*u4-.2f*u5));
  }
  float RotationPredictionLimit(HumanBodyBones bone){
   if(bone==HumanBodyBones.Hips||bone==HumanBodyBones.Head||bone==HumanBodyBones.Neck)return 12;
   if(bone==HumanBodyBones.LeftUpperLeg||bone==HumanBodyBones.RightUpperLeg||bone==HumanBodyBones.LeftLowerLeg||bone==HumanBodyBones.RightLowerLeg||bone==HumanBodyBones.LeftFoot||bone==HumanBodyBones.RightFoot)return 18;
   return 40;
  }
  void ApplyModelPose(float time){
   bool nextAvailable=streamQueue.Count>0&&!BoundaryNeedsTransition();
   float frame=Mathf.Max(0,time*clip.fps);int a=Mathf.Min((int)frame,clip.frames.Length-1);float t=Mathf.Clamp01(frame-a);
   var first=FrameAt(a);var second=FrameAt(a+1);var previous=FrameAt(a-1);var next=FrameAt(a+2);
   bool predict=time>=clip.Duration&&!nextAvailable;
   for(int j=0;j<mapping.Length;j++)if(bones.TryGetValue(mapping[j],out var bone)){
    Quaternion q;
    if(predict){
     var v=TailAngularVelocity(j);var recent=RotationVector(first.rotations[j]*Quaternion.Inverse(previous.rotations[j]))*clip.fps;
     var older=RotationVector(previous.rotations[j]*Quaternion.Inverse(FrameAt(a-2).rotations[j]))*clip.fps;
     var acceleration=Vector3.ClampMagnitude((recent-older)*clip.fps*.25f,100);
     q=Travel(first.rotations[j],PredictDisplacement(v,acceleration,time-clip.Duration,RotationPredictionLimit(mapping[j])),1);
    }else{
     var delta=RotationVector(second.rotations[j]*Quaternion.Inverse(first.rotations[j]));
     var before=a==0&&previousModelFrame==null?delta:RotationVector(first.rotations[j]*Quaternion.Inverse(previous.rotations[j]));
     var after=RotationVector(next.rotations[j]*Quaternion.Inverse(second.rotations[j]));
     var start=Tangent(before,delta);var end=a+1==clip.frames.Length-1&&!nextAvailable?TailAngularVelocity(j)/clip.fps:Tangent(delta,after);
     q=RotationCurve(first.rotations[j],second.rotations[j],start,end,t);
    }
    bone.rotation=transform.rotation*q*restGlobal[mapping[j]];
   }
   Vector3 root;
   if(predict){var v=TailRootVelocity();root=first.root+PredictDisplacement(v,Vector3.zero,time-clip.Duration,.12f);}
   else{
    var delta=second.root-first.root;var before=a==0&&previousModelFrame==null?delta:first.root-previous.root;
    var end=a+1==clip.frames.Length-1&&!nextAvailable?TailRootVelocity()/clip.fps:Tangent(delta,next.root-second.root);
    root=PositionCurve(first.root,second.root,Tangent(before,delta),end,t);
   }
   root.x-=startRoot.x;root.z-=startRoot.z;root*=hipHeight/clip.referenceHipHeight;
   bones[HumanBodyBones.Hips].position=transform.TransformPoint(root+motionOrigin);
  }
 }
}
