"""Import existing PixelLab chapter 1 boss / chapter 2 exports; never generate art.

Run with the bundled Python runtime (Pillow required). Downloads are cached outside
Unity. PNG bytes are copied unchanged, GUIDs survive reruns, and every directional
revision is recorded alongside the resulting clip. REJECTED groups stay archived.
"""
from __future__ import annotations

import hashlib
import io
import json
from pathlib import Path
import re
import sys
import uuid
import zipfile

from PIL import Image
import pixellab_spirit_batch as pipeline

ROOT = pipeline.ROOT
OUT = ROOT / "artifacts/pixellab/MonsterImport"
ART = ROOT / "Client/Assets/Resources/Art/Monsters"
DIRS = ["south", "south-east", "east", "north-east", "north", "north-west", "west", "south-west"]
CHARACTERS = [
    (3000101, "TwistedMokgui", "뒤틀린 목귀", "f2293fbd-f966-458c-8985-06a379771dbc", 0),
    (2000201, "MudWretch", "갯벌 잡귀", "a47d65ff-c87f-4254-97e2-2bad336e7496", 5),
    (2000202, "DrownedSoul", "물에 젖은 망령", "7b67f6b2-ace9-4265-b537-835418efcd17", 2),
    (2000203, "BoatSongGhost", "뱃노래 귀신", "efd6d1d1-c6ed-433c-ae44-b89f51a5b2a2", 1),
    (2000204, "NetFiend", "그물 악귀", "6187fb59-19e9-4be7-a12a-ecc09adae88f", 4),
    (3000201, "WaterFerryman", "물귀신 사공", "8e93c443-96b1-46c2-b28d-f2293f9d6a8e", 18),
]
MOKGUI_NAMES = {
    "Idle": "IDLE", "Walk": "RUN", "Attack": "ATTACK", "Hit": "HIT", "Death": "DIE",
    "TankSlam": "TANK_SLAM", "RootAreaCast": "ROOT_AREA_CAST", "ThreadDebuffCast": "THREAD_DEBUFF_CAST",
    "MountainWail": "MOUNTAIN_WAIL", "PhaseTransition": "PHASE_TRANSITION",
    "ExhaustedEnter": "EXHAUSTED_ENTER", "ExhaustedLoop": "EXHAUSTED_LOOP", "ExhaustedRecover": "EXHAUSTED_RECOVER",
}


def texture(path, data, base_size, pivot_y, centered=False):
    """Only importer metadata changes; the downloaded PNG is never re-encoded."""
    with Image.open(io.BytesIO(data)) as img:
        width, height = img.size
        if img.mode != "RGBA":
            raise ValueError(f"Expected RGBA: {path}")
    template = (ART / "2000101/Sprites/Rotations/south.png.meta").read_text(encoding="utf-8-sig")
    y = .5 if centered else (pivot_y + (height - base_size[1]) / 2) / height
    template = re.sub(r"^guid: .*", "guid: " + pipeline.asset_guid(path), template, count=1, flags=re.M)
    template = re.sub(r"^  spritePivot: .*", f"  spritePivot: {{x: 0.5, y: {y:.9g}}}", template, count=1, flags=re.M)
    existing = Path(str(path) + ".meta")
    sprite_id = re.search(r"^    spriteID: (\w+)", existing.read_text(encoding="utf-8-sig"), re.M) if existing.exists() else None
    template = re.sub(r"^    spriteID: .*", "    spriteID: " + (sprite_id[1] if sprite_id else uuid.uuid4().hex), template, count=1, flags=re.M)
    max_size = max(2048, 2 ** (max(width, height) - 1).bit_length())
    template = re.sub(r"maxTextureSize: \d+", f"maxTextureSize: {max_size}", template)
    if not existing.exists() or existing.read_text(encoding="utf-8-sig") != template:
        pipeline.put_bytes(existing, template.encode())
    if not path.exists() or path.read_bytes() != data:
        pipeline.put_bytes(path, data)
    return width, height


