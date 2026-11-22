"""Resume the explicitly requested ProjectY spirit-animation production batch.

Credentials are read from the existing Codex PixelLab connection, never copied
into plans, logs, command arguments or Unity assets. Uses the same MCP tools as
Codex, with a durable client-side queue for PixelLab's ten-job limit.
"""
from __future__ import annotations

import argparse
import io
import json
import os
from pathlib import Path
import re
import sys
import time
import tomllib
import urllib.error
import urllib.request
import uuid
import zipfile
from datetime import datetime, timezone, timedelta

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "artifacts/pixellab/SpiritAnimationProduction"
PLAN = OUT / "ProductionPlan.json"
STATE = OUT / "QueueState.json"
UUID = r"[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}"


def now():
    return datetime.now(timezone.utc).isoformat()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, value):
    if path.is_relative_to(ROOT / "Client/Assets"):
        put_bytes(path, json.dumps(value, ensure_ascii=False, indent=2).encode("utf-8"))
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(temp, path)


def put_bytes(path, data):
    """Publish complete files atomically so Unity never imports a partial PNG."""
    ensure_folder(path.parent)
    staging = OUT / "Staging" / (uuid.uuid4().hex + ".tmp")
    staging.parent.mkdir(parents=True, exist_ok=True)
    staging.write_bytes(data)
    os.replace(staging, path)


def texts(result):
    return "\n".join(c.get("text", "") for c in result.get("content", []) if c.get("type") == "text")


