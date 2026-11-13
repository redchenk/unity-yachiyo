using System;
using UnityEngine;

namespace Yachiyo {
 public partial class AvatarMotion {
  readonly System.Random idleRandom=new System.Random(27183);
  sealed class Drift {public float from,to,age,duration;public float Step(float dt,System.Random rng,float low,float high,float amplitude){age+=dt;if(age>=duration){from=to;to=((float)rng.NextDouble()*2-1)*amplitude;age=0;duration=low+(float)rng.NextDouble()*(high-low);}return Mathf.Lerp(from,to,Smooth5(age/duration));}}
  readonly Drift swayX=new Drift(),swayZ=new Drift(),leftWrist=new Drift(),rightWrist=new Drift(),fingerLeft=new Drift(),fingerRight=new Drift();
  float lifeClock,breathPhase,breathPeriod=4.3f,breathAmplitude=1,weightFrom=.45f,weightTo=.65f,weightAge,weightDuration=18;
  float energy=1,gazeClock,gazeAt=2,gazeChangedAt,gazeYaw,gazePitch,headYaw;
  Vector3 leftFootRest,rightFootRest;Quaternion leftFootRotation,rightFootRotation;
  Vector3 leftCurlAxis,rightCurlAxis;
  public float IdleWeight {get;private set;}
  public float IdleBreath=>Breath(0);
  public float IdleGazeYaw=>gazeYaw-(PoseMode=="idle"?headYaw:0);
  public float IdleGazePitch=>gazePitch;
  public int IdleWeightChanges {get;private set;}
  public int AttentionChanges {get;private set;}
  static readonly HumanBodyBones[] leftFingers={HumanBodyBones.LeftIndexProximal,HumanBodyBones.LeftIndexIntermediate,HumanBodyBones.LeftIndexDistal,HumanBodyBones.LeftMiddleProximal,HumanBodyBones.LeftMiddleIntermediate,HumanBodyBones.LeftMiddleDistal,HumanBodyBones.LeftRingProximal,HumanBodyBones.LeftRingIntermediate,HumanBodyBones.LeftRingDistal,HumanBodyBones.LeftLittleProximal,HumanBodyBones.LeftLittleIntermediate,HumanBodyBones.LeftLittleDistal};
  static readonly HumanBodyBones[] rightFingers={HumanBodyBones.RightIndexProximal,HumanBodyBones.RightIndexIntermediate,HumanBodyBones.RightIndexDistal,HumanBodyBones.RightMiddleProximal,HumanBodyBones.RightMiddleIntermediate,HumanBodyBones.RightMiddleDistal,HumanBodyBones.RightRingProximal,HumanBodyBones.RightRingIntermediate,HumanBodyBones.RightRingDistal,HumanBodyBones.RightLittleProximal,HumanBodyBones.RightLittleIntermediate,HumanBodyBones.RightLittleDistal};
  void InitializeIdle(){
   restRoot=transform.InverseTransformPoint(bones[HumanBodyBones.Hips].position);
   leftFootRest=transform.InverseTransformPoint(bones[HumanBodyBones.LeftFoot].position);rightFootRest=transform.InverseTransformPoint(bones[HumanBodyBones.RightFoot].position);
   leftFootRotation=Quaternion.Inverse(transform.rotation)*bones[HumanBodyBones.LeftFoot].rotation;rightFootRotation=Quaternion.Inverse(transform.rotation)*bones[HumanBodyBones.RightFoot].rotation;
   leftCurlAxis=CurlAxis(true);rightCurlAxis=CurlAxis(false);
  }
  Vector3 CurlAxis(bool left){
   var hand=bones[left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand];
   var index=bones[left?HumanBodyBones.LeftIndexProximal:HumanBodyBones.RightIndexProximal];var pinky=bones[left?HumanBodyBones.LeftLittleProximal:HumanBodyBones.RightLittleProximal];
   // Across the palm: mirrored anatomy gives both hands the same flexion sign.
   return hand.InverseTransformDirection((index.position-pinky.position).normalized)*(left?1:-1);
  }
  float RandomRange(float min,float max)=>min+(float)idleRandom.NextDouble()*(max-min);
  float Breath(float delay){float phase=Mathf.Repeat(breathPhase-delay/breathPeriod,1);return (phase<.38f?Smooth5(phase/.38f):1-Smooth5((phase-.38f)/.62f))*breathAmplitude;}
  void AdvanceLife(float dt){
   lifeClock+=dt;breathPhase+=dt/breathPeriod;
   if(breathPhase>=1){breathPhase-=1;breathPeriod=RandomRange(3.6f,5.2f);breathAmplitude=RandomRange(.78f,1.08f);}
   weightAge+=dt;if(weightAge>=weightDuration){weightFrom=weightTo;weightTo=-Mathf.Sign(weightTo)*RandomRange(.5f,.9f);weightAge=0;weightDuration=RandomRange(16,28);IdleWeightChanges++;}
   // Shift over several seconds, then settle on the supporting leg.
   IdleWeight=Mathf.Lerp(weightFrom,weightTo,Smooth5(weightAge/4.5f));
   float desired=stateEmotion=="happy"||stateEmotion=="playful"?1.1f:stateMode=="listening"||stateEmotion=="sad"?.75f:1;
   energy=Mathf.MoveTowards(energy,desired,dt*.15f);
   gazeClock+=dt;if(gazeClock>=gazeAt){gazeAt=gazeClock+RandomRange(2.2f,6.3f);gazeYaw=RandomRange(-3.8f,3.8f);gazePitch=RandomRange(-1.1f,1.3f);gazeChangedAt=lifeClock;AttentionChanges++;}
   if(lifeClock-gazeChangedAt>.11f)headYaw=Mathf.Lerp(headYaw,gazeYaw*.45f,1-Mathf.Exp(-dt*2.8f));
  }
  void ApplyNaturalIdle(float dt){
   float x=swayX.Step(dt,idleRandom,5.1f,8.7f,.003f),z=swayZ.Step(dt,idleRandom,7.3f,11.4f,.002f);
   bones[HumanBodyBones.Hips].position+=transform.TransformVector(new Vector3(IdleWeight*.023f+x,-.009f,z));
   RotateWorld(HumanBodyBones.Hips,Vector3.forward,-IdleWeight*1.3f);
   RotateWorld(HumanBodyBones.Spine,Vector3.forward,IdleWeight*.9f);
   RotateWorld(HumanBodyBones.Chest,Vector3.forward,IdleWeight*.4f);
   RotateLocal(HumanBodyBones.Spine,new Vector3(-Breath(.02f)*.23f*energy,0,0));
   RotateLocal(HumanBodyBones.Chest,new Vector3(-Breath(.09f)*.5f*energy,0,0));
   RotateLocal(HumanBodyBones.Neck,new Vector3(Breath(.13f)*.24f*energy,headYaw*.35f,0));
   RotateLocal(HumanBodyBones.Head,new Vector3(Breath(.17f)*.49f*energy,headYaw*.65f,0));
   RotateWorld(HumanBodyBones.LeftShoulder,Vector3.forward,-Breath(.12f)*.45f*energy);
   RotateWorld(HumanBodyBones.RightShoulder,Vector3.forward,Breath(.18f)*.38f*energy);
   RotateLocal(HumanBodyBones.LeftUpperArm,new Vector3(Breath(.19f)*.35f,0,0));
   RotateLocal(HumanBodyBones.RightUpperArm,new Vector3(Breath(.24f)*.28f,0,0));
   RotateLocal(HumanBodyBones.LeftLowerArm,new Vector3(Breath(.25f)*.4f,0,0));
   RotateLocal(HumanBodyBones.RightLowerArm,new Vector3(Breath(.30f)*.32f,0,0));
   float lw=leftWrist.Step(dt,idleRandom,4.6f,9.1f,1.5f),rw=rightWrist.Step(dt,idleRandom,6.2f,10.7f,1.2f);
   RotateLocal(HumanBodyBones.LeftHand,new Vector3(Breath(.32f)*.3f,lw,0));RotateLocal(HumanBodyBones.RightHand,new Vector3(Breath(.37f)*.24f,rw,0));
   FlexFingers(true,fingerLeft.Step(dt,idleRandom,5.5f,10.2f,1.7f));FlexFingers(false,fingerRight.Step(dt,idleRandom,7.4f,12.8f,1.5f));
   PlantFoot(true);PlantFoot(false);
  }
  void FlexFingers(bool left,float drift){
   var hand=bones[left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand];var axis=hand.TransformDirection(left?leftCurlAxis:rightCurlAxis);
   var fingers=left?leftFingers:rightFingers;
   for(int i=0;i<fingers.Length;i++)if(bones.TryGetValue(fingers[i],out var joint)){float curl=(i/3)*1.1f+3+(i%3==1?3:0)+drift*(.5f+(i/3)*.12f);joint.rotation=Quaternion.AngleAxis(curl,axis)*joint.rotation;}
  }
  void PlantFoot(bool left){
   var upper=bones[left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg];var lower=bones[left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg];var foot=bones[left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot];
   var target=transform.TransformPoint((left?leftFootRest:rightFootRest)+idleOrigin);var start=upper.position;
   float a=Vector3.Distance(start,lower.position),b=Vector3.Distance(lower.position,foot.position);var direction=target-start;float distance=Mathf.Clamp(direction.magnitude,.001f,a+b-.00001f);direction.Normalize();
   var bend=Vector3.ProjectOnPlane(transform.forward,direction).normalized;float along=(a*a+distance*distance-b*b)/(2*distance);
   var knee=start+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
   upper.rotation=Quaternion.FromToRotation(lower.position-start,knee-start)*upper.rotation;
   lower.rotation=Quaternion.FromToRotation(foot.position-lower.position,target-lower.position)*lower.rotation;
   foot.rotation=transform.rotation*(left?leftFootRotation:rightFootRotation);
  }
 }
}
