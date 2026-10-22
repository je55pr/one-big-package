#!/usr/bin/env python3
"""Pure registration grammar and retained payload-free R&C1 atlas checks."""
import importlib.util
import json
import struct
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools/rac1-moby-updater-atlas.py"
spec = importlib.util.spec_from_file_location("rac1_moby_updater_atlas", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class Rac1MobyUpdaterAtlasTests(unittest.TestCase):
    def setUp(self):
        self.ids = set(range(1, 9))
        self.rows = b"".join(struct.pack("<III", i, 0 if i == 3 else 0x1000 + i * 4, 0x800)
                             for i in sorted(self.ids))
        self.sentinel = struct.pack("<III", 0xffffffff, 0, 0x800)
        self.mapped = lambda addr: 0x1000 <= addr <= 0x1100 and addr % 4 == 0

    def test_terminates_before_unrelated_trailing_pointer_data(self):
        records, end = module.parse_registration_rows(
            self.rows + self.sentinel + b"\xff" * 24, self.ids, self.mapped)
        self.assertEqual(len(records), 8)
        self.assertEqual(end, len(self.rows))
        self.assertEqual(records[2], (3, 0, 0x800))
        self.assertEqual(records[0], (1, 0x1004, 0x800))

    def test_rejects_unknown_class_duplicate_and_unmapped_updater(self):
        bad_class = struct.pack("<III", 999, 0x1004, 0x800) + self.rows[12:]
        with self.assertRaisesRegex(ValueError, "absent"):
            module.parse_registration_rows(bad_class + self.sentinel, self.ids, self.mapped)
        repeated = self.rows[:12] + self.rows[:12] + self.rows[24:]
        with self.assertRaisesRegex(ValueError, "duplicate"):
            module.parse_registration_rows(repeated + self.sentinel, self.ids, self.mapped)
        bad_update = self.rows[:12] + struct.pack("<III", 2, 0x9999, 0x800) + self.rows[24:]
        with self.assertRaisesRegex(ValueError, "mapped"):
            module.parse_registration_rows(bad_update + self.sentinel, self.ids, self.mapped)

    def test_rejects_missing_sentinel(self):
        with self.assertRaisesRegex(ValueError, "no terminating sentinel"):
            module.parse_registration_rows(self.rows, self.ids, self.mapped)

    def test_retained_atlas_covers_all_nineteen_levels_without_guessing_fields(self):
        atlas = json.loads((ROOT / "research/generated/rac1-moby-updater-atlas.json").read_text())
        self.assertEqual(atlas["schemaVersion"], 1)
        self.assertEqual(atlas["thirdWordSemantics"][:7], "unknown")
        self.assertEqual(len(atlas["levels"]), 19)
        self.assertEqual(atlas["totals"], {
            "registeredRows": 3077, "nonzeroUpdaterRows": 3037, "noUpdaterRows": 40,
            "authoredClassRows": 3641, "unregisteredAuthoredClassRows": 564,
            "placedInstances": 16232, "placedRegisteredInstances": 15116,
            "placedUnregisteredInstances": 1116,
        })
        self.assertEqual(sum(row["registered"] for row in atlas["levels"]), 3077)
        # Class 505 is a shared-updater candidate, NOT necessarily class 500 behavior.
        levels_505 = [level for level in atlas["levels"]
                      if any(r["classId"] == 505 for r in level["rows"])]
        self.assertEqual(len(levels_505), 18)
        self.assertEqual(sum(next(r["placements"] for r in level["rows"]
                                  if r["classId"] == 505) for level in levels_505), 608)
        for level in levels_505:
            group = [g for g in level["sharedUpdateGroups"] if 505 in g["classes"]]
            self.assertEqual(len(group), 1)
            self.assertTrue({500, 501, 505, 511}.issubset(group[0]["classes"]))


if __name__ == "__main__":
    unittest.main()
