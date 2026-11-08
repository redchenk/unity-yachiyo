"""Convert a bundled official Kimodo sample; no generation/model download."""
import json
from pathlib import Path
import sys
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from service.motion import convert_npz, write_motion
repo=ROOT/"vendor/kimodo"
example=repo/"kimodo/assets/demo/examples/kimodo-soma-rp/06_root_waypoints"
meta=json.loads((example/"meta.json").read_text())
motion=convert_npz(example/"motion.npz",repo,prompt=meta["text"],source="Kimodo 官方样例",fps=30)
write_motion(ROOT/"UnityProject/Assets/StreamingAssets/Motions/kimodo-official.json",motion)
print(f"Converted official sample: {len(motion['frames'])} frames, {len(motion['bones'])} mapped bones")
