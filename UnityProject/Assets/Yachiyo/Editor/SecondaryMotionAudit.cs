using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;

namespace Yachiyo.Editor {
 // Read-only migration inventory. Does not add components, initialize physics,
 // save the scene, modify the source avatar, or imply Magica is installed.
 public static class SecondaryMotionAudit {
  [Serializable]public class Report {
   public string scene,dependencyStatus;
   public bool magicaAvailable;
   public Group[] groups;
   public ColliderInfo[] colliders;
   public ConstraintInfo[] supportConstraints;
   public string[] otherWeightedAccessoryBones;
   public string[] currentDrivers;
  }
  [Serializable]public class Group {
   public string id,label,currentDriver;
   public string[] roots,bones;
   public RendererUse[] renderers;
  }
  [Serializable]public class RendererUse {public string path;public int influencedVertices;public float totalWeight;}
  [Serializable]public class ColliderInfo {public string path,type;public Vector3 center,scale;public float radius,height;}
  [Serializable]public class ConstraintInfo {public string path,type;public bool active;public float weight;public string[] sources;}
  static string PathOf(Transform t,Transform root){return AnimationUtility.CalculateTransformPath(t,root);}
  static bool IsSettings(Transform t){for(var p=t;p;p=p.parent)if(p.name=="PB")return true;return false;}
  static string Category(string name){
   if(name.StartsWith("Acs_ArmSleeve",StringComparison.Ordinal))return "sleeve-arm";
   if(name.StartsWith("Acs_CenterSleeve",StringComparison.Ordinal))return "sleeve-hanging";
   if(name.StartsWith("Acs_UnderSleeveRibbon",StringComparison.Ordinal))return "sleeve-ribbons";
   if(name.IndexOf("Skirt",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("Sirt",StringComparison.OrdinalIgnoreCase)>=0)return "skirt";
   if(name.StartsWith("Acs_Twintail",StringComparison.Ordinal))return "twintails";
   if(name.StartsWith("Acs_FrontHair",StringComparison.Ordinal))return "front-hair";
   if(name.StartsWith("Acs_SideHair",StringComparison.OrdinalIgnoreCase)||name.StartsWith("Acs_Sidetail",StringComparison.Ordinal))return "side-hair";
   if(name.StartsWith("Acs_Obi",StringComparison.Ordinal))return "obi-accessories";
   if(name.StartsWith("Acs_NeckRibbon",StringComparison.Ordinal))return "neck-ribbons";
   if(name.StartsWith("Acs_Ring",StringComparison.Ordinal))return "hair-rings";
   if(name.Contains("Sleeve"))return "sleeve-support";
   return "";
  }
  static string Label(string id){switch(id){
   case "sleeve-arm":return "左右袖面 A-E 骨链";
   case "sleeve-hanging":return "左右大袖下垂部分";
   case "sleeve-ribbons":return "袖下飘带与绳结";
   case "skirt":return "前侧后裙摆及内外层";
   case "twintails":return "左右双马尾";
   case "front-hair":return "刘海";
   case "side-hair":return "侧发及侧尾";
   case "obi-accessories":return "腰带与腰部配饰";
   case "neck-ribbons":return "颈部飘带";
   case "hair-rings":return "发环装饰";
   default:return "原肩袖支撑与跟随约束";
  }}
  static string Driver(string id){switch(id){
   case "sleeve-arm":case "sleeve-hanging":return "SPCR + AuthoredSleevePhysics";
   case "skirt":return "Unity Cloth + NativeClothRig";
   case "twintails":case "front-hair":return "HairPhysics";
   case "side-hair":return "HairPhysics partial coverage (case-sensitive name filter)";
   case "sleeve-support":return "authored constraints / animation attachment";
   default:return "no dedicated runtime simulation found";
  }}
  [MenuItem("Yachiyo/Audit All Secondary Motion")]
  public static void Export(){
   const string scene="Assets/Yachiyo/Studio.unity";
   EditorSceneManager.OpenScene(scene);
   var avatar=UnityEngine.Object.FindFirstObjectByType<AvatarMotion>();if(!avatar)throw new InvalidOperationException("Avatar missing");
   var root=avatar.transform;var all=root.GetComponentsInChildren<Transform>(true).Where(t=>!IsSettings(t)).ToArray();
   var renderers=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
   var weights=new Dictionary<SkinnedMeshRenderer,BoneWeight[]>();
   foreach(var r in renderers)if(r.sharedMesh)weights[r]=r.sharedMesh.boneWeights;
   var weighted=new HashSet<Transform>();
   foreach(var entry in weights)foreach(var w in entry.Value){
    if(w.weight0>.0001f)weighted.Add(entry.Key.bones[w.boneIndex0]);if(w.weight1>.0001f)weighted.Add(entry.Key.bones[w.boneIndex1]);
    if(w.weight2>.0001f)weighted.Add(entry.Key.bones[w.boneIndex2]);if(w.weight3>.0001f)weighted.Add(entry.Key.bones[w.boneIndex3]);
   }
   var groups=new List<Group>();
   foreach(var entry in all.Where(t=>Category(t.name)!="").GroupBy(t=>Category(t.name)).OrderBy(g=>g.Key)){
    var set=new HashSet<Transform>(entry);var use=new List<RendererUse>();
    foreach(var r in renderers){
     if(!weights.ContainsKey(r))continue;int count=0;float total=0;
     foreach(var w in weights[r]){
      float amount=(set.Contains(r.bones[w.boneIndex0])?w.weight0:0)+(set.Contains(r.bones[w.boneIndex1])?w.weight1:0)+(set.Contains(r.bones[w.boneIndex2])?w.weight2:0)+(set.Contains(r.bones[w.boneIndex3])?w.weight3:0);
      if(amount>.0001f){count++;total+=amount;}
     }
     if(count>0)use.Add(new RendererUse{path=PathOf(r.transform,root),influencedVertices=count,totalWeight=total});
    }
    groups.Add(new Group{id=entry.Key,label=Label(entry.Key),currentDriver=Driver(entry.Key),roots=entry.Where(t=>!set.Contains(t.parent)).Select(t=>PathOf(t,root)).OrderBy(s=>s).ToArray(),bones=entry.Select(t=>PathOf(t,root)).OrderBy(s=>s).ToArray(),renderers=use.ToArray()});
   }
   var constraints=new List<ConstraintInfo>();
   foreach(var component in root.GetComponentsInChildren<Component>(true))if(component is IConstraint c){
    var sources=new List<ConstraintSource>();c.GetSources(sources);
    constraints.Add(new ConstraintInfo{path=PathOf(component.transform,root),type=component.GetType().Name,active=c.constraintActive,weight=c.weight,sources=sources.Where(s=>s.sourceTransform).Select(s=>PathOf(s.sourceTransform,root)).ToArray()});
   }
   bool magica=AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetType("MagicaCloth2.MagicaCloth")!=null);
   var report=new Report{scene=scene,magicaAvailable=magica,dependencyStatus=magica?"installed; configuration and runtime verification required":"missing licensed Magica Cloth 2 package; migration not activated",groups=groups.ToArray(),supportConstraints=constraints.ToArray(),otherWeightedAccessoryBones=weighted.Where(t=>t&&t.name.StartsWith("Acs_")&&Category(t.name)=="").Select(t=>PathOf(t,root)).OrderBy(s=>s).ToArray(),currentDrivers=new[]{"GarmentPhysics / SPCR sleeves","NativeClothRig / Unity Cloth skirts","HairPhysics / partial hair chains"},colliders=root.GetComponentsInChildren<Collider>(true).Select(c=>new ColliderInfo{path=PathOf(c.transform,root),type=c.GetType().Name,center=c is CapsuleCollider cap?cap.center:c is SphereCollider sphere?sphere.center:Vector3.zero,radius=c is CapsuleCollider cc?cc.radius:c is SphereCollider sc?sc.radius:0,height=c is CapsuleCollider hc?hc.height:0,scale=c.transform.lossyScale}).ToArray()};
   Directory.CreateDirectory("../artifacts/magica-full-character");
   File.WriteAllText("../artifacts/magica-full-character/inventory.json",JsonUtility.ToJson(report,true));
   Debug.Log("SECONDARY_MOTION_AUDIT groups="+groups.Count+" constraints="+constraints.Count+" magica="+magica+" unmappedWeightedAccessories="+report.otherWeightedAccessoryBones.Length);
  }
 }
}
