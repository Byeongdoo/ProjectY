"""PixelLab exact padding/tip repair. Python only measures and unpacks PNGs."""
import io,json,re,hashlib
from pathlib import Path
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from threading import local
from PIL import Image
import pixellab_spirit_batch as p

OUT=p.ROOT/'artifacts/pixellab/MumyeongJangsuSwordRevision'
def steel(c):
    r,g,b,a=c;return a>128 and r>120 and max(r,g,b)-min(r,g,b)<50 and b>r*.72
def groups(im):
    remain={(x,y) for y in range(im.height) for x in range(im.width) if im.getpixel((x,y))[3]>16};result=[]
    while remain:
        seed=remain.pop();group={seed};stack=[seed]
        while stack:
            x,y=stack.pop()
            for dx in (-1,0,1):
                for dy in (-1,0,1):
                    q=(x+dx,y+dy)
                    if q in remain:remain.remove(q);group.add(q);stack.append(q)
        result.append(group)
    return sorted(result,key=len,reverse=True)
def color(c):return '#'+''.join(f'{v:02x}' for v in c)
def scene(path,view):
    im=Image.open(path).convert('RGBA');w,h=im.size;dx=(128-w)//2;dy=(128-h)//2;gs=groups(im)
    debris=[g for g in gs[1:] if len(g)<140 and any(x in (0,w-1) or y in (0,h-1) for x,y in g)]
    selections={}
    if debris:selections['debris']={'regions':[[min(x for x,y in g),min(y for x,y in g),max(x for x,y in g),max(y for x,y in g)] for g in debris]}
    selections['rest']={};palette={};tips=[]
    for side in ('left','right','top','bottom'):
        edge=[q for q in gs[0] if (q[0]==0 if side=='left' else q[0]==w-1 if side=='right' else q[1]==0 if side=='top' else q[1]==h-1) and steel(im.getpixel(q))]
        if not edge:continue
        horizontal=side in ('left','right');coords=sorted(q[1] if horizontal else q[0] for q in edge)
        lo,hi=min(coords)-1,max(coords)+1;mid=round((lo+hi)/2);sign=-1 if side in ('left','top') else 1
        if horizontal:
            at=0 if sign<0 else w-1;inner=at-sign*3
            nearby=[y for x,y in gs[0] if x==inner and lo-6<=y<=hi+6 and steel(im.getpixel((x,y)))]
            slope=mid-sum(nearby)/len(nearby) if nearby else 0;end=mid+round(max(-5,min(5,slope*2)))
            points=[[at,lo],[at,hi],[at+sign*6,end]]
        else:
            at=0 if sign<0 else h-1;inner=at-sign*3
            nearby=[x for x,y in gs[0] if y==inner and lo-6<=x<=hi+6 and steel(im.getpixel((x,y)))]
            slope=mid-sum(nearby)/len(nearby) if nearby else 0;end=mid+round(max(-5,min(5,slope*2)))
            points=[[lo,at],[hi,at],[end,at+sign*6]]
        metal=Counter(im.getpixel(q) for q in edge);fill=metal.most_common(1)[0][0]
        dark=Counter(im.getpixel((x,y)) for x,y in gs[0] if max(im.getpixel((x,y))[:3])<70);ink=dark.most_common(1)[0][0]
        palette[side+'_steel']=color(fill);palette[side+'_ink']=color(ink)
        tips.append({'op':'polygon','points':points,'color':side+'_steel','outline':side+'_ink','stroke_width':1})
    nodes={'body':{'layer':'body','at':[dx,dy],'draw':[{'op':'copy','part':'rest'}]}}
    if tips:nodes['tip']={'layer':'blade','at':[dx,dy],'draw':tips}
    return {'selections':selections,'nodes':nodes},palette,{'id':view,'offset':[dx,dy]},debris

