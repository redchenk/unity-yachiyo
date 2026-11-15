using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Yachiyo {
 // Explicit native regression; never changes the saved scene/model or camera.
 public sealed class BoneClothVerification:MonoBehaviour {
  [Serializable]class Report {
   public bool passed,finite=true,originalTopology,originalWeights,toggleOff,toggleOn;
   public string solver;public int links,frames;public float maxLengthError,maxLateralStretch,maxTransferMs,meanTransferMs,earlyReturnSpeed,settledSpeed,frameRateError,rootAttachmentError;
   public List<Sample> samples=new List<Sample>();
  }
  [Serializable]class Sample {public float time,angle,speed,lateralStretch;}
  string path;AvatarMotion avatar;GarmentPhysics physics;Transform arm,forearm;Quaternion armRest,forearmRest,actorRest;Transform[] nodes;Quaternion[] previous;Report report=new Report();
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,"--verify-bone-cloth");if(i>=0&&i+1<a.Length)new GameObject("Bone cloth regression").AddComponent<BoneClothVerification>().path=a[i+1];}
  IEnumerator Start(){
   Application.runInBackground=true;Directory.CreateDirectory(path);yield return new WaitForSeconds(4);
   avatar=FindFirstObjectByType<AvatarMotion>();physics=avatar.GetComponent<GarmentPhysics>();avatar.Act("idle","布料验证");yield return new WaitForSeconds(1);
   avatar.enabled=false;arm=avatar.animator.GetBoneTransform(HumanBodyBones.RightUpperArm);forearm=avatar.animator.GetBoneTransform(HumanBodyBones.RightLowerArm);armRest=arm.localRotation;forearmRest=forearm.localRotation;actorRest=avatar.transform.rotation;
   nodes=avatar.GetComponentsInChildren<Transform>().Where(t=>t.childCount>0&&(t.name.Contains("CenterSleeve")||t.name.StartsWith("Acs_ArmSleeve"))&&t.parent.name!="PB"&&!t.name.EndsWith(" anchor",StringComparison.Ordinal)).ToArray();previous=nodes.Select(t=>t.localRotation).ToArray();
   var renderer=avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.name=="cloth1");var mesh=renderer.sharedMesh;var triangles=mesh.triangles;var weights=mesh.boneWeights;
   report.solver=physics.Sleeves.Solver;var anchors=nodes.Select(t=>t.localPosition).ToArray();report.links=physics.Sleeves.LateralLinks;float start=Time.time,previousTime=start,next=0,nextPreview=3;bool preview=Array.IndexOf(Environment.GetCommandLineArgs(),"--bone-cloth-preview")>=0;int previewFrame=0;
   while(Time.time-start<23){
    float elapsed=Time.time-start;Pose(elapsed);yield return new WaitForEndOfFrame();float speed=0,dt=Mathf.Max(.001f,Time.time-previousTime);previousTime=Time.time;
    for(int i=0;i<nodes.Length;i++){var q=nodes[i].localRotation;speed=Mathf.Max(speed,Angle(previous[i],q)/dt);previous[i]=q;report.rootAttachmentError=Mathf.Max(report.rootAttachmentError,Vector3.Distance(anchors[i],nodes[i].localPosition));}
    report.finite&=physics.Sleeves.Finite;report.frames++;report.maxTransferMs=Mathf.Max(report.maxTransferMs,physics.LastTransferMs);report.meanTransferMs+=physics.LastTransferMs;report.maxLengthError=Mathf.Max(report.maxLengthError,physics.Sleeves.MaxLengthError);report.maxLateralStretch=Mathf.Max(report.maxLateralStretch,physics.Sleeves.MaxLateralStretch);
    if(elapsed>9&&elapsed<10.5f)report.earlyReturnSpeed=Mathf.Max(report.earlyReturnSpeed,speed);
    if(elapsed>13&&elapsed<15)report.settledSpeed=Mathf.Max(report.settledSpeed,speed);
    if(preview&&elapsed>=nextPreview&&elapsed<15){Capture("preview-"+(previewFrame++).ToString("0000"));nextPreview+=.1f;}
    if(elapsed>=next){report.samples.Add(new Sample{time=elapsed,angle=physics.Sleeves.MaxJointAngle,speed=speed,lateralStretch=physics.Sleeves.MaxLateralStretch});if(Mathf.Abs(next-Mathf.Round(next))<.01f&&(int)next%2==0)Capture("pose-"+next.ToString("00"));next+=.25f;}
   }
   report.meanTransferMs/=Mathf.Max(1,report.frames);report.originalTopology=triangles.SequenceEqual(renderer.sharedMesh.triangles);report.originalWeights=weights.SequenceEqual(renderer.sharedMesh.boneWeights);
   physics.enabled=false;
   // Same analytic arm motion at different rendering rates. Native skirts are
   // not assessed here; this isolates the authored sleeve's own substep clock.
   var traces=new List<Quaternion[]>();
   foreach(int fps in new[]{30,60,144}){
    Pose(0);physics.ResetSimulation();physics.Tick(0);
    for(int step=1;step<=fps*15;step++){Pose((float)step/fps);physics.Tick(1f/fps);if(step%fps==0&&step>=fps*3)traces.Add(nodes.Select(t=>t.localRotation).ToArray());}
    yield return null;
   }
   int count=traces.Count/3;for(int rate=1;rate<3;rate++)for(int k=0;k<count;k++)for(int i=0;i<nodes.Length;i++)report.frameRateError=Mathf.Max(report.frameRateError,Quaternion.Angle(traces[k][i],traces[rate*count+k][i]));
   physics.SetSimulation(false);physics.Tick(1f/60);report.toggleOff=physics.Sleeves.MaxJointAngle<.001f;
   physics.SetSimulation(true);for(int i=0;i<120;i++)physics.Tick(1f/60);report.toggleOn=physics.Sleeves.Finite&&physics.Sleeves.MaxJointAngle>0;
   report.passed=report.finite&&report.originalTopology&&report.originalWeights&&report.links>0&&report.rootAttachmentError<.00001f&&report.maxLengthError<.001f&&report.frameRateError<8&&report.toggleOff&&report.toggleOn&&report.earlyReturnSpeed>.1f&&report.settledSpeed<report.earlyReturnSpeed*.5f;
   File.WriteAllText(Path.Combine(path,"report.json"),JsonUtility.ToJson(report,true));Application.Quit(report.passed?0:1);
  }
  void Pose(float t){
   float lift=t<3?0:t<5?Smooth((t-3)/2):t<7?1:t<9?1-Smooth((t-7)/2):0;
   float wave=t>5&&t<7?Mathf.Sin((t-5)*Mathf.PI*4)*Mathf.Sin((t-5)*Mathf.PI/2):0;
   arm.localRotation=armRest;forearm.localRotation=forearmRest;
   arm.rotation=Quaternion.AngleAxis(48*lift,Vector3.forward)*Quaternion.AngleAxis(-12*lift,Vector3.right)*arm.rotation;
   forearm.rotation=Quaternion.AngleAxis(48*lift+18*wave,Vector3.forward)*forearm.rotation;
   avatar.transform.rotation=actorRest*Quaternion.Euler(0,110*Smooth((t-16)/1.2f),0);
  }
  static float Smooth(float t){t=Mathf.Clamp01(t);return t*t*t*(t*(t*6-15)+10);}
  static float Angle(Quaternion a,Quaternion b){var d=b*Quaternion.Inverse(a);return 2*Mathf.Atan2(new Vector3(d.x,d.y,d.z).magnitude,Mathf.Abs(d.w))*Mathf.Rad2Deg;}
  void Capture(string name){
   var camera=new GameObject("Cloth inspection camera").AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Camera.main.backgroundColor;camera.fieldOfView=38;camera.nearClipPlane=.03f;
   var target=avatar.transform.TransformPoint(new Vector3(0,.95f,0));camera.transform.position=target+new Vector3(0,.1f,2.8f);camera.transform.LookAt(target);
   var rt=new RenderTexture(800,1000,24);var image=new Texture2D(800,1000,TextureFormat.RGB24,false);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,800,1000),0,0);image.Apply();File.WriteAllBytes(Path.Combine(path,name+".png"),image.EncodeToPNG());RenderTexture.active=null;Destroy(image);Destroy(rt);Destroy(camera.gameObject);
  }
 }
}
