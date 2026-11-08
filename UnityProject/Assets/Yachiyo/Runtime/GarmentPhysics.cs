using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yachiyo {
 // Unity Cloth physics surfaces drive the original clothing bones and preserve layer spacing.
 [DefaultExecutionOrder(100)]
 public class GarmentPhysics : MonoBehaviour {
  [Serializable]public class Chain {
   public Transform[] bones;
   public bool skirt;
   [NonSerialized]public Quaternion[] rest;
   [NonSerialized]public Vector3[] positions,targets;
   [NonSerialized]public Vector3[] across,restAcross;
   [NonSerialized]public Vector3 anchor;
  }
  public Chain[] chains=Array.Empty<Chain>();
  public CapsuleCollider[] bodyCapsules=Array.Empty<CapsuleCollider>();
  public Transform hips;
  [Range(0,1)]public float wind=.25f;
  public bool simulationEnabled=true,highQuality;
  [NonSerialized]public bool inspectRestSleeves,inspectNoContacts,inspectNoTwist;
  public int ParticleCount {get{int n=0;foreach(var c in chains)n+=c.bones.Length;return n;}}
  public int MovableCount=>ParticleCount-chains.Length;
  public NativeClothRig Native {get;private set;}
  public float LastMaxOffset {get;private set;}
  public float LastMaxPenetration {get;private set;}
  public float LastTransferMs {get;private set;}
  public string LastPenetratingBone {get;private set;}
  struct BodyShape {public Vector3 a,b;public float radius;}
  BodyShape[] shapes;
  SkinnedMeshRenderer[] surfaces;
  MaterialPropertyBlock surfaceProperties;
  Transform leftHand,rightHand;
  readonly Vector4[] capsuleA=new Vector4[12],capsuleB=new Vector4[12];
  bool initialized,reset=true;
  Vector3 previousHips;
  Quaternion previousRotation;
  void Awake()=>Initialize();
  void Initialize(){
   if(initialized)return;
   foreach(var c in chains){
    int n=c.bones.Length;c.rest=new Quaternion[n];c.positions=new Vector3[n];c.targets=new Vector3[n];c.across=new Vector3[n];c.restAcross=new Vector3[n];
    for(int i=0;i<n;i++){c.rest[i]=c.bones[i].localRotation;c.positions[i]=c.bones[i].position;c.restAcross[i]=c.bones[i].InverseTransformDirection(transform.forward);}
    c.anchor=c.positions[0];
   }
   var animator=GetComponent<Animator>();leftHand=animator.GetBoneTransform(HumanBodyBones.LeftHand);rightHand=animator.GetBoneTransform(HumanBodyBones.RightHand);
   var colliders=new List<CapsuleCollider>(bodyCapsules);
   AddHandCollider(leftHand,animator.GetBoneTransform(HumanBodyBones.LeftMiddleDistal),"LeftHand",colliders);
   AddHandCollider(rightHand,animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal),"RightHand",colliders);
   bodyCapsules=colliders.ToArray();
   var clothing=new List<SkinnedMeshRenderer>();foreach(var r in GetComponentsInChildren<SkinnedMeshRenderer>())if(r.name=="cloth1"||r.name=="cloth2"){
    clothing.Add(r);r.updateWhenOffscreen=true;
    var mesh=Instantiate(r.sharedMesh);var contacts=mesh.uv3;var weights=mesh.boneWeights;
    if(contacts.Length!=mesh.vertexCount)contacts=new Vector2[mesh.vertexCount];
    for(int i=0;i<weights.Length;i++){var w=weights[i];float free=0;
     foreach(var p in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}){string name=r.bones[p.Item1].name;if(name.Contains("CenterSleeve")||name.Contains("UnderSleeveRibbon"))free+=p.Item2;}
     contacts[i].y=free;
    }
    mesh.uv3=contacts;r.sharedMesh=mesh;
   }
   surfaces=clothing.ToArray();surfaceProperties=new MaterialPropertyBlock();
   shapes=new BodyShape[bodyCapsules.Length];previousHips=hips.position;previousRotation=hips.rotation;initialized=true;Native=new NativeClothRig(this);
  }
  public void SetSimulation(bool value){simulationEnabled=value;ResetSimulation();}
  void AddHandCollider(Transform hand,Transform finger,string name,List<CapsuleCollider> colliders){
   if(!hand||!finger)return;var tip=finger.position+(finger.position-finger.parent.position).normalized*.035f;var axis=tip-hand.position;
   var t=new GameObject("Cloth collision "+name).transform;t.SetParent(hand,false);t.position=(hand.position+tip)*.5f;t.rotation=Quaternion.FromToRotation(Vector3.up,axis);
   var c=t.gameObject.AddComponent<CapsuleCollider>();c.direction=1;c.radius=.034f;c.height=axis.magnitude+c.radius*2;c.isTrigger=true;colliders.Add(c);
  }
  public void ResetSimulation(){reset=true;}
  void RestorePose(){foreach(var c in chains)for(int i=0;i<c.bones.Length;i++)c.bones[i].localRotation=c.rest[i];}
  void Update(){if(initialized)Native.Prepare();}
  void LateUpdate(){
   Initialize();var timer=System.Diagnostics.Stopwatch.StartNew();RestorePose();LastMaxOffset=0;LastMaxPenetration=0;
   bool jump=(hips.position-previousHips).sqrMagnitude>.5f*.5f||Quaternion.Angle(hips.rotation,previousRotation)>90||Time.unscaledDeltaTime>.2f;
   previousHips=hips.position;previousRotation=hips.rotation;
   foreach(var c in chains){
    for(int i=0;i<c.bones.Length;i++)c.targets[i]=c.bones[i].position;
    if(reset||jump||!simulationEnabled){for(int i=0;i<c.bones.Length;i++){c.positions[i]=c.targets[i];}}
    c.anchor=c.targets[0];c.positions[0]=c.anchor;
   }
   Native.Prepare();if(reset||jump)Native.Reset(simulationEnabled);
   reset=false;if(!simulationEnabled){ApplySurfaceContacts(false);LastTransferMs=0;return;}
   for(int i=0;i<shapes.Length;i++)GetCapsule(bodyCapsules[i],out shapes[i].a,out shapes[i].b,out shapes[i].radius);
   Native.Sample();
   if(!Native.Finite){Native.Reset(true);return;}
   foreach(var c in chains)if(c.skirt)for(int i=1;i<c.positions.Length;i++){
    var p=hips.InverseTransformPoint(c.positions[i]);var rest=hips.InverseTransformPoint(c.targets[i]);
    var outward=new Vector2(rest.x,rest.z);if(outward.magnitude>.02f){var actual=new Vector2(p.x,p.z);float minimum=outward.magnitude*.95f;float distance=Vector2.Dot(actual,outward.normalized);if(distance<minimum){actual+=outward.normalized*(minimum-distance);p.x=actual.x;p.z=actual.y;c.positions[i]=hips.TransformPoint(p);}}
   }
   // Bone-length-preserving transfer and a render-surface contact shell follow
   // the native solver. They do not integrate gravity or predict cloth motion.
   // Only rotations change: the original mesh, layer spacing and bone lengths stay intact.
   foreach(var c in chains)if(c.skirt||!inspectRestSleeves)for(int i=0;i<c.bones.Length-1;i++){
    var bone=c.bones[i];var child=c.bones[i+1];var from=child.position-bone.position;var to=c.positions[i+1]-bone.position;
    if(from.sqrMagnitude>.000001f&&to.sqrMagnitude>.000001f){bone.rotation=Quaternion.FromToRotation(from,to)*bone.rotation;AlignSleeveTwist(c,i,to);}
   }
   // Re-project after reconstructing rotations: exact bone lengths can move solved points inward.
   for(int pass=0;pass<6;pass++)foreach(var c in chains)if(c.skirt||!inspectRestSleeves)for(int i=0;i<c.bones.Length-1;i++){
    var bone=c.bones[i];var child=c.bones[i+1];var from=child.position-bone.position;
    var to=ProjectCollisions(child.position,c.skirt)-bone.position;
    to=AvoidBodiesAtLength(bone.position,to,from.magnitude)*from.magnitude;
    if(from.sqrMagnitude>.000001f&&to.sqrMagnitude>.000001f){bone.rotation=Quaternion.FromToRotation(from,to)*bone.rotation;AlignSleeveTwist(c,i,to);}
   }
   foreach(var c in chains)for(int i=1;i<c.bones.Length;i++){
    c.positions[i]=c.bones[i].position;LastMaxOffset=Mathf.Max(LastMaxOffset,Vector3.Distance(c.positions[i],c.targets[i]));
    for(int s=0;s<shapes.Length;s++){var shape=shapes[s];float penetration=shape.radius+.018f-Vector3.Distance(c.positions[i],Closest(c.positions[i],shape.a,shape.b));if(penetration>LastMaxPenetration){LastMaxPenetration=penetration;LastPenetratingBone=c.bones[i].name+" / "+bodyCapsules[s].name;}}
   }
   ApplySurfaceContacts(true);Native.EnsureHems();Native.UpdateHems();
   if(Native.hems.Count>0){surfaces[0].GetPropertyBlock(surfaceProperties);surfaceProperties.SetFloat("_ClothCollision",0);surfaceProperties.SetFloat("_SleeveTwoSided",1);surfaceProperties.SetFloat("_CullMode",0);foreach(var hem in Native.hems)hem.renderer.SetPropertyBlock(surfaceProperties);}
   timer.Stop();LastTransferMs=(float)timer.Elapsed.TotalMilliseconds;
  }
  void AlignSleeveTwist(Chain c,int i,Vector3 axis){
   if(c.skirt||inspectNoTwist)return;axis.Normalize();var actual=Vector3.ProjectOnPlane(c.bones[i].TransformDirection(c.restAcross[i]),axis);var target=Vector3.ProjectOnPlane(c.across[i],axis);
   if(actual.sqrMagnitude>.0001f&&target.sqrMagnitude>.0001f)c.bones[i].rotation=Quaternion.AngleAxis(Vector3.SignedAngle(actual,target,axis),axis)*c.bones[i].rotation;
  }
  void ApplySurfaceContacts(bool active){
   active&=!inspectNoContacts;
   int count=0;
   for(int i=0;i<Mathf.Min(shapes.Length,12);i++){
    var shape=shapes[i];string name=bodyCapsules[i].name;
    bool leg=name.Contains("UpperLeg")||name.Contains("LowerLeg");
    // Include the rendered thigh/boot volume, beyond the narrower bone solver proxy.
    bool limb=name.Contains("Arm")||name.Contains("Hand");
    float extra=name.Contains("UpperLeg")?.022f:name.Contains("LowerLeg")?.025f:name.Contains("Hand")?.018f:limb?.008f:.065f;
    var top=shape.a;
    // Extend the render-contact cap to the actual upper-leg joint; the narrower
    // physics capsule stops short of that joint and misses the thigh/hip seam.
    if(name.Contains("UpperLeg"))top-=(shape.b-shape.a).normalized*.035f;
    var end=shape.b;
    capsuleA[count]=new Vector4(top.x,top.y,top.z,shape.radius+extra);capsuleB[count]=new Vector4(end.x,end.y,end.z,leg?0:limb?2:1);count++;
   }
   foreach(var r in surfaces){r.GetPropertyBlock(surfaceProperties);surfaceProperties.SetFloat("_ClothCollision",active?1:0);surfaceProperties.SetFloat("_ClothColliderCount",count);surfaceProperties.SetVectorArray("_ClothCapsuleA",capsuleA);surfaceProperties.SetVectorArray("_ClothCapsuleB",capsuleB);surfaceProperties.SetVector("_ClothWaist",new Vector4(hips.position.x,hips.position.y,hips.position.z,0));surfaceProperties.SetVector("_ClothUp",hips.up);surfaceProperties.SetVector("_ClothRight",hips.right);surfaceProperties.SetVector("_ClothForward",hips.forward);surfaceProperties.SetVector("_SleeveLeftDirection",SleeveDirection(leftHand,-1));surfaceProperties.SetVector("_SleeveRightDirection",SleeveDirection(rightHand,1));surfaceProperties.SetFloat("_SleeveContactMargin",.03f);surfaceProperties.SetFloat("_ClothPadding",r.name=="cloth1"?.02f:.012f);r.SetPropertyBlock(surfaceProperties);}
  }
  Vector3 SleeveDirection(Transform hand,float side){var v=Vector3.ProjectOnPlane(hand.position-hips.position,hips.up);return v.sqrMagnitude>.01f?v.normalized:hips.right*side;}
  // Start the hem solver from the already resolved visible sleeve, so its first
  // step never begins inside the body or skirt collision shell.
  public Vector3 ProjectSleeveSurface(Vector3 world,float side){
   Vector3 direction=SleeveDirection(side<0?leftHand:rightHand,side).normalized;float shift=0;
   for(int j=0;j<Mathf.Min(shapes.Length,12);j++){
    if(capsuleB[j].w>1.5f)continue;Vector3 a=capsuleA[j],b=capsuleB[j];bool body=capsuleB[j].w>.5f;var center=Closest(world,a,b);var away=world-center;float radius=capsuleA[j].w+(body?.012f:.05f);
    float along=Vector3.Dot(away,direction),perpendicular2=Mathf.Max(0,away.sqrMagnitude-along*along);
    if(perpendicular2<radius*radius){float delta=Mathf.Sqrt(radius*radius-perpendicular2)-along;shift=Mathf.Max(shift,Mathf.Max(delta,0)+.012f*Mathf.Log(1+Mathf.Exp(-Mathf.Abs(delta)/.012f)));}
   }
   var relative=world-hips.position;float below=-Vector3.Dot(relative,hips.up);
   if(below>0&&below<1.02f){float t=Mathf.Clamp01(below/.8f),rx=Mathf.Lerp(.20f,.54f,t),rz=Mathf.Lerp(.28f,.42f,t);var p=new Vector2(Vector3.Dot(relative,hips.right)/rx,(Vector3.Dot(relative,hips.forward)+.025f)/rz);var d=new Vector2(Vector3.Dot(direction,hips.right)/rx,Vector3.Dot(direction,hips.forward)/rz);float a=Vector2.Dot(d,d),b=Vector2.Dot(p,d),c=Vector2.Dot(p,p)-1;if(c<0&&a>.0001f)shift=Mathf.Max(shift,(-b+Mathf.Sqrt(Mathf.Max(0,b*b-a*c)))/a+.015f);}
   return world+direction*shift*.8f;
  }
  Vector3 ProjectCollisions(Vector3 p,bool skirt){
   foreach(var shape in shapes){var closest=Closest(p,shape.a,shape.b);var away=p-closest;float distance=away.magnitude;float margin=skirt?.028f:.032f;if(distance<shape.radius+margin)p=closest+(distance>.00001f?away/distance:hips.right)*(shape.radius+margin);}
   p.y=Mathf.Max(transform.position.y+.055f,p.y);
   // A conservative skirt envelope keeps sleeve cloth outside the underlying dress.
   if(!skirt){var q=hips.InverseTransformPoint(p);var waist=hips.InverseTransformPoint(hips.position);float below=waist.y-q.y;if(below>.10f&&below<.85f){float t=Mathf.Clamp01(below/.85f);float rx=Mathf.Lerp(.105f,.34f,t),rz=Mathf.Lerp(.13f,.34f,t);float nx=q.x/rx,nz=(q.z+.035f)/rz;float norm=Mathf.Sqrt(nx*nx+nz*nz);if(norm<1&&norm>.001f){q.x/=norm;q.z=(q.z+.035f)/norm-.035f;p=hips.TransformPoint(q);}}}
   return p;
  }
  Vector3 AvoidBodiesAtLength(Vector3 origin,Vector3 direction,float length){
   if(length<.00001f)return direction.normalized;
   direction.Normalize();
   // Resolve on the bone-length sphere, rather than projecting and shortening a bone.
   for(int pass=0;pass<4;pass++)foreach(var shape in shapes){
    Vector3 point=origin+direction*length;var center=Closest(point,shape.a,shape.b);float radius=shape.radius+.032f;
    if((point-center).sqrMagnitude>=radius*radius)continue;
    var away=origin-center;float distance=away.magnitude;if(distance<.00001f)continue;
    float cosine=(radius*radius-distance*distance-length*length)/(2*distance*length);
    if(cosine>=1){direction=away/distance;continue;}if(cosine<=-1)continue;
    float allowed=Mathf.Acos(cosine);float current=Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction,away/distance),-1,1));
    if(current>allowed)direction=Vector3.RotateTowards(direction,away/distance,current-allowed+.005f,0).normalized;
   }
   return direction;
  }
  static Vector3 Closest(Vector3 p,Vector3 a,Vector3 b){var d=b-a;return a+d*Mathf.Clamp01(Vector3.Dot(p-a,d)/Mathf.Max(d.sqrMagnitude,.000001f));}
  static void GetCapsule(CapsuleCollider c,out Vector3 a,out Vector3 b,out float radius){var axis=c.transform.up;float scale=c.transform.lossyScale.x;radius=c.radius*scale;var center=c.transform.TransformPoint(c.center);float half=Mathf.Max(0,c.height*scale*.5f-radius);a=center-axis*half;b=center+axis*half;}
 }
}
