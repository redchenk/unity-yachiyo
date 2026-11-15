using System;
using System.Linq;
using UnityEngine;
namespace Yachiyo {
 public sealed partial class FullCharacterCloth {
#if YACHIYO_MAGICA
  sealed class SkirtSupport {public Transform root;public Quaternion rest;public Vector3 radial;public Transform[] tails;public Quaternion[] tailRest;}
  SkirtSupport[] skirtSupports=Array.Empty<SkirtSupport>();
  void PrepareSkirtPoseSupport(){
   skirtSupports=Roots(t=>t.name.Contains("Skirt")||t.name.Contains("Sirt")).Select(t=>{var tails=t.Cast<Transform>().Where(c=>c.name.Contains("Skirt")||c.name.Contains("Sirt")).ToArray();return new SkirtSupport{root=t,rest=t.localRotation,radial=Vector3.ProjectOnPlane(owner.hips.InverseTransformPoint(t.position),Vector3.up).normalized,tails=tails,tailRest=tails.Select(c=>c.localRotation).ToArray()};}).ToArray();
  }
  void UpdateSkirtPoseSupport(){
   foreach(var support in skirtSupports){
    support.root.localRotation=support.rest;for(int i=0;i<support.tails.Length;i++)support.tails[i].localRotation=support.tailRest[i];if(!Active)continue;
    Vector3 axis=Vector3.zero;float angle=0;
    foreach(var limb in limbContacts){if(!limb.from.name.StartsWith("UpLeg"))continue;
     var direction=owner.hips.InverseTransformDirection(limb.to.position-limb.from.position).normalized;float lift=Vector3.Angle(Vector3.down,direction);
     var radial=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;float sector=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.05f,.5f,Vector3.Dot(support.radial,radial)));
     var neutral=support.tails.Length>0?owner.hips.InverseTransformDirection(support.tails[0].position-support.root.position).normalized:Vector3.down;
     float authoredFlare=Mathf.Atan2(Vector3.Dot(neutral,radial),-neutral.y)*Mathf.Rad2Deg;
     float amount=Mathf.Max(0,lift+6-authoredFlare)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(12,40,lift))*sector;
     if(amount>angle){angle=amount;axis=Vector3.Cross(Vector3.down,direction).normalized;}
    }
    // Feed a collision-aware reference pose into MeshCloth. Raising the local
    // skirt panel before a thigh crosses it preserves which side of the fabric
    // the leg is on; the solver still owns the free skirt vertices and folds.
    if(angle>0){var lift=Quaternion.AngleAxis(angle,owner.hips.TransformDirection(axis));support.root.rotation=lift*support.root.rotation;
     // Support the proximal panel, not the entire skirt as a rigid flag. The
     // lower authored chain stays near gravity and remains dynamically solved.
     foreach(var tail in support.tails)tail.rotation=Quaternion.Slerp(Quaternion.identity,Quaternion.Inverse(lift),.9f)*tail.rotation;
    }
   }
  }
#endif
 }
}
