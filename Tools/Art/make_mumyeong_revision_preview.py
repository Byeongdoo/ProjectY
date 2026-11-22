"""QA comparison only: same native pixel scale and foot origin in every panel."""
from pathlib import Path
import json
from PIL import Image,ImageDraw,ImageFont

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'artifacts/pixellab/MumyeongJangsuSwordRevision'
ART=ROOT/'Client/Assets/Resources/Art/Characters/SpiritsV2/MumyeongJangsu'
OLD=OUT/'Before'
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',19)
small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',15)
def info(root):return json.loads((root/'SourceInfo.json').read_text(encoding='utf-8-sig'))['metrics']
def frame(index):
    image=Image.new('RGB',(960,680),(25,31,39));draw=ImageDraw.Draw(image)
    draw.text((25,12),'무명장수 · 검 확대 / 북쪽 공격 수정',font=font,fill='#f0e4cb')
    for col,(title,motion,direction) in enumerate([('정면 기준',None,None),('정면 공격','ATTACK','south'),('북쪽 공격','ATTACK','north')]):
        draw.text((col*320+96,53),title,font=small,fill='#d0d7df')
        for row,root in enumerate((OLD,ART)):
            path=root/'MumyeongJangsu_Front.png' if motion is None else root/'SpritesWithTrail'/motion/direction/f'frame_{index:03d}.png'
            im=Image.open(path).convert('RGBA');meta=info(root)
            pivot_x=meta['pivotPixels']['x']+(im.width-meta['canvasWidth'])/2
            pivot_y=meta['pivotPixels']['y']+(im.height-meta['canvasHeight'])/2
            anchor=(col*320+160,290+row*300)
            view=im.resize((im.width*2,im.height*2),Image.Resampling.NEAREST)
            image.paste(view,(round(anchor[0]-pivot_x*2),round(anchor[1]-(im.height-pivot_y)*2)),view)
            draw.line((anchor[0]-20,anchor[1],anchor[0]+20,anchor[1]),fill='#5aaba5')
            draw.text((col*320+20,95+row*300),'수정 전' if row==0 else '수정 후',font=small,fill='#9cadbf')
    draw.text((25,648),'동일한 픽셀 배율 · 발 기준점 비교 / 원본 보존',font=small,fill='#a9b6c6')
    return image

if __name__=='__main__':
    frames=[frame(i) for i in range(8)]
    frames[0].save(OUT/'SwordRevisionComparison.gif',save_all=True,append_images=frames[1:],duration=83,loop=0,optimize=False)
    frames[2].save(OUT/'SwordRevisionComparison.png')
