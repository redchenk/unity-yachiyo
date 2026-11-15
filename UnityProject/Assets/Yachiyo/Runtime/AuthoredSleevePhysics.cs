using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yachiyo {
 // SPCR drives coupled proxy chains at 120 Hz; bounded rotation transfer
 // preserves the complete original skin, bone lengths and shoulder attachments.
 // The previous spring solver remains an explicit command-line A/B baseline.
 public sealed class AuthoredSleevePhysics {
  sealed class Sleeve {
   public bool hanging;
   public Transform[] bones;
   public Quaternion[] rest;
   public Vector3[] offset,velocity,gravityTarget;
   public Vector3 previousPosition;
   public Quaternion previousFrame;
   public Probe[] probes;
   public Vector3[] previousStepOffset,localOffsets,points,restPoints;
   public Quaternion[] rotations;
   public Vector3 samplePosition,targetPosition,lastStepPosition,anchorVelocity;
   public Quaternion sampleFrame,targetFrame,lastStepFrame;
  }
  sealed class Probe {
   public int joint;
   public readonly Transform[] bones=new Transform[4];
   public readonly Vector3[] local=new Vector3[4];
   public readonly float[] weight=new float[4];
   public Vector3 World(){
    var point=Vector3.zero;
    for(int i=0;i<4;i++){
     if(weight[i]>0)point+=bones[i].TransformPoint(local[i])*weight[i];
    }
    return point;
   }
  }
  readonly List<Sleeve> sleeves=new List<Sleeve>();
  sealed class Weave {public Sleeve a,b;public int ai,bi;public float length,strength;}
  readonly List<Weave> weave=new List<Weave>();
  readonly Dictionary<Transform,(Sleeve sleeve,int index)> simulated=new Dictionary<Transform,(Sleeve,int)>();
  const float Step=1f/120;
  float remainder;
  readonly GarmentPhysics owner;
  Transform obi;
  Vector3 obiCenter,obiRadii;
  Quaternion obiFrame;
  readonly bool rigid,legacy;
  readonly SpcrBoneCloth spcr;
  public string Solver=>spcr!=null?"SPCRJointDynamics":"legacy springs";
  bool reset=true;
  public bool Available=>sleeves.Count==12;
  public bool Finite {get;private set;}=true;
  public int PanelCount=>sleeves.Count/6;
  public int ChainCount=>sleeves.Count;
  public int ProbeCount {get;private set;}
  public float MaxLengthError {get;private set;}
  public float MaxJointAngle {get;private set;}
  public int LateralLinks=>spcr!=null?spcr.Links:weave.Count;
  public float MaxLateralStretch {get;private set;}
  public float MaxAngularSpeed {get;private set;}
  public int LastSubsteps {get;private set;}

  public AuthoredSleevePhysics(GarmentPhysics owner){
   this.owner=owner;
   rigid=Array.IndexOf(Environment.GetCommandLineArgs(),"--sleeve-rigid")>=0;
   legacy=Array.IndexOf(Environment.GetCommandLineArgs(),"--sleeve-legacy")>=0;
   var all=owner.GetComponentsInChildren<Transform>();
   SkinnedMeshRenderer source=null;
   foreach(var renderer in owner.GetComponentsInChildren<SkinnedMeshRenderer>()){
    if(renderer.name=="cloth1"){
     source=renderer;
     break;
    }
   }
   if(!source)return;
   CreateAdornmentContact(source);
   foreach(string side in new[]{"Left","Right"}){
    var roots=new List<string>{"Acs_CenterSleeve_Root_"+side};
    foreach(char row in "ABCDE")roots.Add("Acs_ArmSleeve_"+row+"001_"+side);
    foreach(string name in roots){
    bool hanging=name.Contains("CenterSleeve");
    string prefix=hanging?"Acs_CenterSleeve":name.Replace("001_"+side,"");
    Transform root=null;
    // The PhysBone settings containers have duplicate names and no children.
    foreach(var candidate in all){
     if(candidate.name==name&&candidate.childCount>0){
      root=candidate;
      break;
     }
    }
    if(!root)continue;
    var nodes=new List<Transform>{root};
    var current=root;
    while(current.childCount>0){
     Transform next=null;
     for(int i=0;i<current.childCount;i++){
      var child=current.GetChild(i);
      if(child.name.StartsWith(prefix,StringComparison.Ordinal)){
       next=child;
       break;
      }
     }
     if(!next)break;
     nodes.Add(next);
     current=next;
    }
    if(nodes.Count<2)continue;
    var sleeve=new Sleeve{
     hanging=hanging,
     bones=nodes.ToArray(),
     rest=new Quaternion[nodes.Count],
     offset=new Vector3[nodes.Count-1],
     velocity=new Vector3[nodes.Count-1],
     gravityTarget=new Vector3[nodes.Count-1],
     previousPosition=root.position,
     previousFrame=root.parent.rotation
    };
    sleeve.previousStepOffset=new Vector3[nodes.Count-1];sleeve.localOffsets=new Vector3[nodes.Count];sleeve.points=new Vector3[nodes.Count];sleeve.restPoints=new Vector3[nodes.Count];sleeve.rotations=new Quaternion[nodes.Count];
    for(int i=0;i<nodes.Count;i++){
     sleeve.rest[i]=nodes[i].localRotation;simulated[nodes[i]]=(sleeve,i);
     if(i>0)sleeve.localOffsets[i]=Quaternion.Inverse(nodes[i-1].rotation)*(nodes[i].position-nodes[i-1].position);
    }
    sleeve.probes=CreateProbes(source,side,prefix,hanging?48:8);
    ProbeCount+=sleeve.probes.Length;
    sleeves.Add(sleeve);
    }
   }
   // The original weights blend A-B, B-C, C-D, D-E, but not A-E. Keep
   // that open strip instead of sewing the cuff shut with a loop constraint.
   for(int side=0;side<2;side++)for(int column=1;column<5;column++){
    if(sleeves.Count!=12)break;
    var a=sleeves[side*6+column];var b=sleeves[side*6+column+1];int rows=Mathf.Min(a.bones.Length,b.bones.Length);
    for(int row=1;row<rows;row++){
     AddWeave(a,row,b,row,.65f);
     if(row+1<rows){AddWeave(a,row,b,row+1,.18f);AddWeave(a,row+1,b,row,.18f);}
    }
   }
   if(Available&&!legacy&&!rigid)spcr=new SpcrBoneCloth(owner.transform,sleeves.ConvertAll(s=>s.bones));
   Debug.Log("ORIGINAL_SLEEVE_RIG chains="+sleeves.Count+" cloth links="+LateralLinks+" contact probes="+ProbeCount+" solver="+Solver);
  }
  void AddWeave(Sleeve a,int ai,Sleeve b,int bi,float strength){weave.Add(new Weave{a=a,ai=ai,b=b,bi=bi,strength=strength,length=Vector3.Distance(a.bones[ai].position,b.bones[bi].position)});}
  static Probe[] CreateProbes(SkinnedMeshRenderer source,string side,string prefix,int count){
   var vertices=source.sharedMesh.vertices;
   var weights=source.sharedMesh.boneWeights;
   var bind=source.sharedMesh.bindposes;
   var candidates=new List<int>();
   for(int i=0;i<weights.Length;i++){
    var w=weights[i];
    float amount=0;
    foreach(var pair in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}){
     string name=source.bones[pair.Item1].name;
     if(name.StartsWith(prefix,StringComparison.Ordinal)&&name.EndsWith(side,StringComparison.Ordinal))amount+=pair.Item2;
    }
    if(amount>.7f)candidates.Add(i);
   }
   var chosen=new List<int>();
   // Distributed support points cover the width and lower corners of the
   // authored panel. Contacts rotate the entire panel instead of its vertices.
   for(int sample=0;sample<count&&candidates.Count>0;sample++){
    int best=candidates[0];
    float maximum=-1;
    foreach(int index in candidates){
     float distance=float.PositiveInfinity;
     foreach(int previous in chosen)distance=Mathf.Min(distance,(vertices[index]-vertices[previous]).sqrMagnitude);
     if(distance>maximum){
      maximum=distance;
      best=index;
     }
    }
    chosen.Add(best);
    candidates.Remove(best);
   }
   var result=new List<Probe>();
   foreach(int index in chosen){
    var w=weights[index];
    var probe=new Probe();
    int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};
    float[] values={w.weight0,w.weight1,w.weight2,w.weight3};
    for(int i=0;i<4;i++){probe.weight[i]=values[i];
     probe.bones[i]=source.bones[ids[i]];
     probe.local[i]=bind[ids[i]].MultiplyPoint3x4(vertices[index]);
    }
    int dominant=0;
    for(int i=1;i<4;i++)if(values[i]>values[dominant])dominant=i;
    string name=source.bones[ids[dominant]].name;
    probe.joint=name.Contains("_003_")?2:name.Contains("_002_")?1:0;
    result.Add(probe);
   }
   return result.ToArray();
  }
  public void Reset()=>reset=true;
  void CreateAdornmentContact(SkinnedMeshRenderer source){
   int index=Array.FindIndex(source.bones,b=>b.name=="Acs_Obi_001");
   if(index<0)return;
   var vertices=source.sharedMesh.vertices;
   var weights=source.sharedMesh.boneWeights;
   var bounds=new Bounds();
   bool found=false;
   for(int i=0;i<vertices.Length;i++){
    var w=weights[i];
    float amount=0;
    if(w.boneIndex0==index)amount+=w.weight0;
    if(w.boneIndex1==index)amount+=w.weight1;
    if(w.boneIndex2==index)amount+=w.weight2;
    if(w.boneIndex3==index)amount+=w.weight3;
    if(amount<.7f)continue;
    var point=owner.transform.InverseTransformPoint(source.transform.TransformPoint(vertices[i]));
    if(found)bounds.Encapsulate(point);
    else{bounds=new Bounds(point,Vector3.zero);
     found=true;
    }
   }
   if(!found)return;
   obi=source.bones[index];
   obiCenter=obi.InverseTransformPoint(owner.transform.TransformPoint(bounds.center));
   obiRadii=bounds.extents*1.15f+Vector3.one*.012f;
   obiFrame=Quaternion.Inverse(obi.rotation)*owner.transform.rotation;
  }
  Vector3 ProjectContact(Vector3 point){
   point=owner.ProjectOriginalSleeveContact(point);
   if(!obi)return point;
   var rotation=obi.rotation*obiFrame;
   var center=obi.TransformPoint(obiCenter);
   var local=Quaternion.Inverse(rotation)*(point-center);
   var normalized=new Vector3(local.x/obiRadii.x,local.y/obiRadii.y,local.z/obiRadii.z);
   float radius=normalized.magnitude;
   if(radius<1){
    // The obi is worn on the front of the torso. Keep overlapping sleeve
    // fabric on its outer surface while retaining the original sleeve width.
    local.z=obiRadii.z*Mathf.Sqrt(Mathf.Max(0,1-normalized.x*normalized.x-normalized.y*normalized.y));
    point=center+rotation*local;
   }
   return point;
  }
  public void Tick(float deltaTime,bool discontinuity,bool enabled){
   if(legacy){LegacyTick(deltaTime,discontinuity,enabled);return;}
   if(!Available)return;
   Finite=true;MaxLengthError=MaxJointAngle=MaxAngularSpeed=MaxLateralStretch=0;LastSubsteps=0;
   float dt=Mathf.Max(0,deltaTime);
   bool snap=reset||discontinuity||!enabled||rigid||dt>.2f;
   foreach(var s in sleeves){
    for(int i=0;i<s.bones.Length;i++)s.bones[i].localRotation=s.rest[i];
    s.targetPosition=s.bones[0].position;s.targetFrame=s.bones[0].parent.rotation;
    snap|=(s.targetPosition-s.previousPosition).sqrMagnitude>.16f||Quaternion.Angle(s.targetFrame,s.previousFrame)>75;
   }
   spcr?.Capture();
   if(snap){
    spcr?.Reset();
    remainder=0;
    foreach(var s in sleeves){
     Array.Clear(s.offset,0,s.offset.Length);Array.Clear(s.velocity,0,s.velocity.Length);Array.Clear(s.previousStepOffset,0,s.previousStepOffset.Length);
     s.previousPosition=s.lastStepPosition=s.samplePosition=s.targetPosition;s.previousFrame=s.lastStepFrame=s.sampleFrame=s.targetFrame;s.anchorVelocity=Vector3.zero;BuildPose(s);
    }
   }else if(enabled&&!rigid){
    float before=remainder;remainder+=dt;float time=Step-before;
    while(remainder+1e-7f>=Step){
     float blend=dt>0?Mathf.Clamp01(time/dt):1;
     spcr?.Step(blend,Step);
     foreach(var s in sleeves){
      Array.Copy(s.offset,s.previousStepOffset,s.offset.Length);
      s.samplePosition=Vector3.Lerp(s.previousPosition,s.targetPosition,blend);s.sampleFrame=Quaternion.Slerp(s.previousFrame,s.targetFrame,blend);
      if(spcr!=null){
       for(int i=0;i<s.offset.Length;i++){s.offset[i]=Vector3.ClampMagnitude(RotationVector(spcr.LocalRotation(s.bones[i])*Quaternion.Inverse(s.rest[i])),JointLimit(s,i));s.gravityTarget[i]=s.offset[i];}
       BuildPose(s);
      }
     }
     // Constraint iterations share the same substep poses. Contact and weave
     // corrections do not teleport render vertices or change bone lengths.
     // SPCR couples the complete A-E strip. A single bounded skin-contact
     // projection follows it; no second dynamics solver owns these bones.
     foreach(var s in sleeves)SolveContacts(s);
     foreach(var s in sleeves){
      for(int i=0;i<s.offset.Length;i++){
       // Diagnostic angular speed; SPCR owns the dynamic state. Skin-contact
       // projections never feed artificial energy into the solver.
       s.velocity[i]=Vector3.ClampMagnitude((s.offset[i]-s.previousStepOffset[i])/Step,220);
       MaxAngularSpeed=Mathf.Max(MaxAngularSpeed,s.velocity[i].magnitude);
      }
      s.lastStepPosition=s.samplePosition;s.lastStepFrame=s.sampleFrame;
     }
     remainder=Mathf.Max(0,remainder-Step);time+=Step;LastSubsteps++;
    }
   }
   float alpha=Mathf.Clamp01(remainder/Step);
   foreach(var s in sleeves){
    for(int i=0;i<s.offset.Length;i++){
     var draw=Vector3.Lerp(s.previousStepOffset[i],s.offset[i],alpha);float angle=draw.magnitude;
     s.bones[i].localRotation=(angle>.0001f?Quaternion.AngleAxis(angle,draw/angle):Quaternion.identity)*s.rest[i];
     Finite&=float.IsFinite(draw.sqrMagnitude)&&float.IsFinite(s.bones[i].position.sqrMagnitude);
     MaxJointAngle=Mathf.Max(MaxJointAngle,angle);
     MaxLengthError=Mathf.Max(MaxLengthError,Mathf.Abs(Vector3.Distance(s.bones[i].position,s.bones[i+1].position)-s.localOffsets[i+1].magnitude));
    }
    s.previousPosition=s.targetPosition;s.previousFrame=s.targetFrame;
   }
   foreach(var link in weave)MaxLateralStretch=Mathf.Max(MaxLateralStretch,Vector3.Distance(link.a.bones[link.ai].position,link.b.bones[link.bi].position)/Mathf.Max(.001f,link.length));
   spcr?.EndFrame();
   reset=false;
  }
  static Vector3 RotationVector(Quaternion q){q.ToAngleAxis(out float angle,out var axis);if(angle>180)angle-=360;return angle>.0001f||angle<-.0001f?axis*angle:Vector3.zero;}
  void BuildPose(Sleeve s){
   Quaternion restFrame=s.sampleFrame;
   for(int i=0;i<s.bones.Length;i++){
    if(i==0){s.points[i]=s.restPoints[i]=s.samplePosition;}
    else{s.points[i]=s.points[i-1]+s.rotations[i-1]*s.localOffsets[i];s.restPoints[i]=s.restPoints[i-1]+restFrame*s.localOffsets[i];}
    restFrame*=s.rest[i];var parent=i==0?s.sampleFrame:s.rotations[i-1];
    var offset=i<s.offset.Length?s.offset[i]:Vector3.zero;float angle=offset.magnitude;
    s.rotations[i]=parent*(angle>.0001f?Quaternion.AngleAxis(angle,offset/angle):Quaternion.identity)*s.rest[i];
   }
  }
  Vector3 ProbeWorld(Probe probe){
   var point=Vector3.zero;
   for(int i=0;i<4;i++)if(probe.weight[i]>0){var bone=probe.bones[i];point+=(simulated.TryGetValue(bone,out var node)?node.sleeve.points[node.index]+node.sleeve.rotations[node.index]*Vector3.Scale(probe.local[i],bone.lossyScale):bone.TransformPoint(probe.local[i]))*probe.weight[i];}
   return point;
  }
  void SolveContacts(Sleeve s){
   for(int joint=0;joint<Mathf.Min(3,s.offset.Length);joint++){
    var correction=Vector3.zero;int contacts=0;
    foreach(var probe in s.probes){
     if(joint>probe.joint+1)continue;
     var point=ProbeWorld(probe);var delta=ProjectContact(point)-point;if(delta.sqrMagnitude<.000004f)continue;
     var lever=point-s.points[joint];correction+=Vector3.Cross(lever,delta)/Mathf.Max(.005f,lever.sqrMagnitude)*Mathf.Rad2Deg;contacts++;
    }
    if(contacts==0)continue;
    var parent=joint==0?s.sampleFrame:s.rotations[joint-1];
    var offset=s.offset[joint]+Quaternion.Inverse(parent)*Vector3.ClampMagnitude(correction/contacts,2.5f);
    var target=s.gravityTarget[joint];offset=target+Vector3.ClampMagnitude(offset-target,s.hanging?22:14);
    s.offset[joint]=Vector3.ClampMagnitude(offset,JointLimit(s,joint));BuildPose(s);
   }
  }
  void LegacyTick(float deltaTime,bool discontinuity,bool enabled){
   if(!Available)return;
   Finite=true;
   MaxLengthError=MaxJointAngle=0;
   float dt=Mathf.Clamp(deltaTime,.001f,.04f);
   foreach(var sleeve in sleeves){
    var root=sleeve.bones[0];
    for(int i=0;i<sleeve.bones.Length;i++)sleeve.bones[i].localRotation=sleeve.rest[i];
    var frame=root.parent.rotation;
    var movement=root.position-sleeve.previousPosition;
    float turn=Quaternion.Angle(sleeve.previousFrame,frame);
    bool snap=reset||discontinuity||!enabled||rigid||movement.sqrMagnitude>.16f||turn>75;
    if(snap){
     Array.Clear(sleeve.offset,0,sleeve.offset.Length);
     Array.Clear(sleeve.velocity,0,sleeve.velocity.Length);
    }
    if(enabled&&!rigid){
     for(int joint=0;joint<sleeve.offset.Length;joint++){
      var bone=sleeve.bones[joint];
      var parent=bone.parent.rotation;
      var restAxis=(sleeve.bones[joint+1].position-bone.position).normalized;
      var gravity=Quaternion.FromToRotation(restAxis,Vector3.down);
      gravity.ToAngleAxis(out float angle,out var axis);
      if(angle>180)angle-=360;
      float influence=sleeve.hanging?(joint==0?.6f:joint==1?.7f:.6f):0;
      float limit=JointLimit(sleeve,joint)-5;
      var target=Quaternion.Inverse(parent)*axis*Mathf.Clamp(angle*influence,-limit,limit);
      sleeve.gravityTarget[joint]=target;
      int steps=Mathf.Max(1,Mathf.CeilToInt(dt*120));
      float step=dt/steps;
      for(int i=0;i<steps;i++){
       sleeve.velocity[joint]+=(target-sleeve.offset[joint])*(75*step);
       sleeve.velocity[joint]*=Mathf.Exp(-13*step);
       sleeve.offset[joint]=Vector3.ClampMagnitude(sleeve.offset[joint]+sleeve.velocity[joint]*step,limit);
      }
      Apply(sleeve,joint);
     }
     // Contact points rotate a shared chain, preserving the connected skin.
     for(int pass=0;pass<4;pass++){
      for(int joint=0;joint<Mathf.Min(3,sleeve.offset.Length);joint++){
      var bone=sleeve.bones[joint];
      var correction=Vector3.zero;
      int contacts=0;
      foreach(var probe in sleeve.probes){
       if(joint>probe.joint+1)continue;
       var point=probe.World();
       var contact=ProjectContact(point);
       var delta=contact-point;
       if(delta.sqrMagnitude<.000004f)continue;
       var lever=point-bone.position;
       correction+=Vector3.Cross(lever,delta)/Mathf.Max(lever.sqrMagnitude,.005f)*Mathf.Rad2Deg;
       contacts++;
      }
      if(contacts==0)continue;
      correction=Vector3.ClampMagnitude(correction/contacts,5);
      var offset=sleeve.offset[joint]+Quaternion.Inverse(bone.parent.rotation)*correction;
      var target=sleeve.gravityTarget[joint];
      offset=target+Vector3.ClampMagnitude(offset-target,sleeve.hanging?12:10);
      sleeve.offset[joint]=Vector3.ClampMagnitude(offset,JointLimit(sleeve,joint));
      Apply(sleeve,joint);
      }
     }
    }
    sleeve.previousPosition=root.position;
    sleeve.previousFrame=frame;
    for(int i=0;i<sleeve.offset.Length;i++){
     Finite&=float.IsFinite(sleeve.offset[i].sqrMagnitude)&&float.IsFinite(sleeve.bones[i].position.sqrMagnitude);
     MaxJointAngle=Mathf.Max(MaxJointAngle,Quaternion.Angle(sleeve.rest[i],sleeve.bones[i].localRotation));
     float length=Vector3.Distance(sleeve.bones[i].position,sleeve.bones[i+1].position);
     MaxLengthError=Mathf.Max(MaxLengthError,Mathf.Abs(length-sleeve.bones[i+1].localPosition.magnitude));
    }
   }
   reset=false;
  }
  static float JointLimit(Sleeve sleeve,int joint)=>sleeve.hanging?(joint==0?45.7f:joint==1?60:joint==2?75:85):10;
  static void Apply(Sleeve sleeve,int joint){
   var offset=sleeve.offset[joint];
   float angle=offset.magnitude;
   sleeve.bones[joint].localRotation=(angle>.0001f?Quaternion.AngleAxis(angle,offset/angle):Quaternion.identity)*sleeve.rest[joint];
  }
 }
}
