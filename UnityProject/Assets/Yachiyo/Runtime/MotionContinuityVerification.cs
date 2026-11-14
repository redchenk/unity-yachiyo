using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Yachiyo {
 // Deterministic native-skeleton regression. No service, network or audio fixture
 // replaces the player: this exercises the same public motion entry points.
 public sealed class MotionContinuityVerification : MonoBehaviour {
  const float Dt=1f/60;
  [Serializable]class Report {
   public string latePacketDiagnostic;
   public bool passed;public List<string> failures=new List<string>();
   public float endDepartureAt150ms,speechEndDepartureAt150ms,maxTransitionStep,maxRootStep,maxHandoffVelocityError,pauseDrift;
   public float maxCurveVelocityStep,maxLatePacketStep,maxMediaClockError,minMediaClockStep=1,maxPredictionGap;public int predictedFrames,frozenPredictionFrames,unexpectedRecoveryFrames;
   public float streamedBoundaryStep,streamRecoveryStep;public bool finiteEndsImmediately,repeatedReleaseDoesNotRestart,latePacketResumes,streamSettles,returnedToIdle;
  }
  string path;AvatarMotion avatar;MotionClip source;Report report=new Report();
  static readonly HumanBodyBones[] tracked={HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.Head,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightHand};
  static readonly HashSet<string> moving=new HashSet<string>{"RightUpperArm","RightLowerArm","RightHand"};
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var args=Environment.GetCommandLineArgs();int p=Array.IndexOf(args,"--verify-motion-continuity");if(p>=0&&p+1<args.Length)new GameObject("Motion continuity verification").AddComponent<MotionContinuityVerification>().path=args[p+1];}
  IEnumerator Start(){
   yield return new WaitForSeconds(4);avatar=FindFirstObjectByType<AvatarMotion>();avatar.followPointer=false;avatar.enabled=false;
   try{source=JsonUtility.FromJson<MotionClip>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Motions/kimodo-official.json")));Run();}
   catch(Exception e){report.failures.Add(e.ToString());}
   avatar.ResetConversation();avatar.ReleaseMotion();avatar.enabled=true;
   report.passed=report.failures.Count==0;File.WriteAllText(path+".json",JsonUtility.ToJson(report,true));Debug.Log("MOTION_CONTINUITY_VERIFIED "+JsonUtility.ToJson(report));
   yield return new WaitForSeconds(1);ScreenCapture.CaptureScreenshot(path+".png");yield return new WaitForEndOfFrame();Application.Quit();
  }
  Quaternion[] Pose()=>tracked.Select(b=>avatar.animator.GetBoneTransform(b).localRotation).ToArray();
  static float Difference(Quaternion[] a,Quaternion[] b){float max=0;for(int i=0;i<a.Length;i++)max=Mathf.Max(max,Quaternion.Angle(a[i],b[i]));return max;}
  static Vector3 Velocity(Quaternion a,Quaternion b){var d=b*Quaternion.Inverse(a);d.ToAngleAxis(out float angle,out var axis);if(angle>180)angle-=360;return float.IsFinite(axis.x)&&Mathf.Abs(angle)>.001f?axis*angle/Dt:Vector3.zero;}
  void Check(bool ok,string message){if(!ok)report.failures.Add(message);}
  void Steps(int count,Action<int> before=null){for(int n=0;n<count;n++){before?.Invoke(n);avatar.Tick(Dt);}}
  MotionClip Clip(float angle,float speed=0,int frames=120,int startFrame=0){
   var basis=source.frames[Mathf.Min(35,source.frames.Length-1)];var result=new MotionFrame[frames];
   for(int n=0;n<frames;n++){
    var rotations=(Quaternion[])basis.rotations.Clone();
    for(int j=0;j<rotations.Length;j++)if(moving.Contains(source.bones[j]))rotations[j]=Quaternion.AngleAxis(angle+speed*n/30,Vector3.forward)*rotations[j];
    result[n]=new MotionFrame{root=basis.root,rotations=rotations};
   }
   return new MotionClip{version=1,coordinates=source.coordinates,bones=source.bones,fps=30,referenceHipHeight=source.referenceHipHeight,source="Native transition fixture",frames=result,startFrame=startFrame};
  }
  void Track(int frames){
   for(int n=0;n<frames;n++){
    var before=Pose();var root=avatar.animator.GetBoneTransform(HumanBodyBones.Hips).position;avatar.Tick(Dt);
    report.maxTransitionStep=Mathf.Max(report.maxTransitionStep,Difference(before,Pose()));
    report.maxRootStep=Mathf.Max(report.maxRootStep,Vector3.Distance(root,avatar.animator.GetBoneTransform(HumanBodyBones.Hips).position));
   }
  }
  void Handoff(MotionClip next){
   var a=Pose();avatar.Tick(Dt);var b=Pose();avatar.Play(next,"continuity");avatar.Tick(Dt);var c=Pose();
   for(int i=0;i<a.Length;i++)report.maxHandoffVelocityError=Mathf.Max(report.maxHandoffVelocityError,(Velocity(a[i],b[i])-Velocity(b[i],c[i])).magnitude);
   report.maxTransitionStep=Mathf.Max(report.maxTransitionStep,Difference(b,c));Track(42);
  }
  void Run(){
   avatar.ReleaseMotion();Steps(90);
   // A raised hand must start lowering now, not after a 650 ms end-pose hold.
   avatar.Play(Clip(35),"end");Steps(60);var end=Pose();avatar.ReleaseMotion();Steps(9);
   report.endDepartureAt150ms=Difference(end,Pose());Check(report.endDepartureAt150ms>1,"End pose stayed frozen for 150 ms");
   float progress=avatar.TransitionProgress;avatar.ReleaseMotion();report.repeatedReleaseDoesNotRestart=avatar.TransitionProgress>=progress;Steps(42);
   report.returnedToIdle=avatar.PoseMode=="idle";Check(report.returnedToIdle,"Release did not finish within 850 ms");Check(report.repeatedReleaseDoesNotRestart,"Duplicate release restarted settling");
   // Two moving model clips, then another interruption during their blend.
   avatar.Play(Clip(10,24),"moving A");Steps(60);Handoff(Clip(-25,-18));
   avatar.Play(Clip(30,20),"interrupt A");Steps(6);Handoff(Clip(-15,12));
   Check(report.maxHandoffVelocityError<12,"Handoff changed angular velocity abruptly");
   // Finite clips enter the release on their last sample, without an end delay.
   avatar.Play(Clip(35,0,31),"finite");Steps(61);report.finiteEndsImmediately=!avatar.Playing&&avatar.SettlingToIdle;Check(report.finiteEndsImmediately,"Finite clip held the last frame");Track(50);
   // Discontinuous streamed chunks must not jump in the single seam frame.
   avatar.Enqueue(Clip(20,0,31),"stream A");avatar.Enqueue(Clip(-30,0,31),"stream B");Steps(54);
   for(int n=0;n<20;n++){var before=Pose();avatar.Tick(Dt);report.streamedBoundaryStep=Mathf.Max(report.streamedBoundaryStep,Difference(before,Pose()));}
   Steps(180);report.streamSettles=avatar.PoseMode=="idle";Check(report.streamSettles,"A starved stream froze the last pose");
   avatar.Enqueue(Clip(30,15,90),"late packet");report.latePacketDiagnostic=State();
   for(int n=0;n<32;n++){var before=Pose();avatar.Tick(Dt);report.streamRecoveryStep=Mathf.Max(report.streamRecoveryStep,Difference(before,Pose()));}
   report.latePacketDiagnostic+=" -> "+State();report.latePacketResumes=avatar.PoseMode=="model";Check(report.latePacketResumes,"Late packet did not resume its stream");
   // Pause within the entrance blend; both media and inertialization must stop.
   avatar.SpeechAudio(true);avatar.BeginSpeechMotion();avatar.AddSpeechMotion(Clip(35));
   Steps(8,n=>avatar.SpeechFrame(n*Dt,0));avatar.SpeechAudio(false);avatar.SpeechFrame(7*Dt,0);var paused=Pose();Steps(24);report.pauseDrift=Difference(paused,Pose());Check(report.pauseDrift<.05f,"Motion blend advanced during audio pause");
   avatar.SpeechAudio(true);Steps(60,n=>avatar.SpeechFrame((8+n)*Dt,0));var speechEnd=Pose();avatar.SpeechAudio(false);avatar.EndSpeechMotion();Steps(9);report.speechEndDepartureAt150ms=Difference(speechEnd,Pose());Check(report.speechEndDepartureAt150ms>1,"Speech ended with a frozen hand pose");Steps(42);
   Check(avatar.PoseMode=="idle","Speech release never reached idle");
   Check(report.maxTransitionStep<8&&report.streamedBoundaryStep<8&&report.streamRecoveryStep<8,"Transition exceeded 8 degrees in one 60 Hz frame");Check(report.maxRootStep<.02f,"Root snapped during transition");
   VerifyTrajectory();
  }
  string State()=>avatar.PoseMode+" playing="+avatar.Playing+" streaming="+avatar.Streaming+" neural="+avatar.NeuralSpeechMotion+" buffered="+avatar.BufferedSeconds+" progress="+avatar.Progress;
  static Vector3 StableVelocity(Quaternion a,Quaternion b){var q=b*Quaternion.Inverse(a);var v=new Vector3(q.x,q.y,q.z);if(q.w<0)v=-v;float n=v.magnitude;return n<1e-7f?Vector3.zero:v*(2*Mathf.Atan2(n,Mathf.Abs(q.w))*Mathf.Rad2Deg/(n*Dt));}
  void VerifyTrajectory(){
   // A quadratic angle has smoothly increasing velocity. Packet boundaries
   // must preserve the same derivative as samples inside a packet.
   var hand=avatar.animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
   var whole=Clip(0,0,80);whole.fps=20;
   for(int n=0;n<80;n++)for(int j=0;j<whole.bones.Length;j++)if(moving.Contains(whole.bones[j]))whole.frames[n].rotations[j]=Quaternion.AngleAxis(20*Mathf.Pow(n/20f,2),Vector3.forward)*whole.frames[n].rotations[j];
   for(int n=0;n<4;n++){
    var part=new MotionClip{version=1,coordinates=whole.coordinates,bones=whole.bones,fps=20,referenceHipHeight=whole.referenceHipHeight,source="Quadratic trajectory",frames=whole.frames.Skip(n*20).Take(20).ToArray()};
    if(n==0)avatar.StartStream(part,"curve");else avatar.Enqueue(part,"curve");
   }
   Steps(40);var previous=hand.rotation;avatar.Tick(Dt);var velocity=StableVelocity(previous,hand.rotation);previous=hand.rotation;
   for(int n=0;n<120;n++){avatar.Tick(Dt);var next=StableVelocity(previous,hand.rotation);report.maxCurveVelocityStep=Mathf.Max(report.maxCurveVelocityStep,(next-velocity).magnitude);previous=hand.rotation;velocity=next;}
   Check(report.maxCurveVelocityStep<1.2f,"Trajectory velocity stepped at a sample or packet boundary");
   foreach(float delay in new[]{.1f,.3f,.65f,.85f}){
    avatar.StartStream(Clip(0,24,30),"predict");Steps(60);previous=hand.rotation;
    for(int n=0;n<Mathf.RoundToInt(delay/Dt);n++){
     avatar.Tick(Dt);var next=hand.rotation;
     if(StableVelocity(previous,next).magnitude<.01f)report.frozenPredictionFrames++;
     if(avatar.PredictingMotion)report.predictedFrames++;
     if(avatar.PoseMode=="settling"||avatar.PoseMode=="idle")report.unexpectedRecoveryFrames++;
     report.maxPredictionGap=Mathf.Max(report.maxPredictionGap,avatar.PredictionGap);previous=next;
    }
    avatar.Enqueue(Clip(24,24,90),"late continuation");
    for(int n=0;n<30;n++){previous=hand.rotation;avatar.Tick(Dt);report.maxLatePacketStep=Mathf.Max(report.maxLatePacketStep,StableVelocity(previous,hand.rotation).magnitude*Dt);}
   }
   Check(report.predictedFrames>=100&&report.frozenPredictionFrames==0&&report.unexpectedRecoveryFrames==0,"A delayed packet stopped the trajectory or triggered premature idle");
   Check(report.maxLatePacketStep<4,"Late trajectory correction snapped");
   // Media reports arrive every 117 ms. Render time should still advance on
   // every 60 Hz frame, rather than stopping at a 50 ms extrapolation cap.
   avatar.SpeechAudio(true);avatar.BeginSpeechMotion();avatar.AddSpeechMotion(Clip(0,24,120));
   float last=avatar.SpeechRenderClock;
   for(int n=0;n<110;n++){
    if(n%7==0)avatar.SpeechFrame(n*Dt,0);avatar.Tick(Dt);
    report.minMediaClockStep=Mathf.Min(report.minMediaClockStep,avatar.SpeechRenderClock-last);
    report.maxMediaClockError=Mathf.Max(report.maxMediaClockError,Mathf.Abs(avatar.SpeechRenderClock-(n+1)*Dt));last=avatar.SpeechRenderClock;
   }
   Check(report.minMediaClockStep>Dt*.8f&&report.maxMediaClockError<.04f,"Sparse audio reports caused a render-clock stall or drift");
   avatar.SpeechAudio(false);avatar.EndSpeechMotion();Steps(60);
  }
 }
}