class MCP:
    def __init__(self):
        config = tomllib.loads((Path.home() / ".codex/config.toml").read_text(encoding="utf-8-sig"))
        server = config["mcp_servers"]["pixellab"]
        self.url = server.get("url", "https://api.pixellab.ai/mcp")
        if self.url.rstrip("/") != "https://api.pixellab.ai/mcp":
            raise RuntimeError("Unexpected PixelLab endpoint; stopped before sending credentials")
        self.headers = dict(server.get("http_headers", {}))
        for key, env in server.get("env_http_headers", {}).items():
            self.headers[key] = os.environ[env]
        if "Authorization" not in self.headers:
            self.headers["Authorization"] = server.get("env", {}).get("AUTH_HEADER") or os.environ.get("AUTH_HEADER", "")
        if not self.headers.get("Authorization"):
            raise RuntimeError("Existing PixelLab authorization unavailable")
        self.session = None
        self.serial = 0
        result = self.rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "ProjectYSpiritBatch", "version": "1.0"}})
        self.protocol = result.get("protocolVersion", "2024-11-05")
        self.rpc("notifications/initialized", notification=True)

    def rpc(self, method, params=None, notification=False):
        self.serial += 1
        body = {"jsonrpc": "2.0", "method": method}
        if not notification:
            body["id"] = self.serial
        if params is not None:
            body["params"] = params
        headers = {**self.headers, "Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
        if self.session:
            headers["Mcp-Session-Id"] = self.session
        if hasattr(self, "protocol"):
            headers["MCP-Protocol-Version"] = self.protocol
        request = urllib.request.Request(self.url, json.dumps(body).encode(), headers)
        try:
            with urllib.request.urlopen(request, timeout=100) as response:
                self.session = response.headers.get("Mcp-Session-Id", self.session)
                payload = response.read().decode("utf-8")
        except urllib.error.HTTPError as error:
            # Never print request headers or raw connection objects.
            raise RuntimeError("PixelLab HTTP " + str(error.code)) from None
        if notification or not payload.strip():
            return {}
        if payload.lstrip().startswith("{"):
            message = json.loads(payload)
        else:
            messages = [json.loads(line[5:].strip()) for line in payload.splitlines() if line.startswith("data:") and line[5:].strip()]
            message = next((m for m in reversed(messages) if m.get("id") == body["id"]), {})
        if "error" in message:
            raise RuntimeError("PixelLab MCP error " + str(message["error"].get("code")))
        if "result" not in message:
            raise RuntimeError("Missing MCP response; reconcile before resubmitting")
        return message["result"]

    def call(self, name, arguments):
        attempts = 3 if name in {"get_character", "get_image", "wait_for_jobs", "get_balance"} else 1
        for attempt in range(attempts):
            try:
                return self.rpc("tools/call", {"name": name, "arguments": arguments})
            except (RuntimeError, OSError):
                if attempt + 1 == attempts:
                    raise
                time.sleep(5 * (attempt + 1))


def parse_groups(text):
    groups = {}
    pattern = r"^\s{2}(.+?) — \d+ dir \(([^)]*)\), (\d+)f[^\n]*\[group: (" + UUID + r")\]"
    for name, dirs, frames, group in re.findall(pattern, text, re.M):
        groups[name] = {"groupId": group, "directions": [d.strip() for d in dirs.split(",")], "frames": int(frames)}
    return groups


def parse_receipt(text):
    group = re.search(r"^group: (" + UUID + r")", text, re.M)
    jobs = dict(re.findall(r"([a-z-]+)=(" + UUID + r")", text.split("\njob_ids:")[-1].split("\ngroup:")[0]))
    if not group or not jobs:
        raise RuntimeError("Uncertain submission response; waiting for reconciliation")
    return group[1], jobs


def running_jobs(text):
    return re.findall(r"^(" + UUID + r") still running", text, re.M)


class Batch:
    def __init__(self, mcp):
        self.mcp = mcp
        self.plan = read(PLAN)
        self.state = read(STATE) if STATE.exists() else {"createdAt": now(), "tasks": {}, "status": "starting"}
        motion_order = list(dict.fromkeys(t["animation"] for t in self.plan["tasks"]))
        character_order = [c["key"] for c in self.plan["characters"]]
        self.tasks = sorted(self.plan["tasks"], key=lambda t: (motion_order.index(t["animation"]), character_order.index(t["key"])))
        for task in self.tasks:
            self.state["tasks"].setdefault(self.key(task), {"groupId": None, "submitted": {}, "completed": [], "imported": False, "receipts": [], "failures": 0})
        self.seed_receipts()

    @staticmethod
    def key(task):
        return task["key"] + "/" + task["animation"]

    def seed_receipts(self):
        for path in OUT.glob("Request_*.json"):
            doc = read(path)
            task = doc["task"]
            state = self.state["tasks"][self.key(task)]
            for receipt in doc["receipts"]:
                if receipt.get("isError"):
                    continue
                group, jobs = parse_receipt(texts(receipt))
                if state["groupId"] and state["groupId"] != group:
                    raise RuntimeError("Conflicting animation group IDs")
                state["groupId"] = group
                for direction, job in jobs.items():
                    state["submitted"].setdefault(direction, {"jobId": job, "submittedAt": now()})

    def save(self):
        self.state["updatedAt"] = now()
        complete = sum(len(t["completed"]) for t in self.state["tasks"].values())
        submitted = sum(len(t["submitted"]) for t in self.state["tasks"].values())
        self.state["counts"] = {"total": 440, "submitted": submitted, "completed": complete, "waiting": 440 - submitted}
        write(STATE, self.state)
        korean_time = datetime.now(timezone(timedelta(hours=9))).strftime("%Y-%m-%d %H:%M:%S KST")
        label = {"running": "순차 제작 중", "starting": "시작 중", "needs_attention": "확인 필요", "stopped_by_user": "사용자 중지", "generation_complete_pending_visual_review": "전체 생성·저장 완료, 시각 검수 대기"}.get(self.state.get("status"), self.state.get("status", ""))
        lines = ["# 원혼 5종 애니메이션 제작", "", "갱신: " + korean_time, "", "상태: " + label, "", f"PixelLab 접수 {submitted}/440 · 생성 완료 {complete}/440 · 요청 대기 {440-submitted}", "", "| 원혼 | 접수 | 생성 완료 | Unity 저장 |", "|---|---:|---:|---:|"]
        for char in self.plan["characters"]:
            rows = [self.state["tasks"][self.key(t)] for t in self.tasks if t["key"] == char["key"]]
            lines.append(f"| {char['name']} | {sum(len(t['submitted']) for t in rows)}/88 | {sum(len(t['completed']) for t in rows)}/88 | {sum(8 for t in rows if t['imported'])}/88 |")
        lines += ["", "동작: IDLE, RUN, ATTACK, HIT, STUN, DIE, KNOCKBACK, SKILL_1~4. 각 8방향.", "", "완성된 방향별 PNG·시트·Unity 애니메이션 클립은 Client/Assets/Resources/Art/Characters/SpiritsV2/<캐릭터>/에 저장합니다.", "스킬 이펙트와 실제 이동·타격 판정은 별도입니다. 생성 완료는 시각 검수 승인과 구별합니다.", "작업이 계속되려면 컴퓨터가 켜져 있고 인터넷에 연결되어 있어야 합니다. Codex·Unity 창을 닫아도 작업은 계속됩니다."]
        if self.state.get("lastError"):
            lines += ["", "최근 확인 사항: " + self.state["lastError"]]
        (OUT / "Progress.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

    def reconcile(self):
        for char in self.plan["characters"]:
            result = self.mcp.call("get_character", {"character_id": char["characterId"], "include_preview": False})
            if result.get("isError"):
                raise RuntimeError("Cannot inspect character " + char["key"])
            write(OUT / "ServiceState" / (char["key"] + ".json"), result)
            groups = parse_groups(texts(result))
            for task in self.tasks:
                if task["key"] != char["key"] or task["animation"] not in groups:
                    continue
                remote = groups[task["animation"]]
                local = self.state["tasks"][self.key(task)]
                if local["groupId"] and local["groupId"] != remote["groupId"]:
                    raise RuntimeError("Unexpected duplicate group: " + self.key(task))
                if remote["frames"] != task["frameCount"]:
                    raise RuntimeError("Unexpected frame count: " + self.key(task))
                local["groupId"] = remote["groupId"]
                local["completed"] = remote["directions"]
                if local["imported"]:
                    asset_root = ROOT / "Client/Assets/Resources/Art/Characters/SpiritsV2" / char["key"]
                    if any(not (asset_root / "Animations" / (task["animation"] + "_" + d.upper().replace("-", "_") + ".anim")).exists() for d in task["directions"]):
                        local["imported"] = False
                for direction in remote["directions"]:
                    local["submitted"].setdefault(direction, {"jobId": "reconciled", "submittedAt": now()})
                if set(local.get("intent", {}).get("directions", [])) <= set(remote["directions"]):
                    local.pop("intent", None)
        self.save()

    def submit(self, task, directions):
        state = self.state["tasks"][self.key(task)]
        state["intent"] = {"directions": directions, "createdAt": now()}
        self.save()  # write intent BEFORE the charged operation
        args = {"character_id": task["characterId"], "animation_name": task["animation"], "mode": task["mode"], "action_description": task["action"], "frame_count": task["frameCount"], "keep_first_frame": False, "directions": directions}
        if state["groupId"]:
            args["animation_group_id"] = state["groupId"]
        try:
            result = self.mcp.call("animate_character", args)
        except Exception:
            # An uncertain charged request must not be blindly repeated.
            self.state["status"] = "needs_reconciliation"
            self.state["lastError"] = "제작 요청의 응답을 확인하지 못해 중복 요청 방지를 위해 중지했습니다. 재실행 시 완료 상태부터 대조합니다."
            self.save()
            raise RuntimeError("Uncertain generation submission; stopped without retry") from None
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%f")
        receipt_file = OUT / "Receipts" / task["key"] / (task["animation"] + "_" + stamp + ".json")
        write(receipt_file, {"request": args, "response": result})
        if result.get("isError"):
            state.pop("intent", None)
            self.save()
            raise RuntimeError("Generation submission refused; inspect saved receipt")
        group, jobs = parse_receipt(texts(result))
        state["groupId"] = group
        for direction, job in jobs.items():
            state["submitted"][direction] = {"jobId": job, "submittedAt": now()}
        state["receipts"].append(str(receipt_file.relative_to(ROOT)))
        state.pop("intent", None)
        self.state["status"] = "running"
        self.state.pop("lastError", None)
        self.save()
        print(now(), "submitted", self.key(task), ",".join(jobs), flush=True)
        time.sleep(2.1)
        return len(jobs)

    def import_ready(self):
        from PIL import Image
        for char in self.plan["characters"]:
            pending = [t for t in self.tasks if t["key"] == char["key"] and len(self.state["tasks"][self.key(t)]["completed"]) == 8 and not self.state["tasks"][self.key(t)]["imported"]]
            if not pending:
                continue
            url = "https://api.pixellab.ai/mcp/characters/" + char["characterId"] + "/download"
            data = download(url)
            archive_path = OUT / "Downloads" / (char["key"] + ".zip")
            archive_path.parent.mkdir(parents=True, exist_ok=True)
            archive_path.write_bytes(data)
            with zipfile.ZipFile(io.BytesIO(data)) as archive:
                if any(p.endswith("MISSING_FRAMES.txt") for p in archive.namelist()):
                    raise RuntimeError("PixelLab reports missing frame storage; import postponed")
                metadata = json.loads(archive.read("metadata.json"))
                remote = next(s for s in metadata["states"] if s["character"]["id"] == char["characterId"])
                root = ROOT / "Client/Assets/Resources/Art/Characters/SpiritsV2" / char["key"]
                for direction, path in remote["frames"]["rotations"].items():
                    target = root / "Sprites/Rotations" / (direction + ".png")
                    target.parent.mkdir(parents=True, exist_ok=True)
                    blob = archive.read(path)
                    with Image.open(io.BytesIO(blob)) as image:
                        texture_meta(target, root, image.size)
                    put_bytes(target, blob)
                import_doc = read(root / "AnimationSource.json") if (root / "AnimationSource.json").exists() else {"key": char["key"], "characterId": char["characterId"], "pixelsPerUnit": 32, "rootMotion": False, "visualApproval": "Generated assets require visual review", "clips": []}
                for task in pending:
                    anim = task["animation"]
                    paths = remote["frames"]["animations"].get(anim)
                    if not paths or any(len(paths.get(d, [])) != task["frameCount"] for d in task["directions"]):
                        raise RuntimeError("Incomplete downloaded animation: " + self.key(task))
                    images = []
                    for direction in task["directions"]:
                        row = []
                        for index, path in enumerate(paths[direction]):
                            target = root / "Sprites" / anim / direction / f"frame_{index:03d}.png"
                            target.parent.mkdir(parents=True, exist_ok=True)
                            blob = archive.read(path)
                            with Image.open(io.BytesIO(blob)) as image:
                                texture_meta(target, root, image.size)
                                put_bytes(target, blob)
                                row.append(image.convert("RGBA"))
                        images.append(row)
                    width = max(image.width for row in images for image in row)
                    height = max(image.height for row in images for image in row)
                    sheet = Image.new("RGBA", (width * task["frameCount"], height * 8))
                    for y, row in enumerate(images):
                        for x, image in enumerate(row):
                            # Canvas padding only: keep every native source pixel unchanged.
                            sheet.paste(image, (x * width + (width-image.width)//2, y * height + (height-image.height)//2))
                    target = root / "Sheets" / (char["key"] + "_" + anim + ".png")
                    target.parent.mkdir(parents=True, exist_ok=True)
                    buffer = io.BytesIO()
                    sheet.save(buffer, format="PNG")
                    texture_meta(target, root, sheet.size, centered=True)
                    put_bytes(target, buffer.getvalue())
                    layout = {"animation": anim, "directions": task["directions"], "frameCount": task["frameCount"], "framesPerSecond": task["fps"], "loop": task["loop"], "cellWidth": width, "cellHeight": height, "sourcePixelsResized": False, "alignment": "PixelLab canvas centres; no per-frame trimming", "groupId": self.state["tasks"][self.key(task)]["groupId"]}
                    write(target.with_suffix(".json"), layout)
                    ensure_meta(target.with_suffix(".json"), text=True)
                    layout["animationClips"] = [make_clip(root, task, d) for d in task["directions"]]
                    import_doc["clips"] = [c for c in import_doc["clips"] if c["animation"] != anim] + [layout]
                    self.state["tasks"][self.key(task)]["imported"] = True
                write(root / "AnimationSource.json", import_doc)
                ensure_meta(root / "AnimationSource.json", text=True)
                write(OUT / "Downloads" / (char["key"] + "_metadata.json"), metadata)
            self.save()

    def step(self):
        self.reconcile()
        self.import_ready()
        result = self.mcp.call("wait_for_jobs", {"min_finished": 1, "timeout_seconds": 5})
        if result.get("isError"):
            raise RuntimeError("Cannot inspect available generation slots")
        text = texts(result)
        active = running_jobs(text)
        if not active and not re.search(r"nothing.*running|no.*running|0 still running|0 remaining|no active", text, re.I):
            # Finished jobs can coexist with active ones; only use explicit running lines.
            count = re.search(r"(\d+) still running", text)
            if count and int(count[1]) > 0:
                raise RuntimeError("Cannot parse remote queue; stopped before new charges")
        free = max(0, 10 - len(active))
        outstanding = [self.key(task) for task in self.tasks if set(self.state["tasks"][self.key(task)]["submitted"]) - set(self.state["tasks"][self.key(task)]["completed"])]
        self.state["inactiveChecks"] = self.state.get("inactiveChecks", 0) + 1 if not active and outstanding else 0
        if self.state["inactiveChecks"] >= 3:
            raise RuntimeError("Submitted jobs are no longer running but their frames are unavailable: " + ", ".join(outstanding))
        for task in self.tasks:
            local = self.state["tasks"][self.key(task)]
            if local.get("intent"):
                raise RuntimeError("Unreconciled previous submission; inspect service before resuming")
            missing = [d for d in task["directions"] if d not in local["submitted"]]
            if free and missing:
                free -= self.submit(task, missing[:free])
            if not free:
                break
        self.save()
        return all(len(t["completed"]) == 8 and t["imported"] for t in self.state["tasks"].values())


def ensure_meta(path, text=False):
    meta = Path(str(path) + ".meta")
    if meta.exists():
        return
    head = "fileFormatVersion: 2\nguid: " + uuid.uuid4().hex + "\n"
    if text:
        head += "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
    # Let Unity fill the importer settings, preserving this already assigned GUID.
    meta.write_text(head, encoding="utf-8")


def ensure_folder(path):
    assets = ROOT / "Client/Assets"
    if not path.is_relative_to(assets) or path == assets:
        path.mkdir(parents=True, exist_ok=True)
        return
    ensure_folder(path.parent)
    path.mkdir(exist_ok=True)
    meta = Path(str(path) + ".meta")
    if not meta.exists():
        meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid4().hex + "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n", encoding="utf-8")


def asset_guid(path):
    meta = Path(str(path) + ".meta")
    if meta.exists():
        match = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(encoding="utf-8-sig"), re.M)
        if match:
            return match[1]
    return uuid.uuid4().hex


def texture_meta(path, root, size, centered=False):
    """Reuse the verified Unity 6 importer and stable per-character foot anchor."""
    ensure_folder(path.parent)
    template = (root / (root.name + "_Front.png.meta")).read_text(encoding="utf-8-sig")
    source = read(root / "SourceInfo.json")["metrics"]
    width, height = size
    x = .5
    y = .5 if centered else (source["pivotPixels"]["y"] + (height-source["canvasHeight"])/2) / height
    template = re.sub(r"^guid: .*", "guid: " + asset_guid(path), template, count=1, flags=re.M)
    template = re.sub(r"^  spritePivot: .*", f"  spritePivot: {{x: {x:.9g}, y: {y:.9g}}}", template, count=1, flags=re.M)
    existing = Path(str(path) + ".meta")
    sprite_id = re.search(r"^    spriteID: ([0-9a-f]{32})", existing.read_text(encoding="utf-8-sig"), re.M) if existing.exists() else None
    template = re.sub(r"^    spriteID: .*", "    spriteID: " + (sprite_id[1] if sprite_id else uuid.uuid4().hex), template, count=1, flags=re.M)
    put_bytes(Path(str(path) + ".meta"), template.encode("utf-8"))


def make_clip(root, task, direction, frame_folder="Sprites"):
    """Write a native clip using the existing Unity-verified hero clip format."""
    name = task["animation"] + "_" + direction.upper().replace("-", "_")
    target = root / "Animations" / (name + ".anim")
    fps = task["fps"]
    guids = [asset_guid(root / frame_folder / task["animation"] / direction / f"frame_{i:03d}.png") for i in range(task["frameCount"])]
    template = (ROOT / "Client/Assets/Resources/Art/Characters/Protagonist/Animations/IDLE_SOUTH.anim").read_text(encoding="utf-8-sig")
    template = re.sub(r"^  m_Name: .*", "  m_Name: " + name, template, count=1, flags=re.M)
    curve = "  m_PPtrCurves:\n  - curve:\n"
    for i, guid in enumerate(guids):
        curve += f"    - time: {i/fps:.9g}\n      value: {{fileID: 21300000, guid: {guid}, type: 3}}\n"
    curve += "    attribute: m_Sprite\n    path: \n    classID: 212\n    script: {fileID: 0}\n"
    template = re.sub(r"  m_PPtrCurves:.*?(?=  m_SampleRate:)", lambda _: curve, template, count=1, flags=re.S)
    template = re.sub(r"^  m_SampleRate: .*", "  m_SampleRate: " + str(fps), template, count=1, flags=re.M)
    mapping = "    pptrCurveMapping:\n" + "".join(f"    - {{fileID: 21300000, guid: {guid}, type: 3}}\n" for guid in guids)
    template = re.sub(r"    pptrCurveMapping:.*?(?=  m_AnimationClipSettings:)", lambda _: mapping, template, count=1, flags=re.S)
    template = re.sub(r"^    m_StopTime: .*", "    m_StopTime: " + f"{len(guids)/fps:.9g}", template, count=1, flags=re.M)
    template = re.sub(r"^    m_LoopTime: .*", "    m_LoopTime: " + str(int(task["loop"])), template, count=1, flags=re.M)
    ensure_folder(target.parent)
    guid = asset_guid(target)
    meta = "fileFormatVersion: 2\nguid: " + guid + "\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 7400000\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
    put_bytes(Path(str(target) + ".meta"), meta.encode("utf-8"))
    put_bytes(target, template.encode("utf-8"))
    return str(target.relative_to(root)).replace("\\", "/")


def download(url):
    for attempt in range(3):
        try:
            with urllib.request.urlopen(url, timeout=100) as response:
                return response.read()
        except (urllib.error.URLError, TimeoutError):
            if attempt == 2:
                raise RuntimeError("PixelLab download temporarily unavailable; safe to resume") from None
            time.sleep(5 * (attempt + 1))


def acquire_lock():
    import msvcrt
    handle = (OUT / "worker.lock").open("a+b")
    handle.seek(0)
    if not handle.read(1):
        handle.write(b"0")
        handle.flush()
    handle.seek(0)
    msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
    return handle


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["probe", "once", "run", "status"])
    args = parser.parse_args()
    if args.mode == "status":
        print(json.dumps(read(STATE).get("counts", {}), ensure_ascii=False))
        return
    client = MCP()
    if args.mode == "probe":
        result = client.call("get_balance", {})
        print(texts(result))
        return
    OUT.mkdir(parents=True, exist_ok=True)
    lock = acquire_lock()
    write(OUT / "WorkerProcess.json", {"pid": os.getpid(), "startedAt": now(), "plan": str(PLAN)})
    batch = Batch(client)
    try:
        while True:
            if (OUT / "STOP").exists():
                batch.state["status"] = "stopped_by_user"
                batch.save()
                return
            complete = batch.step()
            if complete:
                batch.state["status"] = "generation_complete_pending_visual_review"
                batch.save()
                print("ALL_440_CLIPS_GENERATED_AND_SAVED", flush=True)
                return
            if args.mode == "once":
                return
            result = client.call("wait_for_jobs", {"min_finished": 1, "timeout_seconds": 40})
            write(OUT / "LastWait.json", result)
            if not running_jobs(texts(result)):
                time.sleep(10)
    except Exception as error:
        batch.state["status"] = "needs_attention"
        batch.state["lastError"] = str(error)
        batch.save()
        print(type(error).__name__ + ": " + str(error), file=sys.stderr, flush=True)
        raise SystemExit(1)
    finally:
        lock.close()


if __name__ == "__main__":
    main()
