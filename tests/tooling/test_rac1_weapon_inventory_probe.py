import importlib.util
import json
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-weapon-inventory-probe.py"
GENERATED = ROOT / "research" / "generated" / "rac1-weapon-inventory-state.json"

SPEC = importlib.util.spec_from_file_location("rac1_weapon_inventory_probe", SCRIPT)
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


class Rac1WeaponInventoryProbeTests(unittest.TestCase):
    def test_generated_witness_pins_save_layout_and_generic_ammo_fields(self):
        report = json.loads(GENERATED.read_text(encoding="utf-8"))

        self.assertEqual(report["authority"]["serial"], "SCUS-97199")
        descriptors = report["layout"]["saveDescriptors"]
        self.assertEqual(
            {
                name: (
                    value["blockId"],
                    value["runtimeAddress"],
                    value["size"],
                )
                for name, value in descriptors.items()
            },
            {
                "ammo": (9, "0x0013d428", 148),
                "items": (10, "0x0013d4c0", 37),
                "unlockFlags": (11, "0x0013d4e8", 37),
                "quickSelect": (13, "0x00141ea0", 32),
                "lastEquippedGadget": (21, "0x0015ed8c", 4),
                "equippedGadgets": (32, "0x00141660", 28),
            },
        )

        ammo_rows = {
            row["itemId"]: (
                row["firstAcquisitionAmmoFloor"],
                row["maxAmmo"],
            )
            for row in report["itemDescriptors"]
            if row["maxAmmo"] != 0
        }
        self.assertEqual(
            ammo_rows,
            {
                10: (10, 40),
                11: (10, 20),
                13: (10, 20),
                15: (100, 200),
                16: (120, 240),
                17: (25, 50),
                19: (120, 240),
                20: (3, 10),
                23: (25, 50),
                24: (3, 10),
                25: (10, 20),
            },
        )

    def test_generated_witness_separates_persistent_gadgets_from_current_item(self):
        report = json.loads(GENERATED.read_text(encoding="utf-8"))
        opening = report["openingSavestate"]["inventory"]
        resume = report["resumeSavestate"]["inventory"]
        saved = report["memoryCard"]

        self.assertEqual(opening["quickSelect"], [10, 0, 0, 0, 0, 0, 0, 0])
        self.assertEqual(opening["equippedGadgets"], [10, 0, 0, 0, 0, 0, 0])
        self.assertEqual(opening["currentItemMirrors"], [8, 8])
        self.assertEqual(opening["ammo"][10], 6)

        self.assertEqual(resume["quickSelect"], [10, 16, 15, 12, 0, 0, 0, 0])
        self.assertEqual(resume["equippedGadgets"], [12, 0, 0, 2, 0, 0, 0])
        self.assertEqual(resume["lastEquippedGadget"], 16)
        self.assertEqual(resume["currentItemMirrors"], [12, 12])
        self.assertEqual(
            [index for index, value in enumerate(resume["items"]) if value],
            [2, 10, 12, 15, 16],
        )
        self.assertEqual(resume["items"], resume["unlockFlags"])

        for field in (
            "ammo",
            "items",
            "unlockFlags",
            "quickSelect",
            "lastEquippedGadget",
            "equippedGadgets",
        ):
            self.assertEqual(saved[field], resume[field])

    def test_code_signature_set_covers_inventory_mutation_and_switch_boundaries(self):
        required = {
            "acquireItemsBase",
            "acquireUnlocksBase",
            "acquireInitialAmmo",
            "acquireQuickSelectStore",
            "consumeCurrentItem",
            "consumeMaxAmmo",
            "addAmmoMax",
            "persistPreviousGadgetA",
            "restoreEquippedGadgetsBase",
        }
        self.assertTrue(required.issubset(PROBE.CODE_SIGNATURES))
        self.assertEqual(PROBE.ITEM_COUNT, 37)
        self.assertEqual(PROBE.ITEM_DESCRIPTOR_STRIDE, 0x18)


if __name__ == "__main__":
    unittest.main()