def run():
    jobs=p.read(OUT/'Jobs.json')['jobs'];entries=[]
    for path in sorted((OUT/'Edited').glob('*/*/*.png')):
        if path.parent.parent.name=='ATTACK' or path.parent.name=='north':continue
        im=Image.open(path).convert('RGBA');b=im.getchannel('A').getbbox()
        if not b or not (b[0]==0 or b[1]==0 or b[2]==im.width or b[3]==im.height):continue
        target=OUT/'TipRepaired'/path.relative_to(OUT/'Edited')
        if target.exists():continue
        motion,direction=path.parent.parent.name,path.parent.name;index=int(path.stem[-3:]);job=next(j for j in jobs if j['kind']=='edit' and j['motion']==motion and j['direction']==direction and j['start']<=index<j['start']+j['count'])
        entries.append({'path':path,'target':target,'reference':'job:'+job['jobId']+':'+str(index-job['start'])})
    batches=[entries[i:i+4] for i in range(0,len(entries),4)];thread=local()
    def work(batch):
        if not hasattr(thread,'client'):thread.client=p.MCP()
        client=thread.client;views=[];sources={};scenes={};palette={};records=[]
        for i,e in enumerate(batch):
            name='f'+str(i);sc,pal,v,debris=scene(e['path'],name)
            # Per-view named colours must not overwrite another view's palette.
            for old,new in [(k,name+'_'+k) for k in pal]:
                for op in sc['nodes'].get('tip',{}).get('draw',[]):
                    for field in ('color','outline'):
                        if op.get(field)==old:op[field]=new
                palette[new]=pal[old]
            views.append(v);scenes[name]=sc;sources[name]=e['reference'];records.append({'path':str(e['path'].relative_to(OUT)),'offset':v['offset'],'debrisPixels':sum(map(len,debris)),'tipPolygons':len(sc['nodes'].get('tip',{}).get('draw',[]))})
        recipe={'job':{'canvas':[128,128],'frame_count':1,'views':views,'source_bookends_exact':False,'require_clear_border':True},'scene':{'palette':palette,'layers':[{'id':'body','name':'Body - preserved'},{'id':'blade','name':'Body - completed blade tip'}],'views':scenes}}
        result=client.call('pixelart_workbench',{'argv':['draw','--recipe',json.dumps(recipe),'--sources',json.dumps(sources)],'mode':'low'})
        text=p.texts(result);match=re.search(r'(?:drawing_id|image_id): ([\w-]+)',text)
        if not match or result.get('isError'):raise RuntimeError(text)
        drawing=match[1]
        refs=[drawing+':f'+str(i) for i in range(len(batch))]
        if len(refs)==1:refs=refs*2
        result=client.call('pixelart_workbench',{'argv':['join',*refs,'--as','sheet','--columns',str(len(refs))],'mode':'low'})
        text=p.texts(result);match=re.search(r'image_id: ([\w-]+)',text)
        if not match:raise RuntimeError(text)
        data=p.download('https://api.pixellab.ai/mcp/pixel-tools/'+match[1]+'/image.png');sheet=Image.open(io.BytesIO(data)).convert('RGBA');assert sheet.size==(128*len(refs),128),sheet.size
        for i,(e,record) in enumerate(zip(batch,records)):
            im=sheet.crop((i*128,0,(i+1)*128,128));box=im.getchannel('A').getbbox();assert box[0]>0 and box[1]>0 and box[2]<128 and box[3]<128
            original=Image.open(e['path']).convert('RGBA');dx,dy=record['offset'];gs=groups(original);removed=set().union(*[g for g in gs[1:] if len(g)<140 and any(x in (0,original.width-1) or y in (0,original.height-1) for x,y in g)]) if len(gs)>1 else set()
            differences=sum(original.getpixel((x,y))!=im.getpixel((x+dx,y+dy)) for y in range(1,original.height-1) for x in range(1,original.width-1) if original.getpixel((x,y))[3] and (x,y) not in removed)
            assert differences==0,('Unexpected interior pixel change',record['path'],differences)
            e['target'].parent.mkdir(parents=True,exist_ok=True);im.save(e['target']);record.update(drawingId=drawing,sheetId=match[1],interiorPixelChanges=differences,pixelResampling=False)
            p.write(e['target'].with_suffix('.json'),record)
        print('REPAIRED',len(batch),'frames',flush=True)
    with ThreadPoolExecutor(max_workers=3) as pool:list(pool.map(work,batches))
    print('TIP_REPAIRS_READY',len(entries),flush=True)

if __name__=='__main__':run()
