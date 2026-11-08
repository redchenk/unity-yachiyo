"""Local autoregressive DiP inference with its trained DistilBERT encoder.

Public weights: guytevet/CLoSD, DiP_no-target_10steps_context20_predict40.
No gated Llama weights, substitute text vectors, or recorded output fallback.
"""
from pathlib import Path
from types import SimpleNamespace
import json
import sys
import time
import numpy as np
from scipy.spatial.transform import Rotation
from .motion import convert

ROOT = Path(__file__).resolve().parents[1]
CHAINS = [[0,2,5,8,11],[0,1,4,7,10],[0,3,6,9,12,15],[9,14,17,19,21],[9,13,16,18,20]]
# HumanML's rotation at a child orients the parent-to-child segment.
SEGMENTS = {"Hips":0,"Spine1":6,"Spine2":9,"Chest":12,"Neck2":12,"Head":12,
            "LeftShoulder":16,"LeftArm":18,"LeftForeArm":20,"LeftHand":20,
            "RightShoulder":17,"RightArm":19,"RightForeArm":21,"RightHand":21,
            "LeftLeg":4,"LeftShin":7,"LeftFoot":10,"LeftToeBase":10,
            "RightLeg":5,"RightShin":8,"RightFoot":11,"RightToeBase":11}


def root_trajectory(data, initial_angle=0., origin=None):
    angles=initial_angle+np.cumsum(np.r_[0., data[:-1,0]])
    rotation=Rotation.from_rotvec(np.stack([np.zeros(len(data)),2*angles,np.zeros(len(data))],axis=1)).as_matrix()
    velocity=np.zeros((len(data),3));velocity[1:,[0,2]]=data[:-1,1:3]
    root=np.cumsum(np.einsum('tji,tj->ti',rotation,velocity),axis=0)
    if origin is not None:root+=origin
    root[:,1]=data[:,3]
    return rotation,root


def align_segment(rotations, reference, desired):
    """Match the model's primary joint-position output while retaining axial twist.

HumanML predicts positions and 6D rotations redundantly; those heads are not
exactly consistent. The upstream viewer uses positions, so use them for swing.
"""
    length=np.linalg.norm(desired,axis=-1,keepdims=True)
    target=desired/np.maximum(length,1e-8);source=rotations@np.asarray(reference)
    cross=np.cross(source,target);cosine=np.clip((source*target).sum(-1),-1,1)
    k=np.zeros_like(rotations);k[:,0,1]=-cross[:,2];k[:,0,2]=cross[:,1];k[:,1,0]=cross[:,2];k[:,1,2]=-cross[:,0];k[:,2,0]=-cross[:,1];k[:,2,1]=cross[:,0]
    correction=np.eye(3)+k+(k@k)/np.maximum(1+cosine,1e-8)[:,None,None]
    for i in np.where(cosine<-.999999)[0]:
        axis=np.cross(source[i],[1,0,0] if abs(source[i,0])<.9 else [0,1,0]);axis/=np.linalg.norm(axis)
        correction[i]=2*np.outer(axis,axis)-np.eye(3)
    result=correction@rotations
    return np.where((length>1e-6)[...,None],result,rotations)


def decode_features(features, *, prompt="", source="DiP · RTX 3070 实时生成", start=0, initial_angle=0., origin=None):
    """Decode the actual predicted 263D HumanML features, preserving accumulated root motion."""
    data=np.asarray(features,dtype=np.float64)
    if data.ndim!=2 or data.shape[1]!=263 or not np.isfinite(data).all(): raise ValueError("Invalid HumanML features")
    root_rotation,root=root_trajectory(data,initial_angle,origin)
    six=data[:,67:193].reshape(-1,21,6)
    a=six[...,:3];a=a/np.maximum(np.linalg.norm(a,axis=-1,keepdims=True),1e-8)
    b=six[...,3:];b=b-(a*b).sum(-1,keepdims=True)*a;b=b/np.maximum(np.linalg.norm(b,axis=-1,keepdims=True),1e-8)
    local=np.stack((a,b,np.cross(a,b)),axis=-1)
    global_rot=np.zeros((len(data),22,3,3));global_rot[:,0]=root_rotation
    # Arm branches in the upstream HumanML encoding restart at root rotation.
    for chain in CHAINS:
        running=root_rotation.copy()
        for joint in chain[1:]:
            running=running@local[:,joint-1];global_rot[:,joint]=running
    global_rot[:,0]=root_rotation.transpose(0,2,1)
    names=list(SEGMENTS)
    rotations=global_rot[:,[SEGMENTS[n] for n in names]].copy()
    for side,angle in (("Left",-90),("Right",90)):
        # HumanML's arm reference points DOWN; the target is a T-pose.
        calibration=Rotation.from_euler('z',angle,degrees=True).as_matrix()
        for part in ("Shoulder","Arm","ForeArm","Hand"):
            index=names.index(side+part);rotations[:,index]=rotations[:,index]@calibration
    # Position-based HumanML rotations do not encode axial spine twist. Recover
    # chest heading from both shoulders instead of holding the torso at zero yaw.
    joints=np.concatenate((np.zeros((len(data),1,3)),data[:,4:67].reshape(-1,21,3)),axis=1)
    joints=np.einsum('tji,tkj->tki',root_rotation,joints)
    for side,sign,chain in (("Left",1,(13,16,18,20,1,4,7,10)),("Right",-1,(14,17,19,21,2,5,8,11))):
        shoulder,arm,elbow,wrist,hip,knee,ankle,toe=chain
        for name,a,b,axis in (("Shoulder",shoulder,arm,[sign,0,0]),("Arm",arm,elbow,[sign,0,0]),
                              ("ForeArm",elbow,wrist,[sign,0,0]),("Hand",elbow,wrist,[sign,0,0]),
                              ("Leg",hip,knee,[0,-1,0]),("Shin",knee,ankle,[0,-1,0]),
                              ("Foot",ankle,toe,[0,0,1]),("ToeBase",ankle,toe,[0,0,1])):
            i=names.index(side+name);rotations[:,i]=align_segment(rotations[:,i],axis,joints[:,b]-joints[:,a])
    left=(joints[:,16]-joints[:,17])+(joints[:,1]-joints[:,2])
    for name,a,b in (("Spine1",3,6),("Spine2",6,9),("Chest",9,12),("Neck2",9,12),("Head",9,12)):
        up=joints[:,b]-joints[:,a];up/=np.maximum(np.linalg.norm(up,axis=-1,keepdims=True),1e-8)
        x=left-(left*up).sum(-1,keepdims=True)*up;x/=np.maximum(np.linalg.norm(x,axis=-1,keepdims=True),1e-8)
        frame=np.stack((x,up,np.cross(x,up)),axis=-1)
        valid=(np.linalg.norm(left,axis=-1)>.01)&(np.linalg.norm(up,axis=-1)>.5)
        rotations[valid,names.index(name)]=frame[valid]
    output={"global_rot_mats":rotations[start:],"root_positions":root[start:],"foot_contacts":np.clip(data[start:,-4:],0,1)}
    return convert(output,names,fps=20,source=source,prompt=prompt,hip_height=0.95)


