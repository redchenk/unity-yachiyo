"""Fetch pinned public source code, without model weights or changes to existing trees."""
import argparse
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parents[1]
SOURCES={
    'kimodo':('nv-tlabs/kimodo','e1c219c99b4b94ba48be1fb6a1cbc64ecee4c954'),
    'dip':('GuyTevet/motion-diffusion-model','ef8edce6a53c6ab19e53b4d4dcf15bc0bc60a778'),
    'ardy':('nv-tlabs/ARDY','e706e27acc88070918308a10922b8f7174d0825e'),
}
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--skeleton-only',action='store_true',help='Only Kimodo source, as required by skeleton tests')
args=parser.parse_args()
for name,(repository,revision) in SOURCES.items():
    if args.skeleton_only and name!='kimodo':continue
    destination=ROOT/'vendor'/name
    if destination.exists():
        current=subprocess.check_output(['git','-C',str(destination),'rev-parse','HEAD'],text=True).strip()
        if current!=revision:raise SystemExit(f'{destination} has a different revision; existing files were not modified')
        print(f'{name}: pinned source already present; local changes preserved')
        continue
    destination.mkdir(parents=True)
    subprocess.run(['git','init','-q',str(destination)],check=True)
    subprocess.run(['git','-C',str(destination),'remote','add','origin','https://github.com/'+repository+'.git'],check=True)
    subprocess.run(['git','-C',str(destination),'fetch','--depth=1','origin',revision],check=True)
    subprocess.run(['git','-C',str(destination),'checkout','--detach','FETCH_HEAD'],check=True)
    print(f'{name}: {revision}')
