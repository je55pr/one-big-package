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


def row(frame, final, pre, sequence=4, planar=None, vertical=None):
    planar = pre if planar is None else planar
    vertical = (0.0, 0.0, 0.0) if vertical is None else vertical
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
                "0x0a0": bits(vertical[0]),
                "0x0a4": bits(vertical[1]),
                "0x0a8": bits(vertical[2]),
                "0x0b0": bits(planar[0]),
                "0x0b4": bits(planar[1]),
                "0x0b8": bits(planar[2]),
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
                row(
                    10,
                    (0.09500919, 0.0, 0.0),
                    (0.09500919, 0.0, -0.015),
                    planar=(0.09500919, 0.0, 0.0),
                    vertical=(0.0, 0.0, 0.0),
                ),
                row(
                    11,
                    (0.089498079, 0.0, 0.03188324),
                    (0.089498079, 0.0, 0.01688324),
                    planar=(0.089498079, 0.0, 0.0),
                    vertical=(0.0, 0.0, 0.01688324),
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
        decomposition = report["componentDecomposition"]
        self.assertLess(decomposition["uphillMaxReconstructionError"], 1e-7)
        self.assertAlmostEqual(decomposition["flatResidualZMedian"], -0.015, places=6)
        self.assertLess(decomposition["flatMaxResidualPlanarMagnitude"], 1e-7)
        self.assertLess(decomposition["maxPlanarComponentAbsZ"], 1e-7)
        self.assertLess(decomposition["maxVerticalComponentPlanarMagnitude"], 1e-7)
        self.assertLess(decomposition["maxFinalVsPlanarHorizontalError"], 1e-7)
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
