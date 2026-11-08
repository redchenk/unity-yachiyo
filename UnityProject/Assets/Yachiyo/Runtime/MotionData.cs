using System;
using UnityEngine;

namespace Yachiyo {
 [Serializable] public class MotionFrame {
  public Vector3 root;
  public Quaternion[] rotations;
  public float[] contacts;
 }
 [Serializable] public class MotionClip {
  public int version;
  public string source, prompt, coordinates;
  public float fps, referenceHipHeight;
  public float generationSeconds;
  public int sequence,revision;
  public string[] bones;
  public MotionFrame[] frames;
  public float Duration => frames == null || fps <= 0 ? 0 : (frames.Length - 1) / fps;
  public void Validate() {
   if(version!=1 || coordinates!="unity-lh-y-up-global-tpose-delta") throw new ArgumentException("Unsupported motion format");
   if(fps<=0 || fps>240 || referenceHipHeight<0.1f || referenceHipHeight>3 || bones==null || bones.Length==0 || frames==null || frames.Length<2 || frames.Length>24000) throw new ArgumentException("Invalid motion header");
   foreach(var f in frames) {
    if(f.rotations==null || f.rotations.Length!=bones.Length || !Finite(f.root.x)||!Finite(f.root.y)||!Finite(f.root.z)) throw new ArgumentException("Invalid motion frame");
    for(int i=0;i<f.rotations.Length;i++) {var q=f.rotations[i];float n=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;if(!Finite(n)||n<0.5f||n>1.5f)throw new ArgumentException("Invalid rotation");f.rotations[i]=q.normalized;}
   }
  }
  static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
 }
 [Serializable] public class GenerationRequest { public string prompt; public float duration=4; public int seed=7; public int steps=30; }
 [Serializable] public class GenerationJob { public string id, status, error; public bool cached; }
 [Serializable] public class ServiceHealth { public string provider, state, model; public bool cuda,streaming; }
 [Serializable] public class MotionSession {public string session;public int sequence;}
 [Serializable] public class StreamRequest {public string prompt;public int sequence,revision;}
}