class DiPProvider:
    name="dip"
    model_name="DiP 10-step / DistilBERT / RTX 3070"
    cache_identity="dip-600343-v3-ric"
    supports_stream=True
    def __init__(self):
        self.state="unloaded";self.model=None;self.idle_prefix=None
        self.base=ROOT/"models/dip"
    def load(self):
        if self.model is not None and self.idle_prefix is not None:return
        self.state="loading"
        import torch
        if not torch.cuda.is_available():raise RuntimeError("CUDA unavailable")
        torch.set_num_threads(4)
        sys.path.insert(0,str(ROOT/"vendor/dip"))
        from utils.model_util import create_model_and_diffusion,load_saved_model
        from utils.sampler_util import ClassifierFreeSampleModel
        checkpoint=self.base/"checkpoints/dip/DiP_no-target_10steps_context20_predict40"
        args=SimpleNamespace(**json.loads((checkpoint/"args.json").read_text()))
        model,self.diffusion=create_model_and_diffusion(args,SimpleNamespace(dataset=SimpleNamespace(num_actions=1)))
        load_saved_model(model,str(checkpoint/"model000600343.pt"),use_avg=True)
        self.model=ClassifierFreeSampleModel(model).to("cuda").eval()
        data=self.base/"data/dataset/HumanML3D"
        self.mean=np.load(data/"Mean.npy");self.std=np.load(data/"Std.npy")
        raw=np.load(data/"new_joint_vecs/012314.npy")[:20]
        prefix=torch.tensor((raw-self.mean)/self.std,dtype=torch.float32,device="cuda").T[None,:,None]
        # A published context clip initializes the model, then generated standing
        # windows settle the state. The seed context itself is never played.
        torch.manual_seed(2026)
        with torch.inference_mode():
            for _ in range(4):prefix=self.step_tensor("A person stands still with arms relaxed at their sides.",prefix)[...,-20:]
        self.idle_prefix=prefix.detach().clone();self.state="ready"
    def step_tensor(self,prompt,prefix):
        import torch
        y={"text":[prompt],"text_embed":self.model.encode_text([prompt]),"prefix":prefix,
           "mask":torch.ones((1,1,1,40),device="cuda",dtype=torch.bool),
           "lengths":torch.tensor([40],device="cuda"),"scale":torch.tensor([7.5],device="cuda")}
        return self.diffusion.p_sample_loop(self.model,(1,263,1,40),clip_denoised=False,model_kwargs={"y":y},progress=False)
    def new_state(self,seed):
        self.load()
        return {"prefix":self.idle_prefix.clone(),"features":[],"seed":seed,"sequence":0,"angle":0.,"origin":np.zeros(3)}
    def step(self,prompt,state):
        import torch
        started=time.perf_counter()
        with torch.inference_mode():
            torch.manual_seed(state["seed"]+state["sequence"])
            sample=self.step_tensor(prompt,state["prefix"])
            state["prefix"]=sample[...,-20:].clone()
            raw=sample[0,:,0,:].T.cpu().numpy()*self.std+self.mean
        state["features"].append(raw);state["sequence"]+=1
        # Keep bounded history without resetting world heading/translation.
        if len(state['features'])>2:
            dropped=state['features'].pop(0)
            _,path=root_trajectory(np.concatenate((dropped,np.zeros((1,263)))),state['angle'],state['origin'])
            state['origin']=path[-1];state['angle']+=float(dropped[:,0].sum())
        start=sum(len(a) for a in state['features'][:-1])
        clip=decode_features(np.concatenate(state["features"]),prompt=prompt,start=start,initial_angle=state['angle'],origin=state['origin'])
        clip["generationSeconds"]=round(time.perf_counter()-started,4)
        clip["sequence"]=state["sequence"]
        return clip
    def generate(self,request):
        state=self.new_state(request["seed"]);frames=[]
        for _ in range(int(np.ceil(request["duration"]/2))):
            clip=self.step(request["prompt"],state);frames.extend(clip["frames"])
        clip["frames"]=frames[:int(request["duration"]*20)]
        return clip
