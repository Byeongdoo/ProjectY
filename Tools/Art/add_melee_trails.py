"""Author weapon-following trail recipes in PixelLab; keep base sprites intact."""
import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import re
import time
import zipfile
from PIL import Image, ImageChops, ImageDraw
import pixellab_spirit_batch as pipeline

ROOT=pipeline.ROOT
OUT=ROOT/'artifacts/pixellab/SpiritAnimationQA/Trails'
ART=ROOT/'Client/Assets/Resources/Art/Characters/SpiritsV2'
PLAN=pipeline.read(ROOT/'artifacts/pixellab/SpiritAnimationProduction/ProductionPlan.json')
DIRS=PLAN['directionOrder']
MOTIONS={'MumyeongJangsu':['ATTACK'],'Mangnani':['ATTACK','SKILL_1','SKILL_2','SKILL_3'],'Kkokdusoe':['ATTACK','SKILL_1','SKILL_2','SKILL_3','SKILL_4']}
FACING={'south':(0,1),'south-east':(.707,.707),'east':(1,0),'north-east':(.707,-.707),'north':(0,-1),'north-west':(-.707,-.707),'west':(-1,0),'south-west':(-.707,.707)}
TIP_OVERRIDES={('Kkokdusoe','SKILL_2','south-east'):[[42,39],[46,43],[51,47],[56,52],[58,54],[54,52],[49,49],[39,55]],('MumyeongJangsu','ATTACK','north'):[[61.75,16.25],[46.5,16.25],[42,16],[42,25],[44,32],[46,40],[57,28],[67.75,17.75]]}
LIFT_BELOW_FACE={('ATTACK','south-west'),('ATTACK','west'),('SKILL_1','south'),('SKILL_2','south'),('SKILL_2','west'),('SKILL_3','south'),('SKILL_3','south-east')}


def load(path):
    with Image.open(path) as image:return image.convert('RGBA')


def tip(image,key,origin,reference=None):
    """Find a visible metal edge outside the face/torso; retain measured points."""
    pixels=image.load();w,h=image.size;cx,cy=origin
    candidates=[]
    for y in range(h):
        for x in range(w):
            r,g,b,a=pixels[x,y]
            if a<128:continue
            distance=math.hypot(x-cx,y-cy)
            if distance<(4 if key=='Kkokdusoe' else 13):continue
            # The bright blade is neutral/pink steel; don't track skin or gold armour.
            material=r>130 and abs(r-g)<42 and abs(g-b)<32 and b>r*.7
            if key=='Kkokdusoe':
                material=r>130 and g>85 and r>g and b<g*.85
                if abs(x-cx)<25 and y<cy-15:continue  # brim and hat ornaments
                if distance>42:continue
                if reference and max(abs(pixels[x,y][v]-reference.getpixel((x,y))[v]) for v in range(4))<35:continue
            else:
                if abs(x-cx)<12 and cy-34<y<cy-8:continue  # face/fur
                if distance>53:continue
            if material:candidates.append((distance,x,y))
    if key=='Kkokdusoe' and candidates:
        # Gong faces form large gold islands; the mallet head is a small island.
        remaining={(x,y) for _,x,y in candidates};small=set();heads=[]
        while remaining:
            seed=remaining.pop();component={seed};stack=[seed]
            while stack:
                x,y=stack.pop()
                for neighbor in ((x-1,y),(x+1,y),(x,y-1),(x,y+1)):
                    if neighbor in remaining:
                        remaining.remove(neighbor);component.add(neighbor);stack.append(neighbor)
            xs=[p[0] for p in component];ys=[p[1] for p in component]
            cw=max(xs)-min(xs)+1;ch=max(ys)-min(ys)+1;aspect=max(cw,ch)/min(cw,ch)
            if 4<=len(component)<=120 and min(cw,ch)>=3 and aspect>=1.2:
                heads.append((len(component)*aspect,component))
            if 2<=len(component)<=24:small.update(component)
        if heads:
            component=max(heads,key=lambda item:item[0])[1]
            return [round(sum(x for x,y in component)/len(component),2),round(sum(y for x,y in component)/len(component),2)],len(candidates)
        if small:candidates=[v for v in candidates if (v[1],v[2]) in small]
    if not candidates:return None,0
    candidates.sort(reverse=True)
    best=candidates[:min(4,len(candidates))]
    return [round(sum(p[1] for p in best)/len(best),2),round(sum(p[2] for p in best)/len(best),2)],len(candidates)


