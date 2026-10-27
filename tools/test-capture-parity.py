#!/usr/bin/env python3
"""Source-free regression tests for the before/after comparator's normalization boundary."""

import importlib.util
import contextlib
import gzip
import io
import json
import tempfile
import unittest
from unittest import mock
from pathlib import Path

spec = importlib.util.spec_from_file_location("capture_parity", Path(__file__).with_name("compare-capture-parity.py"))
parity = importlib.util.module_from_spec(spec)
spec.loader.exec_module(parity)


class CaptureParityTests(unittest.TestCase):
    def test_only_build_identity_and_start_time_are_removed_from_header(self):
        header = {"AssemblyId": "first", "Assemblies": {"app": "first"}, "StartedUtc": "first",
                  "Sources": [{"Game": 1, "BuildId": "native", "Sha256": "fixture"}], "Version": 2}
        other = {**header, "AssemblyId": "next", "Assemblies": {"app": "next"}, "StartedUtc": "later"}
        self.assertEqual(parity.stable_header([{"Header": header}]), parity.stable_header([{"Header": other}]))
        self.assertNotEqual(parity.stable_header([{"Header": header}]), parity.stable_header([{"Header": {**other, "Assemblies": {"other": "next"}}}]))
        other["Sources"] = [{"Game": 2, "BuildId": "native", "Sha256": "fixture"}]
        self.assertNotEqual(parity.stable_header([{"Header": header}]), parity.stable_header([{"Header": other}]))

    def test_input_origin_order_and_frame_kind_survive_wall_time_normalization(self):
        event = {"Milliseconds": 12, "Code": 0, "PhysicalCode": 8, "Pressed": True}
        step = {"Index": 1, "Kind": 0, "Milliseconds": 12, "Events": [event], "Commands": []}
        later = {**step, "Milliseconds": 100, "Events": [{**event, "Milliseconds": 100}]}
        self.assertEqual(parity.stable_step(step), parity.stable_step(later))
        for changed in ({**later, "Kind": 1}, {**later, "Index": 2},
                        {**later, "Events": [{**event, "Code": 8, "PhysicalCode": 0}]},
                        {**later, "Events": [event, {**event, "Pressed": False}]}):
            self.assertNotEqual(parity.stable_step(step), parity.stable_step(changed))

    def test_informational_version_keeps_semantic_version_and_non_commit_metadata(self):
        def header(version):
            return parity.stable_header([{"Header": {"ApplicationVersion": version}}])
        self.assertEqual(header("1.0.0+" + "a" * 40), header("1.0.0+" + "b" * 40))
        self.assertNotEqual(header("1.0.0+" + "a" * 40), header("2.0.0+" + "b" * 40))
        self.assertNotEqual(header("1.0.0+custom"), header("1.0.0+other"))

    def test_qualified_commands_are_not_normalized(self):
        def rows(source):
            return [{"Step": {"Kind": 0, "Events": [], "Commands": [{"Kind": "equip", "Value": source + "/item/30"}]}}]
        self.assertNotEqual(parity.protocol(rows("rac1")), parity.protocol(rows("rac2")))

    def test_incomplete_or_empty_archives_cannot_claim_parity(self):
        self.assertFalse(parity.completed([]))
        self.assertFalse(parity.completed([{"Step": {}}]))
        self.assertFalse(parity.completed([{"Step": {}}, {"End": {"Reason": "saved", "Steps": 2}}]))
        self.assertTrue(parity.completed([{"Step": {}}, {"End": {"Reason": "saved", "Steps": 1}}]))

    def test_protocol_rejects_physics_drift_but_allows_async_waits_before_destination_entry(self):
        idle = {"Step": {"Kind": 0, "Events": [], "Commands": []}}
        action = {"Step": {"Kind": 0, "Events": [{"Code": 1}], "Commands": []}}
        entry = {"Step": {"Kind": 1, "Events": [], "Commands": [{"Kind": "destination", "Value": "rac2:LEVEL1"}]}}
        self.assertNotEqual(parity.protocol([action]), parity.protocol([idle, action]))
        self.assertEqual(parity.protocol([entry, action], True), parity.protocol([idle, entry, action], True))
        self.assertNotEqual(parity.protocol([entry, action], True), parity.protocol([entry, idle, action], True))

    def test_corrupt_or_truncated_gzip_emits_failure_json_without_a_traceback(self):
        payload = gzip.compress(b'{"Header":{}}\n')
        corrupt = bytearray(payload)
        corrupt[10] = 255
        for tape in (payload[:-8], bytes(corrupt)):
            with tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                for name in ("capabilities-observed.json", "profile-write.json", "profile-restart.json"):
                    (root / name).write_text(json.dumps({"Result": "passed", "Checks": [{"Passed": True}]}), encoding="utf-8")
                (root / "legacy.run.jsonl.gz").write_bytes(tape)
                output = root / "comparison.json"
                console = io.StringIO()
                with mock.patch("sys.argv", ["compare", str(root), str(root), "--out", str(output)]), contextlib.redirect_stdout(console):
                    self.assertEqual(1, parity.main())
                self.assertFalse(json.loads(output.read_text(encoding="utf-8"))["Passed"])
                self.assertNotIn("Traceback", console.getvalue())
                self.assertNotIn(str(root), console.getvalue())


if __name__ == "__main__":
    unittest.main()
