using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if YACHIYO_MAGICA
using MagicaCloth2;
#endif
namespace Yachiyo {
 public sealed partial class FullCharacterCloth {
  public int MeshSleeveCount {
   get {
#if YACHIYO_MAGICA
    return meshSleeves.Count;
#else
    return 0;
#endif
   }
  }
#if YACHIYO_MAGICA
  sealed class MeshSleeve {public SleeveGrid grid;public SkinnedMeshRenderer proxy;public Mesh source,bake;public Quaternion[] frames,rotations;}
  sealed class SleeveTie {public Transform root;public int a,b,c;public Vector3 offset,localPosition;public Quaternion rotation,localRotation;}
  readonly List<MeshSleeve> meshSleeves=new List<MeshSleeve>();
  readonly List<SleeveTie> sleeveTies=new List<SleeveTie>();
  readonly List<ColliderComponent> sleeveSkirtEnvelope=new List<ColliderComponent>();
  SurfaceBinding sleeveDisplay;
  static Quaternion SurfaceFrame(Vector3 a,Vector3 b,Vector3 c){var x=(b-a).normalized;var normal=Vector3.Cross(x,c-a).normalized;return normal.sqrMagnitude>.1f?Quaternion.LookRotation(normal,x):Quaternion.identity;}
  Vector3 DisplayPoint(int i){var b=sleeveDisplay;var w=b.weights[i];var p=b.vertices[i];return Skin(b,w.boneIndex0,w.weight0,p)+Skin(b,w.boneIndex1,w.weight1,p)+Skin(b,w.boneIndex2,w.weight2,p)+Skin(b,w.boneIndex3,w.weight3,p);}
  void PrepareFurisode(SkinnedMeshRenderer r){
   bool baseline=Array.IndexOf(Environment.GetCommandLineArgs(),"--bone-furisode-baseline")>=0;
   PrepareBoneFurisode(r,baseline?.24f:.30f);
   if(baseline)return;
   // Preserve one deformation surface for the outer fabric and lining. The
   // KAGURA MeshCloth solver drives this single-layer proxy; visible topology
   // and UVs remain authored. The former BoneCloth chain solver is not built.
   foreach(var grid in furisodeGrids){
    var nodes=grid.nodes.Cast<Transform>().ToArray();var vertices=nodes.Select(n=>r.transform.InverseTransformPoint(n.position)).ToArray();
    var weights=new BoneWeight[nodes.Length];var triangles=new List<int>();
    var original=furisodeOriginal.boneWeights;
    for(int col=0;col<SleeveColumns;col++){
     var sums=new Dictionary<int,float>();foreach(int i in grid.anchors[col].sourceIds){var w=original[i];foreach(var p in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)})if(p.Item2>0)sums[p.Item1]=sums.TryGetValue(p.Item1,out var n)?n+p.Item2:p.Item2;}
     var best=sums.OrderByDescending(p=>p.Value).Take(4).ToArray();float total=best.Sum(p=>p.Value);var bw=new BoneWeight();
     if(best.Length>0){bw.boneIndex0=best[0].Key;bw.weight0=best[0].Value/total;}if(best.Length>1){bw.boneIndex1=best[1].Key;bw.weight1=best[1].Value/total;}if(best.Length>2){bw.boneIndex2=best[2].Key;bw.weight2=best[2].Value/total;}if(best.Length>3){bw.boneIndex3=best[3].Key;bw.weight3=best[3].Value/total;}
     for(int row=0;row<SleeveRows;row++){int a=col*SleeveRows+row;weights[a]=bw;if(col<SleeveColumns-1&&row<SleeveRows-1){int b=a+SleeveRows;triangles.AddRange(new[]{a,b,a+1,b,b+1,a+1});}}
    }
    var go=new GameObject("Sleeve simulation surface / "+grid.side);go.transform.SetParent(r.transform,false);var proxy=go.AddComponent<SkinnedMeshRenderer>();
    var mesh=new Mesh{name="Single layer sleeve proxy / "+grid.side};mesh.vertices=vertices;mesh.uv=vertices.Select(p=>new Vector2(p.x,p.z)).ToArray();mesh.boneWeights=weights;mesh.bindposes=furisodeOriginalBones.Select(b=>b.worldToLocalMatrix*r.transform.localToWorldMatrix).ToArray();mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
    proxy.sharedMesh=mesh;proxy.bones=furisodeOriginalBones;proxy.rootBone=r.rootBone;proxy.sharedMaterials=r.sharedMaterials;proxy.forceRenderingOff=true;proxy.updateWhenOffscreen=true;
    var entry=new MeshSleeve{grid=grid,proxy=proxy,source=mesh,bake=new Mesh(),frames=new Quaternion[nodes.Length],rotations=nodes.Select(n=>n.rotation).ToArray()};
    for(int col=0;col<SleeveColumns;col++)for(int row=0;row<SleeveRows;row++){
     int a=col*SleeveRows+row,b=(col<SleeveColumns-1?col+1:col-1)*SleeveRows+row,c=col*SleeveRows+(row<SleeveRows-1?row+1:row-1);
     entry.frames[a]=SurfaceFrame(nodes[a].position,nodes[b].position,nodes[c].position);
    }
    meshSleeves.Add(entry);
   }
   // The example's sleeve ribbons are BoneCloth. Keep their original weights
   // and drive their fixed root from the current displayed sleeve, not the arm.
   var runtimeWeights=furisodeRuntime.boneWeights;var sourceWeights=furisodeOriginal.boneWeights;
   for(int i=0;i<runtimeWeights.Length;i++){
    var w=sourceWeights[i];var entries=new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}.Where(p=>p.Item2>.001f&&furisodeOriginalBones[p.Item1].name.StartsWith("Acs_UnderSleeveRibbon_")).ToArray();
    if(entries.Length==0)continue;float total=entries.Sum(p=>p.Item2);var bw=new BoneWeight();
    if(entries.Length>0){bw.boneIndex0=entries[0].Item1;bw.weight0=entries[0].Item2/total;}if(entries.Length>1){bw.boneIndex1=entries[1].Item1;bw.weight1=entries[1].Item2/total;}if(entries.Length>2){bw.boneIndex2=entries[2].Item1;bw.weight2=entries[2].Item2/total;}if(entries.Length>3){bw.boneIndex3=entries[3].Item1;bw.weight3=entries[3].Item2/total;}runtimeWeights[i]=bw;
   }
   furisodeRuntime.boneWeights=runtimeWeights;
   sleeveDisplay=new SurfaceBinding{bones=r.bones,bindposes=furisodeRuntime.bindposes,vertices=furisodeRuntime.vertices,weights=runtimeWeights};
   var trianglesSource=furisodeOriginal.triangles;
   for(int side=0;side<2;side++){
    var root=furisodeOriginalBones.First(b=>b.name=="Acs_UnderSleeveRibbon_Root_"+(side==0?"Left":"Right"));int a=sleeveBowBindings[side].surface;
    int triangle=Enumerable.Range(0,trianglesSource.Length/3).First(t=>Enumerable.Range(0,3).Any(k=>trianglesSource[t*3+k]==a));
    int b=trianglesSource[triangle*3],c=trianglesSource[triangle*3+1];if(b==a)b=trianglesSource[triangle*3+2];if(c==a||c==b)c=trianglesSource[triangle*3+2];
    var frame=SurfaceFrame(DisplayPoint(a),DisplayPoint(b),DisplayPoint(c));sleeveTies.Add(new SleeveTie{root=root,a=a,b=b,c=c,offset=Quaternion.Inverse(frame)*(root.position-DisplayPoint(a)),rotation=Quaternion.Inverse(frame)*root.rotation,localPosition=root.localPosition,localRotation=root.localRotation});
   }
   MagicaManager.afterDelayedDelegate+=UpdateMeshSleeveDisplay;
  }
  void BuildMeshSleeveGroups(MagicaCloth garment){
   // A layered animated skirt is not a closed collision volume. These guards
   // apply only to sleeves, never to the skirt itself, and prevent a reset from
   // resolving a sleeve through the dress to the opposite side.
   foreach(var band in new[]{(.91f,.17f),(.69f,.22f),(.46f,.255f)}){
    var go=new GameObject("Sleeve contact / skirt volume");go.transform.SetParent(owner.hips,true);go.transform.position=transform.TransformPoint(new Vector3(0,band.Item1,0));
    var collider=go.AddComponent<MagicaSphereCollider>();collider.SetSize(band.Item2);sleeveSkirtEnvelope.Add(collider);
   }
   foreach(var sleeve in meshSleeves){
    var cloth=NewGroup("Sleeve MeshCloth / "+sleeve.grid.side,out var info);var s=cloth.SerializeData;info.type="MeshCloth";info.renderers=new[]{furisodeSource.name};info.roots=Array.Empty<string>();
    s.clothType=ClothProcess.ClothType.MeshCloth;s.sourceRenderers.Add(sleeve.proxy);s.animationPoseRatio=0;
    s.reductionSetting.simpleDistance=0;s.reductionSetting.shapeDistance=0;s.gravity=5;s.damping=new CurveSerializeData(.158f);
    s.radius=new CurveSerializeData(.01f);s.tetherConstraint.distanceCompression=.8f;s.distanceConstraint.stiffness=new CurveSerializeData(1,1,.95f);s.triangleBendingConstraint.stiffness=.35f;
    s.angleRestorationConstraint.stiffness=new CurveSerializeData(.12f,new AnimationCurve(new Keyframe(0,1,-3.4345763f,-3.4345763f),new Keyframe(.61187744f,.055889904f,-.19770783f,-.19770783f)));s.angleRestorationConstraint.velocityAttenuation=.481f;
    s.angleLimitConstraint.useAngleLimit=false;s.motionConstraint.useMaxDistance=false;s.motionConstraint.useBackstop=false;
    s.inertiaConstraint.anchor=null;s.inertiaConstraint.anchorInertia=0;s.inertiaConstraint.worldInertia=1;s.inertiaConstraint.localInertia=1;s.colliderCollisionConstraint.friction=.05f;s.colliderCollisionConstraint.colliderList.AddRange(sleeveSkirtEnvelope);
    s.selfCollisionConstraint.selfMode=SelfCollisionConstraint.SelfCollisionMode.FullMesh;s.selfCollisionConstraint.surfaceThickness=new CurveSerializeData(.008f);s.selfCollisionConstraint.syncMode=SelfCollisionConstraint.SelfCollisionMode.FullMesh;s.selfCollisionConstraint.syncPartner=garment;s.selfCollisionConstraint.clothMass=.45f;
    var paint=Enumerable.Range(0,SleeveRows*SleeveColumns).Select(i=>i%SleeveRows==0?VertexAttribute.Fixed:VertexAttribute.Move).ToArray();cloth.GetSerializeData2().vertexAttributeList.Add(paint);info.movable=paint.Count(a=>a.IsMove());info.fixedVertices=paint.Count(a=>a.IsFixed());
   }
   BoneGroup("Sleeve ties",sleeveTies.Select(t=>t.root).ToArray(),.7f,25,.003f,garment);
   var tieCloth=cloths.Last().SerializeData;tieCloth.gravity=0;tieCloth.damping=new CurveSerializeData(.18f);tieCloth.colliderCollisionConstraint.colliderList.Clear();tieCloth.rootRotation=1;tieCloth.inertiaConstraint.anchor=null;tieCloth.selfCollisionConstraint.syncMode=SelfCollisionConstraint.SelfCollisionMode.None;tieCloth.selfCollisionConstraint.syncPartner=null;
  }
  void UpdateSleeveTieRoots(){if(!Active)return;foreach(var tie in sleeveTies){var p=DisplayPoint(tie.a);var frame=SurfaceFrame(p,DisplayPoint(tie.b),DisplayPoint(tie.c));tie.root.SetPositionAndRotation(p+frame*tie.offset,frame*tie.rotation);}}
  void UpdateMeshSleeveDisplay(){
   if(!Ready||!Active)return;
   long started=System.Diagnostics.Stopwatch.GetTimestamp();
   foreach(var sleeve in meshSleeves){
    sleeve.proxy.BakeMesh(sleeve.bake);var points=sleeve.bake.vertices;for(int i=0;i<points.Length;i++)points[i]=sleeve.proxy.transform.TransformPoint(points[i]);
    for(int col=0;col<SleeveColumns;col++)for(int row=0;row<SleeveRows;row++){
     int a=col*SleeveRows+row,b=(col<SleeveColumns-1?col+1:col-1)*SleeveRows+row,c=col*SleeveRows+(row<SleeveRows-1?row+1:row-1);
     var frame=SurfaceFrame(points[a],points[b],points[c]);sleeve.grid.nodes[col,row].SetPositionAndRotation(points[a],frame*Quaternion.Inverse(sleeve.frames[a])*sleeve.rotations[a]);
    }
   }UpdateSleeveTieRoots();LastSimulationMs+=(float)((System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency);
  }
  void DisposeMeshSleeves(){MagicaManager.afterDelayedDelegate-=UpdateMeshSleeveDisplay;foreach(var sleeve in meshSleeves){Destroy(sleeve.source);Destroy(sleeve.bake);}}
#endif
 }
}
