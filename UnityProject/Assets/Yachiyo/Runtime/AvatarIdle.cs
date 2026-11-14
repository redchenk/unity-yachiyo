using System;
using UnityEngine;

namespace Yachiyo {
 public partial class AvatarMotion {
  readonly System.Random idleRandom=CreateIdleRandom();
  sealed class Drift {public float from,to,age,duration;public float Step(float dt,System.Random rng,float low,float high,float amplitude){age+=dt;if(age>=duration){from=to;to=((float)rng.NextDouble()*2-1)*amplitude;age=0;duration=low+(float)rng.NextDouble()*(high-low);}return Mathf.Lerp(from,to,Smooth5(age/duration));}}
  readonly Drift swayX=new Drift(),swayZ=new Drift(),leftWrist=new Drift(),rightWrist=new Drift(),fingerLeft=new Drift(),fingerRight=new Drift();
  readonly Drift torsoYaw=new Drift(),torsoPitch=new Drift(),leftArm=new Drift(),rightArm=new Drift(),leftElbow=new Drift(),rightElbow=new Drift(),leftShoulder=new Drift(),rightShoulder=new Drift();
  float lifeClock,breathPhase,breathPeriod=4.3f,breathAmplitude=1,weightFrom,weightTo,weightAge,weightDuration,weightTravel=3.5f,relaxedArms=1;
  bool adjustedSameSide;
  float energy=1,gazeClock,gazeAt=2,gazeChangedAt,gazeYaw,gazePitch,headYaw;
  Vector3 leftFootRest,rightFootRest;Quaternion leftFootRotation,rightFootRotation;
  Vector3 leftCurlAxis,rightCurlAxis;
  public float IdleWeight {get;private set;}
  public float IdleBreath=>Breath(0);
  public float IdleGazeYaw=>gazeYaw-(PoseMode=="idle"?headYaw:0);
  public float IdleGazePitch=>gazePitch;
  public int IdleWeightChanges {get;private set;}
  public int AttentionChanges {get;private set;}
  static System.Random CreateIdleRandom(){
   var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--idle-seed");
   // Reproducible only when explicitly requested by a native regression run.
   if(index>=0&&index+1<args.Length&&int.TryParse(args[index+1],out int seed))return new System.Random(seed);
   return new System.Random(Guid.NewGuid().GetHashCode());
  }
  static readonly HumanBodyBones[] leftFingers={HumanBodyBones.LeftIndexProximal,HumanBodyBones.LeftIndexIntermediate,HumanBodyBones.LeftIndexDistal,HumanBodyBones.LeftMiddleProximal,HumanBodyBones.LeftMiddleIntermediate,HumanBodyBones.LeftMiddleDistal,HumanBodyBones.LeftRingProximal,HumanBodyBones.LeftRingIntermediate,HumanBodyBones.LeftRingDistal,HumanBodyBones.LeftLittleProximal,HumanBodyBones.LeftLittleIntermediate,HumanBodyBones.LeftLittleDistal};
  static readonly HumanBodyBones[] rightFingers={HumanBodyBones.RightIndexProximal,HumanBodyBones.RightIndexIntermediate,HumanBodyBones.RightIndexDistal,HumanBodyBones.RightMiddleProximal,HumanBodyBones.RightMiddleIntermediate,HumanBodyBones.RightMiddleDistal,HumanBodyBones.RightRingProximal,HumanBodyBones.RightRingIntermediate,HumanBodyBones.RightRingDistal,HumanBodyBones.RightLittleProximal,HumanBodyBones.RightLittleIntermediate,HumanBodyBones.RightLittleDistal};
  void InitializeIdle(){
   restRoot=transform.InverseTransformPoint(bones[HumanBodyBones.Hips].position);
   leftFootRest=transform.InverseTransformPoint(bones[HumanBodyBones.LeftFoot].position);rightFootRest=transform.InverseTransformPoint(bones[HumanBodyBones.RightFoot].position);
   leftFootRotation=Quaternion.Inverse(transform.rotation)*bones[HumanBodyBones.LeftFoot].rotation;rightFootRotation=Quaternion.Inverse(transform.rotation)*bones[HumanBodyBones.RightFoot].rotation;
   leftCurlAxis=CurlAxis(true);rightCurlAxis=CurlAxis(false);
   weightFrom=weightTo=(idleRandom.Next(2)==0?-1:1)*RandomRange(.55f,.85f);weightDuration=RandomRange(3.5f,6.5f);
   breathPeriod=RandomRange(3.5f,5.2f);breathPhase=RandomRange(0,1);gazeAt=RandomRange(1.2f,3.5f);
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
   if(breathPhase>=1){breathPhase-=1;breathPeriod=RandomRange(3.5f,5.2f);breathAmplitude=RandomRange(.78f,1.08f);}
   weightAge+=dt;if(weightAge>=weightDuration){
    weightFrom=weightTo;
    // Occasionally readjust the same supporting leg. Never get stuck there;
    // the next transfer crosses sides, with a newly sampled pace and dwell.
    bool stay=!adjustedSameSide&&idleRandom.NextDouble()<.22;adjustedSameSide=stay;
    weightTo=(stay?1:-1)*Mathf.Sign(weightTo)*RandomRange(stay?.35f:.65f,stay?.6f:.95f);
    weightAge=0;weightTravel=RandomRange(2.8f,4.4f);weightDuration=RandomRange(7.5f,13.5f);IdleWeightChanges++;
   }
   // Shift over several seconds, then settle on the supporting leg.
   IdleWeight=Mathf.Lerp(weightFrom,weightTo,Smooth5(weightAge/weightTravel));
   float desired=stateEmotion=="happy"||stateEmotion=="playful"?1.1f:stateMode=="listening"||stateEmotion=="sad"?.75f:1;
   energy=Mathf.MoveTowards(energy,desired,dt*.15f);
   gazeClock+=dt;if(gazeClock>=gazeAt){gazeAt=gazeClock+RandomRange(2.0f,5.8f);gazeYaw=RandomRange(-6.8f,6.8f);gazePitch=RandomRange(-1.4f,1.8f);gazeChangedAt=lifeClock;AttentionChanges++;}
   if(lifeClock-gazeChangedAt>.11f)headYaw=Mathf.Lerp(headYaw,gazeYaw*.6f,1-Mathf.Exp(-dt*2.6f));
  }
  void ApplyNaturalIdle(float dt){
   float x=swayX.Step(dt,idleRandom,3.8f,7.3f,.005f),z=swayZ.Step(dt,idleRandom,5.7f,9.2f,.0035f);
   bones[HumanBodyBones.Hips].position+=transform.TransformVector(new Vector3(IdleWeight*.04f+x,-.013f,z));
   // Pelvis and spine counterbalance; no head roll or thinking-state trigger.
   RotateWorld(HumanBodyBones.Hips,Vector3.forward,-IdleWeight*2.1f);
   RotateWorld(HumanBodyBones.Spine,Vector3.forward,IdleWeight*1.35f);
   RotateWorld(HumanBodyBones.Chest,Vector3.forward,IdleWeight*.75f);
   float yaw=torsoYaw.Step(dt,idleRandom,4.1f,8.4f,3.4f),pitch=torsoPitch.Step(dt,idleRandom,5.3f,9.1f,1.4f);
   RotateLocal(HumanBodyBones.Spine,new Vector3(pitch*.35f-Breath(.02f)*.5f*energy,yaw*.4f,0));
   RotateLocal(HumanBodyBones.Chest,new Vector3(pitch*.65f-Breath(.09f)*1.15f*energy,yaw*.6f,0));
   RotateLocal(HumanBodyBones.Neck,new Vector3((Breath(.13f)*.5f*energy-pitch*.5f),headYaw*.35f-yaw*.18f,0));
   RotateLocal(HumanBodyBones.Head,new Vector3(Breath(.17f)*.7f*energy-pitch*.5f,headYaw*.65f-yaw*.32f,0));
   RotateWorld(HumanBodyBones.LeftShoulder,Vector3.forward,-Breath(.12f)*.8f*energy+leftShoulder.Step(dt,idleRandom,3.7f,6.5f,.8f));
   RotateWorld(HumanBodyBones.RightShoulder,Vector3.forward,Breath(.18f)*.7f*energy+rightShoulder.Step(dt,idleRandom,4.9f,8.1f,.7f));
   // Independent, continuous pose targets: never mirrored beats or per-frame
   // noise. An explicit conversational gesture smoothly takes over the arms.
   relaxedArms=Mathf.MoveTowards(relaxedArms,speechActive||ConversationGestureActive?0:1,dt/.4f);
   RelaxIdleArm(true,dt,relaxedArms);RelaxIdleArm(false,dt,relaxedArms);
   FlexFingers(true,fingerLeft.Step(dt,idleRandom,3.2f,6.4f,3.5f));FlexFingers(false,fingerRight.Step(dt,idleRandom,4.3f,7.9f,3.1f));
   PlantFoot(true);PlantFoot(false);
  }
  void RelaxIdleArm(bool left,float dt,float amount){
   float side=left?-1:1;
   float upper=(left?leftArm:rightArm).Step(dt,idleRandom,left?3.4f:4.6f,left?7.2f:8.3f,4.5f);
   float elbow=(left?leftElbow:rightElbow).Step(dt,idleRandom,left?4.3f:3.6f,left?8.8f:7.9f,7.5f);
   float wrist=(left?leftWrist:rightWrist).Step(dt,idleRandom,left?3.1f:4.2f,left?6.8f:8.4f,5);
   var arm=left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm;
   var forearm=left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm;
   var hand=left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand;
   RotateWorld(arm,Vector3.forward,side*(3+upper*.45f)*amount);
   RotateWorld(arm,Vector3.right,-(4+upper+Breath(left?.19f:.27f)*.65f)*amount);
   RotateWorld(forearm,Vector3.right,-(8+elbow+Breath(left?.26f:.33f)*.8f)*amount);
   RotateLocal(hand,new Vector3(wrist*.45f,side*wrist,wrist*.2f)*amount);
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
