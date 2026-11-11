using System;
using System.IO;
using UnityEngine;

namespace Yachiyo {
 // Opt-in integration instrumentation; does not export character geometry.
 public sealed class DirectorVerification : MonoBehaviour {
  public DirectorBridge bridge;
  string path;float next;Report report=new Report();
  bool capturedSpeech;
  [Serializable]class Report {
   public bool connected,localActionSeen,happySeen,audioActive,bodyPlaying,localActionActive,generatedMotionSeen;
   public int performances,mouthFrames;public float mouthOpen,closedVisemeMax,maxJawWithViseme,currentVisemeMax;
   public float[] maxVisemes=new float[5];public string lastReceipt,motionSource;
   public float speechBodyWeight,maxSpeechBodyWeight,cameraX,cameraY,cameraYaw,cameraPitch,cameraDistance;
   public Vector3 leftHand,rightHand,screenHead;public string gesture;
  }
  void Start(){var args=Environment.GetCommandLineArgs();int p=Array.IndexOf(args,"--verify-director");if(p<0||p+1>=args.Length){enabled=false;return;}path=args[p+1];}
  void LateUpdate(){
   var face=bridge.face;report.connected=bridge.Connected;report.performances=bridge.ReceivedPerformances;report.mouthFrames=bridge.ReceivedMouthFrames;
   report.audioActive=!string.IsNullOrEmpty(bridge.ActiveUtterance);report.bodyPlaying=bridge.avatar.Playing;report.localActionActive=bridge.avatar.LocalActionActive;report.localActionSeen|=report.localActionActive;report.happySeen|=face.Emotion==FaceEmotion.Happy;
   if(report.bodyPlaying){report.generatedMotionSeen=true;report.motionSource=bridge.avatar.MotionSource;}
   report.mouthOpen=face.AudioMouthOpen;report.lastReceipt=bridge.LastReceipt;
   report.speechBodyWeight=bridge.avatar.SpeechBodyWeight;report.maxSpeechBodyWeight=Mathf.Max(report.maxSpeechBodyWeight,report.speechBodyWeight);report.gesture=bridge.avatar.ConversationGesture;
   var animator=bridge.avatar.animator;report.leftHand=animator.GetBoneTransform(HumanBodyBones.LeftHand).position;report.rightHand=animator.GetBoneTransform(HumanBodyBones.RightHand).position;
   report.screenHead=bridge.ui.view.WorldToViewportPoint(animator.GetBoneTransform(HumanBodyBones.Head).position);
   report.cameraX=bridge.ui.DirectorFraming.x;report.cameraY=bridge.ui.DirectorFraming.y;report.cameraYaw=bridge.ui.DirectorCameraYaw;report.cameraPitch=bridge.ui.DirectorCameraPitch;report.cameraDistance=bridge.ui.DirectorCameraDistance;
   if(!capturedSpeech&&report.speechBodyWeight>.9f){capturedSpeech=true;ScreenCapture.CaptureScreenshot(path+"-speaking.png");}
   string[] names={"vrc.v_aa","vrc.v_ih","vrc.v_ou","vrc.v_e","vrc.v_oh"};float jaw=0,peak=0;
   foreach(var renderer in bridge.avatar.GetComponentsInChildren<SkinnedMeshRenderer>()){
    var mesh=renderer.sharedMesh;if(!mesh)continue;
    for(int i=0;i<5;i++){int index=mesh.GetBlendShapeIndex(names[i]);float weight=index<0?0:renderer.GetBlendShapeWeight(index);report.maxVisemes[i]=Mathf.Max(report.maxVisemes[i],weight);peak=Mathf.Max(peak,weight);}
    int j=mesh.GetBlendShapeIndex("JawOpen");if(j>=0)jaw=Mathf.Max(jaw,renderer.GetBlendShapeWeight(j));
   }
   report.currentVisemeMax=peak;
   if(report.mouthOpen>.1f&&peak>10)report.maxJawWithViseme=Mathf.Max(report.maxJawWithViseme,jaw);
   if(!report.audioActive)report.closedVisemeMax=peak;
   if(Time.unscaledTime>=next){next=Time.unscaledTime+.15f;File.WriteAllText(path+".json",JsonUtility.ToJson(report,true));}
  }
 }
}
