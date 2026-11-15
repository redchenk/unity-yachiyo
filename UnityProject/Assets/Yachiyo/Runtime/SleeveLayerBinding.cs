using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Yachiyo {
 public sealed partial class FullCharacterCloth {
#if YACHIYO_MAGICA
  bool[] sleeveShellMask;float[] sleeveCuffOverlap;
  void BindSleeveOverlap(Vector3[] vertices,Vector3[] normals,int[] triangles,List<int[]> panels,BoneWeight[] weights){
   // The short cuff has a black backing beneath the long purple shell. Their
   // independently blended animation weights let that backing overtake the
   // shell on elbow bends. Bind only the overlap to the same surface weights.
   var ordered=panels.OrderBy(p=>p.Min(t=>Mathf.Min(vertices[triangles[t]].z,Mathf.Min(vertices[triangles[t+1]].z,vertices[triangles[t+2]].z)))).ToArray();
   var shell=ordered[0];var cuff=ordered[1].SelectMany(t=>new[]{triangles[t],triangles[t+1],triangles[t+2]}).Distinct();var before=weights.ToArray();int bound=0;
   foreach(int t in shell)for(int j=0;j<3;j++)sleeveShellMask[triangles[t+j]]=true;
   foreach(int i in cuff){
    if(!furisodeMask[i]||sleeveFixedMask[i])continue;
    float closest=.025f*.025f;int face=-1;Vector3 bary=Vector3.zero;
    foreach(int t in shell){int a=triangles[t],b=triangles[t+1],c=triangles[t+2];if(Vector3.Dot(normals[i],normals[a]+normals[b]+normals[c])<.5f)continue;
     var uvw=TriangleCoordinates(vertices[i],vertices[a],vertices[b],vertices[c]);var point=vertices[a]*uvw.x+vertices[b]*uvw.y+vertices[c]*uvw.z;float d=(point-vertices[i]).sqrMagnitude;
     if(d<closest){closest=d;face=t;bary=uvw;}
    }
    if(face<0)continue;float blend=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.012f,.025f,Mathf.Sqrt(closest)));sleeveCuffOverlap[i]=blend;
    var sum=new Dictionary<int,float>();
    void Add(BoneWeight w,float scale){foreach(var p in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)})if(p.Item2*scale>0)sum[p.Item1]=(sum.TryGetValue(p.Item1,out var old)?old:0)+p.Item2*scale;}
    Add(before[i],1-blend);Add(before[triangles[face]],bary.x*blend);Add(before[triangles[face+1]],bary.y*blend);Add(before[triangles[face+2]],bary.z*blend);
    var best=sum.OrderByDescending(p=>p.Value).Take(4).ToArray();float total=best.Sum(p=>p.Value);var wnew=new BoneWeight();
    if(best.Length>0){wnew.boneIndex0=best[0].Key;wnew.weight0=best[0].Value/total;}if(best.Length>1){wnew.boneIndex1=best[1].Key;wnew.weight1=best[1].Value/total;}if(best.Length>2){wnew.boneIndex2=best[2].Key;wnew.weight2=best[2].Value/total;}if(best.Length>3){wnew.boneIndex3=best[3].Key;wnew.weight3=best[3].Value/total;}weights[i]=wnew;bound++;
   }
   Debug.Log("SLEEVE_OVERLAP bound="+bound);
  }
  static Vector3 TriangleCoordinates(Vector3 p,Vector3 a,Vector3 b,Vector3 c){
   var ab=b-a;var ac=c-a;if(Vector3.Cross(ab,ac).sqrMagnitude<1e-14f)return new Vector3(1,0,0);var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);if(d1<=0&&d2<=0)return new Vector3(1,0,0);
   var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return new Vector3(0,1,0);
   float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0){float v=d1/(d1-d3);return new Vector3(1-v,v,0);}
   var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return new Vector3(0,0,1);
   float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0){float w=d2/(d2-d6);return new Vector3(1-w,0,w);}
   float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0){float w=(d4-d3)/((d4-d3)+(d5-d6));return new Vector3(0,1-w,w);}
   float denominator=va+vb+vc;if(Mathf.Abs(denominator)<1e-12f)return new Vector3(1,0,0);float y=vb/denominator,z=vc/denominator;return new Vector3(1-y-z,y,z);
  }
#endif
 }
}
