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


def tag(loops, reg, mode=0, pre=False, prim=0):
    value = loops | (1 << 15) | (int(pre) << 46) | (prim << 47) | (mode << 58) | (1 << 60)
    return struct.pack("<QQ", value, reg)


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

    def test_unknown_bucket_counts_unobserved_vertices(self):
        parsed = audit.GsAudit().analyse(dump(tag(2, 0x5) + b"\0" * 32))
        self.assertEqual(2, parsed["depth_state_counts_by_vertex_writes"]["unknown"])
        self.assertEqual(2, sum(parsed["depth_state_counts_by_vertex_writes"].values()))

    def test_ad_packed_and_reglist_vertex_sources(self):
        ad_vertices = tag(2, 0xE) + ad(0x05, 0) + ad(0x0C, 0)
        packed_vertices = tag(1, 0x5) + b"\0" * 16
        reglist_vertices = tag(2, 0x5, mode=1) + b"\0" * 16
        parsed = audit.GsAudit().analyse(dump(ad_vertices + packed_vertices + reglist_vertices))
        self.assertEqual(5, parsed["draw_batches"][0]["vertex_writes"])
        self.assertEqual({"ad": 2, "packed": 1, "reglist": 2}, parsed["draw_batches"][0]["vertex_sources"])

    def test_reglist_prim_and_tex0_select_context_two(self):
        state = tag(2, 0xE) + ad(0x48, 0x5360B) + ad(0x4F, 0x010000D8)
        prim = tag(1, 0x0, mode=1) + struct.pack("<Q", 1 << 9) + b"\0" * 8
        tex0 = tag(1, 0x7, mode=1) + struct.pack("<Q", 0x4BEEF) + b"\0" * 8
        vertices = tag(1, 0x5) + b"\0" * 16
        parsed = audit.GsAudit().analyse(dump(state + prim + tex0 + vertices))
        batch = parsed["draw_batches"][0]
        self.assertEqual(1, batch["context"])
        self.assertEqual("gequal", batch["gs_state"]["depth_function"])
        self.assertEqual(0x4BEEF & 0x3FFF, batch["texture_base"])

    def test_packed_tex0_register_is_observed(self):
        tex0 = tag(1, 0x6) + struct.pack("<QQ", 0x812345, 0)
        vertices = tag(1, 0x5) + b"\0" * 16
        parsed = audit.GsAudit().analyse(dump(tex0 + vertices))
        self.assertEqual(0x812345 & 0x3FFF, parsed["draw_batches"][0]["texture_base"])

    def test_zero_loop_pre_does_not_change_primitive_context(self):
        state = tag(1, 0xE) + ad(0x47, 0x5360B)
        ignored_pre = tag(0, 0x5, pre=True, prim=(1 << 9))
        vertices = tag(1, 0x5) + b"\0" * 16
        parsed = audit.GsAudit().analyse(dump(state + ignored_pre + vertices))
        batch = parsed["draw_batches"][0]
        self.assertEqual(0, batch["context"])
        self.assertEqual("gequal", batch["gs_state"]["depth_function"])

    def test_incomplete_gif_paths_are_reported(self):
        tail = audit.GsAudit().analyse(dump(b"\0" * 8))
        pending_vertex = audit.GsAudit().analyse(dump(tag(2, 0x5) + b"\0" * 16))
        pending_image = audit.GsAudit().analyse(dump(
            struct.pack("<QQ", 2 | (2 << 58), 0) + b"\xff" * 16))
        for parsed in (tail, pending_vertex, pending_image):
            self.assertEqual([1], parsed["incomplete_paths"])

    def test_xz_combined_stream_limit(self):
        import lzma
        import tempfile
        from unittest.mock import patch

        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "sample.gs.xz"
            path.write_bytes(lzma.compress(b"a" * 20) + lzma.compress(b"b" * 20))
            with patch.object(audit, "MAX_DECOMPRESSED_BYTES", 64):
                self.assertEqual(b"a" * 20 + b"b" * 20, audit.decompress(path))
            path.write_bytes(lzma.compress(b"a" * 40) + lzma.compress(b"b" * 40))
            with patch.object(audit, "MAX_DECOMPRESSED_BYTES", 64):
                with self.assertRaises(ValueError):
                    audit.decompress(path)

    def test_invalid_dump_and_truncation_fail(self):
        with self.assertRaises(ValueError):
            audit.GsAudit().analyse(b"junk")
        with self.assertRaises(ValueError):
            audit.GsAudit().analyse(dump(tag(1, 0x5) + b"\0" * 16)[:-1])


if __name__ == "__main__":
    unittest.main()
