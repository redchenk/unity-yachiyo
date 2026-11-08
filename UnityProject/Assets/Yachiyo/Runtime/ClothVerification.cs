using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Yachiyo {
 // Invoked only by --verify-cloth <output-prefix>.
 public static class ClothVerification {
  [Serializable]class Report {
   public string device,solver="Unity Cloth / PhysX proxy surfaces";
   public bool finite=true,toggleOffWorked,toggleOnWorked,stopWorked;
   public int samples,particles,movableParticles,bodyCapsules,nativeVertices,nativePanels;
   public float maxOffset,maxProxyPenetration,meanFrameMs,p95FrameMs,meanTransferMs;
   public List<State>states=new List<State>();
  }
  [Serializable]class State{public string name,worstProxy;public float maxOffset,maxProxyPenetration;}
  [Serializable]class SurfaceSnapshot{public string name;public Vector3[] vertices;public Vector2[] contacts;public int[] triangles;public Vector4[] capsuleA,capsuleB;public Vector4 waist,up,right,forward,leftDirection,rightDirection;public float padding,count,sleeveMargin;public bool contactsApplied;}
  [Serializable]class SurfaceSnapshots{public List<SurfaceSnapshot> meshes=new List<SurfaceSnapshot>();}
  [Serializable]class RigBone{public string name,parent;public Vector3 position;public Quaternion rotation;}
  [Serializable]class RigMesh{public string name;public string[] bones;public Vector4[] indices,weights;}
  [Serializable]class RigSnapshot{public List<RigBone> bones=new List<RigBone>();public List<RigMesh> meshes=new List<RigMesh>();}
  public static IEnumerator Run(StudioUI ui,AvatarMotion avatar,GarmentPhysics physics,string[] args){
   int hems=Array.IndexOf(args,"--verify-preserved-hems");if(hems>=0&&hems+1<args.Length){yield return CheckHems(ui,avatar,physics,args[hems+1]);yield break;}
   int diagnosis=Array.IndexOf(args,"--diagnose-sleeves");
   if(diagnosis>=0&&diagnosis+1<args.Length){
    avatar.followPointer=false;
    foreach(string mode in new[]{"current","no-contacts","rest-sleeves","rest-no-contacts","no-twist"}){
     physics.inspectRestSleeves=mode.StartsWith("rest");physics.inspectNoContacts=mode.Contains("no-contacts");physics.inspectNoTwist=mode=="no-twist";physics.ResetSimulation();yield return new WaitForSeconds(3);CaptureSkirt(avatar,args[diagnosis+1]+"-"+mode);
    }
    physics.inspectRestSleeves=false;physics.inspectNoContacts=false;physics.inspectNoTwist=false;Debug.Log("SLEEVE_DIAGNOSIS_DONE");yield break;
   }
   int inspect=Array.IndexOf(args,"--inspect-sleeve-drape");if(inspect>=0&&inspect+1<args.Length){avatar.followPointer=false;yield return new WaitForSeconds(4);CaptureSkirt(avatar,args[inspect+1]);CaptureRig(avatar,args[inspect+1]);yield break;}
   int all=Array.IndexOf(args,"--verify-sleeve-final");if(all>=0&&all+1<args.Length){string prefix=args[all+1];yield return Run(ui,avatar,physics,new[]{"--verify-cloth",prefix+"-actions"});yield return CheckIdle(ui,avatar,physics,prefix+"-idle");yield break;}
   int idle=Array.IndexOf(args,"--verify-sleeve-idle");if(idle>=0&&idle+1<args.Length){yield return CheckIdle(ui,avatar,physics,args[idle+1]);yield break;}
   int arg=Array.IndexOf(args,"--verify-cloth");if(arg<0||arg+1>=args.Length||!physics)yield break;
   string path=args[arg+1];var report=new Report{device=SystemInfo.processorType+" / "+SystemInfo.graphicsDeviceName,particles=physics.ParticleCount,movableParticles=physics.MovableCount,bodyCapsules=physics.bodyCapsules.Length};
   report.nativeVertices=physics.Native.VertexCount;report.nativePanels=physics.Native.PanelCount;var times=new List<float>();var solverTimes=new List<float>();avatar.followPointer=false;
   yield return new WaitForSeconds(2);
   report.nativeVertices=physics.Native.VertexCount;report.nativePanels=physics.Native.PanelCount;
   physics.SetSimulation(false);yield return new WaitForSeconds(.3f);
   report.toggleOffWorked=!physics.simulationEnabled&&!physics.Native.Enabled&&physics.chains.All(c=>c.bones.Select((b,i)=>Quaternion.Angle(b.localRotation,c.rest[i])<.01f).All(x=>x));
   ScreenCapture.CaptureScreenshot(path+"-off.png");yield return new WaitForEndOfFrame();
   physics.SetSimulation(true);physics.wind=.25f;yield return new WaitForSeconds(.5f);report.toggleOnWorked=physics.simulationEnabled&&physics.Native.Enabled;
   string[] names={"default","idle-wind","turn","wave","official-dance","stop-reset"};
   foreach(var name in names){
    if(name=="idle-wind")physics.wind=1;
    if(name=="turn")avatar.Act("turn","转一圈");
    if(name=="wave")avatar.Act("wave","挥手");
    if(name=="official-dance")avatar.Play(JsonUtility.FromJson<MotionClip>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Motions/kimodo-official.json"))),"官方动作样例");
    if(name=="stop-reset"){ui.StopMotion();physics.ResetSimulation();report.stopWorked=!avatar.Playing;}
    var state=new State{name=name};float started=Time.realtimeSinceStartup;float end=started+(name=="official-dance"?6.3f:3.2f);int nextCapture=1;
    while(Time.realtimeSinceStartup<end){
     yield return new WaitForEndOfFrame();times.Add(Time.unscaledDeltaTime*1000);solverTimes.Add(physics.LastTransferMs);
     float offset=physics.LastMaxOffset,penetration=physics.LastMaxPenetration;
     if(!physics.Native.Finite||float.IsNaN(offset)||float.IsInfinity(offset))report.finite=false;
     state.maxOffset=Mathf.Max(state.maxOffset,offset);if(penetration>state.maxProxyPenetration){state.maxProxyPenetration=penetration;state.worstProxy=physics.LastPenetratingBone;}report.samples++;
     if(Time.realtimeSinceStartup-started>nextCapture){ScreenCapture.CaptureScreenshot(path+"-"+name+"-"+nextCapture+".png");nextCapture++;}
    }
    report.maxOffset=Mathf.Max(report.maxOffset,state.maxOffset);report.maxProxyPenetration=Mathf.Max(report.maxProxyPenetration,state.maxProxyPenetration);
    report.states.Add(state);ScreenCapture.CaptureScreenshot(path+"-"+name+".png");yield return new WaitForEndOfFrame();
    if(name=="default")CaptureSkirt(avatar,path);
    else CaptureSurfaces(avatar,path+"-"+name);
   }
   physics.wind=.25f;avatar.followPointer=true;
   times.Sort();if(times.Count>0){report.meanFrameMs=times.Average();report.p95FrameMs=times[Mathf.Min(times.Count-1,Mathf.FloorToInt(times.Count*.95f))];report.meanTransferMs=solverTimes.Average();}
   File.WriteAllText(path+".json",JsonUtility.ToJson(report,true));Debug.Log("CLOTH_VERIFICATION_DONE "+path);
  }
  [Serializable]class HemCase {public string name;public int frames;public bool finite=true;public float maxPhysicalSeamLag,maxVisibleSeamGap,meanFrameMs;}
  [Serializable]class HemReport {public bool toggleOff,toggleOn;public List<HemCase> cases=new List<HemCase>();}
  static IEnumerator CheckHems(StudioUI ui,AvatarMotion avatar,GarmentPhysics physics,string path){
   avatar.followPointer=false;physics.wind=.25f;yield return new WaitForSeconds(4);
   var report=new HemReport();
   foreach(string name in new[]{"stand","wave","wave-return","turn","motion","motion-return","settled"}){
    if(name=="wave")avatar.Act("wave","挥手");
    if(name=="turn")avatar.Act("turn","转一圈");
    if(name=="motion")avatar.Play(JsonUtility.FromJson<MotionClip>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Motions/kimodo-official.json"))),"官方动作样例");
    if(name.EndsWith("return"))ui.StopMotion();
    float duration=name=="wave"?1.6f:name=="turn"?1.7f:name=="motion"?2.2f:name=="settled"?20:3;
    var state=new HemCase{name=name};float elapsed=0,totalFrame=0;
    while(elapsed<duration){yield return new WaitForEndOfFrame();elapsed+=Time.deltaTime;state.frames++;totalFrame+=Time.unscaledDeltaTime*1000;state.finite&=physics.Native.Finite;
     foreach(var hem in physics.Native.hems){state.maxPhysicalSeamLag=Mathf.Max(state.maxPhysicalSeamLag,hem.PhysicalSeamLag);state.maxVisibleSeamGap=Mathf.Max(state.maxVisibleSeamGap,hem.SeamError);}
    }
    state.meanFrameMs=totalFrame/state.frames;report.cases.Add(state);CaptureSkirt(avatar,path+"-"+name);CaptureRig(avatar,path+"-"+name);
    // Camera exports block the main thread; let the existing stall/reset path
    // finish before triggering the next action, so it cannot skip its blend.
    yield return null;yield return new WaitForEndOfFrame();
   }
   physics.SetSimulation(false);yield return new WaitForSeconds(.5f);report.toggleOff=!physics.Native.Enabled&&physics.Native.hems.All(h=>!h.renderer.enabled);
   physics.SetSimulation(true);yield return new WaitForSeconds(4);report.toggleOn=physics.Native.Enabled&&physics.Native.hems.All(h=>h.renderer.enabled);
   CaptureSkirt(avatar,path+"-final");CaptureRig(avatar,path+"-final");File.WriteAllText(path+".json",JsonUtility.ToJson(report,true));Debug.Log("HEM_VERIFICATION_DONE "+path);
  }
  static void CaptureRig(AvatarMotion avatar,string path){
   var result=new RigSnapshot();
   foreach(var t in avatar.GetComponentsInChildren<Transform>())if(t.name.Contains("Sleeve"))result.bones.Add(new RigBone{name=t.name,parent=t.parent?t.parent.name:"",position=t.position,rotation=t.rotation});
   foreach(var r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.name=="cloth1"||r.name=="cloth2")){
    var weights=r.sharedMesh.boneWeights;var mesh=new RigMesh{name=r.name,bones=r.bones.Select(b=>b.name).ToArray(),indices=new Vector4[weights.Length],weights=new Vector4[weights.Length]};
    for(int i=0;i<weights.Length;i++){var w=weights[i];mesh.indices[i]=new Vector4(w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3);mesh.weights[i]=new Vector4(w.weight0,w.weight1,w.weight2,w.weight3);}
    result.meshes.Add(mesh);
   }
   File.WriteAllText(path+"-rig.json",JsonUtility.ToJson(result));Debug.Log("SLEEVE_DRAPE_DONE "+path);
  }
  [Serializable]class IdleReport{public bool finite=true;public float duration=60,minSleeveSideDistance=100,meanFrameMs;public int samples;}
  static IEnumerator CheckIdle(StudioUI ui,AvatarMotion avatar,GarmentPhysics physics,string path){
   yield return new WaitForSeconds(2);ui.StopMotion();avatar.followPointer=false;physics.wind=.25f;physics.ResetSimulation();
   var report=new IdleReport();float start=Time.realtimeSinceStartup;int capture=10;float frames=0;
   while(Time.realtimeSinceStartup-start<60){yield return new WaitForEndOfFrame();report.samples++;frames+=Time.unscaledDeltaTime*1000;report.finite&=physics.Native.Finite;
    foreach(var chain in physics.chains.Where(c=>!c.skirt)){float side=chain.bones[0].name.Contains("Left")?-1:1;float distance=Vector3.Dot(chain.bones[chain.bones.Length-1].position-physics.hips.position,physics.hips.right)*side;report.minSleeveSideDistance=Mathf.Min(report.minSleeveSideDistance,distance);}
    if(Time.realtimeSinceStartup-start>=capture){CaptureSurfaces(avatar,path+"-"+capture);ScreenCapture.CaptureScreenshot(path+"-"+capture+".png");capture+=10;}
   }
   report.meanFrameMs=frames/Mathf.Max(1,report.samples);CaptureSkirt(avatar,path);File.WriteAllText(path+".json",JsonUtility.ToJson(report,true));avatar.followPointer=true;Debug.Log("SLEEVE_IDLE_DONE "+path);
  }
  static void CaptureSurfaces(AvatarMotion avatar,string path){
   var surfaces=new SurfaceSnapshots();
   foreach(var r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>()){
    if(r.sharedMesh.name!="Body"&&r.name!="cloth1"&&r.name!="cloth2"&&!r.name.StartsWith("sleeve-"))continue;
    var baked=new Mesh();r.BakeMesh(baked);var vertices=baked.vertices;
    for(int i=0;i<vertices.Length;i++)vertices[i]=r.transform.TransformPoint(vertices[i]);
    var p=new MaterialPropertyBlock();r.GetPropertyBlock(p);
    surfaces.meshes.Add(new SurfaceSnapshot{name=r.name.StartsWith("cloth")||r.name.StartsWith("sleeve-")?r.name:r.sharedMesh.name,vertices=vertices,contacts=r.sharedMesh.uv3,triangles=baked.triangles,capsuleA=p.GetVectorArray("_ClothCapsuleA"),capsuleB=p.GetVectorArray("_ClothCapsuleB"),waist=p.GetVector("_ClothWaist"),up=p.GetVector("_ClothUp"),right=p.GetVector("_ClothRight"),forward=p.GetVector("_ClothForward"),leftDirection=p.GetVector("_SleeveLeftDirection"),rightDirection=p.GetVector("_SleeveRightDirection"),padding=p.GetFloat("_ClothPadding"),sleeveMargin=p.GetFloat("_SleeveContactMargin"),count=p.GetFloat("_ClothColliderCount")});UnityEngine.Object.Destroy(baked);
   }
   foreach(var hem in avatar.GetComponent<GarmentPhysics>().Native.hems){
    surfaces.meshes.Add(new SurfaceSnapshot{name="physics-"+hem.renderer.name,vertices=hem.PhysicsWorldVertices(),triangles=hem.PhysicsTriangles,contactsApplied=true});
    Debug.Log("HEM_SEAM "+hem.renderer.name+" lag="+hem.PhysicalSeamLag);
    var p=new MaterialPropertyBlock();hem.renderer.GetPropertyBlock(p);
    surfaces.meshes.Add(new SurfaceSnapshot{name=hem.renderer.name,vertices=hem.WorldVertices(),contacts=hem.mesh.uv3,triangles=hem.mesh.triangles,contactsApplied=true,capsuleA=p.GetVectorArray("_ClothCapsuleA"),capsuleB=p.GetVectorArray("_ClothCapsuleB"),waist=p.GetVector("_ClothWaist"),up=p.GetVector("_ClothUp"),right=p.GetVector("_ClothRight"),forward=p.GetVector("_ClothForward"),leftDirection=p.GetVector("_SleeveLeftDirection"),rightDirection=p.GetVector("_SleeveRightDirection"),padding=p.GetFloat("_ClothPadding"),sleeveMargin=p.GetFloat("_SleeveContactMargin"),count=p.GetFloat("_ClothColliderCount")});
   }
   File.WriteAllText(path+"-surfaces.json",JsonUtility.ToJson(surfaces));
  }
  static void CaptureSkirt(AvatarMotion avatar,string path){
   CaptureSurfaces(avatar,path);
   var camera=new GameObject("Clothing inspection camera").AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Camera.main.backgroundColor;camera.fieldOfView=38;camera.nearClipPlane=.03f;
   var target=avatar.animator.GetBoneTransform(HumanBodyBones.Hips).position;target.y=.64f;
   foreach(float angle in new[]{0f,45f,-45f,180f}){
    camera.transform.position=target+Quaternion.Euler(0,angle,0)*new Vector3(0,0,1.65f);camera.transform.LookAt(target);
    var rt=new RenderTexture(700,900,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(700,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,700,900),0,0);image.Apply();File.WriteAllBytes(path+"-skirt-"+angle+".png",image.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);
   }
   foreach(float angle in new[]{0f,90f,180f}){
    target=avatar.animator.GetBoneTransform(HumanBodyBones.Hips).position;target.y=.85f;
    camera.transform.position=target+Quaternion.Euler(-8,angle,0)*new Vector3(0,0,3.1f);camera.transform.LookAt(target);
    var rt=new RenderTexture(800,1000,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(800,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,800,1000),0,0);image.Apply();File.WriteAllBytes(path+"-full-"+angle+".png",image.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);
   }
   UnityEngine.Object.Destroy(camera.gameObject);
  }
 }
}
