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
