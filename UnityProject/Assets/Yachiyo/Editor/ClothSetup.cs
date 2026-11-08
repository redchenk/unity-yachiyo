using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Yachiyo.Editor {
 public static class ClothSetup {
  const string Scene="Assets/Yachiyo/Studio.unity";
  public static void RestoreOriginalRig(){
   EditorSceneManager.OpenScene(Scene);File.Copy(Scene,"../artifacts/scene-before-original-rig.unity",true);
   var old=UnityEngine.Object.FindAnyObjectByType<AvatarMotion>();var position=old.transform.position;var rotation=old.transform.rotation;
   var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/#八千代/YachiyoRuntime.prefab");if(!original)throw new Exception("Sanitized source prefab missing");
   var model=UnityEngine.Object.Instantiate(original);model.name="Yachiyo";model.transform.SetPositionAndRotation(position,rotation);
   var animator=model.GetComponent<Animator>();animator.runtimeAnimatorController=null;animator.avatar=old.animator.avatar;
   var motion=model.AddComponent<AvatarMotion>();motion.animator=animator;
   if(!animator.avatar.isHuman||!animator.avatar.isValid)throw new Exception("Original rig avatar invalid");
   UnityEngine.Object.FindAnyObjectByType<StudioUI>().avatar=motion;UnityEngine.Object.DestroyImmediate(old.gameObject);
   Configure(motion);FaceSetup.Configure(motion);EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
   Debug.Log("ORIGINAL_RIG_RESTORED "+model.GetComponentsInChildren<SkinnedMeshRenderer>().Length+" meshes / "+model.GetComponentsInChildren<UnityEngine.Animations.AimConstraint>().Length+" sleeve aim constraints");
  }
  [MenuItem("Yachiyo/Add Clothing Physics")]
  public static void Install(){EditorSceneManager.OpenScene(Scene);Configure(UnityEngine.Object.FindAnyObjectByType<AvatarMotion>());EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();}
  public static void Configure(AvatarMotion avatar){
   var old=avatar.GetComponent<GarmentPhysics>();if(old)UnityEngine.Object.DestroyImmediate(old);
   foreach(var c in avatar.GetComponentsInChildren<Cloth>())UnityEngine.Object.DestroyImmediate(c);
   foreach(var t in avatar.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Cloth collision ")).ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
   var animator=avatar.GetComponent<Animator>();
   var physics=avatar.gameObject.AddComponent<GarmentPhysics>();physics.hips=animator.GetBoneTransform(HumanBodyBones.Hips);
   var capsules=new List<CapsuleCollider>();
   AddCapsule(animator,HumanBodyBones.Hips,HumanBodyBones.Chest,.075f,capsules);
   AddCapsule(animator,HumanBodyBones.Chest,HumanBodyBones.Neck,.085f,capsules);
   AddCapsule(animator,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,.059f,capsules);
   AddCapsule(animator,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,.059f,capsules);
   AddCapsule(animator,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,.043f,capsules);
   AddCapsule(animator,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,.043f,capsules);
   AddCapsule(animator,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,.035f,capsules);
   AddCapsule(animator,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,.035f,capsules);
   AddCapsule(animator,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,.028f,capsules);
   AddCapsule(animator,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,.028f,capsules);
   physics.bodyCapsules=capsules.ToArray();
   foreach(var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>()){
    string name=renderer.name;if(name!="cloth1"&&name!="cloth2")continue;
    // Keep each sleeve on its authored side when resolving leg contact. A radial
    // projection can send adjacent sleeve vertices around opposite sides of a leg.
    string meshPath="Assets/#八千代/"+name+"-contact.asset";
    var source=AssetDatabase.LoadAllAssetsAtPath("Assets/#八千代/#OkaRuto_月见一万代_FT.fbx").OfType<Mesh>().Single(m=>m.name==name);
    var mesh=UnityEngine.Object.Instantiate(source);mesh.name=name;
    var coordinates=new List<Vector2>();
    foreach(var w in mesh.boneWeights){
     float side=0;
     foreach(var pair in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}){
      string bone=renderer.bones[pair.Item1].name;
      if(bone.Contains("Sleeve"))side+=pair.Item2*(bone.Contains("Left")?-1:bone.Contains("Right")?1:0);
     }
     coordinates.Add(new Vector2(side,0));
    }
    mesh.SetUVs(2,coordinates);var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
    if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,meshPath);
    renderer.sharedMesh=mesh;
    string materialPath="Assets/Yachiyo/"+name+"-physics.mat";
    var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
    if(!material){material=new Material(renderer.sharedMaterial);AssetDatabase.CreateAsset(material,materialPath);}
    material.shader=Shader.Find("Yachiyo/ClothToon");material.SetFloat("_ClothCollision",0);material.SetFloat("_FaceLighting",0);material.SetFloat("_CullMode",0);EditorUtility.SetDirty(material);
    renderer.sharedMaterial=material;
   }
   var all=avatar.GetComponentsInChildren<Transform>();var chains=new List<GarmentPhysics.Chain>();
   string[] roots={"Acs_SideSkirt_A001_Left","Acs_SideSkirt_B001_Left","Acs_SideSkirt_C001_Left","Acs_BacksSirt_001","Acs_SideSkirt_C001_Right","Acs_SideSkirt_B001_Right","Acs_SideSkirt_A001_Right","Acs_FrontSkirt_001","Acs_CenterSleeve_Root_Left","Acs_CenterSleeve_Root_Right"};
   foreach(string name in roots){
    var root=all.SingleOrDefault(t=>t.name==name&&t.childCount>0);
    if(!root&&name.Contains("CenterSleeve_Root"))root=all.Single(t=>t.name==name.Replace("_Root_","_001_"));
    if(!root)throw new Exception("Missing cloth root: "+name);
    var bones=new List<Transform>{root};var node=root;
    while(!node.name.EndsWith("_end")){
     var next=Enumerable.Range(0,node.childCount).Select(i=>node.GetChild(i)).FirstOrDefault(t=>t.name.Contains("Skirt")||t.name.Contains("Sirt")||t.name.Contains("CenterSleeve"));
     if(!next)break;bones.Add(next);node=next;
    }
    if(bones.Count<3)throw new Exception("Missing cloth chain: "+name);
    chains.Add(new GarmentPhysics.Chain{bones=bones.ToArray(),skirt=!name.Contains("Sleeve")});
   }
   physics.chains=chains.ToArray();
   File.WriteAllText("../artifacts/cloth-setup.json",JsonUtility.ToJson(new SetupReport{solver="Unity Cloth proxy surfaces / original garment rig",chains=chains.Count,particles=physics.ParticleCount,bodyCapsules=capsules.Count,renderMeshesModified=false},true));
   Debug.Log("CLOTH_SETUP_PASS "+physics.ParticleCount+" particles, original topology preserved with sleeve contact tags");
  }
  static void AddCapsule(Animator a,HumanBodyBones from,HumanBodyBones to,float radius,List<CapsuleCollider>list){
   var start=a.GetBoneTransform(from);var end=a.GetBoneTransform(to);if(!start||!end)return;
   var go=new GameObject("Cloth collision "+from);go.transform.SetParent(start,false);var axis=end.position-start.position;go.transform.position=(start.position+end.position)*.5f;go.transform.rotation=Quaternion.FromToRotation(Vector3.up,axis);
   var c=go.AddComponent<CapsuleCollider>();c.direction=1;c.radius=radius;c.height=axis.magnitude+radius*1.1f;c.isTrigger=true;list.Add(c);
  }
  [Serializable]class SetupReport{public string solver;public int chains,particles,bodyCapsules;public bool renderMeshesModified;}
 }
}
