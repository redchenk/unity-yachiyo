"""Fetch pinned official StreamTalk assets. SMPL-X is supplied separately by its licensee."""
import csv
import hashlib
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
CODE = '550faa3d2fcf56d2055552fef6a19a107c998d44'
WEIGHTS = '35a40bc4ddd968afefc79bec23aff2331ef539dc'
WAVLM = 'e70e3a062ec399c46008ee55d1fb52c7ba338d5c'
BEAT = '8689ecb43513ba31964fd60e0ca69be02d3b0872'
MODEL = 'streamtalk_speaker2_combined_e0946_cfg3.pt'
SHA = '07065d52e333cc48960e4e8fad5c0331f6b9e482f14aa1b78cb721904da5b075'


def download(url, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists(): return path
    partial = path.with_suffix(path.suffix + '.part')
    subprocess.run(['curl', '-fLsS', '--retry', '4', '--connect-timeout', '20',
                    '-C', '-', '-o', str(partial), url], check=True)
    partial.replace(path)
    print('Downloaded', path.relative_to(ROOT), flush=True)
    return path


def main():
    vendor = ROOT / 'vendor/streamtalk'
    if not vendor.exists():
        vendor.mkdir(parents=True)
        for args in [['init', '-q'], ['remote', 'add', 'origin', 'https://github.com/Xiangyue-Zhang/StreamTalk.git'],
                     ['fetch', '--depth=1', 'origin', CODE], ['checkout', '--detach', 'FETCH_HEAD']]:
            subprocess.run(['git', '-C', str(vendor), *args], check=True)
    if subprocess.check_output(['git', '-C', str(vendor), 'rev-parse', 'HEAD'], text=True).strip() != CODE:
        raise RuntimeError('Existing StreamTalk source revision differs; not overwritten')
    base = ROOT / 'models/streamtalk'
    split = download(f'https://huggingface.co/datasets/H-Liu1997/BEAT2/resolve/{BEAT}/beat_english_v2.0.0/train_test_split.csv', base / 'beat/train_test_split.csv')
    jobs = [(f'https://huggingface.co/X-Zhang/StreamTalk/resolve/{WEIGHTS}/{MODEL}', base / MODEL)]
    for file in ['config.json', 'preprocessor_config.json', 'pytorch_model.bin', 'vocab.json', 'tokenizer_config.json', 'special_tokens_map.json', 'added_tokens.json']:
        jobs.append((f'https://huggingface.co/patrickvonplaten/wavlm-libri-clean-100h-large/resolve/{WAVLM}/{file}', base / 'wavlm' / file))
    # Match the published speaker-2 retrieval database, not a few arbitrary examples.
    for row in csv.DictReader(split.open()):
        if row['type'] == 'train' and row['id'].startswith('2_scott_0'):
            name = row['id'] + '.npz'
            jobs.append((f'https://huggingface.co/datasets/H-Liu1997/BEAT2/resolve/{BEAT}/beat_english_v2.0.0/smplxflame_30/{name}', base / 'beat/smplxflame_30' / name))
    with ThreadPoolExecutor(max_workers=4) as pool:
        list(pool.map(lambda pair: download(*pair), jobs))
    if hashlib.sha256((base / MODEL).read_bytes()).hexdigest() != SHA:
        raise RuntimeError('StreamTalk checkpoint checksum mismatch')
    print('Verified official StreamTalk checkpoint. Licensed SMPL-X file is still required.', flush=True)


if __name__ == '__main__': main()
