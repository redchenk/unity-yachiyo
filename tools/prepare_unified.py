"""Build the macOS WebKit host and stage a verified, private unified runtime."""
from pathlib import Path
import hashlib,json,shutil,subprocess,tarfile,urllib.request

ROOT=Path(__file__).resolve().parents[1]
NODE_VERSION="24.22.2"
NODE_SHA="1f5ac075be50bed4780ea3be19a007fda95ea4d527d28d76d0b91de47d915115"
stage=ROOT/"artifacts/unified-director";stage.mkdir(parents=True,exist_ok=True)
archive=ROOT/"vendor"/f"node-v{NODE_VERSION}-darwin-arm64.tar.xz";archive.parent.mkdir(exist_ok=True)
if not archive.exists():
    urllib.request.urlretrieve(f"https://nodejs.org/dist/v{NODE_VERSION}/{archive.name}",archive)
if hashlib.sha256(archive.read_bytes()).hexdigest()!=NODE_SHA:raise SystemExit("Node archive checksum mismatch")
with tarfile.open(archive) as source:
    base=f"node-v{NODE_VERSION}-darwin-arm64"
    for filename in ["bin/node","LICENSE"]:
        out=stage/"runtime"/("node" if filename=="bin/node" else "NODE-LICENSE")
        out.parent.mkdir(exist_ok=True);out.write_bytes(source.extractfile(base+"/"+filename).read())
(stage/"runtime/node").chmod(0o755)
director=ROOT/"director"
for name in ["tools","src/shared","memory-seeds","licenses","docs","lib","assets","dist/live2d-studio"]:
    source=director/name;target=stage/name
    shutil.copytree(source,target,dirs_exist_ok=True,ignore=shutil.ignore_patterns("__pycache__","tests"))
for name in ["package.json","LICENSE","UPSTREAM.md"]:shutil.copy2(director/name,stage/name)
# Include only installed production dependencies; never copy model/config/profile data.
lock=json.loads((director/"package-lock.json").read_text())
for rel,info in lock["packages"].items():
    if not rel.startswith("node_modules/") or info.get("dev") or info.get("optional"):continue
    source=director/rel
    if not source.is_dir():continue
    shutil.copytree(source,stage/rel,dirs_exist_ok=True,symlinks=True,ignore=shutil.ignore_patterns("node_modules",".cache","test","tests",".git"))
plugin=ROOT/"UnityProject/Assets/Plugins/macOS/YachiyoWebView.bundle"
plugin.parent.mkdir(parents=True,exist_ok=True)
subprocess.run(["xcrun","clang++","-bundle","-fobjc-arc","-arch","arm64","-arch","x86_64","-mmacosx-version-min=11.0","-framework","Cocoa","-framework","WebKit","-framework","QuartzCore",str(ROOT/"native/macos/YachiyoWebView.mm"),"-o",str(plugin)],check=True)
subprocess.run(["codesign","--force","--sign","-",str(plugin)],check=True)
print("Unified resources prepared:",stage)
