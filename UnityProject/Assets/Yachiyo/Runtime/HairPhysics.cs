using System.Collections.Generic;
using UnityEngine;

namespace Yachiyo {
 // Lightweight spring-bone hair layer for the authored twintail / side hair /
 // front hair accessory chains. The body clip only writes humanoid bones, so
 // these chains stay free for secondary motion. Runs after AvatarMotion and
 // before the face layer.
 [DefaultExecutionOrder(300)]
 public sealed class HairPhysics : MonoBehaviour {
  public float maxAngle=30f, gravity=1.2f;
  Transform head; Quaternion previousHeadRotation; Vector3 previousHeadPosition; bool ready;
  sealed class Chain {
   public Transform[] bones; public Quaternion[] rest; public Vector3[] offset; public Vector3[] velocity;
   public float phase, stiffness, damping;
  }
  readonly List<Chain> chains=new List<Chain>();

  void Start()=>Initialize();
  public void Initialize(){
   if(ready)return;
   var animator=GetComponent<Animator>();
   head=animator?animator.GetBoneTransform(HumanBodyBones.Head):null;
   if(!head)return;
   var groups=new Dictionary<Transform,List<Transform>>();
   foreach(var t in GetComponentsInChildren<Transform>(true)){
    if(t==head)continue;
    string name=t.name;
    if(!IsHair(name))continue;
    var root=t;
    while(root.parent&&root.parent!=head&&IsHair(root.parent.name))root=root.parent;
    if(!groups.TryGetValue(root,out var list)){list=new List<Transform>();groups[root]=list;}
    list.Add(t);
   }
   foreach(var pair in groups){
    var list=pair.Value;
    if(list.Count==0)continue;
    list.Sort((a,b)=>Depth(a).CompareTo(Depth(b)));
    var chain=new Chain{bones=list.ToArray(),rest=new Quaternion[list.Count],offset=new Vector3[list.Count],velocity=new Vector3[list.Count],phase=Random.value*Mathf.PI*2f};
    for(int i=0;i<chain.bones.Length;i++)chain.rest[i]=chain.bones[i].localRotation;
    bool twintail=chain.bones[0].name.Contains("Twintail");
    chain.stiffness=twintail?52f:88f;
    chain.damping=twintail?7.5f:11f;
    chains.Add(chain);
   }
   previousHeadRotation=head.rotation;previousHeadPosition=head.position;ready=true;
   int bones=0;foreach(var c in chains)bones+=c.bones.Length;
   Debug.Log("HAIR_PHYSICS chains="+chains.Count+" bones="+bones);
  }

  void LateUpdate(){
   if(!ready){Initialize();if(!ready)return;}
   float dt=Mathf.Clamp(Time.deltaTime,.001f,.05f);
   var inv=Quaternion.Inverse(previousHeadRotation);
   var delta=head.rotation*inv;
   delta.ToAngleAxis(out float angle,out Vector3 axis);
   if(angle>180f)angle-=360f;
   Vector3 worldAngular=axis*(angle/Mathf.Max(dt,.001f));
   Vector3 localAngular=Quaternion.Inverse(head.rotation)*worldAngular;
   float jump=Vector3.Distance(head.position,previousHeadPosition);
   if(jump>.5f||Mathf.Abs(angle)>70f)for(int c=0;c<chains.Count;c++){for(int i=0;i<chains[c].bones.Length;i++){chains[c].offset[i]=Vector3.zero;chains[c].velocity[i]=Vector3.zero;}}
   for(int c=0;c<chains.Count;c++){
    var chain=chains[c];
    for(int i=0;i<chain.bones.Length;i++){
     float depth=chain.bones.Length<=1?0f:i/(float)(chain.bones.Length-1);
     float gain=.55f+depth*.65f;
     var target=new Vector3(
      Mathf.Clamp(-localAngular.x*.028f*gain,-maxAngle,maxAngle),
      Mathf.Clamp(-localAngular.y*.020f*gain,-maxAngle,maxAngle),
      Mathf.Clamp(-localAngular.z*.016f*gain,-maxAngle,maxAngle));
     target.x+=gravity*(.5f+depth);
     target.z+=Mathf.Sin(Time.time*1.15f+chain.phase+i*.35f)*1.1f;
     target.y+=Mathf.Cos(Time.time*.85f+chain.phase+i*.28f)*.7f;
     var v=chain.velocity[i];
     v+=(target-chain.offset[i])*(chain.stiffness*dt);
     v*=Mathf.Exp(-chain.damping*dt);
     chain.velocity[i]=v;
     chain.offset[i]=Vector3.ClampMagnitude(chain.offset[i]+v*dt,maxAngle);
     chain.bones[i].localRotation=chain.rest[i]*Quaternion.Euler(chain.offset[i]);
    }
   }
   previousHeadRotation=head.rotation;previousHeadPosition=head.position;
  }

  static bool IsHair(string name)=>name.Contains("Twintail")||name.Contains("SideHair")||name.Contains("FrontHair");
  static int Depth(Transform t){int d=0;while(t.parent){d++;t=t.parent;}return d;}
 }
}
