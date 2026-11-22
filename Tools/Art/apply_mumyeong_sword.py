"""Publish the reviewed PixelLab sword revision without changing asset GUIDs."""
import argparse,base64,hashlib,io,json,re,zipfile
from PIL import Image,ImageDraw
import pixellab_spirit_batch as p

ROOT=p.ROOT;OUT=ROOT/'artifacts/pixellab/MumyeongJangsuSwordRevision'
ART=ROOT/'Client/Assets/Resources/Art/Characters/SpiritsV2/MumyeongJangsu'
KEY='MumyeongJangsu';NEW='6f8c501b-a360-4d0a-85fd-556e6a2e0b9d'

def load(path):return Image.open(path).convert('RGBA')
def blob(im):
    stream=io.BytesIO();im.save(stream,format='PNG');return stream.getvalue()

def fetch_extra():
    client=p.MCP();job='7585b53d-a2db-4515-bd5a-b6e5286defda'
    result=client.call('get_image',{'job_id':job});text=p.texts(result)
    if 'status: completed' not in text:raise RuntimeError(text)
    p.write(OUT/'RotationEditReceipt.json',{'text':text,'jobId':job})
    inline=[base64.b64decode(b['data']) for b in result.get('content',[]) if b.get('type')=='image']
    dirs=p.read(OUT/'Before/AnimationSource.json')['clips'][0]['directions']
    folder=OUT/'EditedRotations';folder.mkdir(exist_ok=True)
    sheet=Image.new('RGB',(9*160,320),(38,42,49));draw=ImageDraw.Draw(sheet)
    for i,name in enumerate([*dirs,'front']):
        data=inline[i] if len(inline)==9 else p.download('https://api.pixellab.ai/mcp/images/'+job+'/download?index='+str(i))
        target=OUT/'EditedFront.png' if name=='front' else folder/(name+'.png');target.write_bytes(data)
        image=load(target);assert image.size==(80,80)
        original=load(OUT/'Before'/('MumyeongJangsu_Front.png' if name=='front' else 'Sprites/Rotations/'+name+'.png'))
        before=Image.new('RGBA',(80,80));before.paste(original,(8,8))
        for row,im in enumerate((before,image)):
            view=im.resize((160,160),Image.Resampling.NEAREST);sheet.paste(view,(i*160,row*160),view)
        draw.text((i*160+2,2),name,fill='white')
    sheet.save(OUT/'RotationComparison.png');print('Saved 9 padded weapon edits')

def fetch_tip():
    client=p.MCP();job='ad02f0ef-63af-41e9-a5d0-42fb7cd68395'
    result=client.call('get_image',{'job_id':job});text=p.texts(result)
    if 'status: completed' not in text:raise RuntimeError(text)
    p.write(OUT/'FrontTipEditReceipt.json',{'jobId':job,'text':text})
    inline=[base64.b64decode(b['data']) for b in result.get('content',[]) if b.get('type')=='image']
    for i,target in enumerate((OUT/'EditedRotations/south.png',OUT/'EditedFrontTip.png')):
        data=inline[i] if len(inline)==2 else p.download('https://api.pixellab.ai/mcp/images/'+job+'/download?index='+str(i))
        target.write_bytes(data);image=load(target);assert image.size==(96,96)
        box=image.getchannel('A').getbbox();assert box[0]>0 and box[1]>0 and box[2]<96 and box[3]<96,'Visible pixels touch the canvas edge'
    print('Saved complete sword tips with transparent margin')

