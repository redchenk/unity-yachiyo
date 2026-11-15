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
 // Opt-in native geometry/visual regression. Exports displayed surfaces after
 // all cloth writes, so collisions in the render skin are not hidden by proxies.
 public sealed class ClothContactVerification:MonoBehaviour {
  [Serializable]class Surface {public string name;public Vector3[] vertices;public int[] triangles;public string[] regions,materials;}
  [Serializable]class Shape {public string name;public Vector3 center,axis,size;}
  [Serializable]class Snapshot {public List<Surface> surfaces=new List<Surface>();public List<Shape> colliders=new List<Shape>();}
  [Serializable]class Report {public bool ready,finite=true;public int groups;public string device;public List<string> poses=new List<string>();}
  string path;AvatarMotion avatar;FullCharacterCloth cloth;Animator animator;Report report=new Report();
  readonly Dictionary<Transform,Quaternion> rest=new Dictionary<Transform,Quaternion>();
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--verify-cloth-contacts");if(i>=0&&i+1<args.Length)new GameObject("Cloth contact verification").AddComponent<ClothContactVerification>().path=args[i+1];}
  IEnumerator Start(){
   Directory.CreateDirectory(path);Application.runInBackground=true;
   avatar=FindFirstObjectByType<AvatarMotion>();animator=avatar.GetComponent<Animator>();cloth=avatar.GetComponent<GarmentPhysics>().FullCloth;
   float deadline=Time.realtimeSinceStartup+60;while(cloth&&!cloth.Ready&&!cloth.Failed&&Time.realtimeSinceStartup<deadline)yield return null;
   report.ready=cloth&&cloth.Ready;report.device=SystemInfo.graphicsDeviceName;if(!report.ready){Finish();yield break;}report.groups=cloth.GroupCount;
   avatar.followPointer=false;yield return new WaitForSeconds(2);avatar.enabled=false;
   foreach(var b in new[]{HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg}){var t=animator.GetBoneTransform(b);rest[t]=t.localRotation;}
   var args=Environment.GetCommandLineArgs();int selection=Array.IndexOf(args,"--cloth-contact-poses");
   var poses=selection>=0&&selection+1<args.Length?args[selection+1].Split(','):new[]{"idle","wrists","raised-arms","left-knee","right-knee","left-side-kick","right-side-kick","wide-step","release"};
   foreach(string pose in poses){
    foreach(var p in rest)p.Key.localRotation=p.Value;
    float start=Time.realtimeSinceStartup;bool peak=false;
    while(Time.realtimeSinceStartup-start<5){
     float t=Time.realtimeSinceStartup-start;float amount=Mathf.SmoothStep(0,1,t/.65f);
     foreach(var p in rest)p.Key.localRotation=p.Value;
     if(pose=="wrists"||pose=="raised-arms")for(int side=0;side<2;side++){
      var upper=animator.GetBoneTransform(side==0?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);var lower=animator.GetBoneTransform(side==0?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);var hand=animator.GetBoneTransform(side==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
      upper.rotation=Quaternion.AngleAxis((side==0?-1:1)*amount*(pose=="wrists"?20:85),avatar.transform.forward)*upper.rotation;
      var axis=Vector3.Cross(hand.position-lower.position,avatar.transform.forward).normalized;lower.rotation=Quaternion.AngleAxis(amount*100,axis)*lower.rotation;
      hand.localRotation=rest[hand]*Quaternion.Euler(45*amount,0,25*amount);
     }
     if(pose.EndsWith("knee")){bool left=pose.StartsWith("left");var upper=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);var lower=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);upper.rotation=Quaternion.AngleAxis(-85*amount,avatar.transform.right)*upper.rotation;lower.rotation=Quaternion.AngleAxis(110*amount,avatar.transform.right)*lower.rotation;}
     if(pose.EndsWith("side-kick")){bool left=pose.StartsWith("left");var upper=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);upper.rotation=Quaternion.AngleAxis((left?-1:1)*75*amount,avatar.transform.forward)*upper.rotation;
      // Balance arms clear the kick path; do not conflate impossible hand/leg
      // self-intersection with the thigh/garment regression being exercised.
      for(int side=0;side<2;side++){var arm=animator.GetBoneTransform(side==0?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);arm.rotation=Quaternion.AngleAxis((side==0?-1:1)*55*amount,avatar.transform.forward)*arm.rotation;}
     }
     if(pose=="wide-step")for(int side=0;side<2;side++){var leg=animator.GetBoneTransform(side==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);leg.rotation=Quaternion.AngleAxis((side==0?-1:1)*45*amount,avatar.transform.forward)*leg.rotation;}
     yield return new WaitForEndOfFrame();
     if(t>1&&!peak){Capture(pose+"-moving",pose=="wrists"||pose=="raised-arms");peak=true;}
    }
    yield return new WaitForEndOfFrame();Capture(pose,pose=="wrists"||pose=="raised-arms");report.poses.Add(pose);
   }Finish();
  }
  static string Region(string name){if(name.Contains("Finger")||name.StartsWith("Hand")||name.StartsWith("TwistHand"))return "hand";if(name.StartsWith("ForeArm")||name.StartsWith("Elbow"))return "forearm";if(name.StartsWith("UpLeg"))return "thigh";if(name.StartsWith("Leg")||name.StartsWith("Foot")||name.StartsWith("Toes"))return "shin";return "other";}
  void Capture(string name,bool upper){
   var snapshot=new Snapshot();var renderers=avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.sharedMesh.name=="Body"||r.name=="cloth1"||r.name=="cloth2").ToArray();
   foreach(var r in renderers){var mesh=new Mesh();r.BakeMesh(mesh);var v=mesh.vertices.Select(r.transform.TransformPoint).ToArray();var weights=r.sharedMesh.boneWeights;var regions=new string[v.Length];
    for(int i=0;i<v.Length;i++){
#if YACHIYO_MAGICA
     v[i]=cloth.DisplayContactPoint(r,i,v[i]);
#endif
     report.finite&=float.IsFinite(v[i].x)&&float.IsFinite(v[i].y)&&float.IsFinite(v[i].z);var w=weights[i];regions[i]=Region(r.bones[w.boneIndex0].name);}
    snapshot.surfaces.Add(new Surface{name=r.sharedMesh.name=="Body"?"Body":r.name,vertices=v,triangles=mesh.triangles,regions=regions,materials=r.sharedMaterials.Select(m=>m.name+" / "+(m.mainTexture?m.mainTexture.name:"")).ToArray()});Destroy(mesh);
   }
