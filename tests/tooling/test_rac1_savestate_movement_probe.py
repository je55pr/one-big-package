import importlib.util
import pathlib
import struct
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-savestate-movement-probe.py"
SPEC = importlib.util.spec_from_file_location("rac1_savestate_probe", SCRIPT)
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


class Rac1SavestateMovementProbeTests(unittest.TestCase):
    def make_memory(self):
        memory = bytearray(max(PROBE.ANALOGUE_SIGNATURES) + 4)
        for address, word in PROBE.ANALOGUE_SIGNATURES.items():
            struct.pack_into("<I", memory, address, word)
        struct.pack_into("<f", memory, PROBE.CONTROL_HEADING, -2.073779582977295)
        struct.pack_into("<f", memory, PROBE.HIGH_MAGNITUDE_THRESHOLD, 0.82)
        return memory

    def test_analogue_report_reconciles_code_and_live_heading(self):
        memory = self.make_memory()
        state_sha = next(iter(PROBE.HEADING_WITNESSES))
        report = PROBE.analogue_report(memory, state_sha)

        self.assertEqual(
            report["conditionedAxes"]["formula"],
            "sign(raw-127) * clamp((abs(raw-127)-48)/76, 0, 1)",
        )
        self.assertEqual(report["magnitude"]["activationThresholdRemappedCounts"], 19.0)
        self.assertAlmostEqual(
            report["magnitude"]["highMagnitudeThresholdRemappedCounts"],
            62.32,
            places=4,
        )
        self.assertEqual(
            report["magnitude"]["firstIntegerCardinalAboveHighMagnitudeThreshold"],
            63,
        )
        self.assertIn("facing-controller", report["magnitude"]["boundaryNote"])
        self.assertLess(abs(report["heading"]["loadedMinusLiveHeading"]), 3e-7)
        self.assertEqual(report["heading"]["controlHeadingAddress"], "0x00166dd8")
        self.assertIn("controlHeading - stickAngle", report["heading"]["equivalentLiveConvention"])

    def test_changed_loaded_signature_fails_closed(self):
        memory = self.make_memory()
        address = next(iter(PROBE.ANALOGUE_SIGNATURES))
        struct.pack_into("<I", memory, address, 0)
        with self.assertRaises(RuntimeError):
            PROBE.verify_analogue_signatures(memory)


if __name__ == "__main__":
    unittest.main()
