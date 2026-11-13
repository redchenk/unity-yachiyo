using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;

namespace Yachiyo {
 // Explicit command-line diagnostic only; no recording or file IO in normal use.
 public sealed class RenderPerformanceProbe : MonoBehaviour {
  [Serializable]class Metric {public double mean,p50,p95,p99;}
  [Serializable]class Report {
   public string device,gpu,unity,action;public int width,height,frames,clothPanels,clothVertices;
   public double fps,seconds;public bool focused,clothFinite;public int underruns;
   public Metric frameMs,cpuMs,renderMs,gpuMs,clothTransferMs,gcBytes;
  }
  string output,action;double start,previous;bool sampling;int focused;
  AvatarMotion avatar;GarmentPhysics cloth;
  ProfilerRecorder cpu,render,gpu,gc;
  readonly List<double> frames=new List<double>(2400),cpus=new List<double>(2400),renders=new List<double>(2400),gpus=new List<double>(2400),transfers=new List<double>(2400),allocations=new List<double>(2400);
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Install(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--profile-render");if(i>=0&&i+1<args.Length){var p=new GameObject("Render performance probe").AddComponent<RenderPerformanceProbe>();p.output=args[i+1];int a=Array.IndexOf(args,"--profile-action");p.action=a>=0&&a+1<args.Length?args[a+1]:"idle";}}
  void Start(){
   avatar=FindFirstObjectByType<AvatarMotion>();cloth=avatar.GetComponent<GarmentPhysics>();
   cpu=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"CPU Main Thread Frame Time");render=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"CPU Render Thread Frame Time");gpu=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GPU Frame Time");gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame");
   start=Time.realtimeSinceStartupAsDouble;
  }
  void LateUpdate(){
   double now=Time.realtimeSinceStartupAsDouble;
   if(!sampling){if(now-start<8)return;sampling=true;start=previous=now;if(action=="sample"){var clip=JsonUtility.FromJson<MotionClip>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Motions/kimodo-official.json")));avatar.Play(clip,"Profile sample");}return;}
   frames.Add((now-previous)*1000);previous=now;if(Application.isFocused)focused++;
   if(cpu.Valid)cpus.Add(cpu.LastValue/1e6);if(render.Valid)renders.Add(render.LastValue/1e6);if(gpu.Valid)gpus.Add(gpu.LastValue/1e6);if(gc.Valid)allocations.Add(gc.LastValue);
   transfers.Add(cloth.LastTransferMs);
   if(now-start<20)return;
   var report=new Report{device=SystemInfo.deviceModel,gpu=SystemInfo.graphicsDeviceName,unity=Application.unityVersion,action=action,width=Screen.width,height=Screen.height,frames=frames.Count,seconds=now-start,fps=frames.Count/(now-start),focused=focused==frames.Count,clothFinite=cloth.Native.Finite,clothPanels=cloth.Native.PanelCount,clothVertices=cloth.Native.VertexCount,underruns=avatar.SpeechMotionUnderruns,frameMs=Stats(frames),cpuMs=Stats(cpus),renderMs=Stats(renders),gpuMs=Stats(gpus),clothTransferMs=Stats(transfers),gcBytes=Stats(allocations)};
   File.WriteAllText(output+".json",JsonUtility.ToJson(report,true));ScreenCapture.CaptureScreenshot(output+".png");Debug.Log("RENDER_PROFILE "+JsonUtility.ToJson(report));enabled=false;Invoke(nameof(Finish),.5f);
  }
  static Metric Stats(List<double> values){if(values.Count==0)return new Metric();values.Sort();return new Metric{mean=values.Average(),p50=values[(int)((values.Count-1)*.5)],p95=values[(int)((values.Count-1)*.95)],p99=values[(int)((values.Count-1)*.99)]};}
  void Finish()=>Application.Quit();
  void OnDestroy(){cpu.Dispose();render.Dispose();gpu.Dispose();gc.Dispose();}
 }
}
