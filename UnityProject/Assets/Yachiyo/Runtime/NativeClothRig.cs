using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Yachiyo {
 // PhysX solves continuous proxy surfaces; the authored garment rig transfers
 // their deformation to the layered render meshes without welding those layers.
 public sealed class NativeClothRig {
  sealed class Panel {
   public Cloth cloth;
   public Transform space;
   public GarmentPhysics.Chain[] chains;
   public int columns, rows;
   public bool skirt;
   public Transform[] anchors;
  }
  readonly List<Panel> panels=new List<Panel>();
  readonly GarmentPhysics owner;
  public readonly List<SleeveHemCloth> hems=new List<SleeveHemCloth>();
  bool reported;
  readonly ClothSphereColliderPair[] sleeveDress;
  public int VertexCount {get;private set;}
  public int FreeVertexCount {get;private set;}
  public int PanelCount=>panels.Count+hems.Count;
  public bool Enabled=>panels.All(p=>p.cloth.enabled)&&hems.All(h=>h.cloth.enabled);
  public bool Finite {get;private set;}=true;
  public float MaxTravel {get;private set;}
  public NativeClothRig(GarmentPhysics owner){
   this.owner=owner;
   var dress=new List<ClothSphereColliderPair>();SphereCollider previous=null;
   for(int i=0;i<4;i++){
    var t=new GameObject("Sleeve physics / dress envelope "+i).transform;t.SetParent(owner.hips,false);t.localPosition=new Vector3(0,-.15f-i*.2f,-.025f);
    var sphere=t.gameObject.AddComponent<SphereCollider>();sphere.radius=new[]{.18f,.26f,.35f,.46f}[i];sphere.isTrigger=true;
    if(previous) dress.Add(new ClothSphereColliderPair(previous,sphere));previous=sphere;
   }
   sleeveDress=dress.ToArray();
   var skirt=owner.chains.Where(c=>c.skirt&&c.bones.Length>=5).ToArray();
   if(skirt.Length>1)Create(skirt,25,17,true);
   foreach(var chain in owner.chains.Where(c=>!c.skirt))Create(new[]{chain},7,16,false);
   foreach(var chain in owner.chains.Where(c=>c.skirt&&c.bones.Length<5))Create(new[]{chain},5,9,true);
  }
  // Keep the authored shoulder, upper sleeves and cuffs. Only the four lower
  // hanging panels receive independent membranes after the idle rig has settled.
  // Keep the original authored sleeves connected to the body and drive them
  // through the same bone chains. No detached hem panels are generated.
  public void EnsureHems(){if(hems.Count==0&&Time.timeSinceLevelLoad>1.2f){hems.AddRange(SleeveHemCloth.Create(owner));VertexCount+=hems.Sum(h=>h.VertexCount);}}

  static Vector3 Along(GarmentPhysics.Chain c,float t){
   float p=t*(c.bones.Length-1);int i=Mathf.Min((int)p,c.bones.Length-2);
   return Vector3.Lerp(c.bones[i].position,c.bones[i+1].position,p-i);
  }
  void Create(GarmentPhysics.Chain[] chains,int columns,int rows,bool skirt){
   var go=new GameObject("Unity Cloth / "+(chains.Length>1?"skirt":chains[0].bones[0].name));
   go.transform.SetParent(owner.transform,false);
   var anchors=new Transform[chains.Length];
   for(int i=0;i<chains.Length;i++){
    var root=chains[i].bones[0];var anchor=new GameObject("Cloth pinned seam").transform;
    if(skirt){anchor.SetParent(root.parent,false);anchor.localPosition=root.localPosition;anchor.localRotation=root.localRotation;anchor.localScale=root.localScale;}
    else{anchor.SetParent(owner.transform,false);anchor.position=root.position;anchor.rotation=SleeveFrame();}
    anchors[i]=anchor;
   }
   var vertices=new Vector3[columns*rows];var weights=new BoneWeight[vertices.Length];var coefficients=new ClothSkinningCoefficient[vertices.Length];
   var indices=new List<int>();var collisions=new List<uint>();
   for(int y=0;y<rows;y++)for(int x=0;x<columns;x++){
    int k=y*columns+x;float t=(float)y/(rows-1),u=(float)x/(columns-1)*(chains.Length-1);
    int a=Mathf.Min((int)u,chains.Length-1),b=Mathf.Min(a+1,chains.Length-1);float blend=u-a;
    Vector3 point=Vector3.Lerp(Along(chains[a],t),Along(chains[b],t),blend);
    if(!skirt){float length=0;for(int i=1;i<chains[0].bones.Length;i++)length+=Vector3.Distance(chains[0].bones[i-1].position,chains[0].bones[i].position);float side=chains[0].bones[0].name.Contains("Left")?-1:1;point=chains[0].bones[0].position+length*t*(Vector3.down*.8660254f+anchors[0].right*(side*.5f));}
    if(chains.Length==1)point+=owner.transform.forward*((float)x/(columns-1)-.5f)*(skirt?.14f:Mathf.Lerp(.09f,.22f,t));
    vertices[k]=go.transform.InverseTransformPoint(point);
    weights[k]=new BoneWeight{boneIndex0=a,weight0=1-blend,boneIndex1=b,weight1=blend};
    // The entire upper seam is pinned. The skirt retains its tailored volume;
    // hanging sleeves are free to align with world gravity as arms move.
    coefficients[k]=new ClothSkinningCoefficient{maxDistance=y==0?0:skirt?.07f*t:1.55f,collisionSphereDistance=0};
    if(y>0){FreeVertexCount++;if(y%2==0&&x%2==0)collisions.Add((uint)k);}
    if(x<columns-1&&y<rows-1){indices.AddRange(new[]{k,k+columns,k+1,k+1,k+columns,k+columns+1});}
   }
   // Sleeve anchors follow seam translation and character heading, keeping the
   // reset surface gravity-aligned instead of rotating it through the torso.
   var bindBones=anchors;
   var mesh=new Mesh{name="Cloth physics surface"};mesh.vertices=vertices;mesh.triangles=indices.ToArray();mesh.boneWeights=weights;mesh.bindposes=bindBones.Select(t=>t.worldToLocalMatrix*go.transform.localToWorldMatrix).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
   var skin=go.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.bones=bindBones;skin.rootBone=owner.hips;skin.updateWhenOffscreen=true;skin.forceRenderingOff=true;skin.localBounds=new Bounds(Vector3.up,Vector3.one*10);
   var cloth=go.AddComponent<Cloth>();cloth.coefficients=coefficients;cloth.capsuleColliders=owner.bodyCapsules;
   if(!skirt)cloth.sphereColliders=sleeveDress;
   cloth.useGravity=true;cloth.stretchingStiffness=1;cloth.bendingStiffness=skirt?.85f:.005f;cloth.damping=.35f;cloth.friction=.5f;
   cloth.useTethers=true;cloth.enableContinuousCollision=true;cloth.useVirtualParticles=1;
   cloth.worldAccelerationScale=.15f;cloth.worldVelocityScale=.1f;cloth.clothSolverFrequency=120;cloth.stiffnessFrequency=120;
   cloth.collisionMassScale=.5f;cloth.selfCollisionDistance=.018f;cloth.selfCollisionStiffness=.8f;cloth.SetSelfAndInterCollisionIndices(collisions);
   // Let Unity generate a matched virtual-particle index/weight table. Mixing
   // automatic generation with a custom table can leave stale weight indices
   // when the solver rebuilds the cloth after a reset.
   Debug.Log("CLOTH_PANEL "+go.name+" mesh="+vertices.Length+" physics="+cloth.vertices.Length+" segments="+string.Join(",",chains.Select(c=>string.Join("/",c.bones.Skip(1).Select((b,i)=>Vector3.Distance(b.position,c.bones[i].position).ToString("F4"))))));
   // Cloth vertices are expressed in the skinned renderer root-bone frame.
   panels.Add(new Panel{cloth=cloth,space=skin.rootBone,chains=chains,columns=columns,rows=rows,skirt=skirt,anchors=anchors});VertexCount+=vertices.Length;
  }
  Quaternion SleeveFrame(){var forward=Vector3.ProjectOnPlane(owner.hips.forward,Vector3.up);return Quaternion.LookRotation(forward.sqrMagnitude>.001f?forward:Vector3.forward,Vector3.up);}
  public void Prepare(){foreach(var hem in hems)hem.Prepare(owner);foreach(var p in panels)if(!p.skirt)for(int i=0;i<p.anchors.Length;i++){p.anchors[i].position=p.chains[i].bones[0].position;p.anchors[i].rotation=SleeveFrame();}}
  public void Reset(bool active){foreach(var cloth in panels.Select(p=>p.cloth).Concat(hems.Select(h=>h.cloth))){cloth.enabled=false;cloth.enabled=active;if(active)cloth.ClearTransformMotion();}foreach(var hem in hems)hem.SetVisible(active);}
  public void Sample(){
   Finite=true;MaxTravel=0;
   foreach(var p in panels){
    p.cloth.clothSolverFrequency=owner.highQuality?180:120;
    p.cloth.externalAcceleration=new Vector3(2.4f,0,.9f)*owner.wind*(.75f+.25f*Mathf.Sin(Time.time*1.5f));
    var vertices=p.cloth.vertices;
    foreach(var v in vertices)if(!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z))Finite=false;
    if(!Finite||vertices.Length!=p.columns*p.rows)continue;
    for(int c=0;c<p.chains.Length;c++){
     var chain=p.chains[c];float x=p.chains.Length==1?(p.columns-1)*.5f:(float)c/(p.chains.Length-1)*(p.columns-1);
     if(!reported&&Time.frameCount>60&&!p.skirt)Debug.Log("SLEEVE_PIN "+chain.bones[0].name+" expected="+chain.anchor+" root="+p.space.TransformPoint(vertices[Mathf.RoundToInt(x)])+" object="+p.cloth.transform.TransformPoint(vertices[Mathf.RoundToInt(x)])+" tipRoot="+p.space.TransformPoint(vertices[(p.rows-1)*p.columns+Mathf.RoundToInt(x)]));
     for(int i=1;i<chain.bones.Length;i++){
      float fraction=(float)i/(chain.bones.Length-1);
      if(!p.skirt){float length=0,along=0;for(int j=1;j<chain.bones.Length;j++){float d=Vector3.Distance(chain.targets[j-1],chain.targets[j]);length+=d;if(j<=i)along+=d;}fraction=along/Mathf.Max(length,.001f);}
      float y=fraction*(p.rows-1);int x0=(int)x,y0=(int)y,x1=Mathf.Min(x0+1,p.columns-1),y1=Mathf.Min(y0+1,p.rows-1);
      var a=Vector3.Lerp(vertices[y0*p.columns+x0],vertices[y0*p.columns+x1],x-x0);var b=Vector3.Lerp(vertices[y1*p.columns+x0],vertices[y1*p.columns+x1],x-x0);
      chain.positions[i]=p.space.TransformPoint(Vector3.Lerp(a,b,y-y0));MaxTravel=Mathf.Max(MaxTravel,Vector3.Distance(chain.positions[i],chain.targets[i]));
      if(!p.skirt){var lo=Vector3.Lerp(vertices[y0*p.columns],vertices[y1*p.columns],y-y0);var hi=Vector3.Lerp(vertices[y0*p.columns+p.columns-1],vertices[y1*p.columns+p.columns-1],y-y0);chain.across[i]=p.space.TransformDirection(hi-lo).normalized;}
     }
     if(!p.skirt)chain.across[0]=p.space.TransformDirection(vertices[p.columns-1]-vertices[0]).normalized;
    }
   }
   if(Time.frameCount>60)reported=true;
  }
  public void UpdateHems(){foreach(var hem in hems){hem.Update(owner);Finite&=hem.Finite;}}
 }
}
