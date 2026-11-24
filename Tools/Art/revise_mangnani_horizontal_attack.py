"""Stage and publish the eight-direction PixelLab horizontal attack revision."""
import argparse
import hashlib
import io
import json
import re
import shutil
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw
import pixellab_spirit_batch as p

ROOT = p.ROOT
OUT = ROOT / 'artifacts/pixellab/MangnaniHorizontalAttackRevision'
ART = ROOT / 'Client/Assets/Resources/Art/Characters/SpiritsV2/Mangnani'
STAGE = OUT / 'Staged/Mangnani'
GROUP = '3b9f454c-c732-4101-8bfd-aaf79ef152ed'
CHARACTER = '478ad62f-9651-46ca-a174-7b869382d4fd'
REMOTE_NAME = 'ATTACK_HORIZONTAL_3TILE'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    with Image.open(path) as image:
        return image.convert('RGBA')


def png(image):
    buffer = io.BytesIO()
    image.save(buffer, format='PNG')
    return buffer.getvalue()


def stage():
    if not (OUT / 'Before').exists():
        shutil.copytree(ART, OUT / 'Before')
        p.write(OUT / 'BeforeHashes.json', {str(f.relative_to(ART)): sha(f) for f in ART.rglob('*') if f.is_file()})
    if not STAGE.exists():
        shutil.copytree(OUT / 'Before', STAGE)
    archive_path = OUT / 'Mangnani.zip'
    if not archive_path.exists():
        archive_path.write_bytes(p.download(f'https://api.pixellab.ai/mcp/characters/{CHARACTER}/download'))
    with zipfile.ZipFile(archive_path) as archive:
        assert not any(n.endswith('MISSING_FRAMES.txt') for n in archive.namelist())
        metadata = json.loads(archive.read('metadata.json'))
        remote = next(s for s in metadata['states'] if s['character']['id'] == CHARACTER)
        paths = remote['frames']['animations'][REMOTE_NAME]
        source = p.read(STAGE / 'AnimationSource.json')
        spec = next(s for s in source['clips'] if s['animation'] == 'ATTACK')
        assert set(paths) == set(spec['directions'])
        for direction in spec['directions']:
            assert len(paths[direction]) == 8
            for i, filename in enumerate(paths[direction]):
                target = STAGE / 'Sprites/ATTACK' / direction / f'frame_{i:03d}.png'
                data = archive.read(filename)
                with Image.open(io.BytesIO(data)) as image:
                    assert image.size == (92, 92) and image.mode == 'RGBA'
                    assert image.getchannel('A').getextrema() == (0, 255)
                target.write_bytes(data)
        spec.update(groupId=GROUP, revision=REMOTE_NAME, sourceAnimationName=REMOTE_NAME)
        source['attackRevision'] = {'groupId': GROUP, 'sourceAnimationName': REMOTE_NAME, 'target': 'One horizontal cleave across front-left/front-center/front-right', 'nativePixelsResized': False}
        p.write(STAGE / 'AnimationSource.json', source)
        p.write(OUT / 'DownloadedMetadata.json', metadata)
    sheet('Sprites', '')
    preview()
    print('STAGED: 8 directions / 64 native frames', flush=True)


