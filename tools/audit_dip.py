"""Real GPU inference audit, not the unit-test synthetic provider."""
from pathlib import Path
import sys,time,json
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from service.dip_provider import DiPProvider,root_trajectory
import numpy as np
from scipy.spatial.transform import Rotation
p=DiPProvider();s=p.new_state(41)
for _ in range(4):c=p.step('A person turns around slowly.',s)
a=np.concatenate(s['features']);r,_=root_trajectory(a,s['angle'],s['origin'])
j=np.concatenate((np.zeros((len(a),1,3)),a[:,4:67].reshape(-1,21,3)),axis=1);j=np.einsum('tji,tkj->tki',r,j)[-40:];f=np.diag([-1,1,1]);angles={}
for bone,start,end,v in [('LeftUpperArm',16,18,[1,0,0]),('RightUpperArm',17,19,[-1,0,0]),('LeftUpperLeg',1,4,[0,-1,0])]:
 q=np.array([[frame['rotations'][c['bones'].index(bone)][k] for k in 'xyzw']for frame in c['frames']]);mat=f@Rotation.from_quat(q).as_matrix()@f
 direction=mat@v;expected=j[:,end]-j[:,start];expected/=np.linalg.norm(expected,axis=-1,keepdims=True)
 error=np.degrees(np.arccos(np.clip((direction*expected).sum(-1),-1,1)));angles[bone]={'mean_degrees':float(error.mean()),'max_degrees':float(error.max())}
s=p.new_state(83);timings=[];finite=True;last=None;boundary=0
for i in range(110):
 c=p.step('A person stands still with arms relaxed at their sides.' if i<100 else 'A person waves hello with their right hand.',s)
 timings.append(c['generationSeconds']);first=np.array(list(c['frames'][0]['root'].values()));end=np.array(list(c['frames'][-1]['root'].values()))
 finite=finite and bool(np.isfinite(first).all() and np.isfinite(end).all())
 if last is not None:boundary=max(boundary,float(np.linalg.norm(first-last)))
 last=end
report={'provider':'DiP v3 / position-aligned retargeting','fresh_windows':110,'predicted_seconds':220,'finite':finite,'median_generation_seconds':float(np.median(timings)),'p95_generation_seconds':float(np.percentile(timings,95)),'max_adjacent_window_root_step_m':boundary,'retained_feature_frames':sum(len(a)for a in s['features']),'turn_pose_alignment':angles}
path=Path('artifacts/dip-final-inference-validation.json');path.write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
