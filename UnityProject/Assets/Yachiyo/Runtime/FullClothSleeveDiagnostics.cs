using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if YACHIYO_MAGICA
using MagicaCloth2;
#endif
namespace Yachiyo {
 public sealed partial class FullCharacterCloth {
  [Serializable]public sealed class HangingSleeveSample {
   public string side;public int vertices,movable;public float meanSimulationOffset,maxSimulationOffset;
  }
  [Serializable]public sealed class SleeveRegionSample {
   public string side,region;public int vertices,driven,protectedVertices;public float meanSimulationOffset,maxSimulationOffset;
  }
  [Serializable]public sealed class SleeveAttachmentSample {
   public int bows,fixedVertices;public float maxAddedBowGap,maxBoundaryStretch,maxFixedError;
  }
  public SleeveAttachmentSample MeasureSleeveAttachments(){
   var sample=new SleeveAttachmentSample();
#if YACHIYO_MAGICA
   if(!furisodeSource)return sample;
   var b=bindings.Single(x=>x.renderer==furisodeSource);var mesh=new Mesh();b.renderer.BakeMesh(mesh);
   var actual=mesh.vertices;var expected=new Vector3[actual.Length];
   for(int i=0;i<actual.Length;i++){
    actual[i]=b.renderer.transform.TransformPoint(actual[i]);var w=b.weights[i];var p=b.vertices[i];
    expected[i]=Skin(b,w.boneIndex0,w.weight0,p)+Skin(b,w.boneIndex1,w.weight1,p)+Skin(b,w.boneIndex2,w.weight2,p)+Skin(b,w.boneIndex3,w.weight3,p);
    if(sleeveFixedMask[i]){sample.fixedVertices++;sample.maxFixedError=Mathf.Max(sample.maxFixedError,Vector3.Distance(expected[i],actual[i]));}
   }
   foreach(var pair in sleeveBowBindings){sample.bows++;float rest=Vector3.Distance(b.initial[pair.bow],b.initial[pair.surface]);sample.maxAddedBowGap=Mathf.Max(sample.maxAddedBowGap,Vector3.Distance(actual[pair.bow],actual[pair.surface])-rest);}
   var tris=b.source.triangles;
   for(int t=0;t<tris.Length;t+=3)for(int j=0;j<3;j++){
    int a=tris[t+j],c=tris[t+(j+1)%3];if(furisodeMask[a]==furisodeMask[c])continue;
    float reference=Mathf.Max(Vector3.Distance(b.initial[a],b.initial[c]),Vector3.Distance(expected[a],expected[c]));
    sample.maxBoundaryStretch=Mathf.Max(sample.maxBoundaryStretch,Vector3.Distance(actual[a],actual[c])-reference);
   }Destroy(mesh);
#endif
   return sample;
  }
  // Measure the previously rigid zones separately. Movement of the hem alone
  // is insufficient evidence that the forearm and cuff participate.
  public SleeveRegionSample[] MeasureSleeveRegions(){
   var result=new List<SleeveRegionSample>();
#if YACHIYO_MAGICA
   if(!furisodeSource)return result.ToArray();
   var b=bindings.Single(x=>x.renderer==furisodeSource);var mesh=new Mesh();b.renderer.BakeMesh(mesh);var actual=mesh.vertices;
   foreach(string side in new[]{"Left","Right"})foreach(string region in new[]{"forearm","cuff","junction"}){
    var sample=new SleeveRegionSample{side=side,region=region};
    for(int i=0;i<actual.Length;i++){
     var w=b.weights[i];float amount=0;var p=b.vertices[i];
     if(region=="forearm"&&(Mathf.Abs(p.x)<=.34f||p.z<=1.08f))continue;
     if(region=="junction"&&p.z<=.98f)continue;
     foreach(var pair in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}){
      string n=b.bones[pair.Item1].name;
      if(n.EndsWith(side)&&(region=="forearm"?(n.StartsWith("Acs_FrontSleeve_")||n.StartsWith("Acs_BackSleeve_")):region=="cuff"?n.StartsWith("Acs_ArmSleeve_"):n.StartsWith("Acs_CenterSleeve_")))amount+=pair.Item2;
     }
     if(amount<.7f)continue;
     sample.vertices++;if(IsFurisodeDriven(b.renderer,i))sample.driven++;else if(IsSleeveFixed(b.renderer,i))sample.protectedVertices++;
     var expected=Skin(b,w.boneIndex0,w.weight0,p)+Skin(b,w.boneIndex1,w.weight1,p)+Skin(b,w.boneIndex2,w.weight2,p)+Skin(b,w.boneIndex3,w.weight3,p);
     float offset=Vector3.Distance(expected,b.renderer.transform.TransformPoint(actual[i]));sample.meanSimulationOffset+=offset;sample.maxSimulationOffset=Mathf.Max(sample.maxSimulationOffset,offset);
    }
    sample.meanSimulationOffset/=Mathf.Max(1,sample.vertices);result.Add(sample);
   }Destroy(mesh);
#endif
   return result.ToArray();
  }
  [Serializable]sealed class SleeveSnapshot {
   public List<SleeveSurface> surfaces=new List<SleeveSurface>();
   public Vector3[] grid;public Vector3 rendererPosition;public Vector3[] proxy;public int[] proxyAttributes,proxyTriangles,proxyParents;public float[] depth;
  }
  [Serializable]sealed class SleeveSurface {
   public string name;public Vector3[] actual,skinned,initial;public float[] hangingWeight;
   public int[] painted,triangles;public bool[] boneSurface;
  }
  // Explicit diagnostic only: measure simulation relative to the authored skin,
  // rather than counting body motion as evidence that a sleeve is simulated.
  public void WriteSleeveDiagnostics(string path){
#if YACHIYO_MAGICA
   var dump=new SleeveSnapshot();var mesh=new Mesh();dump.grid=furisodeGrids.SelectMany(g=>g.nodes.Cast<Transform>()).Select(t=>t.position).ToArray();dump.rendererPosition=bindings[0].renderer.transform.position;
   for(int surface=0;surface<bindings.Count;surface++){
    var b=bindings[surface];var paint=cloths[0].GetSerializeData2().vertexAttributeList[surface];
    b.renderer.BakeMesh(mesh);var actual=mesh.vertices;var expected=new Vector3[actual.Length];var weights=new float[actual.Length];var attributes=new int[actual.Length];
    for(int i=0;i<actual.Length;i++){
     actual[i]=b.renderer.transform.TransformPoint(actual[i]);var w=b.weights[i];var p=b.vertices[i];
     expected[i]=Skin(b,w.boneIndex0,w.weight0,p)+Skin(b,w.boneIndex1,w.weight1,p)+Skin(b,w.boneIndex2,w.weight2,p)+Skin(b,w.boneIndex3,w.weight3,p);
     weights[i]=HangingWeight(b.bones[w.boneIndex0],w.weight0)+HangingWeight(b.bones[w.boneIndex1],w.weight1)+HangingWeight(b.bones[w.boneIndex2],w.weight2)+HangingWeight(b.bones[w.boneIndex3],w.weight3);
     attributes[i]=paint[i].Value;
    }
    dump.surfaces.Add(new SleeveSurface{name=b.renderer.name,actual=actual,skinned=expected,initial=b.initial,hangingWeight=weights,painted=attributes,triangles=b.source.triangles,boneSurface=System.Linq.Enumerable.Range(0,actual.Length).Select(i=>IsFurisodeDriven(b.renderer,i)).ToArray()});
   }
   var vm=cloths[0].Process.ProxyMeshContainer.shareVirtualMesh;
   dump.proxy=new Vector3[vm.VertexCount];dump.proxyAttributes=new int[vm.VertexCount];dump.depth=new float[vm.VertexCount];dump.proxyParents=new int[vm.VertexCount];dump.proxyTriangles=new int[vm.TriangleCount*3];
   for(int i=0;i<vm.VertexCount;i++){dump.proxy[i]=cloths[0].transform.TransformPoint((Vector3)vm.localPositions[i]);dump.proxyAttributes[i]=vm.attributes[i].Value;dump.depth[i]=vm.vertexDepths[i];dump.proxyParents[i]=vm.vertexParentIndices[i];}
   for(int i=0;i<vm.TriangleCount;i++){var t=vm.triangles[i];dump.proxyTriangles[i*3]=t.x;dump.proxyTriangles[i*3+1]=t.y;dump.proxyTriangles[i*3+2]=t.z;}
   Destroy(mesh);File.WriteAllText(path,JsonUtility.ToJson(dump));
#endif
  }
  static float HangingWeight(Transform bone,float weight)=>bone&&bone.name.StartsWith("Acs_CenterSleeve",StringComparison.Ordinal)?weight:0;
  public HangingSleeveSample[] MeasureHangingSleeves(){
   var result=new[]{new HangingSleeveSample{side="Left"},new HangingSleeveSample{side="Right"}};
#if YACHIYO_MAGICA
   var mesh=new Mesh();
   for(int surface=0;surface<bindings.Count;surface++){
    var b=bindings[surface];var paint=cloths[0].GetSerializeData2().vertexAttributeList[surface];b.renderer.BakeMesh(mesh);var actual=mesh.vertices;
    for(int i=0;i<actual.Length;i++){
     var w=b.weights[i];float hanging=HangingWeight(b.bones[w.boneIndex0],w.weight0)+HangingWeight(b.bones[w.boneIndex1],w.weight1)+HangingWeight(b.bones[w.boneIndex2],w.weight2)+HangingWeight(b.bones[w.boneIndex3],w.weight3);
     if(hanging<.7f)continue;
     int side=b.bones[w.boneIndex0].name.EndsWith("Left",StringComparison.Ordinal)?0:1;var sample=result[side];sample.vertices++;if(paint[i].IsMove()||IsFurisodeDriven(b.renderer,i))sample.movable++;
     var p=b.vertices[i];var expected=Skin(b,w.boneIndex0,w.weight0,p)+Skin(b,w.boneIndex1,w.weight1,p)+Skin(b,w.boneIndex2,w.weight2,p)+Skin(b,w.boneIndex3,w.weight3,p);
     float offset=Vector3.Distance(expected,b.renderer.transform.TransformPoint(actual[i]));sample.meanSimulationOffset+=offset;sample.maxSimulationOffset=Mathf.Max(sample.maxSimulationOffset,offset);
    }
   }
   Destroy(mesh);foreach(var sample in result)sample.meanSimulationOffset/=Mathf.Max(1,sample.vertices);
#endif
   return result;
  }
 }
}
