"""Pack completed PixelLab Samcheondo art into Gamcheon-style tile sheets.

No generation or image edits: source pixels are copied at integer coordinates.
Wang pieces are located by exported bounding_box and ordered by corner mask.
Run SamcheondoTilePalettes.Build in Unity after this script finishes.
"""
from __future__ import annotations

import hashlib
import io
from pathlib import Path

from PIL import Image
import pixellab_spirit_batch as pipeline

ROOT = pipeline.ROOT
OUT = ROOT / "artifacts/pixellab/SamcheondoTiles"
DOWNLOADS = OUT / "Downloads"
ART = ROOT / "Client/Assets/Resources/Art/Tiles/Samcheondo"
CELL = 32
ROW_CELLS = 16
TERRAINS = [
    ("RiverBank", "51e345a8-25eb-4483-afa9-d776f735a99f"),
    ("Flagstone", "628b0b55-12c5-4280-b3f6-4972f49dac2c"),
    ("WaterPier", "83a3a9f6-d70b-4aff-9b0c-104e7902a22f"),
    ("Meadow", "4b9140b0-5774-4eb0-a804-55ac3c38ce88"),
    ("BankPier", "815e24c0-260e-4265-a5ad-9837febb463a"),
]
OBJECTS = [
    ("Struct", "Tavern", "ff740606-918c-4ebf-9612-682ee0a02580"),
    ("Struct", "ResurrectionAltar", "416b50ff-1c21-41cc-b676-abd455b8f5e1"),
    ("Struct", "FerryBoat", "15109cfc-d3ce-455e-9575-4f51999c929e"),
    ("Struct", "NoticeBoard", "a9a95146-e505-4442-854e-bfc4023fb3bf"),
    ("Struct", "Lantern", "50f26186-7eb6-4d6f-b739-1f5703a6a14f"),
    ("Struct", "Willow", "5f6aa4fa-4397-4bf5-ab59-6b11225cbabd"),
    ("Struct", "RiverRocks", "25ca361c-d333-440c-9985-a3256546d93c"),
    ("Deco", "Reeds", "85fb3ab5-2a59-4742-b303-d8b965189062"),
    ("Deco", "RiverMist", "11ae5b1b-9c53-41d8-8795-0038de6a1f9f"),
]


def cached(name, url):
    path = DOWNLOADS / name
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(pipeline.download(url))
    return path


def rgba(path):
    with Image.open(path) as image:
        return image.convert("RGBA")


def pixel_hash(image):
    return hashlib.sha256(image.tobytes()).hexdigest()


def write_sheet(category, image, pieces, sources):
    target = ART / category / f"Tile_Samcheondo_{category}.png"
    buffer = io.BytesIO()
    image.save(buffer, format="PNG")
    existing = rgba(target) if target.exists() else None
    if existing is None or existing.size != image.size or existing.tobytes() != image.tobytes():
        pipeline.put_bytes(target, buffer.getvalue())
    return {"category": category, "sheet": str(target.relative_to(ROOT / "Client")).replace("\\", "/"),
            "width": image.width, "height": image.height, "pieces": pieces, "sources": sources,
            "rgbaSha256": pixel_hash(image)}