#if YACHIYO_MAGICA
   foreach(var c in avatar.GetComponentsInChildren<ColliderComponent>())snapshot.colliders.Add(new Shape{name=c.name,center=c.transform.position,axis=c is MagicaCapsuleCollider cap?cap.transform.TransformDirection(cap.GetLocalDir()):Vector3.up,size=c.GetSize()});
#endif
   File.WriteAllText(Path.Combine(path,name+".json"),JsonUtility.ToJson(snapshot));
   var camera=new GameObject("Contact inspection camera").AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.3f,.35f);camera.fieldOfView=38;camera.nearClipPlane=.02f;
   var target=new Vector3(0,upper?1.15f:.8f,0);var rt=new RenderTexture(1000,1000,24);var texture=new Texture2D(1000,1000,TextureFormat.RGB24,false);camera.targetTexture=rt;
   foreach(float yaw in new[]{0f,70f,180f}){camera.transform.position=target+Quaternion.Euler(0,yaw,0)*new Vector3(0,.1f,upper?1.7f:2.8f);camera.transform.LookAt(target);Save(name+"-"+yaw);}
   if(name=="idle"||name=="wrists"){
    // White body distinguishes an exposed black body texture from a hole in
    // the sleeve; no production material or source asset is changed.
    var body=renderers.First(r=>r.sharedMesh.name=="Body");var materials=body.sharedMaterials;var white=new Material(Shader.Find("Unlit/Color")){color=Color.white};body.sharedMaterials=materials.Select(m=>white).ToArray();camera.transform.position=target+new Vector3(0,.1f,upper?1.7f:2.8f);camera.transform.LookAt(target);Save(name+"-white-body");body.sharedMaterials=materials;Destroy(white);
   }
   camera.targetTexture=null;RenderTexture.active=null;Destroy(rt);Destroy(texture);Destroy(camera.gameObject);
   void Save(string file){camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1000,1000),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(path,file+".png"),texture.EncodeToPNG());}
  }
  void Finish(){File.WriteAllText(Path.Combine(path,"report.json"),JsonUtility.ToJson(report,true));Application.Quit(report.ready&&report.finite?0:1);}
 }
}
