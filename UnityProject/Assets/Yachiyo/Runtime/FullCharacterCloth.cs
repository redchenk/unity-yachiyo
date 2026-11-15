using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if YACHIYO_MAGICA
using MagicaCloth2;
#endif

namespace Yachiyo {
 // One owner for all secondary motion. Original geometry and shoulder support
 // stay authored; the continuous sleeve has a runtime copy of its skin weights.
 public sealed partial class FullCharacterCloth : MonoBehaviour {
  public bool Ready {get;private set;}
  public bool Failed {get;private set;}
  public string Failure {get;private set;}="";
  public int GroupCount=>groups.Count;
  public int ProxyVertices {get;private set;}
  public int ColliderCount {get;private set;}
  public int SimulationFrames {get;private set;}
  public float LastSimulationMs {get;private set;}
  public bool Active {get;private set;}=true;
  public readonly List<GroupInfo> groups=new List<GroupInfo>();
  [Serializable]public sealed class GroupInfo {
   public string name,type;public bool built;public int movable,fixedVertices,proxyVertices;public string[] roots,renderers;
  }
  public static bool TryCreate(GarmentPhysics owner,out FullCharacterCloth result){
   result=null;
#if YACHIYO_MAGICA
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--legacy-secondary-motion")>=0)return false;
   result=owner.gameObject.AddComponent<FullCharacterCloth>();result.Configure(owner);return true;
#else
   return false;
#endif
  }
#if YACHIYO_MAGICA
  readonly List<MagicaCloth> cloths=new List<MagicaCloth>();
  readonly List<ColliderComponent> colliders=new List<ColliderComponent>();
  GarmentPhysics owner;Animator animator;Transform[] originalBones;
  long simulationStart;
  Transform[] ornamentEars=Array.Empty<Transform>();Quaternion[] earDisplay=Array.Empty<Quaternion>();bool earDisplayReady;
  sealed class SurfaceBinding {public SkinnedMeshRenderer renderer;public Mesh source;public Transform[] bones;public int[] pinned;public Vector3[] vertices,initial;public BoneWeight[] weights;public Matrix4x4[] bindposes;}
  readonly List<SurfaceBinding> bindings=new List<SurfaceBinding>();
  public float MeasurePinnedError(){
   float error=0;var baked=new Mesh();
   foreach(var binding in bindings){binding.renderer.BakeMesh(baked);var actual=baked.vertices;
    foreach(int i in binding.pinned){var w=binding.weights[i];var p=binding.vertices[i];
     Vector3 expected=Skin(binding,w.boneIndex0,w.weight0,p)+Skin(binding,w.boneIndex1,w.weight1,p)+Skin(binding,w.boneIndex2,w.weight2,p)+Skin(binding,w.boneIndex3,w.weight3,p);
     error=Mathf.Max(error,Vector3.Distance(binding.renderer.transform.TransformPoint(actual[i]),expected));
    }
   }Destroy(baked);return error;
  }
  static Vector3 Skin(SurfaceBinding b,int i,float weight,Vector3 p)=>weight>0?b.bones[i].TransformPoint(b.bindposes[i].MultiplyPoint3x4(p))*weight:Vector3.zero;
  public bool OriginalMeshesIntact=>bindings.All(b=>b.source.vertexCount==b.vertices.Length&&b.source.boneWeights.SequenceEqual(b.weights));
  void Configure(GarmentPhysics source){
   owner=source;animator=GetComponent<Animator>();originalBones=GetComponentsInChildren<Transform>(true).Where(t=>!SettingsNode(t)).ToArray();
   MagicaManager.SetSimulationFrequency(60);MagicaManager.SetMaxSimulationCountPerFrame(4);
   MagicaManager.SetUpdateLocation(TimeManager.UpdateLocation.AfterLateUpdate);
   MagicaManager.OnPreSimulation+=BeforeSimulation;MagicaManager.OnPostSimulation+=AfterSimulation;
   foreach(var hair in GetComponents<HairPhysics>())hair.enabled=false;
   ornamentEars=originalBones.Where(t=>t.name=="Acs_Obi_A001"||t.name=="Acs_Obi_B001").ToArray();earDisplay=new Quaternion[ornamentEars.Length];
   CreateColliders();
   PrepareSkirtPoseSupport();
   var clothes=GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.name=="cloth1"||r.name=="cloth2").OrderBy(r=>r.name).ToArray();
   if(clothes.Length!=2){Fail("Expected the original cloth1 and cloth2 renderers");return;}
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--mesh-furisode-baseline")<0)PrepareFurisode(clothes.Single(r=>r.name=="cloth1"));
   // Skirts and ribbons share one collision surface. Both continuous sleeve
   // grids synchronize with it, retaining sleeve-to-skirt contact.
   var garments=MeshGroup("Garments · sleeves / skirt / ribbons",clothes);
   BuildFurisodeGroups(garments);
   var tails=Roots(t=>t.name.StartsWith("Acs_Twintail",StringComparison.Ordinal));
   BoneGroup("Twintails",tails,.09f,38,.014f,garments);
   BoneGroup("Fringe",Roots(t=>t.name.StartsWith("Acs_FrontHair",StringComparison.Ordinal)),.65f,9,.003f,garments);
   BoneGroup("Side hair",Roots(t=>t.name.StartsWith("Acs_SideHair",StringComparison.OrdinalIgnoreCase)||t.name.StartsWith("Acs_Sidetail",StringComparison.Ordinal)),.3f,24,.007f,garments);
   BoneGroup("Hair ornaments",Roots(t=>t.name.StartsWith("Acs_Ring",StringComparison.Ordinal)),.8f,7,.004f,garments);
   BoneGroup("Obi accessories",Roots(t=>t.name.StartsWith("Acs_Obi",StringComparison.Ordinal)),.75f,10,.008f,garments);
   ColliderCount=colliders.Count;
   foreach(var cloth in cloths){cloth.Initialize();if(!cloth.BuildAndRun())Fail("Build could not start: "+cloth.name+" / "+cloth.Process.Result.GetResultString());}
   Debug.Log("FULL_CLOTH_CONFIG groups="+groups.Count+" colliders="+ColliderCount+" solver=MagicaCloth2-2.18.4");
  }
  static bool SettingsNode(Transform t){for(var p=t;p;p=p.parent)if(p.name=="PB")return true;return false;}
  Transform[] Roots(Func<Transform,bool> matches)=>originalBones.Where(matches).Where(t=>!t.parent||!matches(t.parent)).ToArray();
  MagicaCloth NewGroup(string label,out GroupInfo info){
   var go=new GameObject("Magica / "+label);go.transform.SetParent(transform,false);
   var cloth=go.AddComponent<MagicaCloth>();cloth.DisableAutoBuild();cloths.Add(cloth);
   info=new GroupInfo{name=label};groups.Add(info);var entry=info;
   cloth.OnBuildComplete+=(c,ok)=>{
    // MC2 also invokes this event when Start skips its disabled auto-build.
    // That is not the completion of our asynchronous BuildAndRun request.
    if(!ok&&!c.Process.Result.IsError())return;
    entry.built=ok;if(!ok){Fail(c.name+" / "+c.Process.Result.GetResultString());return;}
    entry.proxyVertices=c.Process.ProxyMeshContainer?.shareVirtualMesh?.VertexCount??0;
    ProxyVertices=groups.Sum(g=>g.proxyVertices);Ready=!Failed&&groups.All(g=>g.built);
    Debug.Log("FULL_CLOTH_BUILT "+entry.name+" vertices="+entry.proxyVertices+" ready="+Ready);
   };
   var s=cloth.SerializeData;
   s.updateMode=ClothUpdateMode.Normal;s.animationPoseRatio=1;s.stablizationTimeAfterReset=.35f;
   s.cullingSettings.cameraCullingMode=CullingSettings.CameraCullingMode.Off;
   s.gravity=4.5f;s.damping=new CurveSerializeData(.12f);
   s.distanceConstraint.stiffness=new CurveSerializeData(.98f);
   s.inertiaConstraint.anchor=owner.hips;s.inertiaConstraint.anchorInertia=.4f;s.inertiaConstraint.worldInertia=.55f;s.inertiaConstraint.localInertia=.65f;
   s.inertiaConstraint.movementInertiaSmoothing=.35f;s.inertiaConstraint.centrifualAcceleration=0;
   s.inertiaConstraint.teleportMode=InertiaConstraint.TeleportMode.Reset;s.inertiaConstraint.teleportDistance=.6f;s.inertiaConstraint.teleportRotation=90;
   s.colliderCollisionConstraint.mode=ColliderCollisionConstraint.Mode.Edge;s.colliderCollisionConstraint.friction=.12f;
   s.colliderCollisionConstraint.colliderList.AddRange(colliders);
   return cloth;
  }
  MagicaCloth MeshGroup(string label,SkinnedMeshRenderer[] renderers){
   var cloth=NewGroup(label,out var info);var s=cloth.SerializeData;
   info.type="MeshCloth";info.renderers=renderers.Select(r=>r.name).ToArray();info.roots=Array.Empty<string>();
   s.clothType=ClothProcess.ClothType.MeshCloth;s.sourceRenderers.AddRange(renderers);
   s.reductionSetting.simpleDistance=.002f;s.reductionSetting.shapeDistance=.014f;
   s.radius=new CurveSerializeData(.006f);s.gravity=7;
   // Long furisode panels must fold toward their attachment, rather than be
   // held at the package's default minimum tether length (60% of rest).
   s.tetherConstraint.distanceCompression=.9f;
   s.triangleBendingConstraint.stiffness=.12f;
   s.angleRestorationConstraint.stiffness=new CurveSerializeData(.045f,1,.15f);s.angleRestorationConstraint.velocityAttenuation=.55f;
   s.angleLimitConstraint.useAngleLimit=true;s.angleLimitConstraint.limitAngle=new CurveSerializeData(95);
   s.motionConstraint.useMaxDistance=true;s.motionConstraint.maxDistance=new CurveSerializeData(.22f,.05f,1);
   // The source garment has intentional lining folds and mixed normal directions;
   // body + cloth contacts handle them without a global inward-facing Backstop.
   s.motionConstraint.useBackstop=false;
   s.selfCollisionConstraint.selfMode=SelfCollisionConstraint.SelfCollisionMode.FullMesh;
   s.selfCollisionConstraint.surfaceThickness=new CurveSerializeData(.003f);s.selfCollisionConstraint.clothMass=.65f;
   var data=cloth.GetSerializeData2();
   foreach(var r in renderers){
    PrepareBodyContactSurface(r);
    var mesh=r.sharedMesh;var weights=mesh.boneWeights;var baked=new Mesh();r.BakeMesh(baked);
    var vertices=baked.vertices;var attributes=new VertexAttribute[vertices.Length];
    for(int i=0;i<attributes.Length;i++){
     var w=weights[i];var world=r.transform.TransformPoint(vertices[i]);
     float mobile=Mobility(r.bones[w.boneIndex0],world)*w.weight0+Mobility(r.bones[w.boneIndex1],world)*w.weight1+Mobility(r.bones[w.boneIndex2],world)*w.weight2+Mobility(r.bones[w.boneIndex3],world)*w.weight3;
     bool move=mobile>.25f&&!IsFurisodeDriven(r,i)&&!IsSleeveFixed(r,i);attributes[i]=move?VertexAttribute.Move:VertexAttribute.Invalid;
     if(move)info.movable++;
    }
    // Keep non-cloth areas (shoulder roll, neck, plush) on exact original
    // skinning, not an approximate reduced proxy. Only the boundary is pinned.
    var triangles=mesh.triangles;
    for(int pass=0;pass<2;pass++){
     var before=(VertexAttribute[])attributes.Clone();
     for(int k=0;k<triangles.Length;k+=3){int a=triangles[k],b=triangles[k+1],c=triangles[k+2];
      if(before[a]==VertexAttribute.Invalid&&before[b]==VertexAttribute.Invalid&&before[c]==VertexAttribute.Invalid)continue;
      if(attributes[a]==VertexAttribute.Invalid&&!IsSleeveFixed(r,a)&&!IsFurisodeDriven(r,a))attributes[a]=VertexAttribute.Fixed;
      if(attributes[b]==VertexAttribute.Invalid&&!IsSleeveFixed(r,b)&&!IsFurisodeDriven(r,b))attributes[b]=VertexAttribute.Fixed;
      if(attributes[c]==VertexAttribute.Invalid&&!IsSleeveFixed(r,c)&&!IsFurisodeDriven(r,c))attributes[c]=VertexAttribute.Fixed;
     }
    }
    info.fixedVertices+=attributes.Count(a=>a==VertexAttribute.Fixed);
    bindings.Add(new SurfaceBinding{renderer=r,source=furisodeSource==r?furisodeOriginal:mesh,bones=furisodeSource==r?furisodeOriginalBones:r.bones.ToArray(),vertices=mesh.vertices,initial=vertices.Select(r.transform.TransformPoint).ToArray(),weights=furisodeSource==r?furisodeOriginal.boneWeights:weights,bindposes=furisodeSource==r?furisodeOriginal.bindposes:mesh.bindposes,pinned=Enumerable.Range(0,attributes.Length).Where(i=>attributes[i]==VertexAttribute.Invalid&&i%7==0&&!(r==furisodeSource&&furisodeMask[i])).ToArray()});
    Destroy(baked);data.vertexAttributeList.Add(attributes);r.updateWhenOffscreen=true;
    var properties=new MaterialPropertyBlock();r.GetPropertyBlock(properties);properties.SetFloat("_ClothCollision",0);r.SetPropertyBlock(properties);
   }
   return cloth;
  }
  float Mobility(Transform bone,Vector3 world){
   if(!bone)return 0;var n=bone.name;
   // Plush/obi keeps a rigid skinned silhouette and moves through its bone group,
   // including the separate Mendako eyes. Shoulder supports are always animated.
   if(n.StartsWith("Acs_Obi",StringComparison.Ordinal))return 0;
   bool skirt=n.IndexOf("Skirt",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("Sirt",StringComparison.OrdinalIgnoreCase)>=0;
   if(skirt)return owner.hips.InverseTransformPoint(world).y<-.055f?1:0;
   bool sleeve=n.StartsWith("Acs_ArmSleeve",StringComparison.Ordinal)||n.StartsWith("Acs_CenterSleeve",StringComparison.Ordinal);
   bool ribbon=n.StartsWith("Acs_UnderSleeveRibbon",StringComparison.Ordinal)||n.StartsWith("Acs_NeckRibbon",StringComparison.Ordinal);
   if(!sleeve&&!ribbon)return 0;
   bool root=n.Contains("Root")||n.Contains("001");
   return root?Mathf.InverseLerp(.025f,.075f,Vector3.Distance(bone.position,world)):1;
  }
  void BoneGroup(string label,Transform[] roots,float restoration,float maxAngle,float radius,MagicaCloth garment){
   if(roots.Length==0){Fail("Missing dynamic group: "+label);return;}
   var cloth=NewGroup(label,out var info);var s=cloth.SerializeData;
   info.type="BoneCloth";info.roots=roots.Select(t=>t.name).ToArray();info.renderers=Array.Empty<string>();
   s.clothType=ClothProcess.ClothType.BoneCloth;s.rootBones.AddRange(roots);
   s.connectionMode=RenderSetupData.BoneConnectionMode.Line;s.rotationalInterpolation=1;s.rootRotation=0;
   s.radius=new CurveSerializeData(radius,.4f,1);s.gravity=label=="Twintails"?2:.7f;
   s.angleRestorationConstraint.stiffness=new CurveSerializeData(restoration,1,.5f);s.angleRestorationConstraint.velocityAttenuation=.4f;
   s.angleLimitConstraint.useAngleLimit=true;s.angleLimitConstraint.limitAngle=new CurveSerializeData(maxAngle,.35f,1);
   s.inertiaConstraint.anchor=label=="Obi accessories"?owner.hips:animator.GetBoneTransform(HumanBodyBones.Head);
   s.selfCollisionConstraint.syncMode=SelfCollisionConstraint.SelfCollisionMode.FullMesh;s.selfCollisionConstraint.syncPartner=garment;
   s.selfCollisionConstraint.surfaceThickness=new CurveSerializeData(radius);s.selfCollisionConstraint.clothMass=.2f;
   if(label=="Obi accessories"){
    info.type="BoneSpring";s.clothType=ClothProcess.ClothType.BoneSpring;
    s.springConstraint.springPower=.12f;s.springConstraint.limitDistance=.0025f;s.springConstraint.normalLimitRatio=.3f;s.springConstraint.springNoise=0;
    s.selfCollisionConstraint.syncMode=SelfCollisionConstraint.SelfCollisionMode.None;s.selfCollisionConstraint.syncPartner=null;
    // The ornament's envelope protects other garments, never its own ears.
    // Keep collider helpers outside this simulated hierarchy as well.
    s.colliderCollisionConstraint.colliderList.RemoveAll(c=>c.name=="Magica body / Obi");
    s.gravity=0;s.damping=new CurveSerializeData(.35f);
    s.inertiaConstraint.worldInertia=.2f;s.inertiaConstraint.localInertia=.25f;
    s.angleRestorationConstraint.stiffness=new CurveSerializeData(.85f);
    s.angleRestorationConstraint.velocityAttenuation=.85f;
    s.angleLimitConstraint.limitAngle=new CurveSerializeData(5);
   }
   var rootSet=new HashSet<Transform>(roots);var nodes=roots.SelectMany(t=>t.GetComponentsInChildren<Transform>()).Distinct();
   foreach(var t in nodes){bool fixedNode=rootSet.Contains(t);cloth.GetSerializeData2().boneAttributeDict[t]=fixedNode?VertexAttribute.Fixed:VertexAttribute.Move;if(fixedNode)info.fixedVertices++;else info.movable++;}
  }
  void CreateColliders(){
   foreach(var c in owner.bodyCapsules){
    if(c.name.Contains("Arm")||c.name.Contains("Leg"))continue;
    var t=new GameObject("Magica body / "+c.name).transform;t.SetParent(c.transform,false);t.localPosition=c.center;
    var mc=t.gameObject.AddComponent<MagicaCapsuleCollider>();mc.direction=(MagicaCapsuleCollider.Direction)c.direction;
    float pad=c.name.Contains("UpperLeg")?.012f:c.name.Contains("Arm")?.008f:.012f;
    mc.SetSize(c.radius+pad,c.radius+pad,c.height+pad*2);colliders.Add(mc);
   }
   CreateLimbContacts();
   AddSphere("Head",animator.GetBoneTransform(HumanBodyBones.Head),new Vector3(0,.065f,.005f),.095f);
   AddSegment("Neck",HumanBodyBones.Neck,HumanBodyBones.Head,.042f);
   AddSegment("Shoulders",HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,.043f);
   AddSegment("Left palm",HumanBodyBones.LeftHand,HumanBodyBones.LeftMiddleDistal,.035f);
   AddSegment("Right palm",HumanBodyBones.RightHand,HumanBodyBones.RightMiddleDistal,.035f);
   AddSegment("Left boot",HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,.052f);
   AddSegment("Right boot",HumanBodyBones.RightFoot,HumanBodyBones.RightToes,.052f);
   // The fixed waist ornament also occupies volume; it must not be treated as air.
   var obi=originalBones.FirstOrDefault(t=>t.name=="Acs_Obi_001");if(obi)AddSphere("Obi",obi.parent,obi.localPosition,.115f);
   var floor=new GameObject("Magica floor");floor.transform.position=new Vector3(transform.position.x,.005f,transform.position.z);floor.transform.SetParent(transform,true);colliders.Add(floor.AddComponent<MagicaPlaneCollider>());
  }
  void AddSphere(string label,Transform parent,Vector3 center,float radius){
   if(!parent)return;var t=new GameObject("Magica body / "+label).transform;t.SetParent(parent,false);t.localPosition=center;
   var sphere=t.gameObject.AddComponent<MagicaSphereCollider>();sphere.SetSize(radius);colliders.Add(sphere);
  }
  void AddSegment(string label,HumanBodyBones from,HumanBodyBones to,float radius){
   var a=animator.GetBoneTransform(from);var b=animator.GetBoneTransform(to);if(!a)return;
   if(!b&&label.Contains("boot")){
    var tFoot=new GameObject("Magica body / "+label).transform;tFoot.SetParent(a,false);tFoot.position=a.position+transform.forward*.065f;tFoot.rotation=Quaternion.FromToRotation(Vector3.up,transform.forward);
    var foot=tFoot.gameObject.AddComponent<MagicaCapsuleCollider>();foot.direction=MagicaCapsuleCollider.Direction.Y;foot.SetSize(radius,radius,.20f);colliders.Add(foot);return;
   }
   if(!b)return;
   var t=new GameObject("Magica body / "+label).transform;t.SetParent(label=="Shoulders"?animator.GetBoneTransform(HumanBodyBones.Chest):a,false);t.position=(a.position+b.position)*.5f;t.rotation=Quaternion.FromToRotation(Vector3.up,b.position-a.position);
   var capsule=t.gameObject.AddComponent<MagicaCapsuleCollider>();capsule.direction=MagicaCapsuleCollider.Direction.Y;capsule.SetSize(radius,radius,Vector3.Distance(a.position,b.position)+radius*2);colliders.Add(capsule);
  }
  void BeforeSimulation(){SimulationFrames++;simulationStart=System.Diagnostics.Stopwatch.GetTimestamp();UpdateSkirtPoseSupport();UpdateLimbContacts();UpdateFurisodeAnchors();UpdateSleeveTieRoots();}
  void AfterSimulation(){
   // Small stiff decorative ears expose fixed-step quantization more than cloth.
   // Interpolate only their displayed rotation, retaining the solver's inertia.
   float alpha=1-Mathf.Exp(-Mathf.Min(Time.deltaTime,.1f)/.065f);
   for(int i=0;i<ornamentEars.Length;i++){
    var target=ornamentEars[i].localRotation;
    earDisplay[i]=earDisplayReady?Quaternion.Slerp(earDisplay[i],target,alpha):target;
    ornamentEars[i].localRotation=earDisplay[i];
   }
   earDisplayReady=true;
   LastSimulationMs=(float)((System.Diagnostics.Stopwatch.GetTimestamp()-simulationStart)*1000.0/System.Diagnostics.Stopwatch.Frequency);
  }
  void Fail(string message){Failed=true;Ready=false;Failure=message;Debug.LogError("FULL_CLOTH_FAILED "+message);}
  void OnDestroy(){DisposeBodyContacts();DisposeMeshSleeves();MagicaManager.OnPreSimulation-=BeforeSimulation;MagicaManager.OnPostSimulation-=AfterSimulation;if(furisodeRuntime)Destroy(furisodeRuntime);}
#endif
  public void SetSimulation(bool value){
   Active=value;
#if YACHIYO_MAGICA
   earDisplayReady=false;foreach(var c in cloths){c.enabled=value;if(value)c.ResetCloth();}
   if(!value)RestoreSleeveTies();
   UpdateVisibleBodyContacts(value);
#endif
  }
  public void ResetSimulation(){
#if YACHIYO_MAGICA
   earDisplayReady=false;foreach(var c in cloths)c.ResetCloth(true);
#endif
  }
 }
}
