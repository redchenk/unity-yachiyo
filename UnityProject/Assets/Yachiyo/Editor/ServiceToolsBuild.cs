using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Yachiyo.Editor {
 public class ServiceToolsBuild : IPreprocessBuildWithReport,IPostprocessBuildWithReport {
  public int callbackOrder=>10;
  public void OnPreprocessBuild(BuildReport report){
   string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
   string target=Path.Combine(Application.streamingAssetsPath,"ServiceTools");Directory.CreateDirectory(target);
   foreach(string file in new[]{"connect_backend.sh","connect_windows.sh","connect_laptop.sh","windows.command","ubuntu.command"})File.Copy(Path.Combine(root,"tools",file),Path.Combine(target,file),true);
   AssetDatabase.Refresh();
  }
  public void OnPostprocessBuild(BuildReport report){
   if(report.summary.platform!=BuildTarget.StandaloneOSX)return;
   foreach(string path in Directory.GetFiles(report.summary.outputPath,"*.command",SearchOption.AllDirectories))if(path.Contains("ServiceTools"))
    using(var process=Process.Start("/bin/chmod","+x \""+path.Replace("\"","\\\"")+"\""))process.WaitForExit();
  }
 }
}
