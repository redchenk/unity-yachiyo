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
   int desktop=Array.IndexOf(args,"--verify-desktop-switch");if(desktop>=0&&desktop+1<args.Length){yield return Desktop(ui,avatar,args[desktop+1]);yield break;}
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
  [Serializable]class DesktopReport {public bool passed,windowsConnected,windowsGenerated,switchStopped,desktopConnected,desktopGenerated,noLateMotion,windowsReconnected,desktopReconnected;public int desktopChunks;public string savedBackend,status;}
  static IEnumerator Desktop(StudioUI ui,AvatarMotion avatar,string path){
   // The director's initial ownership handshake clears pending motion. Begin
   // only after that handshake, just as a user does after the ready indicator.
   var bridge=ui.GetComponent<DirectorBridge>();float readyAt=Time.realtimeSinceStartup+20;
   while(!bridge.Connected&&Time.realtimeSinceStartup<readyAt)yield return null;
   yield return new WaitForSeconds(1);var r=new DesktopReport();
   ui.SelectBackend("windows",false);yield return ui.ConnectSelectedService(false);r.windowsConnected=ui.ServiceConnected;
   yield return new WaitForSeconds(1);
   ui.RequestMotion("挥手");float deadline=Time.realtimeSinceStartup+30;
   while(!avatar.Streaming&&Time.realtimeSinceStartup<deadline)yield return null;
   r.windowsGenerated=avatar.Streaming;yield return new WaitForSeconds(2);
   ui.RequestMotion("向前走");yield return null;ui.SelectBackend("desktop",false);r.switchStopped=!avatar.Streaming&&!avatar.Playing;
   yield return ui.ConnectSelectedService(false);r.desktopConnected=ui.ServiceConnected&&ui.serviceUrl=="http://127.0.0.1:28787";
   yield return new WaitForSeconds(2);r.noLateMotion=!avatar.Playing&&!avatar.Streaming;
   int chunks=ui.RemoteChunks;ui.RequestMotion("挥手");deadline=Time.realtimeSinceStartup+30;
   while(ui.RemoteChunks<chunks+3&&Time.realtimeSinceStartup<deadline)yield return null;
   r.desktopChunks=ui.RemoteChunks-chunks;r.desktopGenerated=avatar.Streaming&&r.desktopChunks>=3;
   ui.SelectBackend("windows",false);yield return ui.ConnectSelectedService(false);r.windowsReconnected=ui.ServiceConnected;
   ui.SelectBackend("desktop",false);yield return ui.ConnectSelectedService(false);r.desktopReconnected=ui.ServiceConnected;
   r.savedBackend=PlayerPrefs.GetString("motion-backend");r.status=ui.ServiceStatus;
   r.passed=r.windowsConnected&&r.windowsGenerated&&r.switchStopped&&r.desktopConnected&&r.desktopGenerated&&r.noLateMotion&&r.windowsReconnected&&r.desktopReconnected&&r.savedBackend=="desktop";
   File.WriteAllText(path+".json",JsonUtility.ToJson(r,true));UnityEngine.Debug.Log("DESKTOP_SWITCH_VERIFIED "+JsonUtility.ToJson(r));Application.Quit();
  }
 }
}
