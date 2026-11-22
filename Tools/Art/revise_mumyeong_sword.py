"""Measured weapon-only PixelLab edits; originals stay in the revision archive."""
import argparse,base64,hashlib,io,json,math,re,shutil,time,zipfile
from pathlib import Path
from PIL import Image,ImageDraw
import pixellab_spirit_batch as p

ROOT=p.ROOT
OUT=ROOT/'artifacts/pixellab/MumyeongJangsuSwordRevision'
ART=ROOT/'Client/Assets/Resources/Art/Characters/SpiritsV2/MumyeongJangsu'
CHAR='d08b8efa-ee49-477d-8db1-fbe0512ccdda'

def rgba(im):return im.convert('RGBA')
def steel(c):
    r,g,b,a=c
    return a>150 and r>100 and max(r,g,b)-min(r,g,b)<48 and b>r*.72

def blade(im,foot):
    w,h=im.size;cx=w/2
    points={(x,y) for y in range(h) for x in range(w) if steel(im.getpixel((x,y))) and y>foot-35 and not (abs(x-cx)<10 and y<foot-7)}
    groups=[]
    while points:
        seed=points.pop();group={seed};todo=[seed]
        while todo:
            x,y=todo.pop()
            for dx in (-1,0,1):
                for dy in (-1,0,1):
                    v=(x+dx,y+dy)
                    if v in points:points.remove(v);group.add(v);todo.append(v)
        if len(group)>=4:
            xs=[v[0] for v in group];ys=[v[1] for v in group]
            span=math.hypot(max(xs)-min(xs),max(ys)-min(ys))
            extent=max(abs(x-cx) for x,y in group)
            if span>=4 and extent>=11:groups.append((span+len(group)*.2+extent*.2,group))
    if not groups:return None
    group=max(groups,key=lambda v:v[0])[1]
    # Hilt is the end nearest the lower torso. Keep the hand and gold guard.
    hand=min(group,key=lambda q:math.hypot(q[0]-cx,q[1]-(foot-18)))
    tip=max(group,key=lambda q:math.hypot(q[0]-hand[0],q[1]-hand[1]))
    if math.dist(hand,tip)<5:return None
    return {'hand':list(hand),'tip':list(tip),'pixels':[list(v) for v in sorted(group)]}

def prepare():
    OUT.mkdir(parents=True,exist_ok=True)
    backup=OUT/'Before'
    if not backup.exists():shutil.copytree(ART,backup)
    source=p.read(ART/'AnimationSource.json');entries=[]
    for spec in source['clips']:
        for direction in spec['directions']:
            paths=sorted((backup/'Sprites'/spec['animation']/direction).glob('frame_*.png'))
            for i,path in enumerate(paths):
                im=rgba(Image.open(path));foot=im.height-(2+(im.height-64)/2)
                measured=blade(im,foot)
                entries.append({'motion':spec['animation'],'direction':direction,'frame':i,'path':str(path.relative_to(ROOT)),'size':list(im.size),'blade':measured})
    p.write(OUT/'Measurements.json',{'entries':entries})
    print('measured',len(entries),'found',sum(e['blade'] is not None for e in entries))
    for motion in [c['animation'] for c in source['clips']]:
        rows=[e for e in entries if e['motion']==motion];n=max(e['frame'] for e in rows)+1
        sheet=Image.new('RGB',(n*180,8*180),(38,42,49));draw=ImageDraw.Draw(sheet)
        for e in rows:
            col=e['frame'];row=spec['directions'].index(e['direction']);im=rgba(Image.open(ROOT/e['path']));im=im.resize((168,168),Image.Resampling.NEAREST)
            sheet.paste(im,(col*180,row*180+12),im);draw.text((col*180+2,row*180),e['direction']+' '+str(col),fill='white')
            if e['blade']:
                for name,color in [('hand','#ff4747'),('tip','#36ffff')]:
                    x,y=e['blade'][name];x=col*180+x*168/e['size'][0];y=row*180+12+y*168/e['size'][1];draw.ellipse((x-3,y-3,x+3,y+3),outline=color,width=1)
        sheet.save(OUT/(motion+'_Measurements.png'))

if __name__=='__main__':
    args=argparse.ArgumentParser();args.add_argument('mode',choices=['prepare']);a=args.parse_args()
    if a.mode=='prepare':prepare()
