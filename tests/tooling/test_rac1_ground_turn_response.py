import importlib.util
import json
import math
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-ground-turn-response.py"
SPEC = importlib.util.spec_from_file_location("rac1_ground_turn_response", SCRIPT)
TURNS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(TURNS)


class Rac1GroundTurnResponseTests(unittest.TestCase):
    def test_trial_matrix_covers_turns_reversals_and_release_restarts(self):
        specs = TURNS.trial_specs()
        ids = {spec["id"] for spec in specs}

        self.assertEqual(len(specs), 26)
        for band in ("run", "walk"):
            for angle in ("45", "90", "135", "180"):
                self.assertIn(f"{band}-forward-turn-{angle}", ids)
            for angle in ("45", "90", "135"):
                self.assertIn(f"{band}-forward-turn-left-{angle}", ids)
            self.assertIn(f"{band}-left-right-reversal", ids)
            self.assertIn(f"{band}-left-to-right-reversal", ids)
            self.assertIn(f"{band}-forward-back-reversal", ids)
            self.assertIn(f"{band}-back-to-forward-reversal", ids)
            self.assertIn(f"{band}-release-turn-90", ids)
            self.assertIn(f"{band}-release-turn-180", ids)

    def test_walk_plan_uses_retained_plateau_magnitude_witnesses(self):
        spec = next(
            item for item in TURNS.trial_specs()
            if item["id"] == "walk-forward-turn-90"
        )
        plan = TURNS.plan_for(spec)
        self.assertEqual(plan["segments"][0]["left"], [127, 17])
        self.assertEqual(plan["segments"][1]["left"], [237, 127])

    def test_capture_resume_requires_matching_movie_and_state_hashes(self):
        manifest = {"movieSha256": "movie-a", "stateSha256": "state-a"}
        with tempfile.TemporaryDirectory() as directory:
            raw = pathlib.Path(directory) / "raw.json"
            self.assertFalse(TURNS.capture_matches_manifest(raw, manifest))

            raw.write_text(json.dumps({
                "movieSha256": "movie-a",
                "stateSha256": "state-a",
            }), encoding="utf-8")
            self.assertTrue(TURNS.capture_matches_manifest(raw, manifest))

            raw.write_text(json.dumps({
                "movieSha256": "movie-b",
                "stateSha256": "state-a",
            }), encoding="utf-8")
            self.assertFalse(TURNS.capture_matches_manifest(raw, manifest))

    def test_reducer_compares_travel_heading_with_yaw_and_target(self):
        yaw = 0.5

        def row(frame, segment, local_frame, speed, facing, target, sequence=4):
            return {
                "frame": frame,
                "segment": segment,
                "local_frame": local_frame,
                "sample": {
                    "disp_x": speed * math.cos(facing),
                    "disp_y": speed * math.sin(facing),
                    "disp_z": 0.0,
                    "pos_z": 29.0,
                    "yaw": facing,
                    "target_yaw": target,
                    "sequence": sequence,
                },
            }

        prelude = [
            row(i, "prelude", i, 0.095, yaw, yaw)
            for i in range(8)
        ]
        turn = [
            row(8, "turn", 0, 0.095, yaw, yaw),
            row(9, "turn", 1, 0.095, yaw + 0.05, yaw + 1.0),
            row(10, "turn", 2, 0.095, yaw + 0.10, yaw + 1.0),
        ]
        spec = {
            "id": "synthetic",
            "band": "run",
            "kind": "turn",
            "prelude": "forward",
            "target": "right",
            "preludeFrames": 8,
            "releaseFrames": 0,
        }
        report = TURNS.derive_trial(spec, {"samples": [*prelude, *turn]})

        self.assertEqual(report["effectiveTurnLocalFrame"], 1)
        self.assertLess(report["summary"]["maxAbsTravelMinusYaw"], 1e-12)
        self.assertGreater(report["summary"]["maxAbsTravelMinusTarget"], 0.8)
        self.assertEqual(report["summary"]["maxAbsVerticalStep"], 0.0)
        self.assertEqual(report["summary"]["positionZRange"], [29.0, 29.0])


if __name__ == "__main__":
    unittest.main()