def align_rotations():
    client=p.MCP();dirs=p.read(OUT/'Before/AnimationSource.json')['clips'][0]['directions']
    refs=['job:ad02f0ef-63af-41e9-a5d0-42fb7cd68395:0']+['job:7585b53d-a2db-4515-bd5a-b6e5286defda:'+str(i) for i in range(1,8)]
    result=client.call('pixelart_workbench',{'argv':['join',*refs,'--as','animation'],'mode':'low'})
    text=p.texts(result);match=re.search(r'image_id: ([\w-]+)',text)
    if not match:raise RuntimeError(text)
    source_id=match[1]
    result=client.call('pixelart_workbench',{'argv':['join',source_id,'--as','sheet','--columns','8'],'mode':'low'})
    match=re.search(r'image_id: ([\w-]+)',p.texts(result))
    if not match:raise RuntimeError(p.texts(result))
    native=p.download('https://api.pixellab.ai/mcp/pixel-tools/'+match[1]+'/image.png');(OUT/'UnalignedRotations.png').write_bytes(native)
    native_sheet=load(OUT/'UnalignedRotations.png');assert native_sheet.size==(768,96)
    # Measure boot baseline inside the body column, excluding the enlarged blade.
    changes=[];offsets=[]
    for i,d in enumerate(dirs):
        image=native_sheet.crop((i*96,0,(i+1)*96,96))
        box=image.crop((36,48,60,96)).getchannel('A').getbbox()
        foot=box[3]+48;dy=78-foot;offsets.append(dy)
        if not dy:continue
        changes.append({'operation':'brush','layer':'image','frames':[i+1],'points':[[23,23],[71,23],[23,71],[71,71]],'size':48,'shape':'square','mode':'erase','reason':'Clear before integer anchor translation'})
        region=[0,0,95,95-dy] if dy>0 else [0,-dy,95,95]
        changes.append({'operation':'copy_region','layer':'image','frames':[i+1],'source_layer':'image','source_frame':i+1,'region':region,'at':[0,max(0,dy)],'reason':'Match original boot anchor; no pixel resampling'})
    result=client.call('pixelart_workbench',{'argv':['edit',source_id,'--changes',json.dumps(changes)],'mode':'low'})
    text=p.texts(result);match=re.search(r'image_id: ([\w-]+)',text)
    if not match:raise RuntimeError(text)
    edited_id=match[1]
    result=client.call('pixelart_workbench',{'argv':['join',edited_id,'--as','sheet','--columns','8'],'mode':'low'})
    text=p.texts(result);match=re.search(r'image_id: ([\w-]+)',text)
    if not match:raise RuntimeError(text)
    data=p.download('https://api.pixellab.ai/mcp/pixel-tools/'+match[1]+'/image.png')
    (OUT/'AlignedRotations.png').write_bytes(data);sheet=load(OUT/'AlignedRotations.png');assert sheet.size==(768,96),sheet.size
    folder=OUT/'AlignedRotations';folder.mkdir(exist_ok=True)
    for i,d in enumerate(dirs):
        image=sheet.crop((i*96,0,(i+1)*96,96));(folder/(d+'.png')).write_bytes(blob(image))
        box=image.crop((36,48,60,96)).getchannel('A').getbbox();assert box[3]+48==78
    (OUT/'AlignedFront.png').write_bytes((folder/'south.png').read_bytes())
    p.write(OUT/'RotationAnchorAlignment.json',{'sourceId':source_id,'editedId':edited_id,'sheetId':match[1],'integerYOffsets':dict(zip(dirs,offsets)),'canvas':96,'bootBaseline':78,'pixelResampling':False})
    print('Aligned 8 rotations and front to the original foot origin')

def selected_motions():
    result={}
    for motion,file in [('ATTACK','SelectedNorthAttack.json'),('IDLE','SelectedNorthIdle.json')]:
        if (OUT/file).exists():result[(motion,'north')]=p.read(OUT/file)
    if (OUT/'SelectedDiagonalAttacks.json').exists():
        for d,record in p.read(OUT/'SelectedDiagonalAttacks.json').items():result[('ATTACK',d)]=record
    return result

def select_north(strict=False):
    archive=OUT/'RevisionCharacter.zip'
    if not archive.exists():archive=OUT/'NorthRevision.zip'
    if not archive.exists():return
    with zipfile.ZipFile(archive) as z:
        data=json.loads(z.read('metadata.json'));remote=next(s for s in data['states'] if s['character']['id']==NEW)
        for (motion,direction),selected in selected_motions().items():
            if selected.get('sourceFolder'):
                folder=OUT/'Edited'/motion/direction;folder.mkdir(parents=True,exist_ok=True)
                paths=sorted((OUT/selected['sourceFolder']).glob('frame_*.png'))
                if strict:assert len(paths)==8
                for path in paths:(folder/path.name).write_bytes(path.read_bytes())
                continue
            name=selected['animationName']
            if name not in remote['frames']['animations'] or direction not in remote['frames']['animations'][name]:
                if strict:raise RuntimeError('Selected motion missing from archive: '+name+'/'+direction)
                continue
            folder=OUT/'Edited'/motion/direction;folder.mkdir(parents=True,exist_ok=True)
            for i,path in enumerate(remote['frames']['animations'][name][direction]):(folder/f'frame_{i:03d}.png').write_bytes(z.read(path))

