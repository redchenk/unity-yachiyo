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
   public string solver="Magica Cloth 2.18.4",failure,device;
   public int frames,colliders,proxyVertices,furisodeControlPoints;public float meanFrameMs,p95FrameMs,meanSimulationMs,p95SimulationMs,maxAttachmentError,maxPinnedSurfaceError,ornamentSettledSpeed,ornamentMaxAngle,furisodeMaxLocalBend,furisodeMeanLocalBend;
   public List<FullCharacterCloth.GroupInfo> groups;public List<State> states=new List<State>();
   public FullCharacterCloth.HangingSleeveSample[] hangingSleeves;
   public FullCharacterCloth.SleeveRegionSample[] sleeveRegions;
   public int bowAttachments;public float maxAddedBowGap,maxSleeveBoundaryStretch,maxSleeveFixedError;
   public float tieRootMaxSpeed,tieBoneMaxSpeed,tieSettledMaxSpeed,tieRootSettledSpeed,tieBoneSettledSpeed,tieMaxLocalStretch;public int tieSamples;
   public string tieSettledNode;
  }
  [Serializable]class State{public string name;public int frames;}
  string path;AvatarMotion avatar;FullCharacterCloth cloth;GarmentPhysics garment;Report report=new Report();
  readonly List<float> frameMs=new List<float>(),simulationMs=new List<float>();
  Transform[] tieNodes;Quaternion[] tiePrevious;Vector3[] tieRestPositions;
  void SampleTieMotion(bool settled){
   if(tieNodes==null){tieNodes=avatar.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Acs_UnderSleeveRibbon_")).ToArray();tiePrevious=tieNodes.Select(t=>t.name.Contains("Root")?t.rotation:t.localRotation).ToArray();tieRestPositions=tieNodes.Select(t=>t.localPosition).ToArray();return;}
   report.tieSamples++;
   for(int i=0;i<tieNodes.Length;i++){
    bool root=tieNodes[i].name.Contains("Root");var q=root?tieNodes[i].rotation:tieNodes[i].localRotation;var delta=q*Quaternion.Inverse(tiePrevious[i]);
    float speed=2*Mathf.Atan2(new Vector3(delta.x,delta.y,delta.z).magnitude,Mathf.Abs(delta.w))*Mathf.Rad2Deg/Mathf.Max(.001f,Time.unscaledDeltaTime);
    if(root)report.tieRootMaxSpeed=Mathf.Max(report.tieRootMaxSpeed,speed);else{report.tieBoneMaxSpeed=Mathf.Max(report.tieBoneMaxSpeed,speed);report.tieMaxLocalStretch=Mathf.Max(report.tieMaxLocalStretch,Mathf.Abs(tieNodes[i].localPosition.magnitude-tieRestPositions[i].magnitude));}
    if(settled){if(speed>report.tieSettledMaxSpeed)report.tieSettledNode=tieNodes[i].name;report.tieSettledMaxSpeed=Mathf.Max(report.tieSettledMaxSpeed,speed);if(root)report.tieRootSettledSpeed=Mathf.Max(report.tieRootSettledSpeed,speed);else report.tieBoneSettledSpeed=Mathf.Max(report.tieBoneSettledSpeed,speed);}tiePrevious[i]=q;
   }
  }
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--verify-full-cloth");if(i>=0&&i+1<args.Length)new GameObject("Full cloth verification").AddComponent<FullClothVerification>().path=args[i+1];}
  IEnumerator Start(){
   Directory.CreateDirectory(path);Application.runInBackground=true;Application.targetFrameRate=60;
   avatar=FindFirstObjectByType<AvatarMotion>();garment=avatar.GetComponent<GarmentPhysics>();cloth=garment.FullCloth;
   report.device=SystemInfo.processorType+" / "+SystemInfo.graphicsDeviceName;
   if(!cloth){report.failure="Full solver unavailable";Finish();yield break;}
   float deadline=Time.realtimeSinceStartup+60;
   while(!cloth.Ready&&!cloth.Failed&&Time.realtimeSinceStartup<deadline)yield return null;
   report.ready=cloth.Ready;report.failure=cloth.Failure;report.groups=cloth.groups;report.colliders=cloth.ColliderCount;report.proxyVertices=cloth.ProxyVertices;report.furisodeControlPoints=cloth.FurisodeControlPoints;
   if(!cloth.Ready){Finish();yield break;}
   report.noLegacySolvers=garment.Native==null&&garment.Sleeves==null&&!avatar.GetComponents<HairPhysics>().Any(h=>h.enabled)&&avatar.GetComponentsInChildren<Cloth>().Length==0;
   avatar.followPointer=false;yield return new WaitForSeconds(3);
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--cloth-sleeve-attachment-stress")>=0){yield return SleeveAttachmentResponse();yield break;}
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--cloth-sleeve-stress")>=0||Array.IndexOf(Environment.GetCommandLineArgs(),"--cloth-sleeve-junction-stress")>=0){yield return SleeveResponse();yield break;}
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
     SampleTieMotion(false);
     frameMs.Add(Time.unscaledDeltaTime*1000);simulationMs.Add(cloth.LastSimulationMs);var bend=cloth.MeasureFurisodeBend();report.furisodeMaxLocalBend=Mathf.Max(report.furisodeMaxLocalBend,bend.y);report.furisodeMeanLocalBend+=bend.x;
     for(int i=0;i<attachments.Length;i++)report.maxAttachmentError=Mathf.Max(report.maxAttachmentError,Vector3.Distance(attachmentPositions[i],attachments[i].localPosition));
     #if YACHIYO_MAGICA
     if(state.frames%60==0){report.maxPinnedSurfaceError=Mathf.Max(report.maxPinnedSurfaceError,cloth.MeasurePinnedError());SampleSleeveAttachments();}
#endif
     if(state.frames%15==0)foreach(var r in renderers){r.BakeMesh(bake);foreach(var v in bake.vertices)report.finite&=float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)&&v.sqrMagnitude<100;}
     if(Time.realtimeSinceStartup-start>1+capture*2&&capture<3){Capture(name+"-"+capture,capture==0?0:capture==1?90:180);capture++;}
    }
    report.states.Add(state);
   }
   // Freeze only the body's input to isolate spontaneous ornament jitter.
   avatar.enabled=false;yield return new WaitForSeconds(2);tieNodes=null;
   var ears=avatar.GetComponentsInChildren<Transform>().Where(t=>t.name=="Acs_Obi_A001"||t.name=="Acs_Obi_B001").ToArray();
   var earStart=ears.Select(t=>t.localRotation).ToArray();var earPrevious=earStart.ToArray();
   float stillStart=Time.realtimeSinceStartup;
   while(Time.realtimeSinceStartup-stillStart<3){yield return new WaitForEndOfFrame();
    SampleTieMotion(true);
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
   // BoneCloth restores animation transforms in EarlyUpdate, then solves
   // after LateUpdate. Sample displayed physics only at end of frame.
   garment.SetSimulation(false);yield return new WaitForSeconds(.5f);yield return new WaitForEndOfFrame();report.toggleOff=!cloth.Active;
   Capture("off",0);garment.SetSimulation(true);yield return new WaitForSeconds(3);yield return new WaitForEndOfFrame();report.toggleOn=cloth.Active&&cloth.Ready;Capture("restored",0);
   report.furisodeMeanLocalBend/=Mathf.Max(1,report.frames);report.meanFrameMs=frameMs.Average();report.p95FrameMs=Percentile(frameMs);report.meanSimulationMs=simulationMs.Average();report.p95SimulationMs=Percentile(simulationMs);
   report.hangingSleeves=cloth.MeasureHangingSleeves();
   report.sleeveRegions=cloth.MeasureSleeveRegions();
#if YACHIYO_MAGICA
   Debug.Log("CLOTH_RUNTIME simulationFrames="+cloth.SimulationFrames);
   foreach(var c in cloth.GetComponentsInChildren<MagicaCloth>()){var t=MagicaManager.Team.GetTeamDataRef(c.Process.TeamId);Debug.Log("CLOTH_RUNTIME "+c.name+" enabled="+c.enabled+" process="+t.IsProcess+" enable="+t.IsEnable+" suspend="+t.flag.IsSet(TeamManager.Flag_SyncSuspend)+" steps="+t.updateCount+" blend="+t.blendWeight+" time="+t.time+" scale="+t.nowTimeScale);}
#endif
   report.passed=(cloth.FurisodeControlPoints==0||report.furisodeMaxLocalBend>15)&&report.ready&&report.finite&&report.noLegacySolvers&&report.originalMeshes&&report.stableAttachments&&report.toggleOff&&report.toggleOn&&report.ornamentSettledSpeed<4&&report.groups.Count==(cloth.MeshSleeveCount>0?9:cloth.FurisodeControlPoints>0?8:6)&&report.groups.All(g=>g.built&&g.movable>0)&&report.hangingSleeves.All(s=>s.vertices>400&&s.movable==s.vertices&&s.meanSimulationOffset>.005f&&s.meanSimulationOffset<.8f&&s.maxSimulationOffset<1.3f);
   if(cloth.FurisodeControlPoints>0||cloth.MeshSleeveCount>0)report.passed&=report.sleeveRegions.Length==6&&report.sleeveRegions.All(s=>s.vertices>=10&&s.driven>=s.vertices*.8f&&s.driven+s.protectedVertices==s.vertices&&s.meanSimulationOffset>.001f&&s.meanSimulationOffset<.4f&&s.maxSimulationOffset<.8f);
   if(cloth.FurisodeControlPoints>0||cloth.MeshSleeveCount>0)report.passed&=SleeveAttachmentsPass();
   if(cloth.MeshSleeveCount>0)report.passed&=TieMotionPass();
   Finish();
  }
  static float Percentile(List<float> values){var sorted=values.OrderBy(v=>v).ToArray();return sorted[Mathf.Min(sorted.Length-1,(int)(sorted.Length*.95f))];}
  void SampleSleeveAttachments(){var s=cloth.MeasureSleeveAttachments();report.bowAttachments=s.bows;report.maxAddedBowGap=Mathf.Max(report.maxAddedBowGap,s.maxAddedBowGap);report.maxSleeveBoundaryStretch=Mathf.Max(report.maxSleeveBoundaryStretch,s.maxBoundaryStretch);report.maxSleeveFixedError=Mathf.Max(report.maxSleeveFixedError,s.maxFixedError);}
  bool SleeveAttachmentsPass()=>report.bowAttachments==2&&report.maxAddedBowGap<.015f&&report.maxSleeveBoundaryStretch<.035f&&report.maxSleeveFixedError<.002f;
  bool TieMotionPass()=>report.tieSamples>120&&report.tieRootMaxSpeed<250&&report.tieBoneMaxSpeed<100&&report.tieRootSettledSpeed<90&&report.tieBoneSettledSpeed<12&&report.tieMaxLocalStretch<.001f;
  IEnumerator SleeveAttachmentResponse(){
   avatar.enabled=false;var animator=avatar.GetComponent<Animator>();var rootRest=avatar.transform.rotation;
   var arms=new[]{animator.GetBoneTransform(HumanBodyBones.RightUpperArm),animator.GetBoneTransform(HumanBodyBones.LeftUpperArm)};
   var elbows=new[]{animator.GetBoneTransform(HumanBodyBones.RightLowerArm),animator.GetBoneTransform(HumanBodyBones.LeftLowerArm)};
   var hands=new[]{animator.GetBoneTransform(HumanBodyBones.RightHand),animator.GetBoneTransform(HumanBodyBones.LeftHand)};
   var armRest=arms.Select(t=>t.rotation).ToArray();var elbowRest=elbows.Select(t=>t.localRotation).ToArray();
   float Envelope(float t)=>t<0||t>=6?0:t<2?Mathf.SmoothStep(0,1,t/2):t<4?1:Mathf.SmoothStep(1,0,(t-4)/2);
   float start=Time.realtimeSinceStartup;int capture=0;
   while(Time.realtimeSinceStartup-start<30){
    float t=Time.realtimeSinceStartup-start;float right=Envelope(t-2),left=Envelope(t-8),both=Envelope(t-14);
    avatar.transform.rotation=Quaternion.AngleAxis((right-left+both)*30,Vector3.up)*rootRest;
    for(int side=0;side<2;side++){
     float weight=Mathf.Max(side==0?right:left,both);arms[side].rotation=Quaternion.AngleAxis((side==0?1:-1)*weight*95,avatar.transform.forward)*(avatar.transform.rotation*Quaternion.Inverse(rootRest))*armRest[side];
     elbows[side].localRotation=elbowRest[side];var axis=Vector3.Cross(hands[side].position-elbows[side].position,avatar.transform.forward).normalized;elbows[side].rotation=Quaternion.AngleAxis(weight*70,axis)*elbows[side].rotation;
    }
    yield return new WaitForEndOfFrame();report.frames++;
    SampleTieMotion(t>27);
    if(t>capture*.5f){SampleSleeveAttachments();Capture("attachment-"+capture.ToString("00"),capture%3==2?160:20);capture++;}
   }
   report.passed=SleeveAttachmentsPass()&&(cloth.MeshSleeveCount==0||TieMotionPass());Finish();
  }
  IEnumerator SleeveResponse(){
   avatar.enabled=false;
   var animator=avatar.GetComponent<Animator>();var arm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);var startRotation=arm.rotation;
   bool junction=Array.IndexOf(Environment.GetCommandLineArgs(),"--cloth-sleeve-junction-stress")>=0;
   var forearm=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);var elbowRest=forearm.localRotation;
   float start=Time.realtimeSinceStartup;int capture=0;
   while(Time.realtimeSinceStartup-start<14){
    float t=Time.realtimeSinceStartup-start;
    float angle=t<2?0:t<4?Mathf.SmoothStep(0,65,(t-2)/2):t<6?65:t<7?Mathf.SmoothStep(65,0,t-6):0;
    arm.rotation=Quaternion.AngleAxis(angle,avatar.transform.forward)*startRotation;
    if(junction){forearm.localRotation=elbowRest;var axis=Vector3.Cross(hand.position-forearm.position,avatar.transform.forward).normalized;forearm.rotation=Quaternion.AngleAxis(angle,axis)*forearm.rotation;}
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
   bool junction=Array.IndexOf(Environment.GetCommandLineArgs(),"--cloth-sleeve-junction-stress")>=0;
   if(junction){target.y=1.12f;camera.transform.position=target+Quaternion.Euler(0,yaw,0)*new Vector3(0,.1f,2.4f);camera.transform.LookAt(target);}
   int width=junction?1200:800;var rt=new RenderTexture(width,1000,24);var texture=new Texture2D(width,1000,TextureFormat.RGB24,false);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,width,1000),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(path,name+".png"),texture.EncodeToPNG());RenderTexture.active=null;Destroy(rt);Destroy(texture);Destroy(camera.gameObject);
  }
 }
}
