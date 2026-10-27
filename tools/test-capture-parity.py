#!/usr/bin/env python3
"""Source-free regression tests for the before/after comparator's normalization boundary."""

import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("capture_parity", Path(__file__).with_name("compare-capture-parity.py"))
parity = importlib.util.module_from_spec(spec)
spec.loader.exec_module(parity)


class CaptureParityTests(unittest.TestCase):
    def test_only_build_identity_and_start_time_are_removed_from_header(self):
        header = {"AssemblyId": "first", "Assemblies": {"app": "first"}, "StartedUtc": "first",
                  "Sources": [{"Game": 1, "BuildId": "native", "Sha256": "fixture"}], "Version": 2}
        other = {**header, "AssemblyId": "next", "Assemblies": {}, "StartedUtc": "later"}
        self.assertEqual(parity.stable_header([{"Header": header}]), parity.stable_header([{"Header": other}]))
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

    def test_qualified_commands_are_not_normalized(self):
        def rows(source):
            return [{"Step": {"Events": [], "Commands": [{"Kind": "equip", "Value": source + "/item/30"}]}}]
        self.assertNotEqual(parity.protocol(rows("rac1")), parity.protocol(rows("rac2")))

    def test_incomplete_or_empty_archives_cannot_claim_parity(self):
        self.assertFalse(parity.completed([]))
        self.assertFalse(parity.completed([{"Step": {}}]))
        self.assertFalse(parity.completed([{"Step": {}}, {"End": {"Reason": "saved", "Steps": 2}}]))
        self.assertTrue(parity.completed([{"Step": {}}, {"End": {"Reason": "saved", "Steps": 1}}]))


if __name__ == "__main__":
    unittest.main()
