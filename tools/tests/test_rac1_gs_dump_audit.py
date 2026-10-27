"""Synthetic portable tests for rac1-gs-dump-audit; no retail files."""
import importlib.util
from pathlib import Path
import struct
import sys
import unittest

spec = importlib.util.spec_from_file_location("gs_audit", Path(__file__).resolve().parents[1] / "rac1-gs-dump-audit.py")
assert spec and spec.loader
audit = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = audit
spec.loader.exec_module(audit)


def tag(loops, reg):
    return struct.pack("<QQ", loops | (1 << 15) | (1 << 60), reg)


def ad(address, value):
    return struct.pack("<QQ", value, address)


def dump(*chunks):
    serial = b"TEST_GS"
    header = struct.pack("<9I", 1, 4, 36, len(serial), 0x12345678, 0, 0, 36 + len(serial), 0)
    result = struct.pack("<II", 0xFFFFFFFF, 36 + len(serial)) + header + serial
    result += b"\0" * (4 + 0x2000)
    for chunk in chunks:
        result += b"\x00" + struct.pack("<BI", 1, len(chunk)) + chunk
    return result + b"\x01\x00"


class GsDumpAuditTests(unittest.TestCase):
    def test_known_retail_boot_depth_alpha_decode(self):
        state = audit.depth_state(0x5360B, 0x010000D8)
        self.assertTrue(state["depth_test_enabled"])
        self.assertEqual("gequal", state["depth_function"])
        self.assertTrue(state["depth_write_enabled"])
        self.assertEqual(1, state["depth_format"])
        self.assertTrue(state["alpha_test_enabled"])
        self.assertEqual("gequal", state["alpha_function"])
        self.assertEqual(3, state["alpha_fail"])

    def test_state_and_vertices_across_split_gif_transfers(self):
        state = tag(3, 0xE) + ad(0x47, 0x5360B) + ad(0x4E, 0x010000D8) + ad(0x06, 0x12345)
        vertices = tag(3, 0x5) + b"\0" * 48
        payload = state + vertices
        parsed = audit.GsAudit().analyse(dump(payload[:13], payload[13:42], payload[42:95], payload[95:]))
        self.assertEqual(3, parsed["draw_batches"][0]["vertex_writes"])
        self.assertEqual("gequal", parsed["draw_batches"][0]["gs_state"]["depth_function"])
        self.assertEqual(0x12345 & 0x3FFF, parsed["draw_batches"][0]["texture_base"])
        self.assertEqual(3, len(parsed["register_changes"]))
        self.assertEqual(1, parsed["dump"]["frame_boundaries"])

    def test_unknown_state_stays_unknown_without_packets(self):
        parsed = audit.GsAudit().analyse(dump(tag(2, 0x5) + b"\0" * 32))
        self.assertFalse(parsed["draw_batches"][0]["gs_state"]["known"])

    def test_gif_image_data_does_not_fake_register_writes(self):
        image = struct.pack("<QQ", 2 | (2 << 58), 0) + b"\xff" * 32
        state = tag(1, 0xE) + ad(0x47, 0x5360B)
        vertices = tag(1, 0x5) + b"\0" * 16
        parsed = audit.GsAudit().analyse(dump(image + state + vertices))
        self.assertEqual("gequal", parsed["draw_batches"][0]["gs_state"]["depth_function"])

    def test_invalid_dump_and_truncation_fail(self):
        with self.assertRaises(ValueError):
            audit.GsAudit().analyse(b"junk")
        with self.assertRaises(ValueError):
            audit.GsAudit().analyse(dump(tag(1, 0x5) + b"\0" * 16)[:-1])


if __name__ == "__main__":
    unittest.main()
