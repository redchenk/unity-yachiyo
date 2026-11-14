using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Yachiyo {
 // Native regression, enabled only by an explicit command-line flag.
 public sealed class NaturalMotionVerification : MonoBehaviour {
  [Serializable]class Report {public bool passed,finite=true,returnedToIdle;public int idleFrames,weightChanges,attentionChanges,blinks,unexpectedIdleFrames;public float fps,maxFootDrift,hipRange,maxSkinningError,maxSeamGap,largestTransitionStep,largestRootStep,repeatStanceDrift;public List<float> blinkTimes=new List<float>();}
  string path;AvatarMotion avatar;GarmentPhysics cloth;YachiyoFaceController face;Report report=new Report();
  static readonly HumanBodyBones[] tracked={HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.Head,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightHand};
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var a=Environment.GetCommandLineArgs();int p=Array.IndexOf(a,"--verify-natural-motion");if(p>=0&&p+1<a.Length)new GameObject("Natural motion verification").AddComponent<NaturalMotionVerification>().path=a[p+1];}
  IEnumerator Start(){
   yield return new WaitForSeconds(4);avatar=FindFirstObjectByType<AvatarMotion>();cloth=avatar.GetComponent<GarmentPhysics>();face=avatar.GetComponent<YachiyoFaceController>();var ui=FindFirstObjectByType<StudioUI>();ui.ResetDirectorCamera();avatar.followPointer=false;
   avatar.Act("idle","自然待机验证");yield return new WaitForSeconds(1.5f);
   var left=avatar.animator.GetBoneTransform(HumanBodyBones.LeftFoot);var right=avatar.animator.GetBoneTransform(HumanBodyBones.RightFoot);var hips=avatar.animator.GetBoneTransform(HumanBodyBones.Hips);
   var lp=left.position;var rp=right.position;float min=hips.position.x,max=min,start=Time.realtimeSinceStartup;int blinks=face.BlinkCount;
   while(Time.realtimeSinceStartup-start<32){
    yield return new WaitForEndOfFrame();report.idleFrames++;report.finite&=cloth.Native.Finite;
    report.maxFootDrift=Mathf.Max(report.maxFootDrift,Vector3.Distance(left.position,lp),Vector3.Distance(right.position,rp));min=Mathf.Min(min,hips.position.x);max=Mathf.Max(max,hips.position.x);
    if(face.BlinkCount!=blinks){report.blinkTimes.Add(Time.realtimeSinceStartup-start);blinks=face.BlinkCount;}
    foreach(var h in cloth.Native.hems){report.maxSeamGap=Mathf.Max(report.maxSeamGap,h.SeamError);if(report.idleFrames%120==0)report.maxSkinningError=Mathf.Max(report.maxSkinningError,h.VerifySeamSkinning());}
   }
   report.fps=report.idleFrames/(Time.realtimeSinceStartup-start);report.hipRange=max-min;report.blinks=report.blinkTimes.Count;report.weightChanges=avatar.IdleWeightChanges;report.attentionChanges=avatar.AttentionChanges;
   ScreenCapture.CaptureScreenshot(path+"-idle.png");yield return new WaitForSeconds(.5f);
   var source=JsonUtility.FromJson<MotionClip>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Motions/kimodo-official.json")));
   var a=Pose(source,Mathf.Min(35,source.frames.Length-1));var b=Pose(source,Mathf.Min(100,source.frames.Length-1));
   avatar.Play(a,"衔接 A");yield return new WaitForSeconds(.9f);avatar.ReleaseMotion();
   float until=Time.realtimeSinceStartup+.2f;while(Time.realtimeSinceStartup<until)yield return null;
   var previous=tracked.Select(id=>avatar.animator.GetBoneTransform(id).localRotation).ToArray();var root=hips.position;avatar.Play(b,"衔接 B");until=Time.realtimeSinceStartup+.8f;
   while(Time.realtimeSinceStartup<until){yield return new WaitForEndOfFrame();if(avatar.PoseMode=="idle")report.unexpectedIdleFrames++;
    for(int i=0;i<tracked.Length;i++){var q=avatar.animator.GetBoneTransform(tracked[i]).localRotation;report.largestTransitionStep=Mathf.Max(report.largestTransitionStep,Quaternion.Angle(previous[i],q));previous[i]=q;}
    report.largestRootStep=Mathf.Max(report.largestRootStep,Vector3.Distance(root,hips.position));root=hips.position;report.finite&=cloth.Native.Finite;
   }
   ScreenCapture.CaptureScreenshot(path+"-joined.png");yield return new WaitForSeconds(.3f);avatar.Act("idle","待机");yield return new WaitForSeconds(1.6f);report.returnedToIdle=avatar.PoseMode=="idle"&&!avatar.SettlingToIdle;
   lp=left.position;rp=right.position;
   for(int cycle=0;cycle<3;cycle++){
    avatar.ReleaseMotion();yield return new WaitForSeconds(.2f);avatar.Play(a,"站位保持");yield return new WaitForSeconds(.9f);
    avatar.ReleaseMotion();yield return new WaitForSeconds(.2f);avatar.Act("idle","待机");yield return new WaitForSeconds(1.6f);
    report.repeatStanceDrift=Mathf.Max(report.repeatStanceDrift,Vector3.Distance(left.position,lp),Vector3.Distance(right.position,rp));
   }
   report.passed=report.finite&&report.returnedToIdle&&report.unexpectedIdleFrames==0&&report.maxFootDrift<.001f&&report.repeatStanceDrift<.001f&&report.hipRange>.015f&&report.maxSkinningError<.0001f&&report.maxSeamGap<.0001f&&report.largestTransitionStep<8&&report.largestRootStep<.02f&&report.blinks>=3&&report.attentionChanges>=4;
   File.WriteAllText(path+".json",JsonUtility.ToJson(report,true));Debug.Log("NATURAL_MOTION_VERIFIED "+JsonUtility.ToJson(report));Application.Quit();
  }
  static MotionClip Pose(MotionClip source,int frame)=>new MotionClip{version=source.version,coordinates=source.coordinates,bones=source.bones,fps=30,referenceHipHeight=source.referenceHipHeight,source="Recorded pose continuity fixture",frames=Enumerable.Repeat(source.frames[frame],60).ToArray()};
 }
}
