"""Import only the user's licensed MC2 core (no examples) into this local project."""
import argparse,hashlib,json,tarfile
from pathlib import Path,PurePosixPath
parser=argparse.ArgumentParser();parser.add_argument('package',type=Path);args=parser.parse_args()
root=Path(__file__).resolve().parents[2]
with tarfile.open(args.package) as archive:
 count=0
 for member in archive:
  if not member.name.endswith('/pathname'):continue
  name=archive.extractfile(member).read().decode('utf-8-sig').splitlines()[0]
  path=PurePosixPath(name)
  if '..' in path.parts or path.is_absolute() or path.parts[:2]!=('Assets','MagicaCloth2'):raise ValueError(f'Unexpected package path: {name}')
  if 'Example (Can be deleted)' in path.parts:continue
  dest=root/'UnityProject'/name;prefix=member.name.rsplit('/',1)[0]
  try:asset=archive.getmember(prefix+'/asset')
  except KeyError:asset=None
  if asset is None:dest.mkdir(parents=True,exist_ok=True)
  else:
   dest.parent.mkdir(parents=True,exist_ok=True);data=archive.extractfile(asset).read()
   if dest.exists() and dest.read_bytes()!=data:raise SystemExit(f'Refusing to overwrite modified package file: {dest}. Use a fresh licensed asset folder.')
   dest.write_bytes(data);count+=1
  try:meta=archive.extractfile(prefix+'/asset.meta').read()
  except KeyError:continue
  dest.with_name(dest.name+'.meta').write_bytes(meta)
print(f'Imported {count} licensed core assets; no examples. Run prepare_unity66.py on Unity 6.6.')
