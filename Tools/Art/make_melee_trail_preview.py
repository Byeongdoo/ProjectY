"""Comparison preview only; all sprite pixels come from the saved PixelLab outputs."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json

ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'Client/Assets/Resources/Art/Characters/SpiritsV2'
OUT=ROOT/'artifacts/pixellab/SpiritAnimationQA'
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',14)
small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',11)
classes=[('MumyeongJangsu','무명장수'),('Mangnani','망나니'),('Kkokdusoe','꼭두쇠')]
frames=[]
for n in range(8):
    sheet=Image.new('RGB',(500,360),(30,36,45));draw=ImageDraw.Draw(sheet)
    for col,(key,name) in enumerate(classes):
        info=json.loads((ART/key/'SourceInfo.json').read_text(encoding='utf-8-sig'))['metrics']
        draw.text((col*160+35,8),name,font=font,fill=(240,229,205))
        for row,folder in enumerate(('Sprites','SpritesWithTrail')):
            image=Image.open(ART/key/folder/'ATTACK/south'/f'frame_{n:03d}.png').convert('RGBA')
            px=info['pivotPixels']['x']+(image.width-info['canvasWidth'])//2;py=info['pivotPixels']['y']+(image.height-info['canvasHeight'])//2
            x=col*160+80-px;y=40+row*160+120-(image.height-py)
            sheet.paste(image,(int(x),int(y)),image)
            draw.text((col*160+8,35+row*160),'원본' if row==0 else 'Trail 적용',font=small,fill=(151,177,190))
            draw.line((col*160+64,160+row*160,col*160+96,160+row*160),fill=(82,162,149))
    frames.append(sheet.resize((1000,720),Image.Resampling.NEAREST))
OUT.mkdir(parents=True,exist_ok=True)
frames[2].save(OUT/'TrailComparison.png')
frames[0].save(OUT/'TrailComparison.gif',save_all=True,append_images=frames[1:],duration=[80,80,90,80,80,90,80,90],loop=0,disposal=2)
print('Preview saved')