def build_recipe(key,motion,direction,frames,fps):
    w,h=frames[0].size
    union=None
    for im in frames:
        box=im.getchannel('A').getbbox()
        if box:union=box if union is None else (min(union[0],box[0]),min(union[1],box[1]),max(union[2],box[2]),max(union[3],box[3]))
    canvas=160 if motion=='SKILL_1' and key=='Mangnani' else 128
    dx=math.floor(canvas/2-(union[0]+union[2])/2+.5);dy=math.floor(canvas/2-(union[1]+union[3])/2+.5)
    source=pipeline.read(ART/key/'SourceInfo.json')['metrics']
    foot=h-(source['pivotPixels']['y']+(h-source['canvasHeight'])/2)
    center=[w/2,foot-24]
    points=[];counts=[]
    fx,fy=FACING[direction]
    for i,im in enumerate(frames):
        point,count=tip(im,key,center,frames[0] if key=='Kkokdusoe' and i else None)
        if point is None:
            point=points[-1] if points else [center[0]-fy*22,center[1]+fx*12]
        points.append(point);counts.append(count)
    manual=TIP_OVERRIDES.get((key,motion,direction))
    if manual:points=manual
    n=len(frames)
    if key=='Kkokdusoe':
        active=list(range(2,min(n-1,6))) if motion in ('ATTACK','SKILL_2','SKILL_3') else list(range(1,n-1))
        width=2.8;ramp=['#9e652d','#ddb866','#fff0b2','#fffbe8']
    elif key=='Mangnani':
        active=list(range(4,min(n-1,10))) if motion=='SKILL_2' else list(range(1 if motion=='SKILL_1' else 2,n-1))
        width=5;ramp=['#8b4253','#c67b84','#eacac3','#fff4e2']
    else:
        active=list(range(1,min(n-1,6)));width=3;ramp=['#6e8e9c','#b5d2d8','#e3eee5','#fff8df']
    layers=[];used=[]
    for index in active:
        old,current=points[index-1],points[index]
        vx0,vy0=old[0]-center[0],old[1]-center[1]
        vx1,vy1=current[0]-center[0],current[1]-center[1]
        a0=math.atan2(vy0,vx0);a1=math.atan2(vy1,vx1)
        delta=(a1-a0+math.pi)%(2*math.pi)-math.pi
        r0=math.hypot(vx0,vy0);r1=math.hypot(vx1,vy1)
        if key=='Kkokdusoe':
            # A mallet leaves a short local wrist stroke, never a sword-sized halo.
            distance=math.hypot(current[0]-old[0],current[1]-old[1])
            if distance<2:continue
            distance=min(distance,14)
            end=current;angle=math.atan2(current[1]-old[1],current[0]-old[0]);start=[end[0]-math.cos(angle)*distance,end[1]-math.sin(angle)*distance]
            normal=[-math.sin(angle),math.cos(angle)]
            outer=[[start[0]+dx,start[1]+dy],[end[0]+dx,end[1]+dy]]
            inner=[[start[0]+normal[0]*width+dx,start[1]+normal[1]*width+dy],[end[0]+dx,end[1]+dy]]
        else:
            # Low-motion preparation/recovery frames get no artificial slash.
            if abs(delta)<.08 or min(r0,r1)<13:continue
            delta=max(-2.5,min(2.5,delta))
            a0=a1-delta
            outer=[];inner=[]
            for j in range(9):
                t=j/8;angle=a0+delta*t;radius=r0*(1-t)+r1*t
                taper=math.sin(math.pi*t)**.7
                outer.append([center[0]+math.cos(angle)*radius+dx,center[1]+math.sin(angle)*radius+dy])
                inner.append([center[0]+math.cos(angle)*(radius-width*taper)+dx,center[1]+math.sin(angle)*(radius-width*taper)+dy])
        polygon=[[round(x,2),round(y,2)] for x,y in outer+inner[::-1]]
        if key=='Kkokdusoe':
            # The short mallet stroke is in front of the sleeve/gong, below the face.
            floor=center[1]-6+dy
            if (motion,direction) in LIFT_BELOW_FACE:
                lift=max(0,floor-min(y for x,y in polygon))
                polygon=[[x,y+lift] for x,y in polygon]
            else:polygon=[[x,max(y,floor)] for x,y in polygon]
        life=[max(0,(index-.22)/(n-1)),min(1,(index+.22)/(n-1))]
        layer={'id':'trail_'+str(index),'type':'shape','z':'back','x':0,'y':0,'points':polygon,'closed':True,'smooth':0,'fill':'edge','bevel':1.2,'outline':False,'life':life,'lo':.1,'fade':0}
        if key=='Kkokdusoe':layer.update(z='front',fill='flat',level=1)
        layers.append(layer)
        if index+1<n-1:
            layers.append({**layer,'id':'fade_'+str(index),'intensity':.68,'opacity':.45,'life':[(index+.78)/(n-1),(index+1.22)/(n-1)]})
        used.append(index)
    recipe={'name':f'ProjectY_{key}_{motion}_{direction}_Trail','canvas':{'w':canvas,'h':canvas},'frames':n,'fps':fps,'loop':False,'origin':[canvas/2,canvas/2],'ramps':{'main':ramp},'outline':{'mode':'none'},'tones':4,'dither':0,'detail':0,'subject':{'x':canvas/2,'y':canvas/2,'align':'center'},'layers':layers}
    return recipe,{'sourceSize':[w,h],'union':union,'subjectOffset':[dx,dy],'trackedTips':points,'trackingMode':'reviewed manual points' if manual else 'material edge candidates','candidatePixels':counts,'activeFrames':used}


