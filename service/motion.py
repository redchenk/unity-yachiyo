"""SOMA global rotations -> Unity T-pose deltas. No Unity/GPU dependency."""
from __future__ import annotations

import ast
import json
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation

BODY = {
    "Hips": "Hips", "Spine1": "Spine", "Spine2": "Chest", "Chest": "UpperChest",
    "Neck2": "Neck", "Head": "Head",
    "LeftShoulder": "LeftShoulder", "LeftArm": "LeftUpperArm", "LeftForeArm": "LeftLowerArm",
    "LeftHand": "LeftHand", "RightShoulder": "RightShoulder", "RightArm": "RightUpperArm",
    "RightForeArm": "RightLowerArm", "RightHand": "RightHand",
    "LeftLeg": "LeftUpperLeg", "LeftShin": "LeftLowerLeg", "LeftFoot": "LeftFoot", "LeftToeBase": "LeftToes",
    "RightLeg": "RightUpperLeg", "RightShin": "RightLowerLeg", "RightFoot": "RightFoot", "RightToeBase": "RightToes",
}
for side in ("Left", "Right"):
    for finger in ("Thumb", "Index", "Middle", "Ring", "Pinky"):
        first = 1 if finger == "Thumb" else 2  # SOMA's non-thumb joint 1 is the metacarpal.
        for i, segment in enumerate(("Proximal", "Intermediate", "Distal"), first):
            BODY[f"{side}Hand{finger}{i}"] = f"{side}{'Little' if finger == 'Pinky' else finger}{segment}"


def joint_names(repo: Path, count: int) -> list[str]:
    """Read the pinned upstream skeleton declaration, without importing its ML stack."""
    if count not in (30, 77):
        raise ValueError(f"Only SOMA 30/77 supported, received {count} joints")
    tree = ast.parse((repo / "kimodo/skeleton/definitions.py").read_text())
    for node in tree.body:
        if isinstance(node, ast.ClassDef) and node.name == f"SOMASkeleton{count}":
            for child in node.body:
                if isinstance(child, ast.Assign) and any(isinstance(t, ast.Name) and t.id == "bone_order_names_with_parents" for t in child.targets):
                    return [name for name, _ in ast.literal_eval(child.value)]
    raise ValueError("Upstream skeleton declaration not found")


def convert(arrays, names, *, fps=30.0, prompt="", source="Kimodo", hip_height=1.0):
    rotations = np.asarray(arrays["global_rot_mats"], dtype=np.float64)
    if rotations.ndim == 5 and rotations.shape[0] == 1:
        rotations = rotations[0]
    if rotations.ndim != 4 or rotations.shape[1:] != (len(names), 3, 3):
        raise ValueError("Expected one clip of global rotation matrices [T,J,3,3]")
    if not 0 < fps <= 240 or not 2 <= len(rotations) <= 24000 or not 0.1 < hip_height < 3:
        raise ValueError("Invalid motion timing or reference height")
    if "root_positions" in arrays:
        roots = np.asarray(arrays["root_positions"], dtype=np.float64)
        if roots.ndim == 3 and roots.shape[0] == 1: roots = roots[0]
    else:
        positions = np.asarray(arrays["posed_joints"], dtype=np.float64)
        if positions.ndim == 4 and positions.shape[0] == 1: positions = positions[0]
        roots = positions[:, names.index("Hips")]
    if roots.shape != (len(rotations), 3): raise ValueError("Root shape does not match motion")
    if not np.isfinite(rotations).all() or not np.isfinite(roots).all(): raise ValueError("Non-finite pose")
    chosen = [(i, BODY[name]) for i, name in enumerate(names) if name in BODY]
    mats = rotations[:, [i for i, _ in chosen]]
    # SOMA is right-handed, +Y up, +Z forward. Mirror X to Unity left-handed space.
    reflection = np.diag([-1.0, 1.0, 1.0])
    mats = reflection @ mats @ reflection
    flat = mats.reshape(-1, 3, 3)
    if not np.allclose(flat @ flat.transpose(0, 2, 1), np.eye(3), atol=0.02) or not np.allclose(np.linalg.det(flat), 1, atol=0.02):
        raise ValueError("Rotation matrices are not proper rotations")
    q = Rotation.from_matrix(flat).as_quat().reshape(len(mats), len(chosen), 4)
    # Maintain the quaternion hemisphere for stable interpolation.
    for t in range(1, len(q)):
        q[t, np.sum(q[t-1]*q[t], axis=1) < 0] *= -1
    roots = roots @ reflection
    contacts = np.asarray(arrays.get("foot_contacts", np.zeros((len(q), 4))), dtype=float)
    if contacts.ndim == 3 and contacts.shape[0] == 1: contacts = contacts[0]
    if contacts.shape == (len(q), 6):
        # Current SOMA77: left foot/toe-base/toe-end, right foot/toe-base/toe-end.
        contacts = np.stack((contacts[:,0],np.maximum(contacts[:,1],contacts[:,2]),
                             contacts[:,3],np.maximum(contacts[:,4],contacts[:,5])),axis=1)
    if contacts.shape != (len(q), 4) or not np.isfinite(contacts).all(): raise ValueError("Invalid foot contacts")
    frames = [{"root": dict(zip("xyz", r.round(6))),
               "rotations": [dict(zip("xyzw", v.round(7))) for v in frame],
               "contacts": contact.tolist()} for r, frame, contact in zip(roots, q, contacts)]
    return {"version": 1, "coordinates": "unity-lh-y-up-global-tpose-delta", "source": source,
            "prompt": prompt, "fps": fps, "referenceHipHeight": hip_height,
            "bones": [name for _, name in chosen], "frames": frames}


def convert_npz(path: Path, repo: Path, **kwargs):
    with np.load(path, allow_pickle=False) as arrays:
        count = arrays["global_rot_mats"].shape[-3]
        return convert(dict(arrays), joint_names(repo, count), **kwargs)


def write_motion(path: Path, motion):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(motion, ensure_ascii=False, separators=(",", ":"), allow_nan=False))
    temp.replace(path)
