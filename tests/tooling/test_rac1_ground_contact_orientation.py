import importlib.util
import pathlib
import struct
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-ground-contact-orientation.py"
SPEC = importlib.util.spec_from_file_location("rac1_ground_contact_orientation", SCRIPT)
ORIENTATION = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ORIENTATION)


def fixture_memory():
    size = max(ORIENTATION.SIGNATURES) + 4
    memory = bytearray(size)
    for address, word in ORIENTATION.SIGNATURES.items():
        struct.pack_into("<I", memory, address, word)
    return memory


class Rac1GroundContactOrientationTests(unittest.TestCase):
    def test_static_path_pins_contact_orientation_contract(self):
        report = ORIENTATION.derive_static(fixture_memory())

        self.assertEqual(report["signaturesVerified"], len(ORIENTATION.SIGNATURES))
        self.assertEqual(
            report["contactOrientationVector"]["initialValue"],
            [0.0, 0.0, 1.0],
        )
        self.assertEqual(report["contactMetricGate"]["comparisonValue"], 0.25)

        self.assertAlmostEqual(
            report["orientationAdmissionBoundRadians"],
            3.141592653589793 / 4.0,
            places=12,
        )
        self.assertIn(
            "request dot-product",
            report["interpretation"],
        )
        self.assertIn("does not claim", report["boundaryNote"])

    def test_changed_loaded_signature_fails_closed(self):
        memory = fixture_memory()
        struct.pack_into("<I", memory, 0x00213018, 0)

        with self.assertRaisesRegex(RuntimeError, "signatures changed"):
            ORIENTATION.derive_static(memory)


if __name__ == "__main__":
    unittest.main()
