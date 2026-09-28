import importlib.util
import pathlib
import struct
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-ground-edge-fall.py"
SPEC = importlib.util.spec_from_file_location("rac1_ground_edge_fall", SCRIPT)
EDGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EDGE)


def bits(value):
    return struct.unpack("<I", struct.pack("<f", value))[0]


def row(frame, support, request_z, actual_z=None, action=2):
    return {
        "frame": frame,
        "segment": "forward",
        "sample": {
            "action_state": action,
            "contact_slot_300": support,
            "contact_counters_30c": 0x00010001 if support == 0 else 0,
            "disp_z": request_z if actual_z is None else actual_z,
            "candidate_words": {"0x068": bits(request_z)},
        },
    }


class Rac1GroundEdgeFallTests(unittest.TestCase):
    def test_derive_recovers_ground_request_then_edge_increment(self):
        e = EDGE.EDGE_INCREMENT
        capture = {
            "authority": "synthetic",
            "movieSha256": "movie",
            "stateSha256": "state",
            "samples": [
                row(10, 7, -EDGE.GROUND_REQUEST, 0.0),
                row(11, 0, -EDGE.GROUND_REQUEST),
                row(12, 0, -EDGE.GROUND_REQUEST - e),
                row(13, 0, -EDGE.GROUND_REQUEST - (2 * e)),
                row(14, 9, 0.0, 0.0),
            ],
        }

        report = EDGE.derive(capture)

        self.assertEqual(report["supportLossFrame"], 11)
        self.assertEqual(report["unsupportedFramesBeforeRecontact"], 3)
        self.assertAlmostEqual(
            report["witnesses"][1]["requestDelta"], -25.0 / 3600.0, places=7
        )


    def test_changed_edge_increment_fails_closed(self):
        capture = {
            "samples": [
                row(1, 3, -EDGE.GROUND_REQUEST, 0.0),
                row(2, 0, -EDGE.GROUND_REQUEST),
                row(3, 0, -EDGE.GROUND_REQUEST - 0.01),
                row(4, 0, -EDGE.GROUND_REQUEST - 0.02),
            ],
        }

        with self.assertRaisesRegex(ValueError, "25/3600"):
            EDGE.derive(capture)


if __name__ == "__main__":
    unittest.main()
