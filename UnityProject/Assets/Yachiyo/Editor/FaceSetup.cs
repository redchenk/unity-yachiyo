using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Yachiyo.Editor {
 public static class FaceSetup {
  public static void Install(){
   EditorSceneManager.OpenScene("Assets/Yachiyo/Studio.unity");var avatar=UnityEngine.Object.FindAnyObjectByType<AvatarMotion>();Configure(avatar);
   EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();Preview(avatar,"../artifacts/face-after");
  }
  public static void Configure(AvatarMotion avatar){
   foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{"Assets/#八千代"})){
    var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
    if(material.name=="mat_ycy_eye_alpha_UTS"){
     var texture=material.mainTexture;material.shader=Shader.Find("Yachiyo/EyeOverlay");material.mainTexture=texture;material.SetColor("_Color",Color.white);material.renderQueue=3000;EditorUtility.SetDirty(material);continue;
    }
    if(material.shader.name!="Yachiyo/SoftToon")continue;
    material.SetFloat("_FaceLighting",material.name=="mat_ycy_face_UTS"?1:0);EditorUtility.SetDirty(material);
    material.SetFloat("_CullMode",material.name.Contains("face")?2:0);
    Debug.Log("FACE_LIGHTING_CONFIG ["+material.name+"] "+AssetDatabase.GetAssetPath(material)+" value="+material.GetFloat("_FaceLighting"));
   }
   if(!avatar.GetComponent<FaceLighting>())avatar.gameObject.AddComponent<FaceLighting>();
   avatar.GetComponent<FaceLighting>().ApplyNow();
   foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))if(light.type==LightType.Directional)light.transform.rotation=Quaternion.Euler(30,155,0);
  }
  static void Preview(AvatarMotion avatar,string path){
   var face=avatar.GetComponent<FaceLighting>();face.ApplyNow();avatar.Initialize();avatar.followPointer=false;avatar.Tick(.4f);face.ApplyNow();
   var camera=Camera.main;camera.rect=new Rect(0,0,1,1);camera.fieldOfView=30;var head=avatar.animator.GetBoneTransform(HumanBodyBones.Head);var target=head.position+new Vector3(0,.067f,0);
   foreach(float angle in new[]{0f,-35f,35f}){camera.transform.position=target+Quaternion.Euler(0,angle,0)*new Vector3(0,0,.53f);camera.transform.LookAt(target);Capture(camera,path+"-"+angle+".png");}
   camera.transform.position=target+new Vector3(0,0,.53f);camera.transform.LookAt(target);
   avatar.expression=.75f;avatar.Tick(.01f);face.ApplyNow();Capture(camera,path+"-smile.png");
   foreach(var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())foreach(string name in new[]{"EyeClosedLeft","EyeClosedRight"}){int index=renderer.sharedMesh.GetBlendShapeIndex(name);if(index>=0)renderer.SetBlendShapeWeight(index,100);}
   Capture(camera,path+"-blink.png");
  }
  public static void Diagnose(){
   EditorSceneManager.OpenScene("Assets/Yachiyo/Studio.unity");var avatar=UnityEngine.Object.FindAnyObjectByType<AvatarMotion>();avatar.Initialize();avatar.followPointer=false;avatar.Tick(.4f);
   var camera=Camera.main;camera.rect=new Rect(0,0,1,1);camera.fieldOfView=30;var head=avatar.animator.GetBoneTransform(HumanBodyBones.Head);var target=head.position+new Vector3(0,.067f,0);camera.transform.position=target+new Vector3(0,0,.53f);camera.transform.LookAt(target);
   Capture(camera,"../artifacts/face-before.png");
   foreach(var r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>()){
    Debug.Log("FACE_MATERIAL "+r.name+" MESH "+r.sharedMesh.name+" "+string.Join("; ",r.sharedMaterials.Select(m=>m.name+" / "+AssetDatabase.GetAssetPath(m.mainTexture))));
    var materials=r.sharedMaterials.Select(m=>new Material(Shader.Find("Unlit/Texture")){mainTexture=m.mainTexture}).ToArray();r.sharedMaterials=materials;
   }
   Capture(camera,"../artifacts/face-unlit.png");
  }
  static void Capture(Camera cam,string path){var rt=new RenderTexture(900,900,24);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var tex=new Texture2D(900,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,900,900),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());cam.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);}
 }
}
