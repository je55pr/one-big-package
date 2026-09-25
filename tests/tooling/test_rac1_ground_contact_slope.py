import importlib.util
import pathlib
import struct
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-ground-contact-slope.py"
SPEC = importlib.util.spec_from_file_location("rac1_ground_contact_slope", SCRIPT)
SLOPE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SLOPE)


def bits(value):
    return struct.unpack("<I", struct.pack("<f", value))[0]


def row(frame, final, pre, sequence=4):
    return {
        "frame": frame,
        "segment": "forward",
        "sample": {
            "sequence": sequence,
            "disp_x": final[0],
            "disp_y": final[1],
            "disp_z": final[2],
            "candidate_words": {
                "0x060": bits(pre[0]),
                "0x064": bits(pre[1]),
                "0x068": bits(pre[2]),
            },
        },
    }


class Rac1GroundContactSlopeTests(unittest.TestCase):
    def test_derive_keeps_flat_and_uphill_contact_witnesses(self):
        capture = {
            "authority": "synthetic",
            "movieSha256": "movie",
            "stateSha256": "state",
            "samples": [
                row(10, (0.09500919, 0.0, 0.0), (0.09500919, 0.0, -0.015)),
                row(
                    11,
                    (0.089498079, 0.0, 0.03188324),
                    (0.089498079, 0.0, 0.01688324),
                ),
            ],
        }

        report = SLOPE.derive(capture)

        self.assertEqual(report["stableWitnessCount"], 2)
        self.assertEqual(report["flatWitnessCount"], 1)
        self.assertEqual(report["uphillWitnessCount"], 1)
        self.assertAlmostEqual(
            report["verticalContactCorrection"]["median"],
            0.015,
            places=6,
        )
        self.assertIn("statically pinned", report["lawStatus"])


    def test_non_run_cap_or_wrong_contact_correction_is_rejected(self):
        capture = {
            "samples": [
                row(1, (0.08, 0.0, 0.0), (0.08, 0.0, -0.015)),
                row(2, (0.09500919, 0.0, 0.0), (0.09500919, 0.0, -0.01)),
            ],
        }

        with self.assertRaisesRegex(ValueError, "no stable"):
            SLOPE.derive(capture)


if __name__ == "__main__":
    unittest.main()
