"""Audio-conditioned StreamTalk, using the pinned official networks and sampler.

The retrieval metric and two-pass sampler match TestFixedExpressions_keyposes.py.
Only eight queried frames and bounded database blocks visit CUDA. SMPL-X's first
55 joints are calculated by its rigid-transform function; mesh skinning is not
needed for these joints. The equivalence is checked against the full model.
Licensed model/data files stay outside the source repository.
"""
from __future__ import annotations

import csv
import hashlib
import json
import math
from pathlib import Path
import sys
import time
import numpy as np
from scipy.spatial.transform import Rotation

from .motion import convert

ROOT = Path(__file__).resolve().parents[1]
CHECKPOINT = "streamtalk_speaker2_combined_e0946_cfg3.pt"
CHECKPOINT_SHA = "07065d52e333cc48960e4e8fad5c0331f6b9e482f14aa1b78cb721904da5b075"
NAMES = ["Hips", "LeftLeg", "RightLeg", "Spine1", "LeftShin", "RightShin", "Spine2",
         "LeftFoot", "RightFoot", "Chest", "LeftToeBase", "RightToeBase", "Neck2",
         "LeftShoulder", "RightShoulder", "Head", "LeftArm", "RightArm", "LeftForeArm",
         "RightForeArm", "LeftHand", "RightHand", "Jaw", "LeftEye", "RightEye"]
for side in ("Left", "Right"):
    for finger in ("Index", "Middle", "Pinky", "Ring", "Thumb"):
        start = 1 if finger == "Thumb" else 2
        NAMES.extend(f"{side}Hand{finger}{i}" for i in range(start, start + 3))


