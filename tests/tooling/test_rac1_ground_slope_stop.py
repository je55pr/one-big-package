import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-ground-slope-stop.py"
SPEC = importlib.util.spec_from_file_location("rac1_ground_slope_stop", SCRIPT)
SLOPE_STOP = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SLOPE_STOP)


def fixture_capture():
    rows = []
    for local_frame in range(16):
        moving = local_frame < 4
        rows.append(
            {
                "frame": 100 + local_frame,
                "segment": "release",
                "local_frame": local_frame,
                "sample": {
                    "disp_x": 0.01 if moving else 0.0,
                    "disp_y": -0.02 if moving else 0.0,
                    "disp_z": 0.003 if moving else 0.0,
                    "pos_x": 10.0 if not moving else 9.0 + local_frame * 0.25,
                    "pos_y": 20.0 if not moving else 19.0 + local_frame * 0.25,
                    "pos_z": 30.0 if not moving else 29.0 + local_frame * 0.25,
                    "sequence": 5 if local_frame < 10 else 0,
                    "contact_slot_300": 200 + local_frame,
                    "contact_counters_30c": 0,
                },
            }
        )
    return {
        "authority": "fixture",
        "movieSha256": "movie",
        "stateSha256": "state",
        "samples": rows,
    }


class Rac1GroundSlopeStopTests(unittest.TestCase):
    def test_reduces_exact_supported_stationary_tail(self):
        report = SLOPE_STOP.derive(fixture_capture())

        self.assertEqual(report["firstPlanarZeroLocalFrame"], 4)
        self.assertEqual(report["firstFullyStationaryLocalFrame"], 4)
        self.assertEqual(report["stationaryFrames"], 12)
        self.assertEqual(
            report["stationaryPosition"],
            {"nativeX": 10.0, "nativeY": 20.0, "nativeZ": 30.0},
        )
        self.assertEqual(report["unsupportedCounter30cEnd"], 0)
        self.assertEqual(report["unsupportedCounter30eEnd"], 0)

    def test_fails_closed_if_stationary_tail_loses_support(self):
        capture = fixture_capture()
        capture["samples"][-1]["sample"]["contact_counters_30c"] = 1

        with self.assertRaisesRegex(RuntimeError, "lost ordinary support"):
            SLOPE_STOP.derive(capture)

    def test_fails_closed_if_motion_resumes(self):
        capture = fixture_capture()
        capture["samples"][-1]["sample"]["disp_z"] = 0.001

        with self.assertRaisesRegex(RuntimeError, "stationary tail resumes motion"):
            SLOPE_STOP.derive(capture)


if __name__ == "__main__":
    unittest.main()
