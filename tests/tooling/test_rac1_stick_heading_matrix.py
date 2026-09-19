import importlib.util
import json
import math
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-stick-heading-matrix.py"
SPEC = importlib.util.spec_from_file_location("rac1_stick_heading_matrix", SCRIPT)
MATRIX = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MATRIX)


class Rac1StickHeadingMatrixTests(unittest.TestCase):
    def test_reducer_recovers_signed_angle_and_3d_basis_rules(self):
        MATRIX.HARNESS.CAPTURES.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=MATRIX.HARNESS.CAPTURES) as directory:
            root = pathlib.Path(directory)
            forward = (math.sqrt(0.99), 0.0, -0.1)
            right = (0.0, -1.0, 0.0)

            for label, left, stick_angle, (fsign, rsign) in MATRIX.DIRECTIONS:
                raw_vector = tuple(
                    fsign * forward[index] + rsign * right[index]
                    for index in range(3)
                )
                vector = MATRIX.normalize(raw_vector)
                capture = {
                    "stateSha256": "state",
                    "movieSha256": f"movie-{label}",
                    "samples": [{
                        "frame": 1,
                        "local_frame": 1,
                        "segment": "direction",
                        "left": list(left),
                        "right": [127, 127],
                        "sample": {
                            "target_yaw": MATRIX.wrap_pi(-stick_angle),
                            "control_dir_x": vector[0],
                            "control_dir_y": vector[1],
                            "control_dir_z": vector[2],
                        },
                    }],
                }
                (root / f"{label}.raw.json").write_text(
                    json.dumps(capture), encoding="utf-8",
                )

            report = MATRIX.derive_matrix(root)
            self.assertEqual(report["stateSha256"], "state")
            self.assertAlmostEqual(report["controlHeadingRad"], 0.0, places=12)
            self.assertLess(report["maxTargetAngleErrorRad"], 1e-12)
            self.assertLess(report["maxControlVectorCombinationError"], 1e-12)
            self.assertGreater(
                report["maxTargetMinusProjectedVectorYawAbsRad"], 0.001,
            )
            by_name = {row["label"]: row for row in report["directions"]}
            self.assertAlmostEqual(
                by_name["right"]["targetDeltaFromForwardRad"],
                -math.pi / 2,
                places=12,
            )
            self.assertAlmostEqual(
                by_name["forward-left"]["targetDeltaFromForwardRad"],
                math.pi / 4,
                places=12,
            )

    def test_fixed_forward_reducer_tracks_control_basis_change(self):
        MATRIX.HARNESS.CAPTURES.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=MATRIX.HARNESS.CAPTURES) as directory:
            path = pathlib.Path(directory) / "fixed.raw.json"
            capture = {
                "stateSha256": "state",
                "movieSha256": "movie",
                "samples": [
                    {
                        "segment": "forward-a",
                        "local_frame": 1,
                        "left": [127, 0],
                        "right": [127, 127],
                        "sample": {
                            "target_yaw": 0.0,
                            "control_dir_x": 1.0,
                            "control_dir_y": 0.0,
                            "control_dir_z": -0.1,
                        },
                    },
                    {
                        "segment": "forward-b",
                        "local_frame": 1,
                        "left": [127, 0],
                        "right": [255, 127],
                        "sample": {
                            "target_yaw": math.pi / 4,
                            "control_dir_x": math.sqrt(0.5),
                            "control_dir_y": math.sqrt(0.5),
                            "control_dir_z": -0.1,
                        },
                    },
                ],
            }
            path.write_text(json.dumps(capture), encoding="utf-8")

            report = MATRIX.derive_fixed_stick(path)
            self.assertEqual(report["samples"], 2)
            self.assertAlmostEqual(report["targetDeltaRad"], math.pi / 4, places=12)
            self.assertLess(
                report["maxTargetMinusProjectedControlVectorYawAbsRad"],
                1e-12,
            )
            self.assertEqual(report["segments"][1]["right"], [255, 127])


if __name__ == "__main__":
    unittest.main()
