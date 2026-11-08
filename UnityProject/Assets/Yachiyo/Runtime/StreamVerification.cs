using System;
using System.IO;
using System.Collections;
using UnityEngine;

namespace Yachiyo {
 public static class StreamVerification {
  [Serializable]class Report{public bool loaded,finite=true,stopWorked,noLateRestart;public int chunks,underruns;public float maxFrameRootTravel,lastGenerationSeconds;public string source;}
  public static IEnumerator Run(StudioUI ui,AvatarMotion avatar,string[] args){
   int p=Array.IndexOf(args,"--verify-stream");if(p<0||p+1>=args.Length)yield break;string path=args[p+1];
   var report=new Report();avatar.followPointer=false;
   yield return new WaitForSeconds(2);ui.RequestMotion("向我挥挥手");
   float deadline=Time.realtimeSinceStartup+150;
   while(!avatar.Streaming&&Time.realtimeSinceStartup<deadline)yield return null;
   report.loaded=avatar.Streaming;
   if(report.loaded){
    var hips=avatar.animator.GetBoneTransform(HumanBodyBones.Hips);Vector3 previous=hips.position;
    foreach(string prompt in new[]{"向我挥挥手","向前走","站着"}){
     if(prompt!="向我挥挥手")ui.RequestMotion(prompt);
     float start=Time.realtimeSinceStartup;int capture=1;
     while(Time.realtimeSinceStartup-start<6){
      yield return new WaitForEndOfFrame();var pos=hips.position;
      report.finite&=float.IsFinite(pos.x)&&float.IsFinite(pos.y)&&float.IsFinite(pos.z);report.maxFrameRootTravel=Mathf.Max(report.maxFrameRootTravel,Vector3.Distance(previous,pos));previous=pos;
      if(Time.realtimeSinceStartup-start>capture){ScreenCapture.CaptureScreenshot(path+"-"+prompt+"-"+capture+".png");capture+=2;}
     }
    }
    report.source=avatar.MotionSource;report.chunks=ui.RemoteChunks;report.underruns=avatar.StreamUnderruns;report.lastGenerationSeconds=ui.LastGenerationSeconds;
    ui.RequestMotion("跳舞");yield return null;ui.StopMotion();report.stopWorked=!avatar.Playing&&!avatar.Streaming;
    yield return new WaitForSeconds(3);report.noLateRestart=!avatar.Playing;
   }
   File.WriteAllText(path+".json",JsonUtility.ToJson(report,true));Debug.Log("STREAM_VERIFICATION_DONE "+path);
  }
 }
}
