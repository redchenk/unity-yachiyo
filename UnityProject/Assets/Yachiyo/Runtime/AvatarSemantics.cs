using UnityEngine;

namespace Yachiyo {
 public partial class AvatarMotion {
  // Sparse, additive corrections over generated motion. Legs, pelvis, root and
  // shoulder attachments remain owned by the model/retargeter. No clip switch.
  public float SemanticCorrectionDegrees {get;private set;}
  void SemanticRotate(HumanBodyBones bone,Vector3 euler){
   float angle=euler.magnitude;if(angle<.001f)return;
   RotateLocal(bone,euler);SemanticCorrectionDegrees=Mathf.Max(SemanticCorrectionDegrees,angle);
  }
  void ApplySemanticCorrection(float arms,float head){
   if(neuralHead&&head>.001f){
    float h=Mathf.Min(.55f,head);
    if(headGesture=="nod"){
     SemanticRotate(HumanBodyBones.Neck,new Vector3(3*h,0,0));
     SemanticRotate(HumanBodyBones.Head,new Vector3(5*h,0,0));
    }else if(headGesture=="shake_head"){
     float turn=Mathf.Sin(Mathf.Clamp01((headClock-headPrepare)/.48f)*Mathf.PI*2)*h;
     SemanticRotate(HumanBodyBones.Neck,new Vector3(0,3*turn,0));
     SemanticRotate(HumanBodyBones.Head,new Vector3(0,6*turn,0));
    }
   }
   if(!neuralGesture||arms<=.001f)return;
   float amount=Mathf.Min(.55f,arms);
   switch(gesture){
    case "explain":case "emphasis":SemanticHand(gestureSide=="left",amount,false);break;
    case "open_palms":SemanticHand(true,amount*.75f,true);SemanticHand(false,amount*.75f,true);break;
    // Small chest attention; no automatic head tilt or root/leg displacement.
    case "lean_in":SemanticRotate(HumanBodyBones.Chest,new Vector3(2*amount,0,0));break;
   }
  }
  float TorsoClearance(Vector3 point){
   if(!bones.TryGetValue(HumanBodyBones.Hips,out var hips)||!bones.TryGetValue(HumanBodyBones.Chest,out var chest))return 1;
   Vector3 a=transform.InverseTransformPoint(hips.position),b=transform.InverseTransformPoint(chest.position),p=transform.InverseTransformPoint(point);
   Vector3 ab=b-a;float t=Mathf.Clamp01(Vector3.Dot(p-a,ab)/Mathf.Max(.0001f,ab.sqrMagnitude));
   // Conservative torso volume used only to reject an added correction, never
   // to push the original generated arm or sleeve outward.
   return Vector3.Distance(p,a+ab*t)-Mathf.Lerp(.14f,.13f,t);
  }
  void SemanticHand(bool left,float amount,bool open){
   var lower=left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm;
   var hand=left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand;
   if(!bones.TryGetValue(lower,out var forearm)||!bones.TryGetValue(hand,out var wrist))return;
   Quaternion before=forearm.localRotation;
   float clearance=TorsoClearance(wrist.position);
   // Bounded wrist presentation and a small forearm lift, keeping the original
   // neural trajectory and elbow location. Never cross deeper into the torso.
   Vector3 delta=new Vector3(-10*amount,0,0);
   RotateLocal(lower,delta);
   float after=TorsoClearance(wrist.position);
   if(after<.025f&&after<clearance)forearm.localRotation=before;
   else SemanticCorrectionDegrees=Mathf.Max(SemanticCorrectionDegrees,delta.magnitude);
   SemanticRotate(hand,new Vector3(0,(left?-1:1)*(open?12:6)*amount,(left?-1:1)*4*amount));
  }
 }
}
