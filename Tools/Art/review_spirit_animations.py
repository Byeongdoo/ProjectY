"""Build reproducible frame metrics and a local, shared-clock review player."""
import json
from pathlib import Path
import statistics
from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "artifacts/pixellab/SpiritAnimationQA"
ART = ROOT / "Client/Assets/Resources/Art/Characters/SpiritsV2"
NAMES = {"MumyeongJangsu": "무명장수", "Mangnani": "망나니", "ChakhoGapsa": "착호갑사", "Kkokdusoe": "꼭두쇠", "Uinyeo": "의녀"}


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def load(path):
    with Image.open(path) as image:
        return image.convert("RGBA")


def centered(image, size=160):
    result = Image.new("RGBA", (size, size))
    result.paste(image, ((size-image.width)//2, (size-image.height)//2))
    return result


def distance(a, b):
    a, b = centered(a), centered(b)
    mask = ImageChops.lighter(a.getchannel("A"), b.getchannel("A"))
    diff = ImageChops.difference(a, b)
    changed = sum(1 for rgba, alpha in zip(diff.get_flattened_data(), mask.get_flattened_data()) if alpha > 16 and max(rgba) > 30)
    total = sum(1 for alpha in mask.get_flattened_data() if alpha > 16)
    return changed / max(1, total)


def url(path):
    return "../../../" + path.relative_to(ROOT).as_posix()


def build():
    OUT.mkdir(parents=True, exist_ok=True)
    data = {"characters": [], "clips": [], "flags": [], "availableClips": 0}
    for key, name in NAMES.items():
        root = ART / key
        front = read(root/"SourceInfo.json")["metrics"]
        source = read(root/"AnimationSource.json") if (root/"AnimationSource.json").exists() else {"clips": []}
        data["characters"].append({"key": key, "name": name, "pivot": front["pivotPixels"], "canvasWidth": front["canvasWidth"], "canvasHeight": front["canvasHeight"]})
        for spec in source["clips"]:
            entry = {"key": key, "name": name, "motion": spec["animation"], "fps": spec["framesPerSecond"], "loop": spec["loop"], "directions": []}
            for direction in spec["directions"]:
                paths = sorted((root/"Sprites"/spec["animation"]/direction).glob("frame_*.png"))
                if len(paths) != spec["frameCount"]:
                    continue
                frames = [load(p) for p in paths]
                boxes = [im.getchannel("A").point(lambda a: 255 if a>16 else 0).getbbox() for im in frames]
                changes = [distance(a,b) for a,b in zip(frames,frames[1:])]
                seam = distance(frames[-1], frames[0])
                seam_ratio = seam / max(.01, statistics.median(changes))
                idle_path = root/"Sprites/IDLE"/direction/"frame_000.png"
                idle = load(idle_path) if idle_path.exists() else frames[0]
                start_idle, end_idle = distance(frames[0],idle), distance(frames[-1],idle)
                flags = []
                if spec["loop"] and seam_ratio>1.6 and seam>.18:
                    flags.append("루프 연결 확인")
                if not spec["loop"] and spec["animation"] not in {"DIE","KNOCKBACK","HIT"} and end_idle>max(.48,start_idle*1.35):
                    flags.append("대기 복귀 자세 확인")
                if any(b and (b[0]==0 or b[1]==0 or b[2]==im.width or b[3]==im.height) for b,im in zip(boxes,frames)):
                    flags.append("캔버스 가장자리 확인")
                normalized_bottom = [im.height-b[3]-(im.height-front['canvasHeight'])/2 for b,im in zip(boxes,frames) if b]
                foot_range = max(normalized_bottom)-min(normalized_bottom) if normalized_bottom else 0
                if spec["animation"]=="IDLE" and foot_range>2:
                    flags.append("대기 발 위치 확인")
                variant = root/"SpritesWithTrail"/spec["animation"]/direction
                variant_paths = sorted(variant.glob("frame_*.png")) if variant.exists() else []
                row = {"direction": direction, "frames": [url(p) for p in paths], "trailFrames": [url(p) for p in variant_paths], "sizes": [im.size for im in frames], "flags": flags, "metrics": {"loopSeam": round(seam,3), "seamVsAdjacent": round(seam_ratio,3), "startVsIdle": round(start_idle,3), "endVsIdle": round(end_idle,3), "bottomRange": foot_range}}
                entry["directions"].append(row)
                data["availableClips"] += 1
                if flags:
                    data["flags"].append({"key": key, "motion": spec["animation"], "direction": direction, "flags": flags, "metrics": row["metrics"]})
            data["clips"].append(entry)
    (OUT/"ReviewData.json").write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding="utf-8")
    html = (Path(__file__).parent/"spirit_animation_review.html").read_text(encoding="utf-8")
    (OUT/"viewer.html").write_text(html.replace("__DATA__",json.dumps(data,ensure_ascii=False)),encoding="utf-8")
    summary = {"availableClips":data["availableClips"], "motionGroups":len(data["clips"]), "automaticReviewFlags":len(data["flags"]), "note":"Flags are inspection cues, not automatic artistic failures.", "flags":data["flags"]}
    (OUT/"FrameAnalysis.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps({k:v for k,v in summary.items() if k!="flags"},ensure_ascii=False))


if __name__=="__main__":
    build()
