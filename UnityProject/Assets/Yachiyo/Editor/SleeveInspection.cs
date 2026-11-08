using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Yachiyo.Editor {
 public static class SleeveInspection {
  [Serializable]class Bone {public int index,parent;public string name;public Vector3 position;public float totalWeight;public int vertices;}
  [Serializable]class Surface {public string name;public Vector3[] vertices;public Vector2[] uv;public int[] triangles;public BoneWeight[] weights;public List<Bone> bones=new List<Bone>();}
  [Serializable]class Report {public List<Surface> surfaces=new List<Surface>();}
  public static void Export(){
   EditorSceneManager.OpenScene("Assets/Yachiyo/Studio.unity");var avatar=UnityEngine.Object.FindFirstObjectByType<AvatarMotion>();var report=new Report();
   foreach(var r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.name=="cloth1"||r.name=="cloth2")){
    var m=r.sharedMesh;var s=new Surface{name=r.name,vertices=m.vertices,uv=m.uv,triangles=m.triangles,weights=m.boneWeights};
    for(int i=0;i<r.bones.Length;i++)s.bones.Add(new Bone{index=i,name=r.bones[i].name,parent=Array.IndexOf(r.bones,r.bones[i].parent),position=r.bones[i].position});
    foreach(var w in m.boneWeights)foreach(var p in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)})if(p.Item2>.001f){s.bones[p.Item1].vertices++;s.bones[p.Item1].totalWeight+=p.Item2;}
    report.surfaces.Add(s);
   }
   File.WriteAllText("../artifacts/sleeve-bind-inspection.json",JsonUtility.ToJson(report));
   avatar.GetComponent<GarmentPhysics>().SendMessage("Initialize");
  }
 }
}
