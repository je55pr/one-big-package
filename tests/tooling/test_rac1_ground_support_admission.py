import importlib.util
import pathlib
import struct
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-ground-support-admission.py"
SPEC = importlib.util.spec_from_file_location("rac1_ground_support_admission", SCRIPT)
ADMISSION = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ADMISSION)


def fixture_memory():
    size = max(ADMISSION.SIGNATURES) + 4
    memory = bytearray(size)
    for address, word in ADMISSION.SIGNATURES.items():
        struct.pack_into("<I", memory, address, word)
    return memory


class Rac1GroundSupportAdmissionTests(unittest.TestCase):
    def test_static_branch_pins_fifty_degree_ordinary_support_limit(self):
        report = ADMISSION.derive_static(fixture_memory())

        self.assertEqual(report["signaturesVerified"], len(ADMISSION.SIGNATURES))
        self.assertAlmostEqual(report["contactMetricLimit"], 0.02, places=7)
        self.assertEqual(report["contactMetricInitialization"], 42.0)
        self.assertEqual(
            report["contactMetricProducer"],
            "0x001ff450(G, P+0x2a0) -> P+0x2dc",
        )
        self.assertAlmostEqual(
            report["ordinarySupportMaxAngleDegrees"], 50.0, places=5
        )
        self.assertIn("P+0x30e", report["ordinaryAdmission"])
        self.assertIn("P+0x20b3 == 1", report["specialOverrides"])

    def test_changed_admission_signature_fails_closed(self):
        memory = fixture_memory()
        struct.pack_into("<I", memory, 0x00212BBC, 0)

        with self.assertRaisesRegex(RuntimeError, "signatures changed"):
            ADMISSION.derive_static(memory)


if __name__ == "__main__":
    unittest.main()
