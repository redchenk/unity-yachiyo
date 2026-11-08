"""Extract the user's FBX/textures/materials without importing VRChat editor scripts."""
import json
import re
from pathlib import Path, PurePosixPath
import tarfile

ROOT=Path(__file__).resolve().parents[1]
packages=list((ROOT/"月见一万代").glob("*.unitypackage"))
if len(packages)!=1: raise SystemExit("Expected exactly one character unitypackage in 月见一万代")
destination=ROOT/"UnityProject"
report={"source":str(packages[0].relative_to(ROOT)),"imported":[],"excluded":[]}
with tarfile.open(packages[0]) as archive:
    for item in archive.getmembers():
        if not item.name.endswith("/pathname"):continue
        name=archive.extractfile(item).read().decode().strip()
        if name=="Assets/#八千代/#OkaRuto_月见一万代.prefab":
            guid=item.name.rsplit("/",1)[0]
            source=archive.extractfile(guid+"/asset").read().decode()
            blocks=list(re.finditer(r"^--- !u!(\d+) &(-?\d+)\n(.*?)(?=^---|\Z)",source,re.M|re.S))
            external={m.group(2) for m in blocks if m.group(1)=="114"}
            output=source[:blocks[0].start()]
            for block in blocks:
                if block.group(1)=="114":continue
                content=block.group(0)
                for identifier in external:content=content.replace("  - component: {fileID: "+identifier+"}\n","")
                output+=content
            # Preserve the author's accessory hierarchy and native Aim/Rotation constraints.
            runtime=destination/"Assets/#八千代/YachiyoRuntime.prefab"
            runtime.parent.mkdir(parents=True,exist_ok=True);runtime.write_text(output)
            report["imported"].append(str(runtime.relative_to(destination)))
            continue
        if not name.startswith("Assets/#八千代/") or PurePosixPath(name).suffix not in (".fbx",".png",".mat"):
            report["excluded"].append(name);continue
        path=(destination/name).resolve()
        if not path.is_relative_to(destination.resolve()):raise ValueError("Unsafe asset path")
        guid=item.name.rsplit("/",1)[0];path.parent.mkdir(parents=True,exist_ok=True)
        # Re-running is safe: preserve materials already adapted by the Unity builder.
        if not path.exists():path.write_bytes(archive.extractfile(guid+"/asset").read())
        meta=Path(str(path)+".meta")
        if not meta.exists():meta.write_bytes(archive.extractfile(guid+"/asset.meta").read())
        report["imported"].append(name)
(ROOT/"artifacts").mkdir(exist_ok=True)
(ROOT/"artifacts/package-inventory.json").write_text(json.dumps(report,ensure_ascii=False,indent=2))
print("Imported",len(report["imported"]),"character assets")
