using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Yachiyo {
 // The model has two authored layers per sleeve: the navy outer sheet and the
 // teal lining. Simulating both as independent flags is what produced the
 // wing/teal-triangle artefacts. This rig simulates the outer sheet only and
 // samples the lining UV on its back faces, with the authored size, all seam
 // pins and a small wrap so the sleeve keeps a three-dimensional silhouette.
 public sealed class SleeveHemCloth {
  public readonly Cloth cloth;
  public readonly MeshRenderer renderer;
  public readonly MeshRenderer shellRenderer;
  public readonly Mesh mesh,shellMesh;
  readonly int[] shellIds,shellAnchor;readonly int[] shellTriangles;readonly Vector3 shellOffset;
  readonly SkinnedMeshRenderer source;
  readonly Transform space;
  readonly Transform[] pins;
  readonly int[] sourceIds,particleForVertex,pinSourceIds,pinRenderIds,physicalTriangles;
  readonly int[] nearestSeam,renderPin;readonly float[] seamBlend;
  Mesh fullMesh,upperMesh;
  readonly Mesh baked=new Mesh();
  readonly Vector3[] worldVertices;
  readonly Vector3[] solvedPoints,seamCorrections,renderVertices,shellVertices;
  readonly SeamPoint[] seamPoints;
  // Only these forty vertices are needed by the two seams. Keep the original
  // bind poses and weights instead of baking all 11,717 clothing vertices six
  // times per frame. Cloth1 has no blend shapes; its authored skin is unchanged.
  sealed class SeamPoint {
   readonly Transform[] bones=new Transform[4];readonly Vector3[] local=new Vector3[4];readonly float[] weights=new float[4];
   public SeamPoint(Vector3 vertex,BoneWeight weight,Transform[] rig,Matrix4x4[] bindposes){
    int[] ids={weight.boneIndex0,weight.boneIndex1,weight.boneIndex2,weight.boneIndex3};
    weights[0]=weight.weight0;weights[1]=weight.weight1;weights[2]=weight.weight2;weights[3]=weight.weight3;
    for(int i=0;i<4;i++)if(weights[i]>0){bones[i]=rig[ids[i]];local[i]=bindposes[ids[i]].MultiplyPoint3x4(vertex);}
   }
   public Vector3 World(){var point=Vector3.zero;for(int i=0;i<4;i++)if(weights[i]>0)point+=bones[i].TransformPoint(local[i])*weights[i];return point;}
  }
  readonly float side;
  readonly int createdFrame;
  public int VertexCount=>cloth.vertices.Length;
  public bool Finite{get;private set;}=true;
  public float SeamError{get;private set;}
  public float PhysicalSeamLag{get;private set;}
  public int PinCount=>renderPin.Count(i=>i>=0);
  static string Key(Vector3 p)=>Mathf.RoundToInt(p.x*100000)+"/"+Mathf.RoundToInt(p.y*100000)+"/"+Mathf.RoundToInt(p.z*100000);
  static float LowerWeight(BoneWeight w,Transform[] bones,string side){
   float sum=0;foreach(var p in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}){
    string n=bones[p.Item1].name;if(n.Contains("CenterSleeve_00")&&!n.Contains("_001_")&&n.EndsWith(side))sum+=p.Item2;
   }return sum;
  }
  public static List<SleeveHemCloth> Create(GarmentPhysics owner){
   var source=owner.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.name=="cloth1");
   var original=source.sharedMesh;var bind=original.vertices;var weights=original.boneWeights;var all=original.triangles;
   var panels=new List<(string side,HashSet<int> faces)>();
   foreach(string side in new[]{"Left","Right"}){
    var selected=new List<int>();var adjacent=new Dictionary<string,HashSet<string>>();
    for(int t=0;t<all.Length;t+=3)if(Enumerable.Range(0,3).All(j=>LowerWeight(weights[all[t+j]],source.bones,side)>.08f)){
     selected.Add(t);
     for(int j=0;j<3;j++){string a=Key(bind[all[t+j]]),b=Key(bind[all[t+(j+1)%3]]);if(!adjacent.ContainsKey(a))adjacent[a]=new HashSet<string>();if(!adjacent.ContainsKey(b))adjacent[b]=new HashSet<string>();adjacent[a].Add(b);adjacent[b].Add(a);}
    }
    var seen=new HashSet<string>();
    foreach(var root in adjacent.Keys){
     if(seen.Contains(root))continue;var nodes=new HashSet<string>();var todo=new Stack<string>();todo.Push(root);
     while(todo.Count>0){var p=todo.Pop();if(!seen.Add(p))continue;nodes.Add(p);foreach(var n in adjacent[p])todo.Push(n);}
     // The small cuff decoration remains on the original renderer.
     if(nodes.Count<80)continue;
     panels.Add((side,new HashSet<int>(selected.Where(t=>nodes.Contains(Key(bind[all[t]]))))));
    }
   }
   if(panels.Count!=4)throw new InvalidOperationException("Expected four authored sleeve layers; found "+panels.Count);
   var removed=new HashSet<int>(panels.SelectMany(p=>p.faces));var kept=new List<int>();
   for(int t=0;t<all.Length;t+=3)if(!removed.Contains(t))kept.AddRange(new[]{all[t],all[t+1],all[t+2]});
   var seamKeys=new HashSet<string>(kept.Select(i=>Key(bind[i])));
   var result=new List<SleeveHemCloth>();int index=0;
   foreach(var group in panels.GroupBy(p=>p.side)){
    // The front layer is the visible navy sheet; the rear layer is its lining.
    var ordered=group.OrderBy(p=>p.faces.SelectMany(t=>new[]{all[t],all[t+1],all[t+2]}).Average(i=>bind[i].y)).ToArray();
    if(ordered.Length!=2)throw new InvalidOperationException("Expected two layers per sleeve; found "+ordered.Length);
    var outer=ordered[0];var lining=ordered[1];
    result.Add(new SleeveHemCloth(owner,source,original,outer.side,
     outer.faces.OrderBy(t=>t).SelectMany(t=>new[]{all[t],all[t+1],all[t+2]}).ToArray(),
     lining.faces.OrderBy(t=>t).SelectMany(t=>new[]{all[t],all[t+1],all[t+2]}).ToArray(),
     seamKeys,index++));
   }
   var upper=UnityEngine.Object.Instantiate(original);upper.name="cloth1 / original shoulder sleeve and cuff";upper.triangles=kept.ToArray();source.sharedMesh=upper;foreach(var hem in result){hem.fullMesh=original;hem.upperMesh=upper;}
   Debug.Log("HEM_PRESERVED source vertices="+upper.vertexCount+" retained triangles="+kept.Count/3+" removed hanging triangles="+removed.Count+" simulated layers="+result.Count+" original vertices="+upper.vertices.SequenceEqual(original.vertices)+" original weights="+upper.boneWeights.SequenceEqual(original.boneWeights));
   return result;
  }
  SleeveHemCloth(GarmentPhysics owner,SkinnedMeshRenderer source,Mesh original,string sleeveSide,int[] faces,int[] liningFaces,HashSet<string> seamKeys,int panelIndex){
   this.source=source;side=sleeveSide=="Left"?-1:1;createdFrame=Time.frameCount;
   sourceIds=faces.Distinct().ToArray();var bind=original.vertices;var remap=sourceIds.Select((id,i)=>(id,i)).ToDictionary(p=>p.id,p=>p.i);
   pinRenderIds=Enumerable.Range(0,sourceIds.Length).Where(i=>seamKeys.Contains(Key(bind[sourceIds[i]]))).ToArray();
   if(pinRenderIds.Length<4)throw new InvalidOperationException("Too few sleeve seam pins: "+pinRenderIds.Length);
   pinSourceIds=pinRenderIds.Select(i=>sourceIds[i]).ToArray();
   if(original.blendShapeCount!=0)throw new InvalidOperationException("Sleeve seam sampling requires the original clothing mesh without blend shapes");
   var originalWeights=original.boneWeights;var originalBones=source.bones;var originalBindposes=original.bindposes;
   seamPoints=pinSourceIds.Select(i=>new SeamPoint(bind[i],originalWeights[i],originalBones,originalBindposes)).ToArray();
   source.BakeMesh(baked);var posed=baked.vertices.Select(source.transform.TransformPoint).ToArray();
   var reference=sourceIds.Select(i=>owner.ProjectSleeveSurface(posed[i],side)).ToArray();

   // Fit the authored sheet to the current seam, but never squeeze it by the
   // seam's pose-space length: lowering the arm shortens that edge by more than
   // half and used to collapse the whole sleeve into the skirt.
   int ia=pinRenderIds[0],ib=pinRenderIds[1];float longest=0;
   foreach(int i in pinRenderIds)foreach(int j in pinRenderIds)if((bind[sourceIds[j]]-bind[sourceIds[i]]).sqrMagnitude>longest){longest=(bind[sourceIds[j]]-bind[sourceIds[i]]).sqrMagnitude;ia=i;ib=j;}
   Vector3 bindA=bind[sourceIds[ia]],bindB=bind[sourceIds[ib]];
   Vector3 bindSeamCenter=(bindA+bindB)*.5f;
   Vector3 a=reference[ia],b=reference[ib];
   Vector3 seamCenter=(a+b)*.5f;
   Vector3 outward=Vector3.ProjectOnPlane(seamCenter-owner.hips.position,owner.hips.up);
   if(outward.sqrMagnitude<.0001f)outward=owner.hips.right*side;
   outward.Normalize();
   Vector3 depthDir=Vector3.Cross(outward,Vector3.down).normalized;
   float curlSign=Mathf.Sign(Vector3.Dot(depthDir,owner.hips.position-seamCenter));
   if(Mathf.Abs(curlSign)<.5f)curlSign=1;
   // Use the panel's own bind-space axes. Comparing bind vertices with the
   // world hips mixed two coordinate spaces and sent the width into depth.
   Vector3 bindMean=sourceIds.Aggregate(Vector3.zero,(sum,i)=>sum+bind[i])/sourceIds.Length;
   Vector3 bmin=bind[sourceIds[0]],bmax=bmin;
   for(int i=1;i<sourceIds.Length;i++){var v=bind[sourceIds[i]];bmin=Vector3.Min(bmin,v);bmax=Vector3.Max(bmax,v);}
   var ranges=new[]{bmax.x-bmin.x,bmax.y-bmin.y,bmax.z-bmin.z};
   var axes=new[]{Vector3.right,Vector3.up,Vector3.forward};
   var order=Enumerable.Range(0,3).OrderByDescending(k=>ranges[k]).ToArray();
   Vector3 NameAxis(int k){var axis=axes[k];return axis*Mathf.Sign(Vector3.Dot(bindMean-bindSeamCenter,axis));}
   Vector3 b0=NameAxis(order[0]),b1=NameAxis(order[1]),b2=Vector3.Cross(b0,b1).normalized;
   float minU=float.MaxValue,maxU=float.MinValue,minV=float.MaxValue,maxV=float.MinValue;
   for(int i=0;i<sourceIds.Length;i++){var d=bind[sourceIds[i]]-bindSeamCenter;float u=Vector3.Dot(d,b0);float v=Vector3.Dot(d,b1);minU=Mathf.Min(minU,u);maxU=Mathf.Max(maxU,u);minV=Mathf.Min(minV,v);maxV=Mathf.Max(maxV,v);}
   float spanU=Mathf.Max(.001f,maxU-minU),spanV=Mathf.Max(.001f,maxV-minV);
   var rest=new Vector3[reference.Length];var pinSet=new HashSet<int>(pinRenderIds);var uCoord=new float[reference.Length];
   for(int i=0;i<rest.Length;i++){
    var d=bind[sourceIds[i]]-bindSeamCenter;
    float u=Mathf.Clamp01((Vector3.Dot(d,b0)-minU)/spanU);uCoord[i]=u;
    float v=Mathf.Clamp01((Vector3.Dot(d,b1)-minV)/spanV);
    var p=seamCenter+outward*((u-.5f)*.38f)+Vector3.down*(.02f+v*.50f)+depthDir*(.03f*Mathf.Sin(v*Mathf.PI));
    rest[i]=p;
    if(pinSet.Contains(i))rest[i]=reference[i];
   }
   // The lining layer is not a second cloth. Find its UV at the same bind-space
   // location so the back faces of the outer sheet show the teal lining.
   var liningIds=liningFaces.Distinct().ToArray();
   var lining=new Vector2[sourceIds.Length];
   for(int i=0;i<sourceIds.Length;i++){
    float best=float.PositiveInfinity;
    for(int j=0;j<liningIds.Length;j++){float d=(bind[liningIds[j]]-bind[sourceIds[i]]).sqrMagnitude;if(d<best){best=d;lining[i]=original.uv[liningIds[j]];}}
   }

   var physicsObject=new GameObject("Unity Cloth / sleeve "+sleeveSide);physicsObject.transform.SetParent(owner.transform,false);
   var anchors=new List<Transform>{owner.hips};pins=new Transform[pinRenderIds.Length];
   for(int i=0;i<pins.Length;i++){pins[i]=new GameObject("Original hem seam "+i).transform;pins[i].SetParent(owner.hips,false);pins[i].position=reference[pinRenderIds[i]];anchors.Add(pins[i]);}

   physicalTriangles=faces.Select(i=>remap[i]).ToArray();
   var physicalRest=sourceIds.Select((id,i)=>rest[i]).ToArray();
   var skinWeights=new BoneWeight[physicalRest.Length];
   for(int i=0;i<skinWeights.Length;i++)skinWeights[i]=new BoneWeight{boneIndex0=0,weight0=1};
   for(int i=0;i<pinRenderIds.Length;i++)skinWeights[pinRenderIds[i]]=new BoneWeight{boneIndex0=i+1,weight0=1};
   var physicsMesh=new Mesh{name="Outer sleeve sheet"};physicsMesh.vertices=physicalRest.Select(physicsObject.transform.InverseTransformPoint).ToArray();physicsMesh.triangles=physicalTriangles;physicsMesh.boneWeights=skinWeights;physicsMesh.bindposes=anchors.Select(t=>t.worldToLocalMatrix*physicsObject.transform.localToWorldMatrix).ToArray();physicsMesh.RecalculateNormals();physicsMesh.RecalculateBounds();
   var skin=physicsObject.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=physicsMesh;skin.bones=anchors.ToArray();skin.rootBone=owner.hips;skin.forceRenderingOff=true;skin.updateWhenOffscreen=true;skin.localBounds=new Bounds(Vector3.up,Vector3.one*10);space=skin.rootBone;
   cloth=physicsObject.AddComponent<Cloth>();var particles=cloth.vertices;particleForVertex=new int[physicalRest.Length];float error=0;
   var coefficients=new ClothSkinningCoefficient[particles.Length];for(int i=0;i<coefficients.Length;i++)coefficients[i]=new ClothSkinningCoefficient{maxDistance=1.5f,collisionSphereDistance=0};
   for(int i=0;i<physicalRest.Length;i++){
    particleForVertex[i]=Enumerable.Range(0,particles.Length).OrderBy(j=>(physicsObject.transform.TransformPoint(particles[j])-physicalRest[i]).sqrMagnitude).First();
    error=Mathf.Max(error,Vector3.Distance(physicsObject.transform.TransformPoint(particles[particleForVertex[i]]),physicalRest[i]));
    float distance=pinSourceIds.Min(j=>Vector3.Distance(bind[sourceIds[i]],bind[j]));
    coefficients[particleForVertex[i]].maxDistance=.06f+.30f*distance;
    if(pinSet.Contains(i))coefficients[particleForVertex[i]].maxDistance=0;
   }
   if(error>.001f)throw new InvalidOperationException("Sleeve panel mapping error "+error);
   renderPin=new int[sourceIds.Length];nearestSeam=new int[sourceIds.Length];seamBlend=new float[sourceIds.Length];
   var physicalSeam=new List<(int vertex,int pin)>();
   for(int i=0;i<sourceIds.Length;i++){renderPin[i]=Array.IndexOf(pinSourceIds,sourceIds[i]);if(renderPin[i]>=0)physicalSeam.Add((i,renderPin[i]));}
   if(physicalSeam.Count<4)throw new InvalidOperationException("Sleeve seam did not survive the physical mapping");
   for(int i=0;i<sourceIds.Length;i++){var closest=physicalSeam.OrderBy(p=>(bind[sourceIds[i]]-bind[sourceIds[p.vertex]]).sqrMagnitude).First();nearestSeam[i]=closest.vertex;seamBlend[i]=1-Mathf.SmoothStep(0,1,Vector3.Distance(bind[sourceIds[i]],bind[sourceIds[closest.vertex]])/.10f);}
   cloth.coefficients=coefficients;cloth.capsuleColliders=Array.Empty<CapsuleCollider>();
   cloth.useGravity=true;cloth.stretchingStiffness=1;cloth.bendingStiffness=.03f;cloth.damping=.18f;cloth.friction=.12f;cloth.useTethers=true;cloth.enableContinuousCollision=true;cloth.useVirtualParticles=1;
   cloth.worldAccelerationScale=.25f;cloth.worldVelocityScale=.15f;cloth.clothSolverFrequency=240;cloth.stiffnessFrequency=240;
   cloth.selfCollisionDistance=.008f;cloth.selfCollisionStiffness=.7f;cloth.SetSelfAndInterCollisionIndices(Enumerable.Range(0,particles.Length).Where(i=>coefficients[i].maxDistance>0).Select(i=>(uint)i).ToList());
   // The authored sheet contains teal lining triangles on its reverse side.
   // Sample the front-facing outer surface so the back of the sleeve stays navy
   // instead of turning into a teal rectangle; keep teal only on the inner edge.
   var frontTriangles=new List<int>();
   for(int t=0;t<physicalTriangles.Length;t+=3){
    var n=Vector3.Cross(rest[physicalTriangles[t+1]]-rest[physicalTriangles[t]],rest[physicalTriangles[t+2]]-rest[physicalTriangles[t]]);
    if(Vector3.Dot(n,owner.hips.forward)>0f)frontTriangles.Add(t);
   }
   if(frontTriangles.Count<8)for(int t=0;t<physicalTriangles.Length;t+=3)frontTriangles.Add(t);
   var navyUV=new Vector2[sourceIds.Length];
   for(int i=0;i<sourceIds.Length;i++){
    float best=float.PositiveInfinity;Vector2 uv=original.uv[sourceIds[i]];
    for(int k=0;k<frontTriangles.Count;k++){
     int t=frontTriangles[k];
     int fa=physicalTriangles[t],fb=physicalTriangles[t+1],fc=physicalTriangles[t+2];
     var center=(rest[fa]+rest[fb]+rest[fc])/3f;float d=(center-rest[i]).sqrMagnitude;
     if(d<best){best=d;uv=(original.uv[sourceIds[fa]]+original.uv[sourceIds[fb]]+original.uv[sourceIds[fc]])/3f;}
    }
    navyUV[i]=uv;
   }
   var go=new GameObject("sleeve-hem-"+sleeveSide);go.transform.SetParent(owner.transform,false);renderer=go.AddComponent<MeshRenderer>();
   var material=new Material(source.sharedMaterial);material.name="Original hanging sleeve / physical surface";material.SetFloat("_ClothCollision",0);material.SetFloat("_SleeveTwoSided",1);material.SetFloat("_CullMode",0);renderer.sharedMaterial=material;
   mesh=new Mesh{name="Authored sleeve sheet / two-sided cloth"};mesh.vertices=physicalRest.Select(go.transform.InverseTransformPoint).ToArray();mesh.triangles=physicalTriangles;mesh.uv=sourceIds.Select(i=>original.uv[i]).ToArray();mesh.uv2=Enumerable.Range(0,sourceIds.Length).Select(i=>uCoord[i]<.18f?lining[i]:navyUV[i]).ToArray();mesh.uv3=sourceIds.Select(i=>original.uv3[i]).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();go.AddComponent<MeshFilter>().sharedMesh=mesh;worldVertices=(Vector3[])physicalRest.Clone();
   // A reversed copy of the same sheet, offset behind it, gives the sleeve a
   // navy back surface. The original two-sided sheet keeps the teal lining only
   // at the exposed edges; the copy is front-faced from behind and culled from
   // the front, so it cannot cover the outer surface.
   shellIds=sourceIds;
   shellTriangles=new int[physicalTriangles.Length];
   for(int t=0;t<physicalTriangles.Length;t+=3){shellTriangles[t]=physicalTriangles[t];shellTriangles[t+1]=physicalTriangles[t+2];shellTriangles[t+2]=physicalTriangles[t+1];}
   shellAnchor=Enumerable.Range(0,sourceIds.Length).ToArray();
   var shellGo=new GameObject("sleeve-back-"+sleeveSide);shellGo.transform.SetParent(owner.transform,false);shellRenderer=shellGo.AddComponent<MeshRenderer>();
   var shellMat=new Material(source.sharedMaterial);shellMat.name="Authored sleeve back / navy";
   var flat=new Texture2D(1,1,TextureFormat.RGBA32,false);flat.SetPixel(0,0,Color.white);flat.Apply();shellMat.SetTexture("_MainTex",flat);shellMat.SetColor("_Color",new Color(.205f,.195f,.345f,1f));
   shellMat.SetFloat("_ClothCollision",0);shellMat.SetFloat("_SleeveTwoSided",0);shellMat.SetFloat("_CullMode",0);shellRenderer.sharedMaterial=shellMat;
   shellMesh=new Mesh{name="Authored sleeve back surface"};shellMesh.vertices=physicalRest.Select(p=>shellGo.transform.InverseTransformPoint(p-owner.hips.forward*.06f)).ToArray();shellMesh.triangles=shellTriangles;shellMesh.uv=sourceIds.Select(i=>original.uv[i]).ToArray();shellMesh.uv2=sourceIds.Select(i=>original.uv[i]).ToArray();shellMesh.uv3=sourceIds.Select(i=>original.uv3[i]).ToArray();shellMesh.RecalculateNormals();shellMesh.RecalculateBounds();shellGo.AddComponent<MeshFilter>().sharedMesh=shellMesh;shellOffset=-owner.hips.forward*.06f;
   Debug.Log("HEM_PANEL "+sleeveSide+" vertices="+mesh.vertexCount+" native="+particles.Length+" triangles="+physicalTriangles.Length/3+" pins="+PinCount+" map error="+error);
   solvedPoints=new Vector3[worldVertices.Length];seamCorrections=new Vector3[worldVertices.Length];renderVertices=new Vector3[worldVertices.Length];shellVertices=new Vector3[shellIds.Length];
   mesh.MarkDynamic();shellMesh.MarkDynamic();
  }
  static CapsuleCollider[] FittedContacts(GarmentPhysics owner){
   var contacts=new List<CapsuleCollider>();
   foreach(var source in owner.bodyCapsules){
    if(source.name.Contains("Arm")||source.name.Contains("Hand"))continue; // the sleeve wraps the arm
    var go=new GameObject("Hem contact / "+source.name);go.transform.SetParent(source.transform,false);
    float margin=source.name.Contains("Arm")?.01f:source.name.Contains("Hand")?.012f:source.name.Contains("Leg")?.025f:.045f;
    var c=go.AddComponent<CapsuleCollider>();c.direction=source.direction;c.center=source.center;c.radius=source.radius+margin;c.height=source.height+2*margin;c.isTrigger=true;contacts.Add(c);
   }
   foreach(var source in owner.bodyCapsules.Where(c=>c.name.Contains("Leg"))){
    var go=new GameObject("Hem moving skirt / "+source.name);go.transform.SetParent(source.transform,false);
    float radius=source.name.Contains("UpperLeg")?.18f:.12f;
    var c=go.AddComponent<CapsuleCollider>();c.direction=source.direction;c.center=source.center;c.radius=radius;c.height=source.height+2*(radius-source.radius);c.isTrigger=true;contacts.Add(c);
   }
   return contacts.ToArray();
  }
  static Vector3 ProjectSkirt(GarmentPhysics owner,Vector3 p,float weight){
   var q=owner.hips.InverseTransformPoint(p);float below=-q.y;
   if(below<.04f||below>.98f)return p;
   float t=Mathf.Clamp01(below/.85f);float rx=Mathf.Lerp(.24f,.46f,t),rz=Mathf.Lerp(.27f,.40f,t);
   float nx=q.x/rx,nz=(q.z+.025f)/rz;float n=Mathf.Sqrt(nx*nx+nz*nz);
   if(n>=1f||n<.001f)return p;
   q.x/=n;q.z=(q.z+.025f)/n-.025f;
   return Vector3.Lerp(p,owner.hips.TransformPoint(q),weight);
  }
  public void Prepare(GarmentPhysics owner){
   for(int i=0;i<pins.Length;i++)pins[i].position=owner.ProjectSleeveSurface(seamPoints[i].World(),side);
  }
  // Used only by the explicit cloth verification, against Unity's skinning.
  public float VerifySeamSkinning(){source.BakeMesh(baked);var vertices=baked.vertices;float error=0;for(int i=0;i<seamPoints.Length;i++)error=Mathf.Max(error,Vector3.Distance(seamPoints[i].World(),source.transform.TransformPoint(vertices[pinSourceIds[i]])));return error;}
  public void Update(GarmentPhysics owner){
   Prepare(owner);if(Time.frameCount==createdFrame)return;
   cloth.externalAcceleration=owner.hips.forward*(.25f*owner.wind*Mathf.Sin(Time.time*1.5f));
   var particles=cloth.vertices;Finite=particles.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z));if(!Finite)return;
   var points=solvedPoints;var corrections=seamCorrections;var toWorld=space.localToWorldMatrix;
   for(int i=0;i<points.Length;i++)points[i]=toWorld.MultiplyPoint3x4(particles[particleForVertex[i]]);PhysicalSeamLag=0;SeamError=0;
   for(int i=0;i<points.Length;i++)if(renderPin[i]>=0){corrections[i]=pins[renderPin[i]].position-points[i];PhysicalSeamLag=Mathf.Max(PhysicalSeamLag,corrections[i].magnitude);}
   for(int i=0;i<worldVertices.Length;i++){var p=points[i]+corrections[nearestSeam[i]]*seamBlend[i];if(renderPin[i]<0)p=ProjectSkirt(owner,p,.55f);worldVertices[i]=p;}
   for(int i=0;i<worldVertices.Length;i++)if(renderPin[i]>=0){worldVertices[i]=pins[renderPin[i]].position;SeamError=Mathf.Max(SeamError,Vector3.Distance(worldVertices[i],pins[renderPin[i]].position));}
   var toLocal=renderer.transform.worldToLocalMatrix;for(int i=0;i<renderVertices.Length;i++)renderVertices[i]=toLocal.MultiplyPoint3x4(worldVertices[i]);
   mesh.vertices=renderVertices;mesh.RecalculateNormals();mesh.RecalculateBounds();
   if(shellMesh){var shellLocal=shellRenderer.transform.worldToLocalMatrix;for(int i=0;i<shellVertices.Length;i++)shellVertices[i]=shellLocal.MultiplyPoint3x4(worldVertices[shellAnchor[i]]+shellOffset);shellMesh.vertices=shellVertices;shellMesh.RecalculateNormals();shellMesh.RecalculateBounds();}
  }
  public Vector3[] WorldVertices()=>worldVertices;
  public Vector3[] PhysicsWorldVertices(){var vertices=cloth.vertices;return particleForVertex.Select(i=>space.TransformPoint(vertices[i])).ToArray();}
  public int[] PhysicsTriangles=>physicalTriangles;
  public void SetVisible(bool active){renderer.enabled=active;if(shellRenderer)shellRenderer.enabled=active;source.sharedMesh=active?upperMesh:fullMesh;}
 }
}