class StreamTalkProvider:
    name = "streamtalk"
    model_name = "StreamTalk speaker 2 / CFG 3 / WavLM Large"

    def __init__(self, root=ROOT):
        self.root = Path(root)
        self.base = self.root / "models/streamtalk"
        self.vendor = self.root / "vendor/streamtalk"
        self.state = "unloaded"
        self.loaded = False
        self.stats = {}

    def load(self):
        if self.loaded:
            return
        self.state = "loading"
        smpl_path = self.base / "SMPLX_NEUTRAL_2020.npz"
        for path in (smpl_path, self.base / CHECKPOINT, self.base / "wavlm/pytorch_model.bin"):
            if not path.is_file():
                self.state = "missing_assets"
                raise FileNotFoundError(path.name)
        if hashlib.sha256((self.base / CHECKPOINT).read_bytes()).hexdigest() != CHECKPOINT_SHA:
            raise ValueError("StreamTalk checkpoint checksum mismatch")
        import torch
        from transformers import Wav2Vec2Processor, WavLMModel
        from smplx.lbs import blend_shapes, vertices2joints, batch_rigid_transform
        if not torch.cuda.is_available():
            raise RuntimeError("StreamTalk requires a CUDA GPU")
        torch.set_num_threads(4)
        sys.path.insert(0, str(self.vendor / "Scripts"))
        from FM.Net import SimpleSpeechModel, DiffusionDITNetPartsFixedExpressions2PostNormInteraction2
        from Common.SMPLX_Util import GetSMPLXModel, SplitPosesIntoSmplxParams, SMPLX_PARENTS
        from Common.SMPLXConfig import GetBodyPartIndices
        self.parents = SMPLX_PARENTS
        self.parent_tensor = torch.tensor(self.parents, device="cuda")
        example = np.load(self.vendor / "Data/Test/2_scott_0_1_1.npz", allow_pickle=False)
        self.betas = example["betas"].astype(np.float32).reshape(1, -1)
        self.seed_poses = example["poses"][:60].astype(np.float32)
        self.seed_trans = example["trans"][:60].astype(np.float32)

        # Compute shape-dependent rest joints once. Expression is zero in the
        # published retrieval pass; pose blend shapes never affect J_transformed.
        smpl = GetSMPLXModel(str(smpl_path)).requires_grad_(False).eval()
        rest_cache = {}
        def rest_joints(betas):
            betas = np.asarray(betas, dtype=np.float32).reshape(1, -1)
            key = betas.tobytes()
            if key not in rest_cache:
                with torch.inference_mode():
                    shaped = smpl.v_template.unsqueeze(0) + blend_shapes(torch.from_numpy(betas), smpl.shapedirs)
                    rest_cache[key] = vertices2joints(smpl.J_regressor, shaped)[0].numpy()
            return rest_cache[key]
        rest = rest_joints(self.betas)
        self.rest = torch.from_numpy(rest).to("cuda")
        self.hip_height = float(rest[0, 1] - min(rest[7, 1], rest[8, 1], rest[10, 1], rest[11, 1]) + .06)
        # Guard the optimization against model/implementation drift.
        test = torch.from_numpy(self.seed_poses[:8].copy())
        test[:, :3] = 0
        with torch.inference_mode():
            actual = smpl(betas=torch.from_numpy(self.betas).expand(8, -1),
                          **SplitPosesIntoSmplxParams(test), expression=torch.zeros(8, 100),
                          transl=torch.zeros(8, 3)).joints[:, :55].numpy()
        fast = self._fk_numpy(test.numpy(), rest)
        error = float(np.max(np.abs(actual - fast)))
        if error > 2e-5:
            raise RuntimeError(f"SMPL-X joint equivalence failed: {error}")
        self.stats["smplJointMaxError"] = error
        self._load_retrieval(smpl_path, rest_joints)
        del smpl, rest_cache

        self.parts = {f"{name}_indices": GetBodyPartIndices(name, "rot6d")
                      for name in ("hand", "upper", "lower")}
        self.parts["face_joint_indices"] = GetBodyPartIndices("face", "rot6d")
        self.parts["trans_indices"] = np.array([330, 331, 332])
        self.face = SimpleSpeechModel().requires_grad_(False).eval().to("cuda")
        self.face.load_state_dict(torch.load(self.vendor / "Scripts/FM/ckpt/split/SimpleSpeechModel/best.pt", map_location="cpu", weights_only=True))
        self.net = DiffusionDITNetPartsFixedExpressions2PostNormInteraction2().requires_grad_(False).eval().to("cuda")
        self.net.load_state_dict(torch.load(self.base / CHECKPOINT, map_location="cpu", weights_only=True), strict=True)
        self.processor = Wav2Vec2Processor.from_pretrained(str(self.base / "wavlm"), local_files_only=True)
        self.audio_model = WavLMModel.from_pretrained(str(self.base / "wavlm"), local_files_only=True).requires_grad_(False).eval().to("cuda")
        self.loaded = True
        self.state = "ready"
        self.stats.update(retrievalFrames=len(self.db_joints), checkpointSHA256=CHECKPOINT_SHA,
                          device=torch.cuda.get_device_name(), loadedMiB=round(torch.cuda.memory_allocated()/1048576, 1))

    def _fk_numpy(self, poses, rest):
        local = Rotation.from_rotvec(poses.reshape(-1, 3)).as_matrix().reshape(-1, 55, 3, 3)
        global_r = np.empty_like(local)
        joints = np.empty((len(poses), 55, 3), dtype=np.float32)
        for j, parent in enumerate(self.parents):
            if parent < 0:
                global_r[:, j] = local[:, j]
                joints[:, j] = rest[j]
            else:
                global_r[:, j] = global_r[:, parent] @ local[:, j]
                joints[:, j] = joints[:, parent] + global_r[:, parent] @ (rest[j]-rest[parent])
        return joints

    def _load_retrieval(self, smpl_path, rest_joints):
        split = self.base / "beat/train_test_split.csv"
        with split.open() as stream:
            files = [self.base / "beat/smplxflame_30" / (row["id"] + ".npz")
                     for row in csv.DictReader(stream) if row["type"] == "train" and row["id"].startswith("2_scott_0")][:200]
        if not files or any(not p.is_file() for p in files):
            raise FileNotFoundError("Complete speaker-2 retrieval data is required")
        digest = hashlib.sha256(b"streamtalk-joints-v1")
        for p in [smpl_path, split, *files]:
            digest.update(p.name.encode()); digest.update(str(p.stat().st_size).encode())
            digest.update(hashlib.sha256(p.read_bytes()).digest())
        cache = self.root / "cache/streamtalk" / digest.hexdigest()[:24]
        cache.mkdir(parents=True, exist_ok=True)
        joint_file, pose_file = cache / "joints.npy", cache / "poses.npy"
        if not joint_file.exists() or not pose_file.exists():
            joints, poses = [], []
            for i, path in enumerate(files):
                with np.load(path, allow_pickle=False) as data:
                    pose = data["poses"].astype(np.float32).copy(); pose[:, :3] = 0
                    rest = rest_joints(data["betas"])
                    for start in range(0, len(pose), 2048):
                        joints.append(self._fk_numpy(pose[start:start+2048], rest).reshape(-1, 165).astype(np.float16))
                    poses.append(pose)
                if i % 10 == 0:
                    print(f"StreamTalk retrieval {i+1}/{len(files)}", flush=True)
            for dest, data in ((joint_file, np.concatenate(joints)), (pose_file, np.concatenate(poses))):
                temp = dest.with_suffix(".tmp")
                with temp.open("wb") as stream: np.save(stream, data, allow_pickle=False)
                temp.replace(dest)
        self.db_joints = np.load(joint_file, mmap_mode="r", allow_pickle=False)
        self.db_poses = np.load(pose_file, mmap_mode="r", allow_pickle=False)

    def _nearest(self, joints):
        """Exact upstream FP16 L1 argmin, with first-index tie handling."""
        import torch
        best = torch.full((len(joints),), float("inf"), device="cuda", dtype=torch.float16)
        ids = torch.zeros(len(joints), device="cuda", dtype=torch.long)
        query = joints.reshape(len(joints), 1, 165).half()
        for start in range(0, len(self.db_joints), 4096):
            block = torch.from_numpy(np.array(self.db_joints[start:start+4096])).to("cuda")
            dist, index = (block - query).abs().sum(-1).min(-1)
            improve = dist < best
            ids = torch.where(improve, index + start, ids)
            best = torch.minimum(best, dist)
        frame = int(best.argmin().item())
        return frame, int(ids[frame].item())

    def generate(self, audio, seed=7, cancelled=lambda: False):
        """Yield final 30-FPS windows, retaining the unpublished eight-frame tail.

        Input is finite mono float32 PCM at 16 kHz. No audio or generated motion
        is persisted. Cancellation is checked between network windows.
        """
        self.load()
        import torch
        import torch.nn.functional as F
        from smplx.lbs import batch_rigid_transform
        from Common.SMPLX_Util import LocalToGlobalAA, global_to_local_batch
        from Common.Util import (axis_angle_to_rotation_6d, NormalizeRot6d,
                                 rotation_6d_to_matrix, matrix_to_rotation_6d)
        torch.manual_seed(seed)
        torch.cuda.reset_peak_memory_stats()
        frames = max(2, math.ceil(len(audio)*30/16000))
        count = max(1, math.ceil((frames-60)/52)+1)
        prompt = torch.from_numpy(axis_angle_to_rotation_6d(LocalToGlobalAA(self.seed_poses))).float().to("cuda")[None]
        trans = torch.from_numpy(self.seed_trans.copy()).to("cuda")[None]
        pid = torch.tensor([2], device="cuda")
        tail = None; offset = 0; origin = None; heading = None
        started = time.perf_counter()
        with torch.inference_mode():
            for n in range(count):
                if cancelled(): return
                at = int(n*52*16000/30)
                samples = np.pad(audio[at:at+32000], (0, max(0, 32000-len(audio[at:at+32000]))))
                values = self.processor(samples, return_tensors="pt", sampling_rate=16000).input_values.to("cuda")
                with torch.autocast("cuda", dtype=torch.float16):
                    features = self.audio_model(values).last_hidden_state
                    features = F.interpolate(features.transpose(1,2), 60, mode="linear", align_corners=True).transpose(1,2)
                    expression = self.face(features, pid)
                    uncond = self.face(features, torch.zeros_like(pid))
                    mask = torch.ones(1, 60, dtype=torch.bool, device="cuda"); mask[:, :8] = False
                    kwargs = dict(self.parts, pid=pid, poses_prompt=prompt, prompt_mask=mask,
                                  trans_prompt=trans, trans_mask=mask.clone(), hubert=features,
                                  expressions=expression, expressions_uncond=uncond)
                    noise = torch.randn(1, 60, 333, device="cuda")
                    samples_list = self.net.sample(noise, normalize_func=NormalizeRot6d, cfg_scale=3., **kwargs)
                coarse = samples_list[-1]
                coarse[..., :330] = NormalizeRot6d(coarse[..., :330])
                local = global_to_local_batch(rotation_6d_to_matrix(coarse[0, -8:, :330].reshape(8, 55, 6)).float())
                local[:, 0] = torch.eye(3, device="cuda")
                joints, _ = batch_rigid_transform(local, self.rest[None].expand(8,-1,-1), self.parent_tensor)
                frame, index = self._nearest(joints)
                nearest = axis_angle_to_rotation_6d(LocalToGlobalAA(np.array(self.db_poses[index:index+1])))
                nearest = rotation_6d_to_matrix(torch.from_numpy(nearest).to("cuda").float().reshape(1,55,6))
                root = rotation_6d_to_matrix(coarse[0,52+frame,:6].reshape(1,6))
                nearest = matrix_to_rotation_6d(root[:,None] @ nearest).reshape(1,330)
                mask[:,52+frame] = False; prompt[:,52+frame] = nearest
                coarse[:,52+frame,:330] = nearest
                with torch.autocast("cuda", dtype=torch.float16):
                    result = self.net.sample_with_kp_bk(noise, samples_list[5], 5, normalize_func=NormalizeRot6d, cfg_scale=3., **kwargs)
                result[...,:330] = NormalizeRot6d(result[...,:330])
                prompt[:,:8] = result[:,-8:,:330]; trans[:,:8] = result[:,-8:,330:333]
                current = result[0].float().cpu().numpy()
                if tail is not None: current[:8] = (tail + current[:8])/2
                tail = current[-8:].copy()
                ready = current if n == count-1 else current[:-8]
                ready = ready[:frames-offset]
                # A final fragment can be one frame: transport needs two, so it
                # is kept with its preceding window by the window count above.
                if len(ready) < 2: break
                mats = rotation_6d_to_matrix(ready[:,:330].reshape(-1,55,6))
                if origin is None:
                    origin = ready[0,330:333].copy()
                    yaw = math.atan2(mats[0,0,0,2], mats[0,0,2,2])
                    heading = Rotation.from_euler("y", -yaw).as_matrix()
                roots = (ready[:,330:333]-origin) @ heading.T
                roots[:,1] += self.hip_height
                motion = convert({"global_rot_mats": heading @ mats, "root_positions": roots}, NAMES,
                                 source="StreamTalk · audio-conditioned neural generation", hip_height=self.hip_height)
                torch.cuda.synchronize()
                motion.update(startFrame=offset, totalFrames=frames, final=n==count-1,
                              generationSeconds=round(time.perf_counter()-started,4),
                              peakMiB=round(torch.cuda.max_memory_allocated()/1048576,1))
                offset += len(ready)
                if cancelled(): return
                yield motion
        self.stats.update(lastSeconds=round(time.perf_counter()-started,4), peakMiB=round(torch.cuda.max_memory_allocated()/1048576,1))
