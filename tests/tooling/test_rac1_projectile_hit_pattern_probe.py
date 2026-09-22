import importlib.util
import json
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-projectile-hit-pattern-probe.py"
GENERATED = ROOT / "research" / "generated" / "rac1-projectile-hit-patterns.json"

SPEC = importlib.util.spec_from_file_location("rac1_projectile_hit_pattern_probe", SCRIPT)
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


class Rac1ProjectileHitPatternProbeTests(unittest.TestCase):
    def test_registration_and_mine_child_signatures_are_pinned(self):
        self.assertEqual(PROBE.OMNIWRENCH_CLASS, 0x47)
        self.assertEqual(PROBE.OMNIWRENCH_UPDATE, 0x002A84C8)
        self.assertEqual(PROBE.MINE_CONTROLLER_CLASS, 0xBE)
        self.assertEqual(PROBE.MINE_CONTROLLER_UPDATE, 0x002C1AD0)
        self.assertEqual(PROBE.MINE_CONSTRUCTOR, 0x002A9ED0)
        self.assertEqual(PROBE.MINE_PROJECTILE_CLASS, 0x4A)
        self.assertEqual(PROBE.MINE_PROJECTILE_UPDATE, 0x002AA670)

        signatures = {address: expected for address, expected, _ in PROBE.SIGNATURES}
        self.assertEqual(signatures[0x001EA354], 0x00000047)
        self.assertEqual(signatures[0x001EA358], 0x002A84C8)
        self.assertEqual(signatures[0x001EA360], 0x0000004A)
        self.assertEqual(signatures[0x001EA364], 0x002AA670)
        self.assertEqual(signatures[0x001EA450], 0x000000BE)
        self.assertEqual(signatures[0x001EA454], 0x002C1AD0)
        self.assertEqual(signatures[0x002C243C], 0x0C0AA7B4)
        self.assertEqual(signatures[0x002A9EEC], 0x2404004A)
        self.assertEqual(signatures[0x002A9EF8], 0x0C093A0E)

    def test_generated_report_separates_wrench_direct_damage_from_mine(self):
        report = json.loads(GENERATED.read_text(encoding="utf-8"))

        self.assertEqual(report["schema"], 3)
        self.assertEqual(
            report["nativeClassBindings"]["omniWrench"],
            {"nativeClass": "0x0047", "registeredUpdate": "0x002a84c8"},
        )
        mine = report["nativeClassBindings"]["mineGlove"]
        self.assertEqual(mine["weaponNativeClass"], "0x00be")
        self.assertEqual(mine["weaponUpdate"], "0x002c1ad0")
        self.assertEqual(mine["projectileNativeClass"], "0x004a")
        self.assertEqual(mine["projectileConstructor"], "0x002a9ed0")
        self.assertEqual(mine["projectileUpdate"], "0x002aa670")

        direct = report["damageHandoffs"]["omniWrenchDirectRepresentative"]
        self.assertEqual(direct["nativeClass"], "0x0047")
        self.assertEqual(direct["nativeDamage"], 1.0)
        self.assertEqual(direct["nativeDamageFlags"], "0x00010000")
        self.assertNotIn("separateDirectRepresentative", report["damageHandoffs"])
        env = report["environmentCollisionPattern"]
        self.assertIn("0x002a8be8", env["omniWrenchUpdateCallers"])
        self.assertEqual(env["mineProjectileUpdateCallers"], ["0x002aab40"])

        meanings = {
            row["address"]: row["meaning"]
            for row in report["staticEvidence"]
        }
        self.assertIn("OmniWrench", meanings["0x002a8c84"])
        self.assertIn("Mine controller", meanings["0x002c243c"])


if __name__ == "__main__":
    unittest.main()
