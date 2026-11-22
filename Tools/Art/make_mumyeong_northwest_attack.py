"""Author a forward NW saber stroke in PixelLab from the raised-guard sprite."""
import json,re,io
from PIL import Image
import pixellab_spirit_batch as p

OUT=p.ROOT/'artifacts/pixellab/MumyeongJangsuSwordRevision'
def run():
    polygon=[[4,2],[12,6],[30,25],[31,29],[28,32],[22,29],[12,21],[5,14],[3,7]]
    recipe={'job':{'canvas':[128,128],'frame_count':8,'durations_ms':[80]*8,'views':[{'id':'north-west','offset':[24,23]}],'source_bookends_exact':True,'return_to_rest':True,'require_clear_border':True,'body_palette_exact':True},'scene':{'palette':{},'layers':[{'id':'weapon','name':'Body - far side saber'},{'id':'body','name':'Body - original general'}],'views':{'north-west':{'parts':{'saber':{'include':[polygon]},'rest':{'exclude':[polygon]}},'nodes':{'root':{'layer':'body','draw':[],'at':[24,23]},'saber':{'layer':'weapon','at':{'node':'root','point':[0,0]},'pivot':[28,32],'draw':[{'op':'copy','part':'saber','method':'nearest'}]},'body':{'layer':'body','at':{'node':'root','point':[0,0]},'draw':[{'op':'copy','part':'rest'}]}},'tracks':[{'node':'saber','property':'angle','interpolation':'step','keys':[[i+1,a] for i,a in enumerate([0,25,30,-20,-40,-45,-15,0])]},{'node':'root','property':'at','interpolation':'step','keys':[[1,[24,23]],[4,[23,24]],[7,[24,23]]]}]}}}}
    client=p.MCP();result=client.call('pixelart_workbench',{'argv':['draw','--recipe',json.dumps(recipe),'--sources',json.dumps({'north-west':'job:c824b729-d1a6-40a5-91a6-c88dd2ab1f09:0'})],'mode':'low','notes':'Replace incorrect backward NW swings with a high forward wrist-driven saber cut toward upper-left; keep the general pixels and grip. Weapon is on far layer behind body, never across visible back. Preserve first/last guard.'})
    text=p.texts(result);p.write(OUT/'NorthWestExactReceipt.json',{'text':text,'recipe':recipe});match=re.search(r'(?:drawing_id|image_id): ([\w-]+)',text)
    if not match or result.get('isError'):raise RuntimeError(text)
    drawing=match[1];result=client.call('pixelart_workbench',{'argv':['join',drawing+':north-west','--as','sheet','--columns','8'],'mode':'low'});text=p.texts(result);match=re.search(r'image_id: ([\w-]+)',text)
    if not match:raise RuntimeError(text)
    data=p.download('https://api.pixellab.ai/mcp/pixel-tools/'+match[1]+'/image.png');(OUT/'NorthWestExactSheet.png').write_bytes(data);sheet=Image.open(io.BytesIO(data)).convert('RGBA');assert sheet.size==(1024,128),sheet.size
    folder=OUT/'ManualAttacks/north-west';folder.mkdir(parents=True,exist_ok=True)
    for i in range(8):sheet.crop((i*128,0,(i+1)*128,128)).save(folder/f'frame_{i:03d}.png')
    doc={'north-east':{'animationName':'R4_ATTACK','groupId':'a4e8f256-4688-4902-b785-fd17eda5d424','jobId':'c14b0d48-6421-4adc-aac4-d35edfbc171b'},'north-west':{'drawingId':drawing,'reference':drawing+':north-west','sourceFolder':'ManualAttacks/north-west','sourceKind':'PixelLab exact layered forward cut','pixelResampling':'Weapon rotation only; body copied at integer offsets'}}
    p.write(OUT/'SelectedDiagonalAttacks.json',doc);print('NORTH_WEST_FORWARD_ATTACK_READY',drawing,flush=True)

if __name__=='__main__':run()
