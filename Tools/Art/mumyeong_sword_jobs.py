"""Durable PixelLab revision queue. Charged calls never retry blindly."""
import argparse,base64,io,json,re,time,zipfile,hashlib
from pathlib import Path
from PIL import Image
import pixellab_spirit_batch as p

ROOT=p.ROOT;OUT=ROOT/'artifacts/pixellab/MumyeongJangsuSwordRevision'
OLD='d08b8efa-ee49-477d-8db1-fbe0512ccdda';NEW='6f8c501b-a360-4d0a-85fd-556e6a2e0b9d'
PROMPT='TARGETED WEAPON EDIT ONLY. Keep this EXACT general, armour, head, face, shield, feet, root position, existing pose, palette and native pixel size unchanged. Enlarge only his small curved steel Korean saber: 1.5x current blade LENGTH and 1.5x WIDTH, clearly readable solid bright silver blade with dark outline and ivory edge, consistent large one-handed saber across all frames. Keep its grip securely in the same original sword hand, gold guard. Preserve every pose, phase and transparency. No second sword, no glow or effects.'

def prepare():
    if (OUT/'Jobs.json').exists():return
    source=p.read(OUT/'Before/AnimationSource.json');jobs=[]
    for spec in source['clips']:
        for d in spec['directions']:
            if d=='north':continue
            paths=sorted((OUT/'Before/Sprites'/spec['animation']/d).glob('frame_*.png'))
            for i in range(0,len(paths),4):
                entry={'kind':'edit','motion':spec['animation'],'direction':d,'start':i,'count':len(paths[i:i+4]),'paths':[str(x.relative_to(ROOT)) for x in paths[i:i+4]],'status':'waiting'}
                if spec['animation']=='ATTACK' and d=='south' and i==0:entry.update(status='submitted',jobId='17327877-e82f-4df7-8b82-f7f3e8fa8882')
                jobs.append(entry)
    for spec in source['clips']:
        jobs.append({'kind':'north','motion':spec['animation'],'direction':'north','count':spec['frameCount'],'status':'waiting'})
    p.write(OUT/'Jobs.json',{'createdAt':p.now(),'sourceCharacter':OLD,'revisionCharacter':NEW,'prompt':PROMPT,'jobs':jobs})

def save(state):
    p.write(OUT/'Jobs.json',state)
    jobs=state['jobs'];counts={s:sum(j['status']==s for j in jobs) for s in ('waiting','intent','submitted','done')}
    print(p.now(),counts,flush=True)
    (OUT/'Progress.md').write_text('# 무명장수 검 확대·북쪽 동작 수정\n\n'+str(counts)+'\n\nPixelLab 결과를 Unity 밖에 검수용으로 보관 중입니다. 원본은 Before에 보존합니다.\n',encoding='utf-8')

def strip(result):
    return {'content':[b for b in result.get('content',[]) if b.get('type')=='text'],'isError':result.get('isError',False)}