def ground_sheet():
    image = Image.new("RGBA", ((len(TERRAINS)*5-1)*CELL, 4*CELL))
    pieces, sources = [], []
    for group, (key, cid) in enumerate(TERRAINS):
        source = rgba(cached(cid+".png", f"https://api.pixellab.ai/mcp/tilesets/{cid}/image?inline=true"))
        metadata = pipeline.read(cached(cid+".json", f"https://api.pixellab.ai/mcp/tilesets/{cid}/metadata"))
        entries = metadata["tileset_data"]["tiles"]
        masks = set()
        for tile in entries:
            corners = tile["corners"]
            mask = sum((corners[corner] == "upper") << bit for corner, bit in [("NW",3),("NE",2),("SW",1),("SE",0)])
            if mask in masks:
                raise ValueError(f"Duplicate Wang mask {mask}: {cid}")
            masks.add(mask)
            box = tile["bounding_box"]
            if (box["width"],box["height"]) != (CELL,CELL):
                raise ValueError(f"Wrong source tile dimensions: {cid}")
            native = source.crop((box["x"],box["y"],box["x"]+CELL,box["y"]+CELL))
            x = (group*5 + mask%4)*CELL
            top = (mask//4)*CELL
            image.paste(native,(x,top))
            assert image.crop((x,top,x+CELL,top+CELL)).tobytes() == native.tobytes()
            pieces.append({"name":f"Tile_Samcheondo_Ground_{key}_Wang_{mask:02d}",
                           "x":x,"y":image.height-top-CELL,"width":CELL,"height":CELL,
                           "paletteX":x//CELL,"paletteY":3-mask//4,"sourceId":cid,
                           "wangMask":mask,"corners":corners,"sourceRect":box,
                           "rgbaSha256":pixel_hash(native)})
        if masks != set(range(16)):
            raise ValueError(f"Incomplete Wang set: {cid}")
        sources.append({"key":key,"id":cid,"sourceFile":f"Downloads/{cid}.png",
                        "metadataFile":f"Downloads/{cid}.json","maskOrder":"NW<<3 | NE<<2 | SW<<1 | SE",
                        "baseTileIds":metadata["base_tile_ids"]})
    return write_sheet("Ground", image, pieces, sources)


def objects_sheet(category):
    placements = []
    x = top = row_height = 0
    for group, key, cid in OBJECTS:
        if group != category:
            continue
        native = rgba(cached(cid+".png",f"https://api.pixellab.ai/mcp/objects/{cid}/download"))
        if native.width % CELL or native.height % CELL:
            raise ValueError(f"Source canvas is not a 32px multiple: {key}")
        if x and x+native.width > ROW_CELLS*CELL:
            top += row_height + CELL
            x = row_height = 0
        placements.append((key,cid,native,x,top))
        x += native.width+CELL
        row_height = max(row_height,native.height)
    height = top+row_height
    image = Image.new("RGBA",(ROW_CELLS*CELL,height))
    pieces, sources = [], []
    for key,cid,native,x,top in placements:
        image.paste(native,(x,top))
        assert image.crop((x,top,x+native.width,top+native.height)).tobytes() == native.tobytes()
        sources.append({"key":key,"id":cid,"sourceFile":f"Downloads/{cid}.png",
                        "atlasTopLeft":{"x":x,"y":top},"width":native.width,"height":native.height,
                        "rgbaSha256":pixel_hash(native)})
        for cy in range(native.height//CELL):
            for cx in range(native.width//CELL):
                tile = native.crop((cx*CELL,cy*CELL,(cx+1)*CELL,(cy+1)*CELL))
                if not tile.getchannel("A").getbbox():
                    continue
                pieces.append({"name":f"Tile_Samcheondo_{category}_{key}_{cx}_{native.height//CELL-1-cy}",
                               "x":x+cx*CELL,"y":height-top-(cy+1)*CELL,"width":CELL,"height":CELL,
                               "paletteX":x//CELL+cx,"paletteY":-top//CELL-cy-1,
                               "sourceId":cid,"sourceCell":{"x":cx,"y":cy},"rgbaSha256":pixel_hash(tile)})
    return write_sheet(category,image,pieces,sources)


def main():
    manifest = {"version":1,"pixelsPerUnit":CELL,"cellSize":CELL,"source":"Existing completed PixelLab resources",
                "sourcePixelsResized":False,"pixelEdits":False,
                "packing":"Integer translation only; source canvas and RGBA pixels preserved; blank cells omitted from tile assets",
                "selection":{"Tavern":"Latest low top-down front version ff740606-918c-4ebf-9612-682ee0a02580",
                             "ResurrectionAltar":"Latest low top-down version 416b50ff-1c21-41cc-b676-abd455b8f5e1"},
                "excludedOlderVersions":["4e559e95-12be-49c7-bc14-28a6ca40168b","f77d9c0e-5fbd-4168-8580-1725730907eb"],
                "sheets":[ground_sheet(),objects_sheet("Struct"),objects_sheet("Deco")]}
    pipeline.write(ART / "SourceInfo.json",manifest)
    pipeline.ensure_meta(ART / "SourceInfo.json",text=True)
    pipeline.write(OUT / "PackingReport.json",manifest)
    for sheet in manifest["sheets"]:
        print(sheet["category"],sheet["width"],sheet["height"],"tiles",len(sheet["pieces"]),flush=True)


if __name__ == "__main__":
    main()
