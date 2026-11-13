"""Add StreamTalk packages to an isolated prefix beside the existing E: runtime.

Run with runtime/python.exe after fetch_streamtalk.py assets and the licensed
SMPL-X file have been copied. Existing DiP/torch dependencies are reused, not
upgraded. No startup task, public listener or second manual launch is added.
"""
from pathlib import Path
import json
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def main():
    base, dest = ROOT / "runtime", ROOT / "runtime-streamtalk"
    if not (base / "python.exe").is_file():
        raise RuntimeError("Install the existing Windows motion runtime first")
    dest.mkdir(exist_ok=True)
    for source in base.iterdir():
        if source.is_file() and source.suffix in (".exe", ".dll", ".pyd", ".zip", ".txt") and not (dest / source.name).exists():
            shutil.copy2(source, dest / source.name)
    (dest / "Lib/site-packages").mkdir(parents=True, exist_ok=True)
    (dest / "python312._pth").write_text("python312.zip\n.\nLib/site-packages\n../runtime/Lib/site-packages\n../vendor/streamtalk/Scripts\n..\nimport site\n")
    subprocess.run([str(dest / "python.exe"), "-m", "pip", "install", "--disable-pip-version-check",
                    "smplx==0.1.28", "einops==0.8.1", "positional-encodings==6.0.3"], check=True)
    subprocess.run([str(dest / "python.exe"), "-c", "import torch, smplx, einops, positional_encodings; assert torch.cuda.is_available()"], check=True)
    (ROOT / "state/streamtalk-installed.json").write_text(json.dumps({"runtime": "runtime-streamtalk", "version": 1}))
    print("StreamTalk runtime ready. Existing start/stop entries manage both models.")


if __name__ == "__main__": main()
