"""Run with Blender --background --python tools/check_sleeve_contacts.py -- snapshots...
Checks the rendered sleeve surfaces against body and dress triangles.
"""
import json, math, sys
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import intersect_ray_tri

def vec(p): return Vector(tuple(p[k] for k in 'xyz'))
def smooth(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def deform(m):
    verts=[vec(p) for p in m['vertices']]
    if m.get('contactsApplied',False): return verts
    if not(m['name'].startswith('cloth') or m['name'].startswith('sleeve-hem-')): return verts
    waist,up,right,forward=(vec(m[k]) for k in ['waist','up','right','forward'])
    for i,world in enumerate(verts):
        contact=m['contacts'][i];side=contact['x'];free=smooth(.001,.03,contact['y']);sleeve=abs(side)>.05
        height=(world-waist).dot(up);shift=0
        if not(height<.02 or (sleeve and free>0)):continue
        direction=vec(m['leftDirection' if side<0 else 'rightDirection']).normalized()
        for j in range(int(m['count'])):
            av,bv=m['capsuleA'][j],m['capsuleB'][j];body=bv['w']>.5
            if bv['w']>1.5:continue
            if body and (not sleeve or free<=0):continue
            a,b=vec(av),vec(bv);axis=b-a
            t=max(0,min(1,(world-a).dot(axis)/max(axis.length_squared,.000001)))
            center=a+axis*t;away=world-center;distance=away.length;radius=av['w']+(.012 if body else m['padding'])
            if sleeve:
                if not body:radius+=m['sleeveMargin']
                along=away.dot(direction);perp2=max(0,away.length_squared-along*along)
                if perp2<radius*radius:
                    delta=math.sqrt(radius*radius-perp2)-along
                    shift=max(shift,(max(delta,0)+.012*math.log1p(math.exp(-abs(delta)/.012)))*(free if body else 1))
            elif distance<radius+.20:
                clearance=radius+.035*math.log1p(math.exp((distance-radius)/.035))
                world=center+(away/distance if distance>.00001 else Vector((0,0,1)))*clearance
        if sleeve and free>0:
            relative=world-waist;below=-relative.dot(up)
            if 0<below<1.02:
                t=max(0,min(1,below/.8));rx=.20+(.54-.20)*t;rz=.28+(.42-.28)*t
                p=Vector((relative.dot(right)/rx,(relative.dot(forward)+.025)/rz));d=Vector((direction.dot(right)/rx,direction.dot(forward)/rz))
                a=d.dot(d);b=p.dot(d);c=p.dot(p)-1
                if c<0 and a>.0001:shift=max(shift,((-b+math.sqrt(max(0,b*b-a*c)))/a+.015)*free)
        if sleeve:world+=direction*shift*.8
        verts[i]=world
    return verts
def intersect(a,b):
    for tri,edges in [(a,b),(b,a)]:
        for i in range(3):
            origin=edges[i];delta=edges[(i+1)%3]-origin
            if delta.length_squared<1e-15:continue
            hit=intersect_ray_tri(*tri,delta,origin,True)
            if hit is not None and .0001<(hit-origin).dot(delta)/delta.length_squared<.9999:return hit
    return None
def check(path,hems_only=False):
    data=json.load(open(path));groups={};sleeve_vertices=[];sleeve_faces=[]
    for m in data['meshes']:
        v=deform(m);tris=[tuple(m['triangles'][i:i+3]) for i in range(0,len(m['triangles']),3)]
        if m['name']=='cloth1':
            faces=[] if hems_only else [t for t in tris if min(m['contacts'][i]['y'] for i in t)>.03];offset=len(sleeve_vertices);sleeve_vertices.extend(v);sleeve_faces.extend(tuple(i+offset for i in t) for t in faces)
            faces=[t for t in tris if all(abs(m['contacts'][i]['x'])<.05 for i in t)];groups['bodice']=(v,faces,BVHTree.FromPolygons(v,faces,all_triangles=True))
        elif m['name'].startswith('sleeve-'):
            offset=len(sleeve_vertices);sleeve_vertices.extend(v);sleeve_faces.extend(tuple(i+offset for i in t) for t in tris)
        elif m['name']=='Body' or m['name']=='cloth2':groups[m['name']]=(v,tris,BVHTree.FromPolygons(v,tris,all_triangles=True))
    groups['sleeves']=(sleeve_vertices,sleeve_faces,BVHTree.FromPolygons(sleeve_vertices,sleeve_faces,all_triangles=True))
    sv,st,sb=groups['sleeves'];result={}
    for name,(v,t,b) in groups.items():
        if name=='sleeves':continue
        hits=[]
        for si,oi in sb.overlap(b):
            hit=intersect([sv[i] for i in st[si]],[v[i] for i in t[oi]])
            if hit is not None:hits.append(list(hit))
        result[name]={'triangle_intersections':len(hits),'height_bins':{str(round(k*.1,1)):sum(k*.1<=h[1]<(k+1)*.1 for h in hits) for k in range(20) if any(k*.1<=h[1]<(k+1)*.1 for h in hits)},'points':hits[:60]}
    out=Path(path.replace('-surfaces.json','-hem-contacts.json' if hems_only else '-sleeve-contacts.json'));out.write_text(json.dumps(result,indent=2))
    print(Path(path).name,{name:r['triangle_intersections'] for name,r in result.items()},flush=True)
args=sys.argv[sys.argv.index('--')+1:]
for path in args:
    if path!='--hems-only':check(path,'--hems-only' in args)
