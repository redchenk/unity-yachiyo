"""Stage a private, self-contained Windows x64 client, never user profiles/secrets.

Build the saved Unity scene with BuildStudio.BuildWindows and compile
native/windows/build.ps1 first. The existing D:/E:/YachiyoMotion models are reused.
"""
from pathlib import Path
import hashlib,json,shutil,zipfile

ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'artifacts/windows-client'
app=ROOT/'build/Windows'
native=ART/'native'
node=ART/'node-win.zip'
NODE_VERSION='24.22.2'
NODE_SHA='ca1bff5d8ead13c9f2ea404a176db46bd430704d5b5113734049fa365e7799e2'
for name in ['Yachiyo.exe','UnityPlayer.dll','Yachiyo_Data/globalgamemanagers','Yachiyo_Data/level0','Yachiyo_Data/ScriptingAssemblies.json']:
    if not (app/name).is_file():raise SystemExit('Missing Windows build file: '+name)
if hashlib.sha256(node.read_bytes()).hexdigest()!=NODE_SHA:raise SystemExit('Windows Node checksum mismatch')
destination=ART/'Yachiyo-Windows'
if destination.exists():shutil.rmtree(destination)
shutil.copytree(app,destination,ignore=shutil.ignore_patterns('*_BurstDebugInformation_DoNotShip','*.log'))
stage=destination/'Yachiyo_Data/Director'
source=ROOT/'director'
for name in ['tools','src/shared','memory-seeds','licenses','docs','lib','assets','dist/live2d-studio']:
    shutil.copytree(source/name,stage/name,ignore=shutil.ignore_patterns('__pycache__','tests','.DS_Store'))
for name in ['package.json','LICENSE','UPSTREAM.md']:shutil.copy2(source/name,stage/name)
lock=json.loads((source/'package-lock.json').read_text())
for rel,info in lock['packages'].items():
    if not rel.startswith('node_modules/') or info.get('dev') or info.get('optional'):continue
    installed=source/rel
    if not installed.is_dir():continue
    shutil.copytree(installed,stage/rel,dirs_exist_ok=True,ignore=shutil.ignore_patterns('node_modules','.cache','test','tests','.git','*.node','.DS_Store'))
with zipfile.ZipFile(node) as archive:
    for name,out in [('node.exe','node.exe'),('LICENSE','NODE-LICENSE')]:
        target=stage/'runtime'/out;target.parent.mkdir(parents=True,exist_ok=True)
        target.write_bytes(archive.read(f'node-v{NODE_VERSION}-win-x64/{name}'))
(stage/'native').mkdir()
for name in ['YachiyoWebHost.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll']:
    shutil.copy2(native/name,stage/'native'/name)
shutil.copy2(native/'YachiyoLauncher.exe',destination/'启动八千代.exe')
(destination/'启动设置.json').write_text(json.dumps(dict(motionRoot='',motionUrl='http://127.0.0.1:8787',backend='desktop',startMotion=True,stopOwnedMotion=True),ensure_ascii=False,indent=2),encoding='utf-8-sig')
shutil.copy2(ROOT/'native/windows/README.txt',destination/'使用说明.txt')
# Runtime dependencies ship their original notices, including the WebView SDK.
sdk=ART/'webview2-sdk'
for file in sdk.glob('*'):
    if file.is_file() and ('license' in file.name.lower() or 'notice' in file.name.lower()):shutil.copy2(file,stage/'native'/file.name)
manifest={str(file.relative_to(destination)).replace('\\','/'):hashlib.sha256(file.read_bytes()).hexdigest() for file in sorted(destination.rglob('*')) if file.is_file()}
(destination/'SHA256.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
pending=Path(shutil.make_archive(str(ART/'Yachiyo-Windows-x64.pending'),'zip',ART,destination.name))
archive=ART/'Yachiyo-Windows-x64.zip'
pending.replace(archive)
print(archive)
print('SHA256',hashlib.sha256(Path(archive).read_bytes()).hexdigest())
