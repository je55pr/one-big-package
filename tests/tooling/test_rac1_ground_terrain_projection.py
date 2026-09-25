import importlib.util
import pathlib
import struct
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-ground-terrain-projection.py"
SPEC = importlib.util.spec_from_file_location("rac1_ground_terrain_projection", SCRIPT)
PROJECTION = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROJECTION)


def fixture_memory():
    size = max(PROJECTION.SIGNATURES) + 4
    memory = bytearray(size)
    for address, word in PROJECTION.SIGNATURES.items():
        struct.pack_into("<I", memory, address, word)
    return memory


class Rac1GroundTerrainProjectionTests(unittest.TestCase):
    def test_static_clip_pins_request_and_contact_vector_math(self):
        report = PROJECTION.derive_static(fixture_memory())

        self.assertEqual(report["signaturesVerified"], len(PROJECTION.SIGNATURES))
        self.assertEqual(report["requestVector"], "P+0xe0")
        self.assertEqual(report["acceptedContactVector"], "0x00173e80")
        self.assertIn("request += s * contact", report["ordinaryClip"])
        self.assertIn("normalize", report["directionalSlopeLaw"])

    def test_changed_clip_signature_fails_closed(self):
        memory = fixture_memory()
        struct.pack_into("<I", memory, 0x00212664, 0)

        with self.assertRaisesRegex(RuntimeError, "signatures changed"):
            PROJECTION.derive_static(memory)


if __name__ == "__main__":
    unittest.main()
