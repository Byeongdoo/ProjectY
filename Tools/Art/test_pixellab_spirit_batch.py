"""Protect paid submissions against overbooking and duplicate resumption."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import pixellab_spirit_batch as batch

DIRS = ["south", "south-east", "east", "north-east", "north", "north-west", "west", "south-west"]
GROUP = "11111111-1111-4111-8111-111111111111"


def result(text):
    return {"content": [{"type": "text", "text": text}]}


class Server:
    def __init__(self, active=0, uncertain=False):
        self.active = active
        self.completed = []
        self.calls = []
        self.uncertain = uncertain

    def call(self, name, arguments):
        if name == "get_character":
            if not self.completed:
                return result("status: completed\nanimations: none")
            return result(f"animations (1 group):\n  IDLE — {len(self.completed)} dir ({', '.join(self.completed)}), 4f [group: {GROUP}]")
        if name == "wait_for_jobs":
            if not self.active:
                return result("no active jobs, nothing is running")
            return result(f"timeout: {self.active} still running:\n" + "\n".join(f"00000000-0000-4000-8000-{i:012d} still running" for i in range(self.active)))
        if name == "animate_character":
            self.calls.append(copy.deepcopy(arguments))
            if self.uncertain:
                raise TimeoutError("response lost after submit")
            jobs = ", ".join(f"{d}=12345678-0000-4000-8000-{i:012d}" for i, d in enumerate(arguments["directions"]))
            return result(f"job_ids: {jobs}\ngroup: {GROUP}\nframes: 4")
        raise AssertionError(name)


class QueueSafetyTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        folder = Path(self.temp.name)
        self.patch = patch.multiple(batch, ROOT=folder, OUT=folder, PLAN=folder/"plan.json", STATE=folder/"state.json")
        self.patch.start()
        batch.write(batch.PLAN, {"characters": [{"key": "Test", "name": "Test", "characterId": GROUP}], "tasks": [{"key": "Test", "characterId": GROUP, "animation": "IDLE", "frameCount": 4, "fps": 8, "loop": True, "mode": "v3", "action": "idle", "directions": DIRS}]})
        self.sleep = patch.object(batch.time, "sleep")
        self.sleep.start()
        self.imports = patch.object(batch.Batch, "import_ready")
        self.imports.start()

    def tearDown(self):
        self.imports.stop(); self.sleep.stop(); self.patch.stop(); self.temp.cleanup()

    def test_full_account_does_not_submit(self):
        server = Server(active=10)
        worker = batch.Batch(server)
        worker.step()
        self.assertEqual(server.calls, [])

    def test_partial_group_resume_appends_only_missing_directions(self):
        server = Server(active=8)
        worker = batch.Batch(server)
        worker.step()
        self.assertEqual(server.calls[0]["directions"], DIRS[:2])
        server.active = 0
        server.completed = DIRS[:2]
        resumed = batch.Batch(server)
        resumed.step()
        self.assertEqual(len(server.calls), 2)
        self.assertEqual(server.calls[1]["animation_group_id"], GROUP)
        self.assertEqual(server.calls[1]["directions"], DIRS[2:])
        self.assertEqual(len(resumed.state["tasks"]["Test/IDLE"]["submitted"]), 8)

    def test_lost_paid_response_is_not_blindly_resubmitted(self):
        server = Server(uncertain=True)
        worker = batch.Batch(server)
        with self.assertRaisesRegex(RuntimeError, "Uncertain"):
            worker.step()
        resumed = batch.Batch(server)
        with self.assertRaisesRegex(RuntimeError, "Unreconciled"):
            resumed.step()
        self.assertEqual(len(server.calls), 1)


if __name__ == "__main__":
    unittest.main()