def previews():
    select_north()
    source=p.read(OUT/'Before/AnimationSource.json')
    for spec in source['clips']:
        sheet=Image.new('RGB',(spec['frameCount']*288,8*288),(38,42,49));draw=ImageDraw.Draw(sheet)
        for y,d in enumerate(spec['directions']):
            for i in range(spec['frameCount']):
                path=OUT/'Edited'/spec['animation']/d/f'frame_{i:03d}.png'
                if not path.exists():continue
                repaired=OUT/'TipRepaired'/path.relative_to(OUT/'Edited')
                if repaired.exists():path=repaired
                im=load(path);display=im.resize((im.width*2,im.height*2),Image.Resampling.NEAREST)
                sheet.paste(display,(i*288+(288-display.width)//2,y*288+(288-display.height)//2),display)
                draw.text((i*288+3,y*288+3),d+' '+str(i+1),fill='white')
        sheet.save(OUT/(spec['animation']+'_Revised.png'))

def publish():
    select_north(strict=True)
    jobs=p.read(OUT/'Jobs.json');assert jobs.get('status')=='staged_for_review'
    for motion,file in [('ATTACK','SelectedNorthAttack.json'),('IDLE','SelectedNorthIdle.json')]:
        if (OUT/file).exists():
            selected=p.read(OUT/file)
            next(j for j in jobs['jobs'] if j['kind']=='north' and j['motion']==motion).update(jobId=selected['jobId'],groupId=selected['groupId'],selectedAnimation=selected['animationName'])
    source=p.read(ART/'AnimationSource.json');before=p.read(OUT/'Before/AnimationSource.json')
    total=0
    for spec in source['clips']:
        images=[]
        for direction in spec['directions']:
            row=[]
            paths=sorted((OUT/'Edited'/spec['animation']/direction).glob('frame_*.png'));assert len(paths)==spec['frameCount']
            for i,path in enumerate(paths):
                repaired=OUT/'TipRepaired'/path.relative_to(OUT/'Edited')
                if repaired.exists():path=repaired
                im=load(path);target=ART/'Sprites'/spec['animation']/direction/f'frame_{i:03d}.png'
                p.texture_meta(target,ART,im.size);p.put_bytes(target,path.read_bytes());row.append(im);total+=1
            images.append(row)
            task={'animation':spec['animation'],'frameCount':spec['frameCount'],'fps':spec['framesPerSecond'],'loop':spec['loop']}
            if spec['animation']!='ATTACK':p.make_clip(ART,task,direction)
        w=max(im.width for row in images for im in row);h=max(im.height for row in images for im in row)
        sheet=Image.new('RGBA',(w*spec['frameCount'],h*8))
        for y,row in enumerate(images):
            for x,im in enumerate(row):sheet.paste(im,(x*w+(w-im.width)//2,y*h+(h-im.height)//2))
        target=ART/'Sheets'/(KEY+'_'+spec['animation']+'.png')
        p.texture_meta(target,ART,sheet.size,centered=True);p.put_bytes(target,blob(sheet))
        spec.update(cellWidth=w,cellHeight=h,revision='LargeSaber_NorthFrontAction',directionSources={})
        for d in spec['directions']:
            entries=[j for j in jobs['jobs'] if j['motion']==spec['animation'] and j['direction']==d]
            spec['directionSources'][d]=[{'jobId':j['jobId'],'sourceKind':j['kind'],**({'groupId':j['groupId'],'characterId':NEW} if j['kind']=='north' else {'startFrame':j['start']})} for j in entries]
            selected=selected_motions().get((spec['animation'],d))
            if selected:spec['directionSources'][d]=[{**selected,'characterId':NEW,'sourceKind':'reviewed forward attack/guard revision'}]
        p.write(target.with_suffix('.json'),spec)
    assert total==608
    for path in sorted((OUT/'AlignedRotations').glob('*.png')):
        target=ART/'Sprites/Rotations'/path.name;im=load(path);p.texture_meta(target,ART,im.size);p.put_bytes(target,path.read_bytes())
    front=OUT/'AlignedFront.png';assert load(front).size==(96,96)
    front_target=ART/(KEY+'_Front.png');p.texture_meta(front_target,ART,(96,96));p.put_bytes(front_target,front.read_bytes())
    info=p.read(ART/'SourceInfo.json');info.update(revision='LargeSaber_NorthFrontAction',phase='Production sword revision',revisionCharacterId=NEW)
    info['designDescription']=info['designDescription'].replace('short curved Korean saber','large readable curved Korean saber')
    info['metrics'].update(canvasWidth=96,canvasHeight=96,pivotPixels={'x':48.0,'y':18.0})
    box=load(front).getchannel('A').getbbox();info['metrics'].update(visibleWidth=box[2]-box[0],visibleHeight=box[3]-box[1],alphaX=box[0],alphaY=box[1])
    info['transparentPaddingAddedPerSide']=16;info['bodyPixelsResized']=False
    p.write(ART/'SourceInfo.json',info)
    source.update(revisionCharacterId=NEW,revision='LargeSaber_NorthFrontAction',tipRepair='PixelLab exact 128px padding, pointed blade completions and detached edge debris removal; original interior pixels retained',visualApproval='Sword enlargement and north animation revision, reviewed separately; other QA observations remain.')
    p.write(ART/'AnimationSource.json',source)
    jobs['unityPublished']=True;p.write(OUT/'Jobs.json',jobs)
    print('PUBLISHED',total,'frames / 88 clips; existing GUIDs retained')

def trail_refs():
    client=p.MCP();jobs=p.read(OUT/'Jobs.json');plan=p.read(ROOT/'artifacts/pixellab/SpiritAnimationQA/Trails/TrailPlan.json')
    for entry in plan['entries']:
        if entry['key']!=KEY:continue
        d=entry['direction'];folder=OUT/'TrailReferences';folder.mkdir(exist_ok=True);target=folder/(d+'.json')
        selected=selected_motions().get(('ATTACK',d))
        if selected:
            ref=selected['reference'] if selected.get('reference') else 'character:'+NEW+':'+d+':'+selected['animationName'];p.write(target,{'reference':ref})
        elif target.exists():ref=p.read(target)['reference']
        else:
            edits=sorted([j for j in jobs['jobs'] if j['kind']=='edit' and j['motion']=='ATTACK' and j['direction']==d],key=lambda j:j['start'])
            result=client.call('pixelart_workbench',{'argv':['join',*['job:'+j['jobId'] for j in edits],'--as','animation'],'mode':'low'})
            text=p.texts(result);match=re.search(r'(?:image_id|drawing_id): ([\w-]+)',text)
            if result.get('isError') or not match:raise RuntimeError(text)
            ref=match[1];p.write(target,{'reference':ref,'receipt':text})
        entry['spriteRef']=ref
    p.write(ROOT/'artifacts/pixellab/SpiritAnimationQA/Trails/TrailPlan.json',plan)
    print('Updated 8 exact revised attack references for trail rendering')

def staged_trails():
    import add_melee_trails as t
    from concurrent.futures import ThreadPoolExecutor
    from threading import local
    jobs=p.read(OUT/'Jobs.json');assert all(j['status']=='done' for j in jobs['jobs'] if j['motion']=='ATTACK')
    select_north();dirs=p.read(OUT/'Before/AnimationSource.json')['clips'][0]['directions']
    for d in dirs:
        paths=sorted((OUT/'Edited/ATTACK'/d).glob('frame_*.png'));frames=[load(x) for x in paths]
        recipe,tracking=t.build_recipe(KEY,'ATTACK',d,frames,12)
        tracking['sourceFileHashes']=[hashlib.sha256(x.read_bytes()).hexdigest() for x in paths]
        folder=t.OUT/KEY/'ATTACK'/d
        p.write(folder/'Recipe.json',recipe);p.write(folder/'Tracking.json',tracking)
    trail_refs();plan=p.read(t.OUT/'TrailPlan.json');entries=[e for e in plan['entries'] if e['key']==KEY];thread=local()
    def render(entry):
        if not hasattr(thread,'client'):thread.client=p.MCP()
        client=thread.client;folder=t.OUT/KEY/'ATTACK'/entry['direction'];recipe=p.read(folder/'Recipe.json');cache=folder/'RenderedRecipeHash.json'
        if cache.exists() and p.read(cache).get('recipeHash')==t.recipe_hash(recipe):return
        result=client.call('vfx_workbench',{'argv':['start','--sprite',entry['spriteRef'],'--fps','12'],'mode':'low'})
        text=p.texts(result);match=re.search(r'effect step: ([\w-]+)',text)
        if result.get('isError') or not match:raise RuntimeError(text)
        result=client.call('vfx_workbench',{'argv':[match[1],'render',json.dumps({'recipe':recipe})],'mode':'low'})
        text=p.texts(result)
        if 'render ok' not in text:raise RuntimeError(text)
        step=re.search(r'effect step: ([\w-]+)',text)[1];p.write(folder/'RenderReceipt.json',{'text':text})
        for i,b in enumerate(result.get('content',[])):
            if b.get('type')=='image':(folder/f'RenderReview_{i}.png').write_bytes(base64.b64decode(b['data']))
        argv=[step,'export','--name',recipe['name'],'--game-pack']
        previous=folder/'ExportReceipt.json'
        if previous.exists():
            match=re.search(r'effect id ([\w-]{36})',p.read(previous)['text'])
            if match:argv+=['--replace',match[1]]
        result=client.call('vfx_workbench',{'argv':argv,'mode':'low'});text=p.texts(result)
        p.write(previous,{'text':text});(folder/'PixelLabGamePack.zip').write_bytes(p.download(t.parse_export(text)))
        p.write(cache,{'recipeHash':t.recipe_hash(recipe)});print('REVISED_TRAIL_RENDERED',entry['direction'],flush=True)
    with ThreadPoolExecutor(max_workers=3) as pool:list(pool.map(render,entries))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('mode',choices=['preview','publish','trail-refs','fetch-extra','fetch-tip','align-rotations','staged-trails']);a=parser.parse_args()
    {'preview':previews,'publish':publish,'trail-refs':trail_refs,'fetch-extra':fetch_extra,'fetch-tip':fetch_tip,'align-rotations':align_rotations,'staged-trails':staged_trails}[a.mode]()
