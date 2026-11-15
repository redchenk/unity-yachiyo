"""Blender --background --python tools/check_cloth_body_contacts.py -- directory

Intersect exported *displayed* cloth with body triangles, independently of the
solver's proxy or collider settings. Keeps anatomical counts for comparisons.
"""
import json, sys
from pathlib import Path
from collections import Counter
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import intersect_ray_tri

def vector(p): return Vector(tuple(p[k] for k in 'xyz'))
def triangles(m): return [tuple(m['triangles'][i:i+3]) for i in range(0,len(m['triangles']),3)]
def crossing(a,b):
    for tri,edge in [(a,b),(b,a)]:
        for i in range(3):
            direction=edge[(i+1)%3]-edge[i]
            if direction.length_squared<1e-14:continue
            hit=intersect_ray_tri(*tri,direction,edge[i],True)
            if hit is not None and .0001<(hit-edge[i]).dot(direction)/direction.length_squared<.9999:return hit
    return None

def check(path):
    data=json.loads(path.read_text());body=next(m for m in data['surfaces'] if m['name']=='Body')
    bv=list(map(vector,body['vertices']));bt=triangles(body)
    regions=[Counter(body['regions'][i] for i in t).most_common(1)[0][0] for t in bt]
    tree=BVHTree.FromPolygons(bv,bt,all_triangles=True);result={}
    for mesh in data['surfaces']:
        if mesh['name']=='Body':continue
        v=list(map(vector,mesh['vertices']));t=triangles(mesh);cloth=BVHTree.FromPolygons(v,t,all_triangles=True)
        counts=Counter();points=[]
        for ci,bi in cloth.overlap(tree):
            region=regions[bi]
            if region=='other':continue
            p=crossing([v[i] for i in t[ci]],[bv[i] for i in bt[bi]])
            if p is not None:counts[region]+=1;points.append({'region':region,'point':list(p)})
        inside={}
        for p in v:
            near,normal,index,distance=tree.find_nearest(p,.08)
            if near is None or regions[index]=='other' or (p-near).dot(normal)>=-.001:continue
            region=regions[index];inside.setdefault(region,[]).append(distance)
        result[mesh['name']]={'intersections':dict(counts),'inside':{r:{'count':len(a),'maxDepth':max(a)} for r,a in inside.items()},'points':points[:120]}
    return result

folder=Path(sys.argv[sys.argv.index('--')+1]);results={}
for path in sorted(folder.glob('*.json')):
    if path.name in ('report.json','body-contacts.json'):continue
    results[path.stem]=check(path)
    print(path.stem,{k:v['intersections'] for k,v in results[path.stem].items()},flush=True)
(folder/'body-contacts.json').write_text(json.dumps(results,indent=2))
