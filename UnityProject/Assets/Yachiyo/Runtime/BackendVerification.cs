using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Yachiyo {
 public static class BackendVerification {
  [Serializable]class Report {
   public bool windowsConnected,windowsGenerated,switchStoppedOldMotion,ubuntuSelected,ubuntuOfflineReported,noLateWindowsMotion,windowsReconnected,windowsGeneratedAgain;
   public int windowsChunks,extraChunksAfterSwitch;public string selected,savedBackend,status;
  }
  public static IEnumerator Run(StudioUI ui,AvatarMotion avatar,string[] args){
   int p=Array.IndexOf(args,"--verify-backend-switch");if(p<0||p+1>=args.Length)yield break;string path=args[p+1];
   yield return new WaitForSeconds(2);var result=new Report();avatar.followPointer=false;
   ui.SelectBackend("windows",false);yield return ui.ConnectSelectedService(false);result.windowsConnected=ui.ServiceConnected;
   ui.RequestMotion("挥手");float deadline=Time.realtimeSinceStartup+30;
   while(!avatar.Streaming&&Time.realtimeSinceStartup<deadline)yield return null;
   result.windowsGenerated=avatar.Streaming;yield return new WaitForSeconds(2);
   result.windowsChunks=ui.RemoteChunks;ScreenCapture.CaptureScreenshot(path+"-windows.png");yield return new WaitForEndOfFrame();
   ui.RequestMotion("向前走");yield return null;ui.SelectBackend("ubuntu",false);
   result.switchStoppedOldMotion=!avatar.Streaming&&!avatar.Playing;
   result.ubuntuSelected=ui.SelectedBackend=="ubuntu"&&ui.serviceUrl=="http://127.0.0.1:8787";
   yield return ui.ConnectSelectedService(false);result.ubuntuOfflineReported=!ui.ServiceConnected&&ui.ServiceStatus.Contains("Ubuntu");
   yield return new WaitForSeconds(3);result.extraChunksAfterSwitch=ui.RemoteChunks-result.windowsChunks;result.noLateWindowsMotion=!avatar.Playing&&!avatar.Streaming&&result.extraChunksAfterSwitch==0;
   ScreenCapture.CaptureScreenshot(path+"-ubuntu-offline.png");yield return new WaitForEndOfFrame();
   ui.SelectBackend("windows",false);yield return ui.ConnectSelectedService(false);result.windowsReconnected=ui.ServiceConnected;
   ui.RequestMotion("站着");deadline=Time.realtimeSinceStartup+30;while(!avatar.Streaming&&Time.realtimeSinceStartup<deadline)yield return null;
   result.windowsGeneratedAgain=avatar.Streaming;ui.StopMotion();
   result.selected=ui.SelectedBackend;result.savedBackend=PlayerPrefs.GetString("motion-backend");result.status=ui.ServiceStatus;
   File.WriteAllText(path+".json",JsonUtility.ToJson(result,true));UnityEngine.Debug.Log("BACKEND_SWITCH_VERIFICATION_DONE "+path);
  }
 }
}
