using System.Linq;
using UnityEngine;

namespace Yachiyo {
 [DefaultExecutionOrder(200)]
 public class FaceLighting : MonoBehaviour {
  Transform head;
  Vector3 localForward,localRight,localUp,localOrigin;
  SkinnedMeshRenderer[] renderers;
  MaterialPropertyBlock properties;
  void Awake()=>Initialize();
  void Initialize(){
   if(head)return;head=GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
   localForward=head.InverseTransformDirection(transform.forward);localRight=head.InverseTransformDirection(transform.right);localUp=head.InverseTransformDirection(transform.up);
   localOrigin=head.InverseTransformPoint(head.position+transform.up*.065f);
   renderers=GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.sharedMaterials.Any(m=>m&&m.HasProperty("_FaceLighting")&&m.GetFloat("_FaceLighting")>.5f)).ToArray();properties=new MaterialPropertyBlock();
  }
  public void ApplyNow(){Initialize();foreach(var r in renderers){r.GetPropertyBlock(properties);properties.SetVector("_FaceOrigin",head.TransformPoint(localOrigin));properties.SetVector("_FaceForward",head.TransformDirection(localForward));properties.SetVector("_FaceRight",head.TransformDirection(localRight));properties.SetVector("_FaceUp",head.TransformDirection(localUp));r.SetPropertyBlock(properties);}}
  void LateUpdate()=>ApplyNow();
 }
}
