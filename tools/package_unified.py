"""Package and install the private application outside Desktop file-provider metadata."""
from pathlib import Path
import plistlib,shutil,subprocess,tempfile
ROOT=Path(__file__).resolve().parents[1]
app=ROOT/'build/Yachiyo Motion Studio.app'
source=ROOT/'artifacts/unified-director'
if not (source/'runtime/node').is_file():raise SystemExit('Run tools/prepare_unified.py first')
# Signing alone cannot detect a nominally successful Unity build that omitted
# its serialized scene data. Never replace the installed app with such a build.
for required in ['globalgamemanagers','ScriptingAssemblies.json','level0']:
    if not (app/'Contents/Resources/Data'/required).is_file():
        raise SystemExit('Unity scene data missing: '+required+'; run BuildStudio.BuildMac with a clean build cache')
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
    # Overlaying bundles leaves files removed by a clean Unity build, invalidating
    # the resource seal. Verify a fresh sibling, then swap with rollback.
    with tempfile.TemporaryDirectory(prefix='.yachiyo-install-',dir=installed.parent) as pending:
        replacement=Path(pending)/app.name
        previous=Path(pending)/'previous.app'
        subprocess.run(['ditto','--norsrc','--noextattr',str(staged),str(replacement)],check=True)
        subprocess.run(['codesign','--verify','--deep','--strict',str(replacement)],check=True)
        if installed.exists():installed.rename(previous)
        try:
            replacement.rename(installed)
        except Exception:
            if previous.exists():previous.rename(installed)
            raise
subprocess.run(['codesign','--verify','--deep','--strict',str(installed)],check=True)
print('Unified application:',installed)
