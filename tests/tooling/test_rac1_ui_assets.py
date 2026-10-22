"""Pure, retail-payload-free tests for the R&C1 UI asset exporter."""
import importlib.util
import struct
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).resolve().parents[2] / "tools" / "rac1-ui-assets.py"
spec = importlib.util.spec_from_file_location("rac1_ui_assets", MODULE_PATH)
assets = importlib.util.module_from_spec(spec)
spec.loader.exec_module(assets)


class Rac1UiAssetsTests(unittest.TestCase):
    def test_wad_literal(self):
        payload = b"ABCDEF"
        stream = b"WAD" + struct.pack("<I", 16 + 1 + len(payload)) + bytes(9) + bytes((3,)) + payload
        self.assertEqual(assets.decode_wad(stream), payload)
        with self.assertRaises(ValueError):
            assets.decode_wad(stream[:-1])

    def test_png_header_and_rgba(self):
        data = assets.png_rgba(1, 1, bytes((9, 4, 2, 255)))
        self.assertTrue(data.startswith(b"\x89PNG\r\n\x1a\n"))
        self.assertIn(b"IHDR", data)
        with self.assertRaises(ValueError):
            assets.png_rgba(1, 1, bytes(3))

    def test_bounds(self):
        with self.assertRaises(ValueError):
            assets.at(bytes(3), 2, 2, "test")
        with self.assertRaises(ValueError):
            assets.u32(bytes(3), 0)

    def test_synthetic_animation_bank(self):
        data = bytearray(0x100)
        for at, val in zip((4, 8, 12, 16), (0xB4, 0xC4, 0xCC, 0xD4)):
            struct.pack_into("<I", data, at, val)
        struct.pack_into("<I", data, 0x14, 1)
        struct.pack_into("<I", data, 0x34, 1)
        struct.pack_into("<3H2B", data, 0xB4, 0x7544, 1, 0, 0, 0)
        struct.pack_into("<H", data, 0xBC, 0xFFFF)
        struct.pack_into("<IHBB", data, 0xCC, 0x80000000, 0, 0, 0)
        struct.pack_into("<IHBB", data, 0xD4, 0x80000000, 0, 5, 5)
        result = assets.parse_bank(data)
        self.assertEqual(result["animations"][0]["id"], 0x7544)
        self.assertEqual(result["images"][0]["width"], 32)
        self.assertEqual(len(result["palettes"]), 1)
        data[0xC6] = 9  # page index for first frame
        with self.assertRaises(ValueError):
            assets.parse_bank(data)


if __name__ == "__main__":
    unittest.main()
