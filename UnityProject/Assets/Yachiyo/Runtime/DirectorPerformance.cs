using System;
using UnityEngine;

namespace Yachiyo {
 public sealed partial class DirectorBridge {
  [Serializable]public class PerformancePlan {
   public int version,id;public string intent,emotion,gaze,timingSource;
   public float intensity,energy,gestureAmount;public PerformanceCue[] cues;
  }
  [Serializable]public class PerformanceCue {
   public string type,side,timingSource;public float startMs,strokeMs,durationMs,prepareMs,releaseMs,intensity;
  }
  PerformancePlan speechPlan;
  bool[] fired;string affectEmotion="neutral";float affectStrength=.2f,affectAt,nextAffect;
  public string PerformanceIntent {get;private set;}="neutral";
  public int PerformanceCuesFired {get;private set;}
  public int PerformanceClock=>mouthClock;
  public float PerformanceGestureAmount {get;private set;}=0;
  public string PerformanceGaze {get;private set;}="camera";
  public string LastCue {get;private set;}="";
  public int LastCueClock {get;private set;}
  void StartPerformance(PerformancePlan plan){
   speechPlan=plan!=null&&plan.version==1?plan:null;
   avatar.EndConversationGesture();
   if(speechPlan==null){PerformanceGestureAmount=0;return;}
   PerformanceIntent=plan.intent;PerformanceGaze=plan.gaze;
   PerformanceGestureAmount=Mathf.Clamp01(plan.gestureAmount);
   fired=new bool[Mathf.Min(plan.cues?.Length??0,3)];
   affectEmotion=plan.emotion;affectStrength=Mathf.Clamp(plan.intensity*.65f,.12f,.5f);affectAt=Time.unscaledTime;
   Emotion(plan.emotion,Mathf.Clamp(plan.intensity,.15f,.8f),15000);emotionUntil=0;gazeUntil=0;
  }
  void AdvancePerformance(int ms){
   if(speechPlan==null||fired==null)return;
   for(int i=0;i<fired.Length;i++){
    var cue=speechPlan.cues[i];
    if(fired[i]||ms<cue.startMs)continue;
    fired[i]=true;
    // Late frames never play an obsolete gesture after its speech moment.
    if(ms>cue.startMs+cue.durationMs||Array.IndexOf(actions,cue.type)<0)continue;
    avatar.GestureTimed(cue.type,cue.startMs/1000,cue.durationMs/1000,cue.prepareMs/1000,cue.releaseMs/1000,cue.intensity,cue.side);
    LastCue=cue.type;LastCueClock=ms;PerformanceCuesFired++;
   }
  }
  void RestoreAffect(){
   if(!avatar||!face)return;
   float decay=Mathf.Exp(-Mathf.Max(0,Time.unscaledTime-affectAt)/90);
   Emotion(affectEmotion,affectStrength*decay,1000);emotionUntil=0;
  }
  void EndPerformance(){speechPlan=null;fired=null;RestoreAffect();if(face&&ui&&ui.view)face.SetLookTarget(ui.view.transform);}
  void UpdatePerformanceGaze(){
   if(!face||!ui||!ui.view)return;
   if(speechPlan==null){if(active==null&&emotionUntil==0&&Time.unscaledTime>nextAffect){nextAffect=Time.unscaledTime+1;RestoreAffect();}return;}
   if(PerformanceGaze=="averted"&&mouthClock>350&&mouthClock%5500<1450){
    if(!gaze)gaze=new GameObject("Director eye target").transform;
    gaze.position=ui.view.transform.position+ui.view.transform.right*.35f-ui.view.transform.up*.12f;
    face.SetLookTarget(gaze);
   }else face.SetLookTarget(ui.view.transform);
  }
 }
}
