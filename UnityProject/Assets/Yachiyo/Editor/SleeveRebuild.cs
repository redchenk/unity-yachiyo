using UnityEngine;
using UnityEditor;
namespace Yachiyo.Editor {
 public static class SleeveRebuild {
  public static void Build(){
   System.IO.Directory.CreateDirectory("Assets/Yachiyo/Resources");
   var shader=Shader.Find("Yachiyo/ContinuousSleeve");if(!shader)throw new System.Exception("Sleeve shader missing");
   const string path="Assets/Yachiyo/Resources/SleeveSurface.mat";
   var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}else mat.shader=shader;
   mat.SetColor("_Outer",new Color(68f/255,65f/255,110f/255,1));
   EditorUtility.SetDirty(mat);AssetDatabase.SaveAssets();BuildStudio.BuildMac();
  }
 }
}
