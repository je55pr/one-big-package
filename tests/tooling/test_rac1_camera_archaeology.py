import importlib.util
import json
import pathlib
import struct
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-camera-archaeology.py"
SPEC = importlib.util.spec_from_file_location("rac1_camera_archaeology", SCRIPT)
CAMERA = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CAMERA)


def f32_word(value):
    return struct.unpack("<I", struct.pack("<f", value))[0]


class FakePine:
    def __init__(self, values):
        self.values = values
        self.addresses = None

    def read32(self, addresses):
        self.addresses = list(addresses)
        return [self.values.get(address, 0) for address in addresses]


class Rac1CameraArchaeologyTests(unittest.TestCase):
    def test_scenario_matrix_covers_required_camera_cases(self):
        specs = CAMERA.scenario_specs()
        self.assertEqual(
            [spec["id"] for spec in specs],
            ["fixed-heading", "moving", "turning", "idle", "recenter", "obstruction"],
        )
        recenter = next(spec for spec in specs if spec["id"] == "recenter")
        self.assertEqual(
            recenter["segments"][1]["right"],
            [0, 127],
        )
        obstruction = next(spec for spec in specs if spec["id"] == "obstruction")
        self.assertTrue(obstruction["requiresObstructionState"])

    def test_field_map_adds_semantic_camera_fields_without_overriding_known_fields(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "fields.json"
            path.write_text(json.dumps({
                "schema": 1,
                "fields": {
                    "camera_position_x": {"address": "0x00167000", "kind": "f32"},
                    "camera_forward_x": {"address": "0x00167010", "kind": "f32"},
                },
                "candidateRanges": [
                    {"start": "0x00167100", "bytes": 16},
                ],
            }), encoding="utf-8")
            fields, ranges = CAMERA.load_field_map(path)
            self.assertEqual(fields["camera_position_x"], (0x00167000, "f32"))
            self.assertEqual(ranges, [(0x00167100, 16)])

            path.write_text(json.dumps({
                "schema": 1,
                "fields": {
                    "control_heading": {"address": "0x00167000", "kind": "f32"},
                },
            }), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "cannot override known field"):
                CAMERA.load_field_map(path)
    def test_sample_frame_batches_known_camera_and_candidate_words(self):
        extra = {
            "camera_position_x": (0x00167000, "f32"),
            "camera_yaw": (0x00167004, "f32"),
        }
        ranges = [(0x00167100, 8)]
        values = {
            address: f32_word(float(index + 1))
            for index, (address, _) in enumerate(CAMERA.KNOWN_FIELDS.values())
        }
        values[0x00167000] = f32_word(12.5)
        values[0x00167004] = f32_word(-0.25)
        values[0x00167100] = 0x12345678
        values[0x00167104] = 0x9ABCDEF0
        pine = FakePine(values)

        sample = CAMERA.sample_frame(pine, extra, ranges)

        self.assertAlmostEqual(sample["camera_position_x"], 12.5)
        self.assertAlmostEqual(sample["camera_yaw"], -0.25)
        self.assertEqual(sample["candidate_words"]["0x00167100"], 0x12345678)
        self.assertIn(CAMERA.CONTROL_HEADING, pine.addresses)
        self.assertIn(CAMERA.INPUT_STATE + 0x100, pine.addresses)
        self.assertEqual(len(pine.addresses), len(CAMERA.KNOWN_FIELDS) + 4)
    def test_reducer_keeps_camera_summaries_but_not_raw_candidate_words(self):
        def row(frame, right, heading, yaw, px, camera_x, candidate):
            sample = {
                "player_yaw": yaw,
                "player_pos_x": px,
                "player_pos_y": 2.0,
                "player_pos_z": 3.0,
                "player_moby_yaw": yaw,
                "control_heading": heading,
                "right_conditioned_x": 0.0 if right[0] == 127 else -1.0,
                "right_conditioned_y": 0.0,
                "camera_position_x": camera_x,
                "camera_forward_x": 1.0,
                "candidate_words": {
                    "0x00166c00": candidate,
                    "0x00166c04": 99,
                },
            }
            return {
                "frame": frame,
                "segment": "test",
                "segment_index": 0,
                "local_frame": frame,
                "left": [127, 0],
                "right": right,
                "buttons": [],
                "sample": sample,
            }

        capture = {
            "stateSha256": "state",
            "movieSha256": "movie",
            "fieldMap": {
                "camera_position_x": {"address": "0x00167000", "kind": "f32"},
                "camera_forward_x": {"address": "0x00167010", "kind": "f32"},
            },
            "samples": [
                row(0, [127, 127], 0.0, 0.0, 1.0, 10.0, f32_word(1.0)),
                row(1, [0, 127], 0.1, 0.05, 1.1, 10.2, f32_word(2.0)),
                row(2, [127, 127], 0.2, 0.10, 1.2, 10.4, f32_word(3.0)),
            ],
        }

        report = CAMERA.derive_scenario("recenter", capture)
        self.assertEqual(
            report["rightStickCommandPath"],
            [[127, 127], [0, 127], [127, 127]],
        )
        self.assertAlmostEqual(report["controlHeading"]["range"], 0.2)
        self.assertAlmostEqual(report["playerPosition"]["netPlanarDelta"], 0.2)
        self.assertAlmostEqual(report["cameraFields"]["camera_position_x"]["range"], 0.4)
        changing = next(
            field for field in report["candidateFields"]
            if field["address"] == "0x00166c00"
        )
        self.assertEqual(changing["distinctWords"], 3)
        self.assertEqual(changing["changes"], 2)
        self.assertNotIn("candidate_words", json.dumps(report))
    def test_suite_shortlist_compares_recenter_turning_and_idle_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            field_map = {}
            for spec in CAMERA.scenario_specs():
                trial = root / spec["id"]
                trial.mkdir()
                is_recenter = spec["id"] == "recenter"
                is_turning = spec["id"] == "turning"
                changing = is_recenter or is_turning
                rows = []
                for frame in range(3):
                    candidate = f32_word(float(frame if changing else 0))
                    sample = {
                        "player_yaw": 0.0,
                        "player_pos_x": 0.0,
                        "player_pos_y": 0.0,
                        "player_pos_z": 0.0,
                        "player_moby_yaw": 0.0,
                        "control_heading": 0.0,
                        "right_conditioned_x": 0.0,
                        "right_conditioned_y": 0.0,
                        "candidate_words": {"0x00166c00": candidate},
                    }
                    rows.append({
                        "frame": frame,
                        "segment": "test",
                        "segment_index": 0,
                        "local_frame": frame,
                        "left": [127, 127],
                        "right": [127, 127],
                        "buttons": [],
                        "sample": sample,
                    })
                (trial / "raw.json").write_text(json.dumps({
                    "stateSha256": "state",
                    "movieSha256": "movie",
                    "fieldMap": field_map,
                    "samples": rows,
                }), encoding="utf-8")

            report = CAMERA.derive_suite(root)
            self.assertEqual(report["missingScenarios"], [])
            self.assertEqual(report["candidateShortlist"][0]["address"], "0x00166c00")
            self.assertEqual(
                report["candidateShortlist"][0]["changesByScenario"]["recenter"],
                2,
            )


if __name__ == "__main__":
    unittest.main()
