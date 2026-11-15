using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if YACHIYO_MAGICA
using MagicaCloth2;
#endif
namespace Yachiyo {
 public sealed class FullClothVerification:MonoBehaviour {
  [Serializable]class Report {
   public bool passed,ready,finite=true,noLegacySolvers,originalMeshes,stableAttachments,toggleOff,toggleOn;
   public string solver="Magica Cloth 2.15.0",failure,device;
   public int frames,colliders,proxyVertices;public float meanFrameMs,p95FrameMs,meanSimulationMs,p95SimulationMs,maxAttachmentError,maxPinnedSurfaceError,ornamentSettledSpeed,ornamentMaxAngle;
   public List<FullCharacterCloth.GroupInfo> groups;public List<State> states=new List<State>();
   public FullCharacterCloth.HangingSleeveSample[] hangingSleeves;
  }
  [Serializable]class State{public string name;public int frames;}
  string path;AvatarMotion avatar;FullCharacterCloth cloth;GarmentPhysics garment;Report report=new Report();
  readonly List<float> frameMs=new List<float>(),simulationMs=new List<float>();
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--verify-full-cloth");if(i>=0&&i+1<args.Length)new GameObject("Full cloth verification").AddComponent<FullClothVerification>().path=args[i+1];}
  IEnumerator Start(){
   Directory.CreateDirectory(path);Application.runInBackground=true;Application.targetFrameRate=60;
   avatar=FindFirstObjectByType<AvatarMotion>();garment=avatar.GetComponent<GarmentPhysics>();cloth=garment.FullCloth;
   report.device=SystemInfo.processorType+" / "+SystemInfo.graphicsDeviceName;
   if(!cloth){report.failure="Full solver unavailable";Finish();yield break;}
   float deadline=Time.realtimeSinceStartup+60;
   while(!cloth.Ready&&!cloth.Failed&&Time.realtimeSinceStartup<deadline)yield return null;
   report.ready=cloth.Ready;report.failure=cloth.Failure;report.groups=cloth.groups;report.colliders=cloth.ColliderCount;report.proxyVertices=cloth.ProxyVertices;
   if(!cloth.Ready){Finish();yield break;}
   report.noLegacySolvers=garment.Native==null&&garment.Sleeves==null&&!avatar.GetComponents<HairPhysics>().Any(h=>h.enabled)&&avatar.GetComponentsInChildren<Cloth>().Length==0;
   avatar.followPointer=false;yield return new WaitForSeconds(3);
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--cloth-sleeve-stress")>=0){yield return SleeveResponse();yield break;}
   var attachments=avatar.GetComponentsInChildren<Transform>().Where(t=>t.name=="Head"||t.name=="Neck1"||t.name=="Neck2"||t.name.Contains("约束基准")).ToArray();
   var attachmentPositions=attachments.Select(t=>t.localPosition).ToArray();
   var renderers=avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.name=="cloth1"||r.name=="cloth2"||r.name=="hair_back"||r.name=="hair_front").ToArray();
   var bake=new Mesh();
   foreach(var name in new[]{"idle","wave","turn","motion","return"}){
    if(name=="wave")avatar.Act("wave","挥手");
    if(name=="turn")avatar.Act("turn","转身");
    if(name=="motion")avatar.Play(JsonUtility.FromJson<MotionClip>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Motions/kimodo-official.json"))),"官方动作样例");
    if(name=="return")avatar.Act("idle","待机");
    var state=new State{name=name};float start=Time.realtimeSinceStartup;int capture=0;
    while(Time.realtimeSinceStartup-start<6){
     yield return new WaitForEndOfFrame();state.frames++;report.frames++;
     frameMs.Add(Time.unscaledDeltaTime*1000);simulationMs.Add(cloth.LastSimulationMs);
     for(int i=0;i<attachments.Length;i++)report.maxAttachmentError=Mathf.Max(report.maxAttachmentError,Vector3.Distance(attachmentPositions[i],attachments[i].localPosition));
     #if YACHIYO_MAGICA
     if(state.frames%60==0)report.maxPinnedSurfaceError=Mathf.Max(report.maxPinnedSurfaceError,cloth.MeasurePinnedError());
#endif
     if(state.frames%15==0)foreach(var r in renderers){r.BakeMesh(bake);foreach(var v in bake.vertices)report.finite&=float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)&&v.sqrMagnitude<100;}
     if(Time.realtimeSinceStartup-start>1+capture*2&&capture<3){Capture(name+"-"+capture,capture==0?0:capture==1?90:180);capture++;}
    }
    report.states.Add(state);
   }
   // Freeze only the body's input to isolate spontaneous ornament jitter.
   avatar.enabled=false;yield return new WaitForSeconds(2);
   var ears=avatar.GetComponentsInChildren<Transform>().Where(t=>t.name=="Acs_Obi_A001"||t.name=="Acs_Obi_B001").ToArray();
   var earStart=ears.Select(t=>t.localRotation).ToArray();var earPrevious=earStart.ToArray();
   float stillStart=Time.realtimeSinceStartup;
   while(Time.realtimeSinceStartup-stillStart<3){yield return new WaitForEndOfFrame();
    for(int i=0;i<ears.Length;i++){
     var q=ears[i].localRotation;var delta=q*Quaternion.Inverse(earPrevious[i]);
     float angle=2*Mathf.Atan2(new Vector3(delta.x,delta.y,delta.z).magnitude,Mathf.Abs(delta.w))*Mathf.Rad2Deg;
     report.ornamentSettledSpeed=Mathf.Max(report.ornamentSettledSpeed,angle/Mathf.Max(.001f,Time.deltaTime));
     report.ornamentMaxAngle=Mathf.Max(report.ornamentMaxAngle,Quaternion.Angle(earStart[i],q));earPrevious[i]=q;
    }
   }Capture("ornament-settled",0);avatar.enabled=true;
   Destroy(bake);report.stableAttachments=report.maxAttachmentError<.00001f&&report.maxPinnedSurfaceError<.002f;
#if YACHIYO_MAGICA
   report.originalMeshes=cloth.OriginalMeshesIntact;
   foreach(var c in cloth.GetComponentsInChildren<MagicaCloth>())foreach(var r in c.SerializeData.sourceRenderers){var mesh=c.GetOriginalMesh(r);report.originalMeshes&=mesh&&mesh.vertexCount>0;}
#endif
   garment.SetSimulation(false);yield return new WaitForSeconds(.5f);report.toggleOff=!cloth.Active;
   Capture("off",0);garment.SetSimulation(true);yield return new WaitForSeconds(3);report.toggleOn=cloth.Active&&cloth.Ready;Capture("restored",0);
   report.meanFrameMs=frameMs.Average();report.p95FrameMs=Percentile(frameMs);report.meanSimulationMs=simulationMs.Average();report.p95SimulationMs=Percentile(simulationMs);
   report.hangingSleeves=cloth.MeasureHangingSleeves();
   report.passed=report.ready&&report.finite&&report.noLegacySolvers&&report.originalMeshes&&report.stableAttachments&&report.toggleOff&&report.toggleOn&&report.ornamentSettledSpeed<4&&report.groups.Count==6&&report.groups.All(g=>g.built&&g.movable>0)&&report.hangingSleeves.All(s=>s.vertices>400&&s.movable==s.vertices&&s.meanSimulationOffset>.005f);
   Finish();
  }
  static float Percentile(List<float> values){var sorted=values.OrderBy(v=>v).ToArray();return sorted[Mathf.Min(sorted.Length-1,(int)(sorted.Length*.95f))];}
  IEnumerator SleeveResponse(){
   avatar.enabled=false;
   var animator=avatar.GetComponent<Animator>();var arm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);var startRotation=arm.rotation;
   float start=Time.realtimeSinceStartup;int capture=0;
   while(Time.realtimeSinceStartup-start<14){
    float t=Time.realtimeSinceStartup-start;
    float angle=t<2?0:t<4?Mathf.SmoothStep(0,65,(t-2)/2):t<6?65:t<7?Mathf.SmoothStep(65,0,t-6):0;
    arm.rotation=Quaternion.AngleAxis(angle,avatar.transform.forward)*startRotation;
    yield return new WaitForEndOfFrame();
    if(t>capture*.5f){Capture("response-"+capture.ToString("00"),25);capture++;}
   }
   Application.Quit(0);
  }
  void Finish(){File.WriteAllText(Path.Combine(path,"report.json"),JsonUtility.ToJson(report,true));Debug.Log("FULL_CLOTH_VERIFIED passed="+report.passed);Application.Quit(report.passed?0:1);}
  void Capture(string name,float yaw){
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--diagnose-magica-sleeves")>=0)cloth.WriteSleeveDiagnostics(Path.Combine(path,name+".json"));
   var camera=new GameObject("Full cloth inspection").AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.075f,.095f);camera.fieldOfView=38;camera.nearClipPlane=.03f;
   var target=avatar.CameraAnchor;target.y=.95f;camera.transform.position=target+Quaternion.Euler(0,yaw,0)*new Vector3(0,.1f,2.9f);camera.transform.LookAt(target);
   var rt=new RenderTexture(800,1000,24);var texture=new Texture2D(800,1000,TextureFormat.RGB24,false);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,800,1000),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(path,name+".png"),texture.EncodeToPNG());RenderTexture.active=null;Destroy(rt);Destroy(texture);Destroy(camera.gameObject);
  }
 }
}
