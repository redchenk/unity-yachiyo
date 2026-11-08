"""Exercise the motion network without the gated text encoder.

This is an UNCONDITIONED infrastructure check, not a text-to-motion result.
Its output is deliberately not exported to the user-facing motion library.
"""
import json,time,sys
from pathlib import Path
import torch
from kimodo import load_model
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from service.motion import convert

class ZeroCondition:
    def __call__(self,texts):
        count=1 if isinstance(texts,str) else len(texts)
        return torch.zeros(count,1,4096),[1]*count

start=time.monotonic()
model=load_model("Kimodo-SOMA-RP-v1.2",device="cuda",text_encoder=ZeroCondition())
loaded=time.monotonic()
with torch.inference_mode():
    output=model("Infrastructure test",60,num_samples=1,num_denoising_steps=4,cfg_type="nocfg",post_processing=True,return_numpy=True)
torch.cuda.synchronize()
converted=convert(output,model.output_skeleton.bone_order_names,fps=model.fps,source="UNCONDITIONED_TEST")
report={"kind":"unconditioned_network_smoke_test","text_conditioning_verified":False,"model_load_seconds":loaded-start,
        "inference_seconds":time.monotonic()-loaded,"peak_gpu_mb":torch.cuda.max_memory_allocated()/1024**2,
        "fps":model.fps,"converted_frames":len(converted["frames"]),"mapped_bones":len(converted["bones"]),
        "shapes":{k:list(v.shape)for k,v in output.items()if hasattr(v,"shape")}}
Path("artifacts").mkdir(exist_ok=True)
Path("artifacts/gpu-smoke.json").write_text(json.dumps(report,indent=2))
print(json.dumps(report))
