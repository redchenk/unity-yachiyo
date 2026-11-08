using System;
using System.IO;
using System.Collections;
using UnityEngine;

namespace Yachiyo {
 public static class FaceVerification {
  public static IEnumerator Run(AvatarMotion avatar,string[] args){
   int arg=Array.IndexOf(args,"--verify-face");if(arg<0||arg+1>=args.Length)yield break;
   string path=args[arg+1];yield return new WaitForSeconds(2);
   avatar.Act("idle","面部检查");avatar.followPointer=false;yield return null;avatar.enabled=false;
   var head=avatar.animator.GetBoneTransform(HumanBodyBones.Head);var lighting=avatar.GetComponent<FaceLighting>();
   var face=avatar.GetComponent<YachiyoFaceController>();
   var camera=new GameObject("Face verification camera").AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Camera.main.backgroundColor;camera.fieldOfView=30;camera.nearClipPlane=.03f;
   var renderers=avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
   foreach(string pose in new[]{"open","smile","blink","surprised","sad","thinking","talk","side","neck-front","neck-side"}){
    if(face){
     face.SetTalking(pose=="talk");
     FaceEmotion em=pose=="smile"?FaceEmotion.Happy:pose=="surprised"?FaceEmotion.Surprised:pose=="sad"?FaceEmotion.Sad:pose=="thinking"?FaceEmotion.Thinking:FaceEmotion.Neutral;
     face.SetEmotion(em,em==FaceEmotion.Neutral?0f:1f,.04f);
     if(pose=="blink")face.ForceBlink(.32f);
    }else{
     foreach(var r in renderers)for(int i=0;i<r.sharedMesh.blendShapeCount;i++){
      string name=r.sharedMesh.GetBlendShapeName(i);
      if(name=="まばたき"||name=="EyeClosedLeft"||name=="EyeClosedRight"||name=="MouthSmileLeft"||name=="MouthSmileRight")r.SetBlendShapeWeight(i,0);
     }
     foreach(var r in renderers){
      string[] names=pose=="blink"?new[]{"_101_FC_eye_close_left","_101_FC_eye_close_right","_101_EL_eye_close_left","_101_EL_eye_close_right"}:pose=="smile"?new[]{"_209_FC_mouth_smile"}:Array.Empty<string>();
      foreach(string name in names){int i=r.sharedMesh.GetBlendShapeIndex(name);if(i>=0)r.SetBlendShapeWeight(i,100);}
     }
    }
    bool neck=pose.StartsWith("neck");var target=head.position+Vector3.up*(neck?-.015f:.067f);camera.transform.position=target+Quaternion.Euler(0,pose=="side"||pose=="neck-side"?35:0,0)*new Vector3(0,0,neck?.83f:.53f);camera.transform.LookAt(target);
    lighting.ApplyNow();
    yield return new WaitForSeconds(pose=="talk"?.38f:pose=="blink"?.16f:.16f);
    yield return new WaitForEndOfFrame();
    var rt=new RenderTexture(900,900,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(900,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,900,900),0,0);image.Apply();File.WriteAllBytes(path+"-"+pose+".png",image.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);
    if(face){face.SetTalking(false);face.SetEmotion(FaceEmotion.Neutral,0f,.04f);yield return new WaitForSeconds(.10f);}
    else foreach(var r in renderers)for(int i=0;i<r.sharedMesh.blendShapeCount;i++){var name=r.sharedMesh.GetBlendShapeName(i);if(name.StartsWith("_101_")||name=="_209_FC_mouth_smile")r.SetBlendShapeWeight(i,0);}
   }
   avatar.enabled=true;avatar.followPointer=true;avatar.Act("idle","待机");UnityEngine.Object.Destroy(camera.gameObject);Debug.Log("FACE_VERIFICATION_DONE");
  }
 }
}
