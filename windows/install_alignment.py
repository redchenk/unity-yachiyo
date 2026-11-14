"""Install optional aligner dependencies into a private prefix, never into TTS.

Run using YachiyoMotion/runtime/python.exe after tools/fetch_alignment.py has
placed the pinned model in models/forced-aligner. Existing start/stop owns it.
"""
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = ['qwen-asr==0.0.6', 'transformers==4.57.6', 'tokenizers==0.22.2',
            'huggingface-hub==0.36.2', 'nagisa==0.3.1', 'soynlp==0.0.493',
            'sox==1.5.0', 'qwen-omni-utils==0.0.9']

if __name__ == '__main__':
    if not (ROOT / 'runtime/python.exe').is_file(): raise RuntimeError('Existing motion runtime required')
    subprocess.run([sys.executable, '-m', 'pip', 'install', '--target',
        str(ROOT / 'runtime-alignment/site-packages'), '--no-deps', *PACKAGES], check=True)
    # Torch, numpy, librosa and soundfile are read from the existing isolated
    # motion runtime. Windows nagisa 0.3 exposes the same tagging API as 0.2.
    subprocess.run([sys.executable, '-c',
        f"import sys;sys.path.insert(0,{str(ROOT / 'runtime-alignment/site-packages')!r});from qwen_asr import Qwen3ForcedAligner;print('Alignment dependencies ready')"], check=True)
