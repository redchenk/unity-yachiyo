"""Fetch the public checkpoint and its published initialization/statistics."""
from pathlib import Path
from huggingface_hub import hf_hub_download

root=Path(__file__).resolve().parents[1]/"models/dip"
files=["checkpoints/dip/DiP_no-target_10steps_context20_predict40/args.json",
       "checkpoints/dip/DiP_no-target_10steps_context20_predict40/model000600343.pt",
       "data/dataset/HumanML3D/Mean.npy","data/dataset/HumanML3D/Std.npy",
       "data/dataset/HumanML3D/new_joint_vecs/012314.npy"]
for file in files:
    hf_hub_download("guytevet/CLoSD",file,revision="de7106b947b6f70700b5320d1cd61fef4a9ebc9b",local_dir=root)
