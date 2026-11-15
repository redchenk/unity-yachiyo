using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
namespace Yachiyo.Editor {
 [InitializeOnLoad]public static class MagicaBuildSupport {
  const string Symbol="YACHIYO_MAGICA";
  static MagicaBuildSupport(){EditorApplication.delayCall+=Configure;}
  [MenuItem("Yachiyo/Configure Licensed Magica Integration")]
  public static void Configure(){
   bool available=AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetType("MagicaCloth2.MagicaCloth")!=null);
   var target=NamedBuildTarget.Standalone;
   var symbols=PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(s=>s.Length>0&&s!=Symbol).ToList();
   if(available)symbols.Add(Symbol);
   var value=string.Join(";",symbols);
   if(value!=PlayerSettings.GetScriptingDefineSymbols(target))PlayerSettings.SetScriptingDefineSymbols(target,value);
  }
 }
}
