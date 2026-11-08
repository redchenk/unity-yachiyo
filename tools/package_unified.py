"""Package and install the private application outside Desktop file-provider metadata."""
from pathlib import Path
import plistlib,shutil,subprocess,tempfile
ROOT=Path(__file__).resolve().parents[1]
app=ROOT/'build/Yachiyo Motion Studio.app'
source=ROOT/'artifacts/unified-director'
if not (source/'runtime/node').is_file():raise SystemExit('Run tools/prepare_unified.py first')
shutil.copytree(source,app/'Contents/Resources/Director',dirs_exist_ok=True,symlinks=True)
info_path=app/'Contents/Info.plist'
info=plistlib.loads(info_path.read_bytes());info['NSMicrophoneUsageDescription']='用于八千代实时语音互动。';info_path.write_bytes(plistlib.dumps(info))
installed=Path.home()/'Applications'/app.name
if installed.exists():
    info=plistlib.loads((installed/'Contents/Info.plist').read_bytes())
    if info.get('CFBundleIdentifier')!='com.Tsukuyomi.Yachiyo-Motion-Studio':raise SystemExit('An unrelated application owns the install path')
    backup=ROOT/'artifacts/unified-previous.app'
    if not backup.exists():subprocess.run(['ditto',str(installed),str(backup)],check=True)
installed.parent.mkdir(parents=True,exist_ok=True)
# Desktop may be managed by File Provider, which recreates empty FinderInfo
# during signing. Stage generated code on a local temporary path instead.
with tempfile.TemporaryDirectory(prefix='yachiyo-sign-') as temp:
    staged=Path(temp)/app.name
    subprocess.run(['ditto','--norsrc','--noextattr',str(app),str(staged)],check=True)
    subprocess.run(['codesign','--force','--deep','--sign','-',str(staged)],check=True)
    subprocess.run(['codesign','--verify','--deep','--strict',str(staged)],check=True)
    subprocess.run(['ditto','--norsrc','--noextattr',str(staged),str(installed)],check=True)
subprocess.run(['codesign','--verify','--deep','--strict',str(installed)],check=True)
print('Unified application:',installed)
