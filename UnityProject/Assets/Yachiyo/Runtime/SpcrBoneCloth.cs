using System;
using System.Collections.Generic;
using System.Linq;
using SPCR;
using UnityEngine;

namespace Yachiyo {
 // SPCR owns proxy particles only. Original skin bones receive rotations from
 // AuthoredSleevePhysics, never the solver's stretchy particle translations.
 public sealed class SpcrBoneCloth {
  sealed class Row {
   public Transform anchor;public Transform[] source,proxy;
   public Vector3 previous,target;public Quaternion previousFrame,targetFrame;
  }
  readonly List<Row> rows=new List<Row>();
  readonly SPCRJointDynamicsController[] controllers;
  readonly Transform container;
  readonly Dictionary<Transform,Transform> lookup=new Dictionary<Transform,Transform>();
  sealed class Contact {
   public CapsuleCollider source;public SPCRJointDynamicsCollider proxy;
   public Vector3 previous,target;public Quaternion previousFrame,targetFrame;
  }
  readonly List<Contact> contacts=new List<Contact>();
  public int Links {get;private set;}
  public SpcrBoneCloth(Transform owner,IEnumerable<Transform[]> chains){
   container=new GameObject("SPCR sleeve simulation (runtime only)").transform;container.SetParent(owner,false);container.gameObject.SetActive(false);
   foreach(var source in owner.GetComponent<GarmentPhysics>().bodyCapsules){
    var t=new GameObject("SPCR contact "+source.name).transform;t.SetParent(container,false);
    var collider=t.gameObject.AddComponent<SPCRJointDynamicsCollider>();
    float scale=source.transform.lossyScale.x;collider._Radius=source.radius*scale;collider._Height=Mathf.Max(0,source.height*scale-2*collider._Radius);collider._Friction=.12f;
    var contact=new Contact{source=source,proxy=collider};CaptureContact(contact);contact.previous=contact.target;contact.previousFrame=contact.targetFrame;t.SetPositionAndRotation(contact.target,contact.targetFrame);contacts.Add(contact);
   }
   var groups=new List<SPCRJointDynamicsController>();
   foreach(string side in new[]{"Left","Right"})foreach(bool hanging in new[]{false,true}){
    var selected=chains.Where(c=>c[0].name.EndsWith(side,StringComparison.Ordinal)&&c[0].name.Contains("CenterSleeve")==hanging).OrderBy(c=>c[0].name,StringComparer.Ordinal).ToArray();
    if(selected.Length==0)continue;
    var host=new GameObject(side+(hanging?" hanging panel":" open A-E sleeve"));host.transform.SetParent(container,false);
    var controller=host.AddComponent<SPCRJointDynamicsController>();controller.ExternalUpdate=true;controller.Name=host.name;controller._RootTransform=owner;
    controller._IsLoopRootPoints=false;controller._IsComputeShear=true;controller._IsComputeBendingVertical=true;controller._IsComputeBendingHorizontal=true;
    controller._StabilizationFrameRate=119;controller._SubSteps=1;controller._Relaxation=4;
    controller._ColliderTbl=contacts.Select(c=>c.proxy).ToArray();
    controller._RootSlideLimit=-1;controller._RootRotateLimit=-1;controller._Gravity=new Vector3(0,hanging?-9.8f:-2,0);
    controller._ResistanceCurve=AnimationCurve.Linear(0,7,1,5);
    controller._HardnessCurve=AnimationCurve.Linear(0,hanging?.6f:8,1,hanging?.15f:3);
    controller._AllShrinkScaleCurve=AnimationCurve.Linear(0,.8f,1,.8f);controller._AllStretchScaleCurve=AnimationCurve.Linear(0,.8f,1,.8f);
    controller._StructuralShrinkVertical=controller._StructuralStretchVertical=1;
    controller._IsAllShearShrink=controller._IsAllShearStretch=false;controller._ShearShrink=controller._ShearStretch=.3f;
    controller._IsAllBendingShrinkVertical=controller._IsAllBendingStretchVertical=false;controller._BendingShrinkVertical=controller._BendingStretchVertical=.08f;
    controller._IsAllBendingShrinkHorizontal=controller._IsAllBendingStretchHorizontal=false;controller._BendingShrinkHorizontal=controller._BendingStretchHorizontal=.12f;
    var roots=new List<SPCRJointDynamicsPoint>();
    foreach(var source in selected){
     var anchor=new GameObject(source[0].name+" anchor").transform;anchor.SetParent(host.transform,false);anchor.SetPositionAndRotation(source[0].position,source[0].parent.rotation);
     var row=new Row{source=source,anchor=anchor,proxy=new Transform[source.Length],previous=source[0].position,target=source[0].position,previousFrame=anchor.rotation,targetFrame=anchor.rotation};
     for(int i=0;i<source.Length;i++){
      var p=new GameObject("particle "+i).transform;p.SetParent(i==0?anchor:row.proxy[i-1],false);p.SetPositionAndRotation(source[i].position,source[i].rotation);
      var point=p.gameObject.AddComponent<SPCRJointDynamicsPoint>();point._IsFixed=i==0;point._PointRadius=.008f;point._Mass=1;
      // OnEnable is delayed until all rest values and constraints exist.
      point._LocalScale=p.localScale;point._LocalPosition=p.localPosition;point._LocalRotation=p.localRotation;
      row.proxy[i]=p;lookup[source[i]]=p;if(i==0)roots.Add(point);
     }
     rows.Add(row);
    }
    controller._RootPointTbl=roots.ToArray();controller.UpdateJointConnection();
    Links+=controller.ConstraintsStructuralHorizontal.Length+controller.ConstraintsShear.Length+controller.ConstraintsBendingHorizontal.Length;
    groups.Add(controller);
   }
   controllers=groups.ToArray();container.gameObject.SetActive(true);
  }
  public Quaternion LocalRotation(Transform bone)=>lookup[bone].localRotation;
  static void CaptureContact(Contact c){c.target=c.source.transform.TransformPoint(c.source.center);var axis=c.source.direction==0?Vector3.right:c.source.direction==2?Vector3.forward:Vector3.up;c.targetFrame=c.source.transform.rotation*Quaternion.FromToRotation(Vector3.up,axis);}
  public void Capture(){foreach(var row in rows){row.target=row.source[0].position;row.targetFrame=row.source[0].parent.rotation;}foreach(var c in contacts)CaptureContact(c);}
  public void EndFrame(){foreach(var row in rows){row.previous=row.target;row.previousFrame=row.targetFrame;}foreach(var c in contacts){c.previous=c.target;c.previousFrame=c.targetFrame;}}
  public void Step(float blend,float dt){
   foreach(var c in controllers)c.InitializeBonePose();foreach(var c in controllers)c.WaitInitializeBoneJobs();
   foreach(var row in rows)row.anchor.SetPositionAndRotation(Vector3.Lerp(row.previous,row.target,blend),Quaternion.Slerp(row.previousFrame,row.targetFrame,blend));
   foreach(var c in contacts)c.proxy.transform.SetPositionAndRotation(Vector3.Lerp(c.previous,c.target,blend),Quaternion.Slerp(c.previousFrame,c.targetFrame,blend));
   foreach(var c in controllers)c.GetCurrentBoneTransform(dt);foreach(var c in controllers)c.ExecuteSimulation();
   foreach(var c in controllers)c.ApplySimulationToBoneTransform();foreach(var c in controllers)c.WaitBoneTransformJobs();
  }
  public void Reset(){
   foreach(var c in controllers)c.InitializeBonePose();foreach(var c in controllers)c.WaitInitializeBoneJobs();
   foreach(var row in rows){row.previous=row.target;row.previousFrame=row.targetFrame;row.anchor.SetPositionAndRotation(row.target,row.targetFrame);}
   foreach(var c in contacts){c.previous=c.target;c.previousFrame=c.targetFrame;c.proxy.transform.SetPositionAndRotation(c.target,c.targetFrame);}
   foreach(var c in controllers)c.ResetExternalState();
  }
 }
}
