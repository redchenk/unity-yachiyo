using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if YACHIYO_MAGICA
using MagicaCloth2;
#endif
namespace Yachiyo {
 public sealed partial class FullCharacterCloth {
#if YACHIYO_MAGICA
  sealed class LimbContact {public Transform from,to;public MagicaCapsuleCollider collider;public float startRadius,endRadius;}
  readonly List<LimbContact> limbContacts=new List<LimbContact>();
  readonly List<Mesh> bodyContactMeshes=new List<Mesh>();
  readonly Dictionary<SkinnedMeshRenderer,Vector2[]> bodyContactMasks=new Dictionary<SkinnedMeshRenderer,Vector2[]>();
  readonly Vector4[] contactA=new Vector4[8],contactB=new Vector4[8];
  readonly MaterialPropertyBlock bodyContactProperties=new MaterialPropertyBlock();
  void PrepareBodyContactSurface(SkinnedMeshRenderer r){
   var mesh=r.sharedMesh;
   if(mesh!=furisodeRuntime){mesh=Instantiate(mesh);mesh.name=r.sharedMesh.name+" / body contact skin";bodyContactMeshes.Add(mesh);r.sharedMesh=mesh;}
   var weights=r==furisodeSource?furisodeOriginal.boneWeights:mesh.boneWeights;var bones=r==furisodeSource?furisodeOriginalBones:r.bones;
   var mask=new Vector2[mesh.vertexCount];var uv=new List<Vector2>(mesh.vertexCount);var baked=new Mesh();r.BakeMesh(baked);var vertices=baked.vertices;var normals=baked.normals;
   var seam=r==furisodeSource?Enumerable.Range(0,vertices.Length).Where(i=>sleeveFixedMask[i]).Select(i=>vertices[i]).ToArray():Array.Empty<Vector3>();
   for(int i=0;i<mask.Length;i++){
    var w=weights[i];float skirt=0,bow=0;
    foreach(var p in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}){string n=bones[p.Item1].name;if(n.Contains("Skirt")||n.Contains("Sirt"))skirt+=p.Item2;if(n.StartsWith("Acs_UnderSleeveRibbon"))bow+=p.Item2;}
    float movable=IsFurisodeDriven(r,i)||skirt>.25f?1:0;if(IsSleeveFixed(r,i)||bow>.001f)movable=0;
    if(movable>0&&IsFurisodeDriven(r,i)&&seam.Length>0){float distance=seam.Min(p=>(p-vertices[i]).sqrMagnitude);movable*=Mathf.SmoothStep(0,1,Mathf.Sqrt(distance)/.18f);}
    var world=r.transform.TransformPoint(vertices[i]);float nearest=float.MaxValue;bool outward=true;
    foreach(var c in limbContacts){var axis=c.to.position-c.from.position;float t=Mathf.Clamp01(Vector3.Dot(world-c.from.position,axis)/axis.sqrMagnitude);var away=world-(c.from.position+axis*t);float d=away.sqrMagnitude;if(d<nearest){nearest=d;outward=Vector3.Dot(r.transform.TransformDirection(normals[i]),away)>0;}}
    float margin=(r.name=="cloth2"?.005f:.003f)+(outward?.003f:0);
    if(r==furisodeSource){if(sleeveShellMask[i]&&outward)margin+=.012f;margin=Mathf.Lerp(margin,.001f,sleeveCuffOverlap[i]);}
    mask[i]=new Vector2(movable,margin);uv.Add(mask[i]);
   }
   Destroy(baked);mesh.SetUVs(4,uv);bodyContactMasks[r]=mask;
  }
  void UpdateVisibleBodyContacts(bool enabled){
   for(int i=0;i<limbContacts.Count;i++){var c=limbContacts[i];var a=c.from.position;var b=c.to.position;contactA[i]=new Vector4(a.x,a.y,a.z,c.startRadius);contactB[i]=new Vector4(b.x,b.y,b.z,c.endRadius);}
   foreach(var r in bodyContactMasks.Keys){r.GetPropertyBlock(bodyContactProperties);bodyContactProperties.SetFloat("_BodyContactEnabled",enabled?1:0);bodyContactProperties.SetFloat("_BodyContactCount",limbContacts.Count);bodyContactProperties.SetVectorArray("_BodyContactA",contactA);bodyContactProperties.SetVectorArray("_BodyContactB",contactB);r.SetPropertyBlock(bodyContactProperties);}
  }
  // CPU counterpart is also used for sewn ornament anchors and diagnostic
  // exports; the vertex shader applies the same contact to the displayed skin.
  Vector3 ContactPoint(Vector3 point,float margin){
   for(int pass=0;pass<2;pass++)for(int i=0;i<limbContacts.Count;i++){
    var a=contactA[i];var b=contactB[i];var axis=(Vector3)b-(Vector3)a;float t=Mathf.Clamp01(Vector3.Dot(point-(Vector3)a,axis)/Mathf.Max(axis.sqrMagnitude,.000001f));var center=(Vector3)a+axis*t;var away=point-center;float distance=away.magnitude,radius=Mathf.Lerp(a.w,b.w,t)+margin;
    if(distance<radius+.004f&&distance>.00001f){float outside=radius+.001f*Mathf.Log(1+Mathf.Exp((distance-radius)/.001f));point=center+away*(outside/distance);}
   }return point;
  }
  public Vector3 DisplayContactPoint(SkinnedMeshRenderer renderer,int vertex,Vector3 world){
   if(!Active||!bodyContactMasks.TryGetValue(renderer,out var mask)||mask[vertex].x<=0)return world;return Vector3.Lerp(world,ContactPoint(world,mask[vertex].y),mask[vertex].x);
  }
  void DisposeBodyContacts(){foreach(var m in bodyContactMeshes)Destroy(m);}
  void CreateLimbContacts(){
   var body=GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.name=="Body");var mesh=new Mesh();body.BakeMesh(mesh);var points=mesh.vertices.Select(body.transform.TransformPoint).ToArray();var weights=body.sharedMesh.boneWeights;
   foreach(var pair in new[]{
    (HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,.035f,.055f),
    (HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,.035f,.055f),
    (HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,.028f,.050f),
    (HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,.028f,.050f),
    (HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,.059f,.12f),
    (HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,.059f,.12f),
    (HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,.043f,.095f),
    (HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,.043f,.095f)}){
    var from=animator.GetBoneTransform(pair.Item1);var to=animator.GetBoneTransform(pair.Item2);var axis=to.position-from.position;float length=axis.magnitude;axis/=length;
    var near=new List<float>();var far=new List<float>();
    for(int i=0;i<points.Length;i++){
     var w=weights[i];float influence=0;
     foreach(var p in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}){var b=body.bones[p.Item1];if(p.Item2>0&&(b==from||b.IsChildOf(from))&&b!=to&&!b.IsChildOf(to))influence+=p.Item2;}
     if(influence<.5f)continue;
     var relative=points[i]-from.position;float t=Vector3.Dot(relative,axis)/length;if(t<-.08f||t>1.08f)continue;
     float radius=Vector3.ProjectOnPlane(relative,axis).magnitude;if(t<.6f)near.Add(radius);if(t>.4f)far.Add(radius);
    }
    float Fit(List<float> values){if(values.Count<5)return pair.Item3+.008f;values.Sort();return Mathf.Clamp(values[Mathf.Min(values.Count-1,(int)(values.Count*.99f))]+.006f,pair.Item3,pair.Item4);}
    float start=Fit(near),end=Fit(far);var tContact=new GameObject("Magica body / "+pair.Item1).transform;tContact.SetParent(transform,false);
    var collider=tContact.gameObject.AddComponent<MagicaCapsuleCollider>();collider.direction=MagicaCapsuleCollider.Direction.Y;
    var contact=new LimbContact{from=from,to=to,collider=collider,startRadius=start,endRadius=end};limbContacts.Add(contact);colliders.Add(collider);
    Debug.Log("CLOTH_BODY_FIT "+pair.Item1+" start="+start+" end="+end+" samples="+(near.Count+far.Count));
   }
   Destroy(mesh);UpdateLimbContacts();
  }
  void UpdateLimbContacts(){
   foreach(var c in limbContacts){
    var axis=c.from.position-c.to.position;float length=axis.magnitude;if(length<.0001f)continue;axis/=length;
    // MC capsule length includes both end radii. Place the sphere centres at
    // the actual joints, including when a knee or elbow bends deeply.
    c.collider.transform.SetPositionAndRotation((c.from.position+c.to.position)*.5f+axis*(c.startRadius-c.endRadius)*.5f,Quaternion.FromToRotation(Vector3.up,axis));
    c.collider.SetSize(c.startRadius,c.endRadius,length+c.startRadius+c.endRadius);
   }
   UpdateVisibleBodyContacts(Active);
  }
#endif
 }
}
