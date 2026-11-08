from concurrent.futures import ThreadPoolExecutor
import threading
import unittest
from service.streams import Streams
from service.dip_provider import decode_features,root_trajectory,align_segment
import numpy as np

class SyntheticProvider:
    def new_state(self,seed):return {'seed':seed,'sequence':0,'features':[]}
    def step(self,prompt,state):
        state['features'].append([prompt]*40);state['sequence']+=1
        return {'history':[part[0] for part in state['features']]}

class StreamTests(unittest.TestCase):
    def test_retarget_matches_predicted_joint_directions_including_opposite(self):
        matrix=np.repeat(np.eye(3)[None],3,axis=0)
        targets=np.array([[0,0,1],[-1,0,0],[.3,-.7,.2]])
        aligned=align_segment(matrix,[1,0,0],targets)
        np.testing.assert_allclose(aligned@np.array([1,0,0]),targets/np.linalg.norm(targets,axis=-1,keepdims=True),atol=1e-6)
        np.testing.assert_allclose(aligned@aligned.transpose(0,2,1),matrix,atol=1e-6)
        np.testing.assert_allclose(np.linalg.det(aligned),1,atol=1e-6)
    def test_discarded_future_branches_from_accepted_cursor(self):
        with ThreadPoolExecutor(1) as pool:
            streams=Streams(SyntheticProvider(),pool);key=streams.create(7)['session']
            first=streams.step(key,dict(sequence=0,revision=1,prompt='wave'))
            streams.step(key,dict(sequence=first['sequence'],revision=1,prompt='discarded'))
            result=streams.step(key,dict(sequence=first['sequence'],revision=2,prompt='walk'))
            self.assertEqual(result['history'],['wave','walk'])
            with self.assertRaises(ValueError):streams.step(key,dict(sequence=first['sequence'],revision=1,prompt='old'))
    def test_stop_during_inference_does_not_commit(self):
        entered=threading.Event();release=threading.Event()
        class Slow(SyntheticProvider):
            def step(self,prompt,state):entered.set();release.wait(3);return super().step(prompt,state)
        with ThreadPoolExecutor(1) as pool,ThreadPoolExecutor(1) as caller:
            streams=Streams(Slow(),pool);key=streams.create(7)['session']
            future=caller.submit(streams.step,key,dict(sequence=0,revision=1,prompt='wave'))
            self.assertTrue(entered.wait(2));streams.close(key);release.set()
            with self.assertRaises(ValueError):future.result()
    def test_decoder_chunk_boundary_preserves_root_and_valid_rotations(self):
        raw=np.zeros((80,263));raw[:,3]=.95;raw[:,2]=.01
        raw[:,67:193]=np.tile([1,0,0,0,1,0],21)
        full=decode_features(raw);last=decode_features(raw,start=40)
        self.assertEqual(last['frames'][0],full['frames'][40])
        self.assertAlmostEqual(full['frames'][40]['root']['z']-full['frames'][39]['root']['z'],.01)
        for frame in full['frames']:
            for q in frame['rotations']:self.assertAlmostEqual(sum(v*v for v in q.values()),1,places=6)
        # Identity HumanML arm rotations mean arms DOWN, not a T-pose.
        from scipy.spatial.transform import Rotation
        frame=full['frames'][0]
        for bone,rest in [('LeftUpperArm',[-1,0,0]),('RightUpperArm',[1,0,0])]:
            q=frame['rotations'][full['bones'].index(bone)]
            direction=Rotation.from_quat([q[k]for k in 'xyzw']).apply(rest)
            np.testing.assert_allclose(direction,[0,-1,0],atol=1e-6)
    def test_history_compaction_preserves_world_motion(self):
        raw=np.zeros((160,263));raw[:,3]=.95;raw[:,2]=.02;raw[:,0]=.003
        raw[:,67:193]=np.tile([1,0,0,0,1,0],21)
        full=decode_features(raw)
        _,prefix=root_trajectory(np.concatenate((raw[:40],np.zeros((1,263)))))
        tail=decode_features(raw[40:],initial_angle=raw[:40,0].sum(),origin=prefix[-1])
        for a,b in zip(full['frames'][40:],tail['frames']):
            np.testing.assert_allclose(list(a['root'].values()),list(b['root'].values()),atol=1e-6)
            for qa,qb in zip(a['rotations'],b['rotations']):self.assertAlmostEqual(abs(sum(qa[k]*qb[k] for k in 'xyzw')),1,places=6)

if __name__=='__main__':unittest.main()
