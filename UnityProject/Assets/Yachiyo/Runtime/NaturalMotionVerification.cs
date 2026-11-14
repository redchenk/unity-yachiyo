using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Yachiyo {
 // Native regression, enabled only by an explicit command-line flag.
 public sealed class NaturalMotionVerification : MonoBehaviour {
  [Serializable]class Report {public bool passed,finite=true,returnedToIdle;public int idleFrames,weightChanges,attentionChanges,blinks,unexpectedIdleFrames;public float fps,maxFootDrift,hipRange,maxSkinningError,maxSeamGap,largestTransitionStep,largestRootStep,repeatStanceDrift,largestIdleSpeed,maxClothPenetration,leftHandTravel,rightHandTravel;public float[] boneRanges=new float[tracked.Length];public List<float> blinkTimes=new List<float>();public List<PoseSample> idleSamples=new List<PoseSample>();}
  [Serializable]class PoseSample {public float time;public Vector3 hips,leftHand,rightHand;public Quaternion[] rotations;}
  string path;AvatarMotion avatar;GarmentPhysics cloth;YachiyoFaceController face;Report report=new Report();
  static readonly HumanBodyBones[] tracked={HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.Head,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightHand};
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var a=Environment.GetCommandLineArgs();int p=Array.IndexOf(a,"--verify-natural-motion");if(p>=0&&p+1<a.Length)new GameObject("Natural motion verification").AddComponent<NaturalMotionVerification>().path=a[p+1];}
  IEnumerator Start(){
   yield return new WaitForSeconds(4);avatar=FindFirstObjectByType<AvatarMotion>();cloth=avatar.GetComponent<GarmentPhysics>();face=avatar.GetComponent<YachiyoFaceController>();var ui=FindFirstObjectByType<StudioUI>();ui.ResetDirectorCamera();avatar.followPointer=false;
   avatar.Act("idle","自然待机验证");yield return new WaitForSeconds(1.5f);
   var left=avatar.animator.GetBoneTransform(HumanBodyBones.LeftFoot);var right=avatar.animator.GetBoneTransform(HumanBodyBones.RightFoot);var hips=avatar.animator.GetBoneTransform(HumanBodyBones.Hips);
   var lp=left.position;var rp=right.position;float min=hips.position.x,max=min,start=Time.realtimeSinceStartup,nextSample=0,nextCapture=8;int blinks=face.BlinkCount;
   var previousIdle=tracked.Select(id=>avatar.animator.GetBoneTransform(id).localRotation).ToArray();float previousTime=Time.realtimeSinceStartup;
   while(Time.realtimeSinceStartup-start<32){
    yield return new WaitForEndOfFrame();report.idleFrames++;report.finite&=cloth.Native.Finite;
    report.maxFootDrift=Mathf.Max(report.maxFootDrift,Vector3.Distance(left.position,lp),Vector3.Distance(right.position,rp));min=Mathf.Min(min,hips.position.x);max=Mathf.Max(max,hips.position.x);
    if(face.BlinkCount!=blinks){report.blinkTimes.Add(Time.realtimeSinceStartup-start);blinks=face.BlinkCount;}
    foreach(var h in cloth.Native.hems){report.maxSeamGap=Mathf.Max(report.maxSeamGap,h.SeamError);if(report.idleFrames%120==0)report.maxSkinningError=Mathf.Max(report.maxSkinningError,h.VerifySeamSkinning());}
    float now=Time.realtimeSinceStartup,elapsed=now-start;var rotations=tracked.Select(id=>avatar.animator.GetBoneTransform(id).localRotation).ToArray();
    for(int i=0;i<tracked.Length;i++)report.largestIdleSpeed=Mathf.Max(report.largestIdleSpeed,SmallAngle(previousIdle[i],rotations[i])/Mathf.Max(.001f,now-previousTime));
    previousIdle=rotations;previousTime=now;report.maxClothPenetration=Mathf.Max(report.maxClothPenetration,cloth.LastMaxPenetration);
    if(elapsed>=nextSample){nextSample+=.25f;report.idleSamples.Add(new PoseSample{time=elapsed,hips=hips.position,leftHand=avatar.animator.GetBoneTransform(HumanBodyBones.LeftHand).position,rightHand=avatar.animator.GetBoneTransform(HumanBodyBones.RightHand).position,rotations=rotations});}
    if(elapsed>=nextCapture){ScreenCapture.CaptureScreenshot(path+"-idle-"+nextCapture+".png");nextCapture+=8;}
   }
   foreach(var first in report.idleSamples)foreach(var second in report.idleSamples){
    report.leftHandTravel=Mathf.Max(report.leftHandTravel,Vector3.Distance(first.leftHand,second.leftHand));report.rightHandTravel=Mathf.Max(report.rightHandTravel,Vector3.Distance(first.rightHand,second.rightHand));
    for(int i=0;i<tracked.Length;i++)report.boneRanges[i]=Mathf.Max(report.boneRanges[i],Quaternion.Angle(first.rotations[i],second.rotations[i]));
   }
   report.fps=report.idleFrames/(Time.realtimeSinceStartup-start);report.hipRange=max-min;report.blinks=report.blinkTimes.Count;report.weightChanges=avatar.IdleWeightChanges;report.attentionChanges=avatar.AttentionChanges;
   ScreenCapture.CaptureScreenshot(path+"-idle.png");CaptureViews();yield return new WaitForSeconds(.5f);
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
   report.passed=report.finite&&report.returnedToIdle&&report.unexpectedIdleFrames==0&&report.maxFootDrift<.001f&&report.repeatStanceDrift<.001f&&report.hipRange>.03f&&report.leftHandTravel>.035f&&report.rightHandTravel>.035f&&report.boneRanges[6]>4&&report.boneRanges[7]>4&&report.largestIdleSpeed<45&&report.maxSkinningError<.0001f&&report.maxSeamGap<.0001f&&report.largestTransitionStep<8&&report.largestRootStep<.02f&&report.blinks>=3&&report.attentionChanges>=4;
   File.WriteAllText(path+".json",JsonUtility.ToJson(report,true));Debug.Log("NATURAL_MOTION_VERIFIED "+JsonUtility.ToJson(report));Application.Quit();
  }
  // Quaternion.Angle rounds sub-frame idle motion to zero at high refresh
  // rates. The vector part retains precision for these very small rotations.
  static float SmallAngle(Quaternion a,Quaternion b){var d=b*Quaternion.Inverse(a);return 2*Mathf.Atan2(new Vector3(d.x,d.y,d.z).magnitude,Mathf.Abs(d.w))*Mathf.Rad2Deg;}
  void CaptureViews(){
   var camera=new GameObject("Idle inspection camera").AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Camera.main.backgroundColor;camera.fieldOfView=38;camera.nearClipPlane=.03f;
   var target=avatar.transform.TransformPoint(new Vector3(0,.85f,0));
   foreach(float angle in new[]{0f,90f,180f}){
    camera.transform.position=target+Quaternion.Euler(-8,angle,0)*new Vector3(0,0,3.1f);camera.transform.LookAt(target);
    var rt=new RenderTexture(800,1000,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(800,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,800,1000),0,0);image.Apply();File.WriteAllBytes(path+"-view-"+angle+".png",image.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=null;Destroy(image);Destroy(rt);
   }
   Destroy(camera.gameObject);
  }
  static MotionClip Pose(MotionClip source,int frame)=>new MotionClip{version=source.version,coordinates=source.coordinates,bones=source.bones,fps=30,referenceHipHeight=source.referenceHipHeight,source="Recorded pose continuity fixture",frames=Enumerable.Repeat(source.frames[frame],60).ToArray()};
 }
}