def sheet(frame_root, suffix):
    source = p.read(STAGE / 'AnimationSource.json')
    spec = next(s for s in source['clips'] if s['animation'] == 'ATTACK')
    rows = [[load(STAGE / frame_root / 'ATTACK' / d / f'frame_{i:03d}.png') for i in range(8)] for d in spec['directions']]
    w = max(im.width for row in rows for im in row)
    h = max(im.height for row in rows for im in row)
    result = Image.new('RGBA', (w * 8, h * 8))
    for y, row in enumerate(rows):
        for x, im in enumerate(row):
            result.paste(im, (x*w+(w-im.width)//2, y*h+(h-im.height)//2))
    target = STAGE / 'Sheets' / ('Mangnani_ATTACK' + suffix + '.png')
    target.write_bytes(png(result))
    layout = {**spec, 'cellWidth': w, 'cellHeight': h}
    if suffix:
        layout.update(frameRoot=frame_root, nativePixelsResized=False)
    p.write(target.with_suffix('.json'), layout)


def preview():
    dirs = next(s for s in p.read(STAGE / 'AnimationSource.json')['clips'] if s['animation'] == 'ATTACK')['directions']
    result = Image.new('RGB', (8*200+90, 8*200), (38, 42, 49))
    draw = ImageDraw.Draw(result)
    for y, d in enumerate(dirs):
        draw.text((4, y*200+95), d, fill='white')
        for i in range(8):
            im = load(STAGE / 'Sprites/ATTACK' / d / f'frame_{i:03d}.png').resize((184,184), Image.Resampling.NEAREST)
            result.paste(im, (90+i*200+8,y*200+8), im)
    result.save(OUT / 'AttackReview.png')


def trails():
    import add_melee_trails as t
    t.ART = STAGE.parent
    t.OUT = OUT / 'Trails'
    dirs = next(s for s in p.read(STAGE / 'AnimationSource.json')['clips'] if s['animation'] == 'ATTACK')['directions']
    entries = []
    for direction in dirs:
        paths = sorted((STAGE / 'Sprites/ATTACK' / direction).glob('frame_*.png'))
        recipe, tracking = t.build_recipe('Mangnani', 'ATTACK', direction, [load(f) for f in paths], 12)
        tracking['sourceFileHashes'] = [sha(f) for f in paths]
        folder = t.OUT / 'Mangnani/ATTACK' / direction
        p.write(folder / 'Recipe.json', recipe)
        p.write(folder / 'Tracking.json', tracking)
        entries.append({'key':'Mangnani', 'motion':'ATTACK', 'direction':direction,
                        'recipe':str((folder/'Recipe.json').relative_to(ROOT)),
                        'spriteRef':f'character:{CHARACTER}:{direction}:{GROUP}'})
    p.write(t.OUT / 'TrailPlan.json', {'entries': entries, 'count': 8})
    t.run(workers=3)
    trail_source = p.read(STAGE / 'TrailSource.json')
    for entry in trail_source['directionalClips']:
        if entry['motion'] != 'ATTACK':
            continue
        folder = t.OUT / 'Mangnani/ATTACK' / entry['direction']
        integrity = p.read(folder / 'PixelIntegrity.json')
        assert all(c['unintendedCharacterPixelChanges'] == 0 for c in integrity['checks'])
        entry.update(recipe=str((folder/'Recipe.json').relative_to(ROOT)), sourceGroupId=GROUP,
                     activeFrames=[c['frame'] for c in integrity['checks'] if c['effectPixels']],
                     unintendedCharacterPixelChanges=0,
                     intentionalTrailOverlapPixels=sum(c['changedCharacterPixels'] for c in integrity['checks']))
    trail_source['attackSourceGroupId'] = GROUP
    p.write(STAGE / 'TrailSource.json', trail_source)
    sheet('SpritesWithTrail', '_WithTrail')
    sheet('TrailLayers', '_TrailOnly')
    print('STAGED_TRAILS: 8 directions / 64 frames; original pixels verified', flush=True)


def changed_files():
    before = OUT / 'Before'
    return [f for f in STAGE.rglob('*') if f.is_file() and
            (not (before/f.relative_to(STAGE)).exists() or sha(f) != sha(before/f.relative_to(STAGE)))]


def verify():
    source = p.read(ART / 'AnimationSource.json')
    spec = next(s for s in source['clips'] if s['animation'] == 'ATTACK')
    assert spec['groupId'] == GROUP
    assert spec['frameCount'] == 8 and spec['framesPerSecond'] == 12 and not spec['loop']
    before_hashes = p.read(OUT / 'BeforeHashes.json')
    changed = []
    guid_count = 0
    for rel, old_hash in before_hashes.items():
        target = ART / rel
        assert target.exists(), rel
        if rel.endswith('.meta'):
            assert p.asset_guid(Path(str(target)[:-5])) == p.asset_guid(Path(str(OUT/'Before'/rel)[:-5])), rel
            guid_count += 1
        if sha(target) != old_hash:
            changed.append(rel)
            portable = Path(rel).as_posix()
            assert ('/ATTACK/' in portable or portable.startswith('Sheets/Mangnani_ATTACK') or portable in ('AnimationSource.json','TrailSource.json')), rel
    for d in spec['directions']:
        clip = (ART / 'Animations' / ('ATTACK_'+d.upper().replace('-','_')+'.anim')).read_text(encoding='utf-8-sig')
        refs = re.findall(r'value: \{fileID: 21300000, guid: ([a-f0-9]{32}), type: 3\}', clip)
        assert len(refs) == 8
        for i, guid in enumerate(refs):
            rel = Path('SpritesWithTrail/ATTACK') / d / f'frame_{i:03d}.png'
            assert p.asset_guid(ART/rel) == guid
            for folder in ('Sprites','SpritesWithTrail','TrailLayers'):
                rel = Path(folder+'/ATTACK') / d / f'frame_{i:03d}.png'
                assert sha(ART/rel) == sha(STAGE/rel)
            original = load(ART/'Sprites/ATTACK'/d/f'frame_{i:03d}.png')
            box = original.getchannel('A').getbbox()
            assert box and 0 < box[0] < box[2] < original.width and 0 < box[1] < box[3] < original.height
            composite = load(ART/'SpritesWithTrail/ATTACK'/d/f'frame_{i:03d}.png')
            effect = load(ART/'TrailLayers/ATTACK'/d/f'frame_{i:03d}.png')
            base = Image.new('RGBA', composite.size)
            base.paste(original, ((composite.width-original.width)//2,(composite.height-original.height)//2))
            assert all(a == b or e[3] or (a[3] == b[3] == 0)
                       for a,b,e in zip(base.get_flattened_data(),composite.get_flattened_data(),effect.get_flattened_data()))
            for folder in ('Sprites','SpritesWithTrail','TrailLayers'):
                path = ART/folder/'ATTACK'/d/f'frame_{i:03d}.png'
                meta = Path(str(path)+'.meta').read_text(encoding='utf-8-sig')
                size = load(path).size
                metrics = p.read(ART/'SourceInfo.json')['metrics']
                pivot = re.search(r'spritePivot: \{x: ([\d.]+), y: ([\d.]+)\}', meta)
                expected = (metrics['pivotPixels']['y']+(size[1]-metrics['canvasHeight'])/2)/size[1]
                assert pivot and abs(float(pivot[1])-.5) < 1e-7 and abs(float(pivot[2])-expected) < 1e-7
                assert 'spritePixelsToUnits: 32' in meta and 'filterMode: 0' in meta
                assert 'enableMipMap: 0' in meta and 'textureCompression: 0' in meta
    report = {'status':'verified', 'directions':8, 'frames':64, 'spriteVariants':3,
              'sourceGroupId':GROUP, 'assetGuidsPreserved':guid_count, 'changedFiles':changed,
              'unrelatedResourcesUnchanged':True, 'fps':12, 'loop':False,
              'canvasEdgeContacts':0, 'pixelsPerUnit':32, 'footPivotPreserved':True,
              'nativePixelsResized':False, 'unintendedPixelChangesOutsideTrail':0}
    p.write(OUT / 'ReplacementReport.json', report)
    print('VERIFIED: 8 clips / 64 frames; GUIDs, references, pixel integrity and unrelated files preserved', flush=True)


def sync_trail_provenance():
    canonical = ROOT/'artifacts/pixellab/SpiritAnimationQA/Trails'
    backup = OUT/'BeforeTrailProvenance'
    if not backup.exists():
        backup.mkdir()
        for name in ('TrailPlan.json','TrailDelivery.json'):
            shutil.copy2(canonical/name, backup/name)
        shutil.copytree(canonical/'Mangnani/ATTACK', backup/'ATTACK')
    for folder in (OUT/'Trails/Mangnani/ATTACK').iterdir():
        if folder.is_dir():
            shutil.copytree(folder, canonical/'Mangnani/ATTACK'/folder.name, dirs_exist_ok=True)
    new_entries = {r['direction']: r for r in p.read(STAGE/'TrailSource.json')['directionalClips'] if r['motion']=='ATTACK'}
    for name, field in (('TrailPlan.json','entries'), ('TrailDelivery.json','entries')):
        doc = p.read(canonical/name)
        for entry in doc[field]:
            if entry['key']=='Mangnani' and entry['motion']=='ATTACK':
                d = entry['direction']
                entry.update(recipe=str((canonical/'Mangnani/ATTACK'/d/'Recipe.json').relative_to(ROOT)),
                             spriteRef=f'character:{CHARACTER}:{d}:{GROUP}', sourceGroupId=GROUP)
                if name == 'TrailDelivery.json':
                    entry.update({k:v for k,v in new_entries[d].items() if k not in ('recipe',)})
        p.write(canonical/name, doc)


def publish():
    assert all((OUT/'Trails/Mangnani/ATTACK'/d/'PixelIntegrity.json').exists()
               for d in next(s for s in p.read(STAGE/'AnimationSource.json')['clips'] if s['animation']=='ATTACK')['directions'])
    changes = changed_files()
    hashes = p.read(OUT / 'BeforeHashes.json')
    for f in changes:
        rel = f.relative_to(STAGE)
        target = ART / rel
        assert sha(target) in (hashes[str(rel)], sha(f)), 'Resource changed while staging: '+str(rel)
    for f in changes:
        p.put_bytes(ART / f.relative_to(STAGE), f.read_bytes())
    verify()
    sync_trail_provenance()
    receipt = p.read(OUT/'Receipt.json')
    receipt.update(status='completed_and_replaced', unityAssetsReplaced=True)
    p.write(OUT/'Receipt.json', receipt)
    print('PUBLISHED:', len(changes), 'files; backup at', OUT/'Before', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['stage','trails','publish','verify'])
    mode = parser.parse_args().mode
    globals()[mode]()