def recipe_set():
    entries=[]
    for key,motions in MOTIONS.items():
        source=pipeline.read(ART/key/'AnimationSource.json')
        for motion in motions:
            spec=next((c for c in source['clips'] if c['animation']==motion),None)
            if not spec:continue
            for direction in DIRS:
                paths=sorted((ART/key/'Sprites'/motion/direction).glob('frame_*.png'))
                frames=[load(p) for p in paths]
                recipe,tracking=build_recipe(key,motion,direction,frames,spec['framesPerSecond'])
                tracking['sourceFileHashes']=[hashlib.sha256(p.read_bytes()).hexdigest() for p in paths]
                folder=OUT/key/motion/direction;folder.mkdir(parents=True,exist_ok=True)
                pipeline.write(folder/'Recipe.json',recipe);pipeline.write(folder/'Tracking.json',tracking)
                # Annotated review only. Native game images are not painted by this utility.
                sheet=Image.new('RGB',(frames[0].width*len(frames),frames[0].height),(43,45,53))
                for i,im in enumerate(frames):
                    sheet.paste(im,(i*im.width,0),im)
                    draw=ImageDraw.Draw(sheet);x,y=tracking['trackedTips'][i]
                    draw.ellipse((i*im.width+x-2,y-2,i*im.width+x+2,y+2),outline='#52e4df')
                    draw.text((i*im.width+2,2),str(i+1),fill='white')
                sheet.save(folder/'TrackingReview.png')
                entries.append({'key':key,'motion':motion,'direction':direction,'recipe':str((folder/'Recipe.json').relative_to(ROOT))})
    pipeline.write(OUT/'TrailPlan.json',{'entries':entries,'count':len(entries),'note':'PixelLab renders these measured weapon-following shapes. Base character pixels are preserved.'})
    print('Prepared',len(entries),'directional trail recipes',flush=True)


def parse_export(text):
    base=re.search(r'download (https://api\.pixellab\.ai/mcp/pixel-tools/[\w-]+/):',text)
    game=re.search(r'([a-z0-9-]+-game\.zip)',text)
    if not base or not game:raise RuntimeError('Missing PixelLab game-pack URL')
    return base[1]+game[1]


