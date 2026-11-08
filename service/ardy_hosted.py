"""Public hosted ARDY inference; on-demand clips, not a realtime stream."""
import base64
import json
from pathlib import Path
import numpy as np
from .motion import convert

ROOT=Path(__file__).resolve().parents[1]
CORE_TO_SOMA={'Hips':'Hips','Spine':'Spine1','Spine2':'Spine2','Spine3':'Chest','Neck':'Neck2','Head':'Head',
              'RightShoulder':'RightShoulder','RightArm':'RightArm','RightForeArm':'RightForeArm','RightHand':'RightHand',
              'LeftShoulder':'LeftShoulder','LeftArm':'LeftArm','LeftForeArm':'LeftForeArm','LeftHand':'LeftHand',
              'RightUpLeg':'RightLeg','RightLeg':'RightShin','RightFoot':'RightFoot','RightToeBase':'RightToeBase',
              'LeftUpLeg':'LeftLeg','LeftLeg':'LeftShin','LeftFoot':'LeftFoot','LeftToeBase':'LeftToeBase'}

def decode(payload,prompt):
    if payload['rig']!='human' or payload['num_joints']!=27:raise ValueError('Unexpected hosted rig')
    frames=int(payload['num_frames'])
    if not 2<=frames<=1200:raise ValueError('Invalid frame count')
    affines=np.frombuffer(base64.b64decode(payload['affines'],validate=True),dtype='<f4').reshape(frames,27,3,4)
    with np.load(ROOT/'vendor/ardy/ardy/assets/skeletons/cskel27/skin_standard.npz',allow_pickle=False) as skin:
        # The API publishes skinning affines, not raw skeleton matrices.
        # Remove the inverse bind transform exactly as in the public Space source.
        bind=skin['bind_rig_transform'];names=list(skin['rig_joint_names'])
        transforms=affines@bind
        chosen=[i for i,n in enumerate(names) if n in CORE_TO_SOMA]
        result=convert({'global_rot_mats':transforms[:,chosen,:3,:3], 'root_positions':np.asarray(payload['root'])},
                       [CORE_TO_SOMA[names[i]] for i in chosen],fps=payload['fps'],prompt=prompt,
                       source='ARDY · 在线新生成（完整片段）',hip_height=float(bind[0,1,3]))
    return result

class HostedArdyProvider:
    name='ardy-hosted';state='ready';model_name='ARDY Core RP / public hosted API'
    cache_identity='hugging-apps-ardy-10eb8b808717c0b485f47912256332e408861765-v1'
    def generate(self,request):
        from gradio_client import Client
        result=Client('hugging-apps/ardy',verbose=False).predict(prompt=request['prompt'],rig='human',duration=request['duration'],
                    diffusion_steps=10,cfg_weight=2,seed=request['seed'],randomize_seed=False,api_name='/generate')
        payload=json.loads(result[0]);return decode(payload,request['prompt'])
