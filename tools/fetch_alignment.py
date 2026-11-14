"""Fetch official Japanese forced-alignment weights into the motion project."""
import hashlib
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
REVISION = 'c7cbfc2048c462b0d63a45797104fc9db3ad62b7'
FILES = ['chat_template.json', 'config.json', 'generation_config.json', 'merges.txt',
         'model.safetensors', 'preprocessor_config.json', 'tokenizer_config.json', 'vocab.json']


def main():
    target = ROOT / 'models/forced-aligner'
    target.mkdir(parents=True, exist_ok=True)
    def fetch(name):
        dest = target / name
        if dest.exists(): return
        partial = target / (name + '.part')
        subprocess.run(['curl', '-fLsS', '--retry', '4', '--connect-timeout', '20', '-C', '-',
            '-o', str(partial), f'https://huggingface.co/Qwen/Qwen3-ForcedAligner-0.6B/resolve/{REVISION}/{name}'], check=True)
        partial.replace(dest)
        print('Downloaded', name, flush=True)
    with ThreadPoolExecutor(max_workers=4) as pool: list(pool.map(fetch, FILES))
    if hashlib.sha256((target / 'model.safetensors').read_bytes()).hexdigest() != '47831d0e82f96b20e9034dba01a075ee06436654719f6a68289e49f1b65ce0e7':
        raise RuntimeError('Forced aligner checkpoint checksum mismatch')
    print('Forced aligner ready:', target, flush=True)

if __name__ == '__main__': main()
