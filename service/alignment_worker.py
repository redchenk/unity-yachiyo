"""Private stdio worker. Launched and stopped by SpeechAligner only."""
import base64
import json
import os
from pathlib import Path
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'runtime-alignment/site-packages'))
# Closing the manual service entry must also close its managed child, even
# during model inference. No persistent service or login task is installed.
if os.name == 'nt':
    import ctypes
    import threading
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
    parent = kernel.OpenProcess(0x00100000, False, os.getppid())
    if parent:
        def watch_parent():
            kernel.WaitForSingleObject(parent, 0xFFFFFFFF)
            os._exit(0)
        threading.Thread(target=watch_parent, daemon=True).start()

# Bound NumPy/BLAS parallelism as well as Torch so tokenization does not
# contend with voice and body generation on Windows.
os.environ['OPENBLAS_NUM_THREADS'] = '1'
os.environ['MKL_NUM_THREADS'] = '4'
os.environ['OMP_NUM_THREADS'] = '4'
import numpy as np
import torch
from qwen_asr import Qwen3ForcedAligner

torch.set_num_threads(4)
device = os.environ.get('YACHIYO_ALIGNMENT_DEVICE', 'auto')
if device == 'auto':
    # Keep the 8-GB laptop's motion/voice budget; a desktop with >=12 GB can
    # align concurrently in BF16 without replacing either existing runtime.
    device = 'cuda:0' if torch.cuda.is_available() and torch.cuda.get_device_properties(0).total_memory >= 12 * 1024**3 else 'cpu'
model = Qwen3ForcedAligner.from_pretrained(str(ROOT / 'models/forced-aligner'),
    dtype=torch.bfloat16 if device.startswith('cuda') else torch.float32,
    device_map=device, attn_implementation='sdpa')
# Warm feature extraction and kernels before advertising readiness. Queued
# speech never pays the first-use initialization on its playback clock.
with torch.inference_mode():
    model.align(audio=(np.zeros(16000, dtype=np.float32), 16000), text='はい', language='Japanese')
print(json.dumps({'ready': True, 'device': device}), flush=True)
for line in sys.stdin:
    try:
        raw = json.loads(line)
        pcm = np.frombuffer(base64.b64decode(raw['pcm16'], validate=True), dtype='<i2').astype(np.float32) / 32768
        start = time.monotonic()
        with torch.inference_mode():
            result = model.align(audio=(pcm, 16000), text=raw['text'], language='Japanese')[0]
        print(json.dumps({'words': [dict(text=w.text, start=w.start_time, end=w.end_time) for w in result],
                          'seconds': round(time.monotonic()-start, 3)}, ensure_ascii=False), flush=True)
    except Exception:
        print(json.dumps({'error': 'alignment-failed'}), flush=True)
