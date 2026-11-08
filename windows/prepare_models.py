"""Download pinned public models into this Windows copy, without an HF login."""
import hashlib
import json
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
os.environ.update(HF_HOME=str(ROOT / "models/huggingface"), HF_HUB_DISABLE_XET="1",
                  HF_HUB_DISABLE_SYMLINKS_WARNING="1")
from huggingface_hub import hf_hub_download, snapshot_download

revision = "de7106b947b6f70700b5320d1cd61fef4a9ebc9b"
checkpoint = "checkpoints/dip/DiP_no-target_10steps_context20_predict40/model000600343.pt"
files = [checkpoint, str(Path(checkpoint).parent / "args.json").replace("\\", "/"),
         "data/dataset/HumanML3D/Mean.npy", "data/dataset/HumanML3D/Std.npy",
         "data/dataset/HumanML3D/new_joint_vecs/012314.npy"]
manifest = {}
for filename in files:
    path = hf_hub_download("guytevet/CLoSD", filename, revision=revision, local_dir=ROOT / "models/dip")
    manifest[filename] = hashlib.sha256(Path(path).read_bytes()).hexdigest()
assert manifest[checkpoint] == "bcf3e482a5236a21731af571c66bd7d0274f5ddf0b6974825fa69333c77f3144", "Checkpoint hash mismatch"
snapshot_download("distilbert/distilbert-base-uncased", revision="12040accade4e8a0f71eabdb258fecc2e7e948be",
                  allow_patterns=["config.json", "tokenizer*.json", "vocab.txt", "model.safetensors", "LICENSE"])
(ROOT / "state").mkdir(exist_ok=True)
(ROOT / "state/model-manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
print("公开动作模型和文字编码器已下载并校验。")
