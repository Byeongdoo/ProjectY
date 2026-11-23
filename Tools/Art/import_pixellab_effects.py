"""Import completed PixelLab VFX, preserving native PNGs and transparency.

No charged generation calls. Unity's PixelLabEffectBuilder creates importers,
clips, controllers and prefabs from the resulting delivery manifest.
"""
from __future__ import annotations

import hashlib
import io
import json
import re
import zipfile

from PIL import Image
import pixellab_spirit_batch as pipeline

ROOT = pipeline.ROOT
OUT = ROOT / "artifacts/pixellab/EffectsImport"
ART = ROOT / "Client/Assets/Resources/Art/Effects"
ONE_SHOTS = {"MountainWailRing", "RootEruption", "TankSlamImpact"}
GROUND = {"WarningGrid", "MountainWailRing", "LowMist", "SealPulse"}
AMBIENT = {"LowMist", "SealPulse", "SoulFlame"}


def main():
    specs, audit = [], []
    for entry in pipeline.read(OUT / "PixelLabSources.json"):
        key, cid = entry["key"], entry["id"]
        scope = "Sansindang" if key in AMBIENT else "Mokgui"
        name = scope + "_" + key
        root = ART / scope / key
        frames_zip = OUT / "Downloads" / (key + "_Frames.zip")
        sheets_zip = OUT / "Downloads" / (key + "_Spritesheet.zip")
        for path, endpoint in [(frames_zip, "download"), (sheets_zip, "spritesheet")]:
            if not path.exists():
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(pipeline.download(f"https://api.pixellab.ai/mcp/objects/{cid}/{endpoint}"))
        with zipfile.ZipFile(sheets_zip) as archive:
            metadata = next(json.loads(archive.read(p)) for p in archive.namelist() if p.endswith(".json"))
        animation_rows = [r for r in metadata["spritesheet"]["rows"] if r["type"] == "animation"]
        if len(animation_rows) != 1:
            raise ValueError("Reconcile animation groups before importing " + key)
        row = animation_rows[0]
        with zipfile.ZipFile(frames_zip) as archive:
            if any(p.endswith("MISSING_FRAMES.txt") for p in archive.namelist()):
                raise ValueError("Missing source frames: " + key)
            paths = sorted(p for p in archive.namelist() if p.startswith("animations/") and p.endswith(".png"))
            if len(paths) != row["frame_count"] or [int(re.search(r"frame_(\d+)\.png$", p)[1]) for p in paths] != list(range(row["frame_count"])):
                raise ValueError("Incomplete or ambiguous frames: " + key)
            base = archive.read("rotations/unknown.png")
            pipeline.put_bytes(root / "Sprites/Base.png", base)
            frames, frame_paths, records = [], [], []
            for i, path in enumerate(paths):
                blob = archive.read(path)
                native = Image.open(io.BytesIO(blob)).convert("RGBA")
                target = root / "Sprites" / f"frame_{i:03d}.png"
                pipeline.put_bytes(target, blob)
                if target.read_bytes() != blob:
                    raise ValueError("Modified source bytes: " + key)
                frames.append(native)
                frame_paths.append(str(target.relative_to(ROOT / "Client")).replace("\\", "/"))
                records.append({"path": frame_paths[-1], "archivePath": path, "sha256": hashlib.sha256(blob).hexdigest(),
                                "width": native.width, "height": native.height})
            width = max(frame.width for frame in frames)
            height = max(frame.height for frame in frames)
            sheet = Image.new("RGBA", (width * len(frames), height))
            for i, frame in enumerate(frames):
                sheet.paste(frame, (i * width + (width-frame.width)//2, (height-frame.height)//2))
            buffer = io.BytesIO()
            sheet.save(buffer, format="PNG")
            sheet_path = root / "Sheets" / (name + ".png")
            pipeline.put_bytes(sheet_path, buffer.getvalue())
            loop = key not in ONE_SHOTS
            spec = {"key": key, "scope": scope, "name": name, "objectId": cid,
                    "root": str(root.relative_to(ROOT / "Client")).replace("\\", "/"),
                    "sourceAnimation": row["animation"], "animationGroupId": row["animation_group_id"],
                    "frameCount": len(frames), "framesPerSecond": 8 if loop else 10, "loop": loop,
                    "timingSource": "Presentation defaults; PixelLab export has no frame durations. Not combat/cast timing.",
                    "clearSpriteOnCompletion": not loop,
                    "sortingTableLayer": "Details" if key in GROUND else "Animated",
                    "pixelsPerUnit": 32, "pivot": {"x": .5, "y": .5}, "rootMotion": False,
                    "sourcePixelsResized": False, "frames": frame_paths,
                    "baseSprite": str((root / "Sprites/Base.png").relative_to(ROOT / "Client")).replace("\\", "/"),
                    "sheet": str(sheet_path.relative_to(ROOT / "Client")).replace("\\", "/"),
                    "clip": str((root / "Animations" / (name + ".anim")).relative_to(ROOT / "Client")).replace("\\", "/"),
                    "controller": str((root / (name + ".controller")).relative_to(ROOT / "Client")).replace("\\", "/"),
                    "prefab": f"Assets/Resources/Prefabs/Effect/{scope}/{name}.prefab",
                    "resourcePath": f"Prefabs/Effect/{scope}/{name}",
                    "lifetimePolicy": "One-shot clears its sprite after the full final frame; owner releases/pools the instance. Loop owner controls stop/release."}
            pipeline.write(root / "SourceInfo.json", {**spec, "archiveSha256": hashlib.sha256(frames_zip.read_bytes()).hexdigest(),
                                                       "sourceFiles": records, "visualApproval": "Existing assets imported; technical validation is not visual approval."})
            layout = {"animation": row["animation"], "animationGroupId": row["animation_group_id"],
                      "columns": len(frames), "rows": 1, "cellWidth": width, "cellHeight": height,
                      "frameCount": len(frames), "sourcePixelsResized": False, "pivot": "cell-center"}
            pipeline.write(sheet_path.with_suffix(".json"), layout)
            specs.append(spec)
            audit.append({"name": name, "frameCount": len(frames), "pngByteEqualityVerified": True, "sourceFiles": records})
            print(name, len(frames), "loop" if loop else "one-shot", flush=True)
    pipeline.write(ART / "PixelLabEffects.json", {"version": 1, "pixelsPerUnit": 32, "effects": specs})
    pipeline.write(OUT / "ImportReport.json", {"effectCount": len(specs), "frameCount": sum(s["frameCount"] for s in specs),
                                               "sourcePixelsResized": False, "effects": audit})


if __name__ == "__main__":
    main()