def select_groups(anims, key):
    base = {name: dict(dirs) for name, dirs in anims.items()
            if not name.startswith(("REJECTED_", "Refined_", "POSE_HOLD_")) and "_FIX_" not in name}
    selection = {(name, d): name for name, dirs in base.items() for d in dirs}
    # First apply named fixes, then the more recent Refined R3 replacements.
    for name in sorted(anims, key=lambda n: (n.startswith("Refined_"), n)):
        if name.startswith("REJECTED_"):
            continue
        target = None
        if "_FIX_" in name:
            target = name.split("_FIX_", 1)[0]
        elif name.startswith("Refined_"):
            target = next((motion for motion in sorted(base, key=len, reverse=True)
                           if name.startswith("Refined_" + motion + "_")), None)
        elif key == "BoatSongGhost" and name == "POSE_HOLD_NW_R4":
            target = "STUN"
        if target is not None:
            if target not in base:
                raise ValueError(f"Unmatched revision: {name}")
            for direction, paths in anims[name].items():
                base[target][direction] = paths
                selection[target, direction] = name
    for name, dirs in base.items():
        if set(dirs) != set(DIRS) or len({len(v) for v in dirs.values()}) != 1:
            raise ValueError(f"Incomplete animation: {name}")
    return base, selection


def main():
    details = pipeline.read(OUT / "CharacterDetails.json")
    table = {r["characterID"]: r for r in pipeline.read(ROOT / "Client/Assets/Resources/Data/CharacterInfos.json")["rows"]}
    strings = {r["stringID"]: r["KR"] for r in pipeline.read(ROOT / "Client/Assets/Resources/Data/Strings.json")["rows"]}
    report = {"createdAt": pipeline.now(), "sourcePixelsResized": False, "characters": []}
    for game_id, key, fallback_name, cid, pivot_y in CHARACTERS:
        archive_path = OUT / "Downloads" / (cid + ".zip")
        if not archive_path.exists():
            archive_path.parent.mkdir(parents=True, exist_ok=True)
            archive_path.write_bytes(pipeline.download(f"https://api.pixellab.ai/mcp/characters/{cid}/download"))
        root = ART / str(game_id)
        with zipfile.ZipFile(archive_path) as archive:
            if any(n.endswith("MISSING_FRAMES.txt") for n in archive.namelist()):
                raise ValueError(f"Missing stored frames for {key}")
            metadata = json.loads(archive.read("metadata.json"))
            remote = next(s for s in metadata["states"] if s["character"]["id"] == cid)
            base_size = tuple(remote["character"]["size"][k] for k in ("width", "height"))
            anims, selected = select_groups(remote["frames"]["animations"], key)
            groups = pipeline.parse_groups(details[cid])
            source = {"key": key, "name": strings.get(table[game_id]["name"], fallback_name),
                      "characterId": cid, "gameId": game_id, "prefabName": table[game_id]["prefabName"],
                      "source": "PixelLab completed character export", "metrics": {
                          "canvasWidth": base_size[0], "canvasHeight": base_size[1], "pixelsPerUnit": 32,
                          "pivotPixels": {"x": base_size[0] / 2, "y": pivot_y}},
                      "pivotPolicy": "One fixed south ground anchor; animation canvas-centre padding compensation only. No per-frame trimming.",
                      "archiveSha256": hashlib.sha256(archive_path.read_bytes()).hexdigest()}
            pipeline.write(root / "SourceInfo.json", source)
            pipeline.ensure_meta(root / "SourceInfo.json", text=True)
            for direction in DIRS:
                blob = archive.read(remote["frames"]["rotations"][direction])
                texture(root / "Sprites/Rotations" / (direction + ".png"), blob, base_size, pivot_y)
                if direction == "south":
                    texture(root / (str(game_id) + "_Front.png"), blob, base_size, pivot_y)
            clips = []
            imported_hashes = []
            edge_contact = 0
            for original, directions in sorted(anims.items()):
                anim = MOKGUI_NAMES[original] if key == "TwistedMokgui" else original
                loop = anim in {"IDLE", "RUN", "STUN", "EXHAUSTED", "EXHAUSTED_LOOP"}
                fps = 8 if loop else 10  # Presentation defaults, never combat timing.
                frames = len(directions["south"])
                images = []
                selection = []
                task = {"animation": anim, "frameCount": frames, "fps": fps, "loop": loop}
                for direction in DIRS:
                    row = []
                    source_group = selected[original, direction]
                    selection.append({"direction": direction, "sourceAnimation": source_group, "groupId": groups[source_group]["groupId"]})
                    for i, remote_path in enumerate(directions[direction]):
                        blob = archive.read(remote_path)
                        path = root / "Sprites" / anim / direction / f"frame_{i:03d}.png"
                        texture(path, blob, base_size, pivot_y)
                        if path.read_bytes() != blob:
                            raise ValueError(f"Pixel bytes changed: {path}")
                        img = Image.open(io.BytesIO(blob)).convert("RGBA")
                        bounds = img.getbbox()
                        if not bounds:
                            raise ValueError(f"Empty source frame: {remote_path}")
                        edge_contact += int(bounds[0] == 0 or bounds[1] == 0 or bounds[2] == img.width or bounds[3] == img.height)
                        row.append(img)
                        imported_hashes.append({"path": str(path.relative_to(root)).replace('\\', '/'), "sourcePath": remote_path,
                                                "sha256": hashlib.sha256(blob).hexdigest()})
                    images.append(row)
                width = max(img.width for row in images for img in row)
                height = max(img.height for row in images for img in row)
                sheet = Image.new("RGBA", (width * frames, height * len(DIRS)))
                for y, row in enumerate(images):
                    for x, img in enumerate(row):
                        sheet.paste(img, (x * width + (width-img.width)//2, y * height + (height-img.height)//2))
                buffer = io.BytesIO()
                sheet.save(buffer, format="PNG")
                sheet_path = root / "Sheets" / (str(game_id) + "_" + anim + ".png")
                texture(sheet_path, buffer.getvalue(), base_size, pivot_y, centered=True)
                layout = {"animation": anim, "sourceAnimation": original, "directions": DIRS, "frameCount": frames,
                          "framesPerSecond": fps, "loop": loop, "cellWidth": width, "cellHeight": height,
                          "sourcePixelsResized": False, "alignment": "Canvas centres, transparent padding only",
                          "directionalSources": selection,
                          "animationClips": [pipeline.make_clip(root, task, d) for d in DIRS]}
                pipeline.write(sheet_path.with_suffix(".json"), layout)
                pipeline.ensure_meta(sheet_path.with_suffix(".json"), text=True)
                clips.append(layout)
            pipeline.write(root / "AnimationSource.json", {"key": key, "characterId": cid, "pixelsPerUnit": 32,
                           "rootMotion": False, "visualApproval": "Existing completed assets imported; technical validation is not new visual approval.",
                           "rejectedGroupsExcluded": sorted(n for n in remote["frames"]["animations"] if n.startswith("REJECTED_")),
                           "clips": clips})
            pipeline.ensure_meta(root / "AnimationSource.json", text=True)
            entry = {"gameId": game_id, "key": key, "name": source["name"], "prefabName": source["prefabName"],
                     "clipCount": len(clips)*8, "frameCount": len(imported_hashes), "sourceFrameEdgeContacts": edge_contact,
                     "directionalRevisionCount": sum(s["sourceAnimation"] != c["sourceAnimation"] for c in clips for s in c["directionalSources"]),
                     "sourceByteEqualityVerified": True, "frames": imported_hashes}
            report["characters"].append(entry)
            pipeline.write(OUT / "ImportReport.json", report)
            print(json.dumps({k:v for k,v in entry.items() if k != "frames"}, ensure_ascii=False), flush=True)
    repair_references()
    report["clipCount"] = sum(c["clipCount"] for c in report["characters"])
    report["frameCount"] = sum(c["frameCount"] for c in report["characters"])
    report["status"] = "imported_pending_unity_validation"
    pipeline.write(OUT / "ImportReport.json", report)
    print("IMPORT_COMPLETE", flush=True)


def repair_references():
    """Reconcile after publication: Unity can discard a meta seen before its PNG.

    At this point every PNG exists. Restore the pixel-art settings, retaining the
    GUID Unity currently knows, then resolve all clips against these final GUIDs.
    This also provides a fast, generation-free repair mode for cached imports.
    """
    for game_id, key, _, _, _ in CHARACTERS:
        root = ART / str(game_id)
        source = pipeline.read(root / "SourceInfo.json")
        metrics = source["metrics"]
        base_size = (metrics["canvasWidth"], metrics["canvasHeight"])
        for path in root.rglob("*.png"):
            texture(path, path.read_bytes(), base_size, metrics["pivotPixels"]["y"], centered=path.parent.name == "Sheets")
        for spec in pipeline.read(root / "AnimationSource.json")["clips"]:
            task = {"animation": spec["animation"], "frameCount": spec["frameCount"],
                    "fps": spec["framesPerSecond"], "loop": spec["loop"]}
            for direction in spec["directions"]:
                pipeline.make_clip(root, task, direction)
        print(f"REFERENCES_RECONCILED {key}", flush=True)


if __name__ == "__main__":
    repair_references() if "--repair" in sys.argv else main()