def run():
    prepare();client=p.MCP();state=p.read(OUT/'Jobs.json');original=p.read(ROOT/'artifacts/pixellab/SpiritAnimationProduction/ProductionPlan.json');specs={t['animation']:t for t in original['tasks'] if t['key']=='MumyeongJangsu'}
    if any(j['status']=='intent' for j in state['jobs']):raise RuntimeError('Uncertain prior charged operation: reconcile before resubmitting')
    while True:
        for j in state['jobs']:
            if j['status']!='submitted':continue
            if j['kind']=='north':continue
            result=client.call('get_image',{'job_id':j['jobId']});text=p.texts(result)
            if result.get('isError') or re.search(r'status: failed',text,re.I):raise RuntimeError(text)
            if 'status: completed' not in text:continue
            folder=OUT/'Edited'/j['motion']/j['direction'];folder.mkdir(parents=True,exist_ok=True)
            inline=[base64.b64decode(b['data']) for b in result.get('content',[]) if b.get('type')=='image']
            for i in range(j['count']):
                blob=inline[i] if len(inline)==j['count'] else p.download('https://api.pixellab.ai/mcp/images/'+j['jobId']+'/download?index='+str(i))
                with Image.open(io.BytesIO(blob)) as im:
                    source=Image.open(ROOT/j['paths'][i]);assert im.size==source.size,'AI changed native canvas'
                (folder/f"frame_{j['start']+i:03d}.png").write_bytes(blob)
            j['status']='done';j['receipt']=strip(result);save(state)
        north=[j for j in state['jobs'] if j['kind']=='north']
        active_n=[j for j in north if j['status']=='submitted']
        ready=False
        if active_n or any(j['kind']=='north' and j['status']=='waiting' for j in state['jobs']):
            result=client.call('get_character',{'character_id':NEW,'include_preview':False});text=p.texts(result);ready='status: completed' in text
            p.write(OUT/'RevisionCharacter.json',strip(result))
            groups=p.parse_groups(text)
            for j in active_n:
                group=groups.get('R2_'+j['motion'])
                if group and 'north' in group['directions']:
                    j.update(status='done',groupId=group['groupId']);save(state)
        if all(j['status']=='done' for j in state['jobs']):
            blob=p.download('https://api.pixellab.ai/mcp/characters/'+NEW+'/download');(OUT/'RevisionCharacter.zip').write_bytes(blob)
            with zipfile.ZipFile(io.BytesIO(blob)) as z:
                data=json.loads(z.read('metadata.json'));remote=next(s for s in data['states'] if s['character']['id']==NEW)
                for j in north:
                    approved=p.read(OUT/'SelectedNorthAttack.json') if j['motion']=='ATTACK' and (OUT/'SelectedNorthAttack.json').exists() else None
                    name=approved['animationName'] if approved else 'R2_'+j['motion']
                    paths=remote['frames']['animations'][name]['north'];assert len(paths)==j['count']
                    if approved:j.update(jobId=approved['jobId'],groupId=approved['groupId'],selectedAnimation=name)
                    folder=OUT/'Edited'/j['motion']/'north';folder.mkdir(parents=True,exist_ok=True)
                    for i,path in enumerate(paths):(folder/f'frame_{i:03d}.png').write_bytes(z.read(path))
                for d,path in remote['frames']['rotations'].items():
                    folder=OUT/'Rotations';folder.mkdir(exist_ok=True);(folder/(d+'.png')).write_bytes(z.read(path))
            state['status']='staged_for_review';save(state);return
        result=client.call('wait_for_jobs',{'min_finished':1,'timeout_seconds':2});text=p.texts(result)
        free=max(0,10-max(len(p.running_jobs(text)),sum(j['status']=='submitted' for j in state['jobs'])))
        # A north-facing attack is the first revised motion to review.
        pending=sorted([j for j in state['jobs'] if j['status']=='waiting'],key=lambda j:(0 if j['kind']=='north' and j['motion']=='ATTACK' else 1 if j['kind']=='north' else 2))
        for j in pending:
            if not free:break
            if j['kind']=='north' and not ready:continue
            j['status']='intent';save(state)
            if j['kind']=='edit':
                args={'frames':[base64.b64encode((ROOT/x).read_bytes()).decode() for x in j['paths']],'description':PROMPT,'seed':11222026,'no_background':True}
                result=client.call('edit_animation',args)
            else:
                spec=specs[j['motion']];action=spec['action'].split(' Preserve the EXACT')[0].replace('short saber','large saber').replace('short sabre','large sabre')
                if j['motion']=='ATTACK':
                    action='BACK VIEW facing NORTH (up-screen) for the entire clip. The enemy is NORTH, beyond his head. Start in guard. Raise the RIGHT sword hand above the RIGHT shoulder, then cut with the large steel saber FORWARD AND UP-SCREEN into the enemy in front of his chest, then recover to the exact guard pose. Blade tip travels ABOVE the head toward top of image at impact. Sword and forearm are on the FAR SIDE of torso and head: torso/head naturally OCCLUDE any overlap. Never sweep a visible blade across his BACK, WAIST, BUTTOCKS or legs. Keep shield forward on his left arm. Clear anticipation, overhead forward strike, recovery. Do not rotate to show the face or camera. '
                else:
                    action+=' BACK VIEW: face NORTH / screen-up throughout. Both arms and all active actions occur in FRONT of his chest, toward screen-up; the sword belongs to the right hand, kept ahead of the body or upright beside the right shoulder. Never draw sword or forearm across the visible back. Torso/head occlude far-side weapons naturally. Preserve the large saber size. '
                prompts={d:(action+' Face '+d+'; fixed camera, pixel size and actor origin. Preserve appearance and equipment hands. No VFX or background.')[:950] for d in original['directionOrder']}
                prompts['north']=(action+' True north back view. Action toward top of image, NEVER across his visible back. Natural body occlusion. Fixed actor origin, scale and pixel style. No background, blood, particles, glow or trails.')[:990]
                args={'character_id':NEW,'animation_name':'R2_'+j['motion'],'mode':'v3','action_description':action,'frame_count':j['count'],'keep_first_frame':False,'directions':['north'],'direction_prompts':prompts}
                result=client.call('animate_character',args)
            text=p.texts(result)
            p.write(OUT/'Receipts'/(j['kind']+'_'+j['motion']+'_'+j['direction']+'_'+str(j.get('start',0))+'.json'),strip(result))
            if result.get('isError'):
                j['status']='waiting';save(state)
                if 'rate limit exceeded' in text:break
                raise RuntimeError(text)
            if j['kind']=='edit':
                match=re.search(r'job_id: ([\w-]+)',text);assert match,text;j['jobId']=match[1]
            else:
                group,jobs=p.parse_receipt(text);j.update(groupId=group,jobId=jobs['north'])
            j['status']='submitted';save(state);free-=1;time.sleep(.25)
        time.sleep(12)

if __name__=='__main__':run()
