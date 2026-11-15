using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if YACHIYO_MAGICA
using MagicaCloth2;
#endif
namespace Yachiyo {
 // A continuous bone surface drives the forearm shell, cuff, hanging walls
 // and lining. Source triangles, UVs and shoulder seam stay unchanged. Horizontal links
 // make this a cloth sheet rather than independent pendulums.
 public sealed partial class FullCharacterCloth {
  public int FurisodeControlPoints {
   get {
#if YACHIYO_MAGICA
    return furisodeGrids.Count*SleeveColumns*SleeveRows;
#else
    return 0;
#endif
   }
  }
  // Exclude anchor rotation: a rigid sheet swinging as a whole must not
  // count as evidence of local cloth bending.
  public Vector2 MeasureFurisodeBend(){
   float total=0,maximum=0;int count=0;
#if YACHIYO_MAGICA
   foreach(var grid in furisodeGrids)for(int col=0;col<SleeveColumns;col++)for(int row=1;row<SleeveRows-1;row++){
    float angle=Quaternion.Angle(grid.rest[col,row],grid.nodes[col,row].localRotation);total+=angle;maximum=Mathf.Max(maximum,angle);count++;
   }
#endif
   return new Vector2(total/Mathf.Max(1,count),maximum);
  }
#if YACHIYO_MAGICA
  const int SleeveColumns=8,SleeveRows=11;
  SkinnedMeshRenderer furisodeSource;Mesh furisodeOriginal,furisodeRuntime;Transform[] furisodeOriginalBones;
  SurfaceBinding furisodeBind;Vector3[] furisodeVertices;BoneWeight[] furisodeWeights;
  bool[] furisodeMask,sleeveFixedMask;readonly List<SleeveGrid> furisodeGrids=new List<SleeveGrid>();
  readonly List<(int bow,int surface)> sleeveBowBindings=new List<(int,int)>();
  sealed class SleeveAnchor {public Transform root,support;public int[] sourceIds;public Quaternion bindSupport,bindRotation;}
  sealed class SleeveGrid {public string side;public Transform[,] nodes;public Quaternion[,] rest;public SleeveAnchor[] anchors;}
  static string SleevePositionKey(Vector3 p)=>Mathf.RoundToInt(p.x*100000)+"/"+Mathf.RoundToInt(p.y*100000)+"/"+Mathf.RoundToInt(p.z*100000);
  static float SleeveSurfaceWeight(BoneWeight w,Transform[] bones,string side){
   float sum=0;foreach(var p in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}){
    string n=bones[p.Item1].name;if(n.EndsWith(side)&&(n.StartsWith("Acs_FrontSleeve_")||n.StartsWith("Acs_BackSleeve_")||n.StartsWith("Acs_ArmSleeve_")||n.StartsWith("Acs_CenterSleeve_")||n.StartsWith("Acs_UnderSleeveRibbon_")))sum+=p.Item2;
   }return sum;
  }
  static float SleeveTorsoWeight(BoneWeight w,Transform[] bones){
   float sum=0;foreach(var p in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}){
    string n=bones[p.Item1].name;if(n.StartsWith("Spine")||n.StartsWith("Hips")||n.StartsWith("Chest")||n.StartsWith("Neck"))sum+=p.Item2;
   }return sum;
  }
  void PrepareFurisode(SkinnedMeshRenderer r){
   furisodeSource=r;furisodeOriginal=r.sharedMesh;furisodeOriginalBones=r.bones.ToArray();
   var original=furisodeOriginal;var vertices=original.vertices;var originalWeights=original.boneWeights;var weights=originalWeights.ToArray();var triangles=original.triangles;
   var bones=r.bones.ToList();var bindposes=original.bindposes.ToList();furisodeMask=new bool[vertices.Length];sleeveFixedMask=new bool[vertices.Length];
   var snapshot=new SurfaceBinding{bones=furisodeOriginalBones,vertices=vertices,weights=originalWeights,bindposes=original.bindposes};furisodeBind=snapshot;furisodeVertices=vertices;furisodeWeights=originalWeights;
   Vector3 OriginalWorld(int i){var w=originalWeights[i];var p=vertices[i];return Skin(snapshot,w.boneIndex0,w.weight0,p)+Skin(snapshot,w.boneIndex1,w.weight1,p)+Skin(snapshot,w.boneIndex2,w.weight2,p)+Skin(snapshot,w.boneIndex3,w.weight3,p);}
   foreach(string side in new[]{"Left","Right"}){
    var selected=new List<int>();var adjacent=new Dictionary<string,HashSet<string>>();
    for(int t=0;t<triangles.Length;t+=3)if(Enumerable.Range(0,3).All(j=>SleeveSurfaceWeight(originalWeights[triangles[t+j]],furisodeOriginalBones,side)>.08f)){
     selected.Add(t);for(int j=0;j<3;j++){string a=SleevePositionKey(vertices[triangles[t+j]]),b=SleevePositionKey(vertices[triangles[t+(j+1)%3]]);if(!adjacent.ContainsKey(a))adjacent[a]=new HashSet<string>();if(!adjacent.ContainsKey(b))adjacent[b]=new HashSet<string>();adjacent[a].Add(b);adjacent[b].Add(a);}
    }
    var seen=new HashSet<string>();var faces=new HashSet<int>();var bowFaces=new HashSet<int>();int walls=0;
    foreach(var first in adjacent.Keys){
     if(seen.Contains(first))continue;var nodes=new HashSet<string>();var pending=new Stack<string>();pending.Push(first);
     while(pending.Count>0){var p=pending.Pop();if(!seen.Add(p))continue;nodes.Add(p);foreach(var n in adjacent[p])pending.Push(n);}
     var component=selected.Where(t=>nodes.Contains(SleevePositionKey(vertices[triangles[t]]))).ToArray();
     // The bow islands are sewn to this sleeve, even though their mesh is
     // disconnected. Leaving them on the old garment skin makes them float.
     if(component.Max(t=>Mathf.Max(vertices[triangles[t]].z,vertices[triangles[t+1]].z,vertices[triangles[t+2]].z))<1.3f){bowFaces.UnionWith(component);continue;}
     if(nodes.Count<80)continue;
     walls++;faces.UnionWith(component);
    }
    if(walls!=2)throw new InvalidOperationException("Expected sleeve shell and cuff on "+side+", found "+walls);
    var ids=faces.SelectMany(t=>new[]{triangles[t],triangles[t+1],triangles[t+2]}).Distinct().ToArray();
    var bowIds=new HashSet<int>(bowFaces.SelectMany(t=>new[]{triangles[t],triangles[t+1],triangles[t+2]}));
    // Bind diagnostics to the authored sewing point, not to a moving nearest
    // point search that could hide a detached ornament during animation.
    float nearest=float.PositiveInfinity;int bowAnchor=-1,surfaceAnchor=-1;
    foreach(int i in bowIds){var bw=originalWeights[i];bool root=false;
     foreach(var pair in new[]{(bw.boneIndex0,bw.weight0),(bw.boneIndex1,bw.weight1),(bw.boneIndex2,bw.weight2),(bw.boneIndex3,bw.weight3)})root|=pair.Item2>.01f&&furisodeOriginalBones[pair.Item1].name.StartsWith("Acs_UnderSleeveRibbon_Root_");
     if(!root)continue;
     foreach(int j in ids){float d=(vertices[i]-vertices[j]).sqrMagnitude;if(d<nearest){nearest=d;bowAnchor=i;surfaceAnchor=j;}}
    }
    if(bowAnchor<0)throw new InvalidOperationException("Sleeve bow attachment missing on "+side);
    sleeveBowBindings.Add((bowAnchor,surfaceAnchor));
    var kept=Enumerable.Range(0,triangles.Length/3).Where(t=>!faces.Contains(t*3)&&!bowFaces.Contains(t*3)).SelectMany(t=>new[]{triangles[t*3],triangles[t*3+1],triangles[t*3+2]}).ToArray();
    var seamKeys=new HashSet<string>(kept.Select(i=>SleevePositionKey(vertices[i])));
    // Torso weights identify the bodice attachment. Shoulder weights are arm
    // support, and also occur far down the hem: never turn them into body pins.
    var seam=ids.Where(i=>seamKeys.Contains(SleevePositionKey(vertices[i]))||SleeveTorsoWeight(originalWeights[i],furisodeOriginalBones)>.005f).ToArray();
    if(seam.Length<8)throw new InvalidOperationException("Furisode seam missing");
    float minX=.19f,maxX=ids.Max(i=>Mathf.Abs(vertices[i].x));
    var upperEdge=ids.Where(i=>vertices[i].z>1.285f&&Mathf.Abs(vertices[i].x)>.18f).ToArray();
    float rootMax=upperEdge.Max(i=>Mathf.Abs(vertices[i].x)),top=1.335f,bottom=ids.Min(i=>vertices[i].z),height=top-bottom;
    float sign=Mathf.Sign(vertices[ids[0]].x);var grid=new SleeveGrid{side=side,nodes=new Transform[SleeveColumns,SleeveRows],rest=new Quaternion[SleeveColumns,SleeveRows],anchors=new SleeveAnchor[SleeveColumns]};
    var baseIndex=bones.Count;
    for(int col=0;col<SleeveColumns;col++){
     // Anchor beneath its authored support bone so resets and disabled
     // frames retain the character's root motion, not world-origin poses.
     float u=(float)col/(SleeveColumns-1),x=Mathf.Lerp(minX,rootMax,u);
     var near=upperEdge.OrderBy(i=>Mathf.Abs(Mathf.Abs(vertices[i].x)-x)+Mathf.Abs(vertices[i].z-top)*.4f).Take(4).ToArray();var rootPosition=near.Aggregate(Vector3.zero,(p,i)=>p+OriginalWorld(i))/near.Length;
     var support=furisodeOriginalBones[originalWeights[near[0]].boneIndex0];float depth=near.Average(i=>vertices[i].y);
     for(int row=0;row<SleeveRows;row++){
      float v=(float)row/(SleeveRows-1);var node=new GameObject("Furisode / "+side+" / "+col+" / "+row).transform;node.SetParent(row==0?support:grid.nodes[col,row-1],true);
      node.position=row==0?rootPosition:r.transform.TransformPoint(new Vector3(sign*Mathf.Lerp(minX,Mathf.Lerp(rootMax,maxX,v),u),depth,top-height*v));node.rotation=support.rotation;
      grid.nodes[col,row]=node;grid.rest[col,row]=node.localRotation;bones.Add(node);bindposes.Add(node.worldToLocalMatrix*r.transform.localToWorldMatrix);
     }
     grid.anchors[col]=new SleeveAnchor{root=grid.nodes[col,0],support=support,sourceIds=near,bindSupport=support.rotation,bindRotation=grid.nodes[col,0].rotation};
    }
    foreach(int i in ids.Concat(bowIds)){
     float v=Mathf.Clamp01((top-vertices[i].z)/height);float u=Mathf.Clamp01((Mathf.Abs(vertices[i].x)-minX)/(Mathf.Lerp(rootMax,maxX,v)-minX));
     float cx=u*(SleeveColumns-1),cy=v*(SleeveRows-1);int x=Mathf.Min(SleeveColumns-2,(int)cx),y=Mathf.Min(SleeveRows-2,(int)cy);float fx=cx-x,fy=cy-y;
     float distance=seam.Min(j=>Vector3.Distance(vertices[i],vertices[j]));
     // Preserve the original shoulder roll and its seam exactly. From the
     // underarm onward, cuff, forearm and hanging panel share one surface.
     float shoulder=Mathf.Lerp(1,Mathf.SmoothStep(0,1,(Mathf.Abs(vertices[i].x)-.24f)/.10f),Mathf.SmoothStep(0,1,(vertices[i].z-1.08f)/.15f));
     // A connected component contains a few bodice vertices with small sleeve
     // weights. Keep those on their authored torso skin; component membership
     // alone is not permission to pull the chest/back into the sleeve grid.
     float blend=bowIds.Contains(i)?1:Mathf.SmoothStep(0,1,distance/.20f)*shoulder;
     sleeveFixedMask[i]=blend==0;
     var values=new Dictionary<int,float>();void Add(int bone,float w){if(w>0)values[bone]=values.TryGetValue(bone,out var old)?old+w:w;}
     var w=originalWeights[i];Add(w.boneIndex0,w.weight0*(1-blend));Add(w.boneIndex1,w.weight1*(1-blend));Add(w.boneIndex2,w.weight2*(1-blend));Add(w.boneIndex3,w.weight3*(1-blend));
     Add(baseIndex+x*SleeveRows+y,(1-fx)*(1-fy)*blend);Add(baseIndex+(x+1)*SleeveRows+y,fx*(1-fy)*blend);Add(baseIndex+x*SleeveRows+y+1,(1-fx)*fy*blend);Add(baseIndex+(x+1)*SleeveRows+y+1,fx*fy*blend);
     var best=values.OrderByDescending(p=>p.Value).Take(4).ToArray();float total=best.Sum(p=>p.Value);var result=new BoneWeight();
     if(best.Length>0){result.boneIndex0=best[0].Key;result.weight0=best[0].Value/total;}if(best.Length>1){result.boneIndex1=best[1].Key;result.weight1=best[1].Value/total;}if(best.Length>2){result.boneIndex2=best[2].Key;result.weight2=best[2].Value/total;}if(best.Length>3){result.boneIndex3=best[3].Key;result.weight3=best[3].Value/total;}
     weights[i]=result;furisodeMask[i]=blend>0;
    }
    furisodeGrids.Add(grid);
   }
   furisodeRuntime=Instantiate(original);furisodeRuntime.name=original.name+" / continuous furisode skin";furisodeRuntime.boneWeights=weights;furisodeRuntime.bindposes=bindposes.ToArray();r.bones=bones.ToArray();r.sharedMesh=furisodeRuntime;
   Debug.Log("FURISODE_GRID vertices="+furisodeMask.Count(x=>x)+" bones="+(bones.Count-furisodeOriginalBones.Length));
  }
  bool IsFurisodeDriven(SkinnedMeshRenderer renderer,int vertex)=>renderer==furisodeSource&&furisodeMask[vertex];
  bool IsSleeveFixed(SkinnedMeshRenderer renderer,int vertex)=>renderer==furisodeSource&&sleeveFixedMask[vertex];
  void BuildFurisodeGroups(MagicaCloth garment){
   foreach(var grid in furisodeGrids){
    var cloth=NewGroup("Furisode surface / "+grid.side,out var info);var s=cloth.SerializeData;info.type="BoneCloth";info.renderers=new[]{furisodeSource.name};info.roots=grid.anchors.Select(a=>a.root.name).ToArray();
    s.clothType=ClothProcess.ClothType.BoneCloth;s.rootBones.AddRange(grid.anchors.Select(a=>a.root));s.connectionMode=RenderSetupData.BoneConnectionMode.SequentialNonLoopMesh;s.rotationalInterpolation=1;s.rootRotation=0;
    s.gravity=6;s.damping=new CurveSerializeData(.13f);s.radius=new CurveSerializeData(.012f);s.tetherConstraint.distanceCompression=.95f;
    s.triangleBendingConstraint.stiffness=.035f;s.angleRestorationConstraint.stiffness=new CurveSerializeData(.015f,1,.1f);s.angleRestorationConstraint.velocityAttenuation=.35f;
    s.angleLimitConstraint.useAngleLimit=true;s.angleLimitConstraint.limitAngle=new CurveSerializeData(135);s.inertiaConstraint.localInertia=.75f;
    s.motionConstraint.useMaxDistance=true;s.motionConstraint.maxDistance=new CurveSerializeData(.7f,.035f,1);
    s.selfCollisionConstraint.selfMode=SelfCollisionConstraint.SelfCollisionMode.FullMesh;s.selfCollisionConstraint.syncMode=SelfCollisionConstraint.SelfCollisionMode.FullMesh;s.selfCollisionConstraint.syncPartner=garment;s.selfCollisionConstraint.surfaceThickness=new CurveSerializeData(.008f);s.selfCollisionConstraint.clothMass=.45f;
    foreach(var node in grid.nodes){bool pin=grid.anchors.Any(a=>a.root==node);cloth.GetSerializeData2().boneAttributeDict[node]=pin?VertexAttribute.Fixed:VertexAttribute.Move;if(pin)info.fixedVertices++;else info.movable++;}
   }
  }
  void UpdateFurisodeAnchors(){
   if(!furisodeSource)return;var vertices=furisodeVertices;var weights=furisodeWeights;var b=furisodeBind;
   foreach(var grid in furisodeGrids)foreach(var a in grid.anchors){Vector3 position=Vector3.zero;foreach(int i in a.sourceIds){var w=weights[i];var p=vertices[i];position+=Skin(b,w.boneIndex0,w.weight0,p)+Skin(b,w.boneIndex1,w.weight1,p)+Skin(b,w.boneIndex2,w.weight2,p)+Skin(b,w.boneIndex3,w.weight3,p);}a.root.position=position/a.sourceIds.Length;a.root.rotation=a.support.rotation*Quaternion.Inverse(a.bindSupport)*a.bindRotation;}
  }
#endif
 }
}
