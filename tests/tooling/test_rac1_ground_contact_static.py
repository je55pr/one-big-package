import importlib.util
import pathlib
import struct
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-ground-contact-static.py"
SPEC = importlib.util.spec_from_file_location("rac1_ground_contact_static", SCRIPT)
STATIC = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(STATIC)


def fixture_memory():
    size = max(max(STATIC.SIGNATURES), STATIC.TICK_SCALAR_ADDRESS) + 4
    memory = bytearray(size)
    for address, word in STATIC.SIGNATURES.items():
        struct.pack_into("<I", memory, address, word)
    struct.pack_into("<f", memory, STATIC.TICK_SCALAR_ADDRESS, 1.0 / 3600.0)
    return memory


class Rac1GroundContactStaticTests(unittest.TestCase):
    def test_static_path_derives_exact_ordinary_ground_downward_request(self):
        report = STATIC.derive_static(fixture_memory())

        self.assertEqual(report["signaturesVerified"], len(STATIC.SIGNATURES))
        self.assertAlmostEqual(report["tickScalar"], 1.0 / 3600.0, places=10)
        self.assertAlmostEqual(
            report["groundDownwardRequestPerTick"], 0.015, places=9
        )
        self.assertIn("request.z = request.z -", report["equation"])

    def test_changed_loaded_signature_fails_closed(self):
        memory = fixture_memory()
        struct.pack_into("<I", memory, 0x0023352C, 0)

        with self.assertRaisesRegex(RuntimeError, "signatures changed"):
            STATIC.derive_static(memory)


if __name__ == "__main__":
    unittest.main()
