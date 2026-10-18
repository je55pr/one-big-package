import importlib.util
import io
from pathlib import Path
import struct
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "rac1_level_overlay_probe", ROOT / "tools/rac1-level-overlay-probe.py")
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


def section(address, data, entry=0x1000, kind=1):
    return struct.pack("<4I", address, len(data), kind, entry) + data


class Rac1LevelOverlayProbeTests(unittest.TestCase):
    def test_maps_separate_sections_by_their_addresses(self):
        blob = section(0x1000, struct.pack("<2I", 0x12345678, 9))
        blob += section(0x2000, struct.pack("<I", 42), kind=8)
        overlay = PROBE.Overlay(blob)
        self.assertEqual(overlay.entry_point, 0x1000)
        self.assertEqual(overlay.word(0x1000), 0x12345678)
        self.assertEqual(overlay.word(0x2000), 42)
        self.assertEqual(PROBE.verify_signatures(overlay, {0x2000: 42}), 1)

    def test_unmapped_runtime_state_is_not_fabricated(self):
        overlay = PROBE.Overlay(section(0x1000, bytes(8)))
        for address, size in ((0x1008, 4), (0x1004, 8), (0, 4), (-1, 4), (0x1000, 0)):
            with self.subTest(address=address, size=size), self.assertRaises(ValueError):
                overlay.read(address, size)

    def test_rejects_empty_truncated_or_oversized_payloads(self):
        good = section(0x1000, bytes(8))
        for blob in (b"", good[:12], good[:-1], good + b"x",
                     struct.pack("<4I", 0x1000, 0xFFFFFFFF, 1, 0x1000)):
            with self.subTest(size=len(blob)), self.assertRaises(ValueError):
                PROBE.Overlay(blob)

    def test_rejects_overlaps_invalid_addresses_and_entry_points(self):
        good = section(0x1000, bytes(8))
        bad = (
            good + section(0x1004, bytes(8)),
            good + section(0x2000, bytes(4), entry=0x2000),
            section(PROBE.EE_SIZE - 4, bytes(8)),
            section(0x1001, bytes(4)),
            section(0x1000, bytes(5)),
            section(0x1000, bytes(4), kind=2),
            section(0x2000, bytes(4)),
        )
        for blob in bad:
            with self.subTest(blob=blob[:16]), self.assertRaises(ValueError):
                PROBE.Overlay(blob)

    def test_signature_drift_fails_closed(self):
        overlay = PROBE.Overlay(section(0x1000, struct.pack("<I", 7)))
        with self.assertRaisesRegex(ValueError, "signature differs"):
            PROBE.verify_signatures(overlay, {0x1000: 8})

    def test_float_immediates_handle_both_literal_shapes(self):
        for words, expected in (
            ([0x3C01431D, 0x44810800], 157.0),
            ([0x3C013F19, 0x3421999A, 0x44811000],
             struct.unpack("<f", struct.pack("<I", 0x3F19999A))[0]),
        ):
            overlay = PROBE.Overlay(section(0x1000, struct.pack(f"<{len(words)}I", *words)))
            self.assertEqual(PROBE.immediate_float(overlay, 0x1000), expected)

    def test_float_decoder_rejects_wrong_registers_or_instruction_shapes(self):
        for words in (
            [0x3C02431D, 0x44810800],  # lui v0, not at
            [0x3C01431D, 0x44820800],  # mtc1 v0, not at
            [0x3C013F19, 0x3442999A, 0x44811000],  # ori v0, not at
            [0x3C01431D, 0],
            [0x3C01431D],
        ):
            overlay = PROBE.Overlay(section(0x1000, struct.pack(f"<{len(words)}I", *words)))
            with self.subTest(words=words), self.assertRaises(ValueError):
                PROBE.immediate_float(overlay, 0x1000)

    def test_disc_read_rejects_bounds_and_short_reads(self):
        stream = io.BytesIO(bytes(4))
        for offset, size, limit in ((-1, 1, 4), (0, 5, 4), (3, 2, 4), (0, 5, 8)):
            with self.subTest(offset=offset, size=size), self.assertRaises(ValueError):
                PROBE.read_at(stream, offset, size, limit)

    def make_disc(self):
        # Small synthetic disc directory with a tiny two-word overlay.
        disc = bytearray(1530 * PROBE.SECTOR_SIZE)
        index = 1500 * PROBE.SECTOR_SIZE
        header = 1520 * PROBE.SECTOR_SIZE
        data = 1522 * PROBE.SECTOR_SIZE
        blob = section(0x1000, bytes(8))
        struct.pack_into("<2I", disc, index, 1, 0x2960)
        struct.pack_into("<I", disc, index + 0x28C8, 1520)
        struct.pack_into("<4I", disc, header, 0, 0x2434, 1522, 1)
        struct.pack_into("<2i", disc, data, 0x80, len(blob))
        disc[data + 0x80:data + 0x80 + len(blob)] = blob
        return disc, blob, data

    def test_resolves_overlay_through_both_bounded_directories(self):
        disc, blob, _ = self.make_disc()
        self.assertEqual(PROBE.read_veldin_overlay(io.BytesIO(disc), len(disc)), blob)

    def test_resolves_native_level_identity_in_a_different_table_slot(self):
        disc, blob, _ = self.make_disc()
        index = 1500 * PROBE.SECTOR_SIZE
        struct.pack_into("<I", disc, index + 0x28C8, 0)
        struct.pack_into("<I", disc, index + 0x28C8 + 4 * 8, 1520)
        struct.pack_into("<I", disc, 1520 * PROBE.SECTOR_SIZE, 12)
        self.assertEqual(PROBE.read_level_overlay(io.BytesIO(disc), len(disc), 12), blob)

    def test_missing_or_duplicate_native_level_identity_is_rejected(self):
        disc, _, _ = self.make_disc()
        with self.assertRaisesRegex(ValueError, "exactly one level 12"):
            PROBE.read_level_overlay(io.BytesIO(disc), len(disc), 12)
        struct.pack_into("<I", disc, 1500 * PROBE.SECTOR_SIZE + 0x28C8 + 8, 1520)
        with self.assertRaisesRegex(ValueError, "exactly one level 0"):
            PROBE.read_veldin_overlay(io.BytesIO(disc), len(disc))

    def test_present_table_entry_without_a_header_cannot_be_ignored(self):
        disc, _, _ = self.make_disc()
        struct.pack_into("<2I", disc, 1500 * PROBE.SECTOR_SIZE + 0x28C8 + 8, 0, 1)
        with self.assertRaisesRegex(ValueError, "zero header LBA"):
            PROBE.read_veldin_overlay(io.BytesIO(disc), len(disc))

    def test_other_level_headers_are_bounded_before_identity_selection(self):
        disc, _, _ = self.make_disc()
        struct.pack_into("<I", disc, 1500 * PROBE.SECTOR_SIZE + 0x28C8 + 8, 1530)
        with self.assertRaisesRegex(ValueError, "bounded source"):
            PROBE.read_veldin_overlay(io.BytesIO(disc), len(disc))

    def test_inner_range_cannot_escape_level_data_into_adjacent_disc_bytes(self):
        disc, _, data = self.make_disc()
        struct.pack_into("<2i", disc, data, 0x800 - 4, 8)
        with self.assertRaisesRegex(ValueError, "level overlay"):
            PROBE.read_veldin_overlay(io.BytesIO(disc), len(disc))

    def test_directory_revision_and_level_identity_are_checked(self):
        for offset in (1500 * PROBE.SECTOR_SIZE, 1520 * PROBE.SECTOR_SIZE):
            disc, _, _ = self.make_disc()
            struct.pack_into("<I", disc, offset, 99)
            with self.subTest(offset=offset), self.assertRaises(ValueError):
                PROBE.read_veldin_overlay(io.BytesIO(disc), len(disc))


if __name__ == "__main__":
    unittest.main()