def unpack(entry,pack,recipe,tracking):
    key,motion,direction=entry['key'],entry['motion'],entry['direction']
    base=ART/key;folder=OUT/key/motion/direction
    source_paths=sorted((base/'Sprites'/motion/direction).glob('frame_*.png'))
    canvas=recipe['canvas']['w'];n=recipe['frames']
    with zipfile.ZipFile(io.BytesIO(pack)) as archive:
        manifest=json.loads(archive.read('manifest.json'))
        def decode(name):
            record=next((c for c in manifest['clips'] if c['name']==name),None)
            result=[Image.new('RGBA',(canvas,canvas)) for _ in range(n)]
            if not record:return result
            cw,ch=record['frame']['w'],record['frame']['h']
            for sheet_info in record['sheets']:
                sheet=Image.open(io.BytesIO(archive.read(sheet_info['file']))).convert('RGBA')
                for i in range(sheet_info['frames']):
                    x=i%sheet_info['columns']*cw;y=i//sheet_info['columns']*ch
                    image=sheet.crop((x,y,x+cw,y+ch))
                    frame=record['start_frame']+sheet_info['first_frame']+i
                    result[frame].paste(image,(manifest['anchor']['x']+record['offset']['x'],manifest['anchor']['y']+record['offset']['y']))
            return result
        effects=decode('whole');composites=decode('whole-with-sprite')
        dx,dy=tracking['subjectOffset'];w,h=tracking['sourceSize']
        shift=((canvas-w)//2-dx,(canvas-h)//2-dy)
        checks=[]
        for i,(effect,composite,path) in enumerate(zip(effects,composites,source_paths)):
            original=load(path)
            changed=0;unintended=0
            for y in range(h):
                for x in range(w):
                    pixel=original.getpixel((x,y))
                    if pixel[3] and composite.getpixel((x+dx,y+dy))!=pixel:
                        changed+=1
                        if effect.getpixel((x+dx,y+dy))[3]==0:unintended+=1
            if unintended:raise RuntimeError(f'PixelLab changed {unintended} pixels outside the trail: {key}/{motion}/{direction}/{i}')
            output=Image.new('RGBA',(canvas,canvas));output.paste(composite,shift)
            separate=Image.new('RGBA',(canvas,canvas));separate.paste(effect,shift)
            for target,image in [(base/'SpritesWithTrail'/motion/direction/f'frame_{i:03d}.png',output),(base/'TrailLayers'/motion/direction/f'frame_{i:03d}.png',separate)]:
                pipeline.texture_meta(target,base,image.size)
                buffer=io.BytesIO();image.save(buffer,format='PNG');pipeline.put_bytes(target,buffer.getvalue())
            checks.append({'frame':i,'changedCharacterPixels':changed,'unintendedCharacterPixelChanges':unintended,'effectPixels':sum(a>0 for a in separate.getchannel('A').get_flattened_data())})
    pipeline.write(folder/'PixelIntegrity.json',{'recipeHash':recipe_hash(recipe),'checks':checks,'sourcePixelsResized':False,'canvasShift':shift,'recipeSubjectOffset':tracking['subjectOffset']})


def recipe_hash(recipe):
    return hashlib.sha256(json.dumps(recipe,sort_keys=True).encode()).hexdigest()


def run(limit=None,workers=1):
    from concurrent.futures import ThreadPoolExecutor
    from threading import local
    thread=local();plan=pipeline.read(OUT/'TrailPlan.json')
    chars={c['key']:c['characterId'] for c in PLAN['characters']}
    pending=[]
    for entry in plan['entries']:
        folder=OUT/entry['key']/entry['motion']/entry['direction']
        recipe=pipeline.read(folder/'Recipe.json')
        if (folder/'PixelIntegrity.json').exists() and pipeline.read(folder/'PixelIntegrity.json').get('recipeHash')==recipe_hash(recipe):continue
        pending.append(entry)
        for part in ('SpritesWithTrail','TrailLayers'):pipeline.ensure_folder(ART/entry['key']/part/entry['motion']/entry['direction'])
    if limit:pending=pending[:limit]
    def work(entry):
        if not hasattr(thread,'client'):thread.client=pipeline.MCP()
        client=thread.client
        folder=OUT/entry['key']/entry['motion']/entry['direction']
        recipe=pipeline.read(folder/'Recipe.json');tracking=pipeline.read(folder/'Tracking.json')
        if not recipe['layers']:raise RuntimeError('No measured swing trail: '+str(folder))
        cached=folder/'RenderedRecipeHash.json'
        if cached.exists() and pipeline.read(cached).get('recipeHash')==recipe_hash(recipe) and (folder/'PixelLabGamePack.zip').exists():
            unpack(entry,(folder/'PixelLabGamePack.zip').read_bytes(),recipe,tracking)
            print('TRAIL_REUSED',entry['key'],entry['motion'],entry['direction'],flush=True)
            return
        reference=entry.get('spriteRef',f"character:{chars[entry['key']]}:{entry['direction']}:{entry['motion']}")
        start=client.call('vfx_workbench',{'argv':['start','--sprite',reference,'--fps',str(recipe['fps'])],'mode':'low','notes':'Add a short measured weapon trail only on active melee swing frames; preserve every original character pixel. Export both separate transparent trail and composited frames.'})
        step=re.search(r'effect step: ([\w-]+)',pipeline.texts(start))[1]
        render=client.call('vfx_workbench',{'argv':[step,'render',json.dumps({'recipe':recipe})],'mode':'low'})
        text=pipeline.texts(render)
        if 'render ok' not in text:raise RuntimeError(text)
        step=re.search(r'effect step: ([\w-]+)',text)[1]
        pipeline.write(folder/'RenderReceipt.json',{'text':text})
        for i,block in enumerate(render.get('content',[])):
            if block.get('type')=='image':
                import base64
                (folder/f'RenderReview_{i}.png').write_bytes(base64.b64decode(block['data']))
        export_args=[step,'export','--name',recipe['name'],'--game-pack']
        if (folder/'ExportReceipt.json').exists():
            previous=pipeline.read(folder/'ExportReceipt.json')['text']
            old_effect=re.search(r'effect id ([\w-]{36})',previous)
            if old_effect:export_args+=['--replace',old_effect[1]]
        export=client.call('vfx_workbench',{'argv':export_args,'mode':'low'})
        text=pipeline.texts(export)
        pipeline.write(folder/'ExportReceipt.json',{'text':text})
        url=parse_export(text);pack=pipeline.download(url);(folder/'PixelLabGamePack.zip').write_bytes(pack)
        unpack(entry,pack,recipe,tracking)
        print('TRAIL_SAVED',entry['key'],entry['motion'],entry['direction'],flush=True)
        time.sleep(.2)
    with ThreadPoolExecutor(max_workers=workers) as pool:
        list(pool.map(work,pending))


def apply():
    import shutil
    plan=pipeline.read(OUT/'TrailPlan.json');summary=[]
    # Verify every output before changing any native clip references.
    for entry in plan['entries']:
        key,motion,direction=entry['key'],entry['motion'],entry['direction']
        folder=OUT/key/motion/direction;recipe=pipeline.read(folder/'Recipe.json')
        integrity=pipeline.read(folder/'PixelIntegrity.json')
        if integrity.get('recipeHash')!=recipe_hash(recipe):raise RuntimeError('Stale recipe output: '+str(folder))
        tracking=pipeline.read(folder/'Tracking.json')
        paths=sorted((ART/key/'Sprites'/motion/direction).glob('frame_*.png'))
        if [hashlib.sha256(p.read_bytes()).hexdigest() for p in paths]!=tracking['sourceFileHashes']:raise RuntimeError('Original source files changed: '+str(folder))
        for item in integrity['checks']:
            frame=item['frame'];original=load(ART/key/'Sprites'/motion/direction/f'frame_{frame:03d}.png');composite=load(ART/key/'SpritesWithTrail'/motion/direction/f'frame_{frame:03d}.png')
            base=Image.new('RGBA',composite.size);base.paste(original,((composite.width-original.width)//2,(composite.height-original.height)//2))
            item['visibleAddedPixels']=sum(c>0 and b==0 for c,b in zip(composite.getchannel('A').get_flattened_data(),base.getchannel('A').get_flattened_data()))
            if key=='Kkokdusoe':
                item['visibleAddedPixels']+=item.get('changedCharacterPixels',0)
        if sum(c['visibleAddedPixels'] for c in integrity['checks'])<3:raise RuntimeError('Trail hidden behind body: '+str(folder))
        pipeline.write(folder/'PixelIntegrity.json',integrity)
    for entry in plan['entries']:
        key,motion,direction=entry['key'],entry['motion'],entry['direction']
        folder=OUT/key/motion/direction;recipe=pipeline.read(folder/'Recipe.json')
        integrity=pipeline.read(folder/'PixelIntegrity.json')
        if integrity.get('recipeHash')!=recipe_hash(recipe):raise RuntimeError('Stale recipe output: '+str(folder))
        if any(c.get('unintendedCharacterPixelChanges',c['changedCharacterPixels']) for c in integrity['checks']):raise RuntimeError('Character integrity failure outside trail')
        if sum(c['effectPixels'] for c in integrity['checks'])<3:raise RuntimeError('Trail is not visible: '+str(folder))
        spec=next(t for t in PLAN['tasks'] if t['key']==key and t['animation']==motion)
        base=ART/key
        native=base/'Animations'/(motion+'_'+direction.upper().replace('-','_')+'.anim')
        backup=OUT/'NativeClipsWithoutTrail'/key/native.name
        backup.parent.mkdir(parents=True,exist_ok=True)
        if not backup.exists():
            shutil.copy2(native,backup);shutil.copy2(str(native)+'.meta',str(backup)+'.meta')
        pipeline.make_clip(base,spec,direction,frame_folder='SpritesWithTrail')
        summary.append({**entry,'activeFrames':[c['frame'] for c in integrity['checks'] if c['effectPixels']>0],'unintendedCharacterPixelChanges':0,'intentionalTrailOverlapPixels':sum(c['changedCharacterPixels'] for c in integrity['checks'])})
    for key in MOTIONS:
        rows=[s for s in summary if s['key']==key]
        source=pipeline.read(ART/key/'AnimationSource.json')
        for motion in MOTIONS[key]:
            spec=next(c for c in source['clips'] if c['animation']==motion)
            for label,frame_root in [('WithTrail','SpritesWithTrail'),('TrailOnly','TrailLayers')]:
                images=[[load(p) for p in sorted((ART/key/frame_root/motion/d).glob('frame_*.png'))] for d in DIRS]
                width=max(im.width for row in images for im in row);height=max(im.height for row in images for im in row)
                sheet=Image.new('RGBA',(width*spec['frameCount'],height*8))
                for y,row in enumerate(images):
                    for x,im in enumerate(row):sheet.paste(im,(x*width+(width-im.width)//2,y*height+(height-im.height)//2))
                target=ART/key/'Sheets'/(key+'_'+motion+'_'+label+'.png')
                pipeline.texture_meta(target,ART/key,sheet.size,centered=True)
                buffer=io.BytesIO();sheet.save(buffer,format='PNG');pipeline.put_bytes(target,buffer.getvalue())
                layout={**spec,'cellWidth':width,'cellHeight':height,'directions':DIRS,'frameRoot':frame_root,'nativePixelsResized':False}
                pipeline.write(target.with_suffix('.json'),layout);pipeline.ensure_meta(target.with_suffix('.json'),text=True)
            spec['nativeClipFrameRoot']='SpritesWithTrail'
            spec['sourceFrameRoot']='Sprites'
            spec['trailLayerRoot']='TrailLayers'
        source['visualApproval']='QA requires revisions; see artifacts/pixellab/SpiritAnimationQA/QAReport.md'
        pipeline.write(ART/key/'AnimationSource.json',source)
        path=ART/key/'TrailSource.json'
        pipeline.write(path,{'source':'PixelLab VFX workbench','originalSourceFramesPreserved':True,'unintendedPixelChangesOutsideTrail':0,'pixelsPerUnit':32,'bakedFrameRoot':'SpritesWithTrail','separateLayerRoot':'TrailLayers','directionalClips':rows})
        pipeline.ensure_meta(path,text=True)
    total_frames=sum(len(pipeline.read(OUT/s['key']/s['motion']/s['direction']/'PixelIntegrity.json')['checks']) for s in summary)
    pipeline.write(OUT/'TrailDelivery.json',{'clips':len(summary),'frames':total_frames,'entries':summary,'originalSourceFramesPreserved':True,'unintendedPixelChangesOutsideTrail':0,'nativeClipsUseTrailFrames':True})
    print('TRAILS_APPLIED',len(summary),'native clips',flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('mode',choices=['prepare','run','apply']);parser.add_argument('--limit',type=int);parser.add_argument('--workers',type=int,choices=range(1,5),default=1);args=parser.parse_args()
    if args.mode=='prepare':recipe_set()
    elif args.mode=='run':run(args.limit,args.workers)
    else:apply()
