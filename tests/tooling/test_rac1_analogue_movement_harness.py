import importlib.util
import json
import pathlib
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-analogue-movement-harness.py"
SPEC = importlib.util.spec_from_file_location("rac1_analogue_harness", SCRIPT)
HARNESS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HARNESS)


class Rac1AnalogueHarnessTests(unittest.TestCase):
    def test_movie_encoding_uses_exact_ds2_bytes(self):
        HARNESS.CAPTURES.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=HARNESS.CAPTURES) as directory:
            root = pathlib.Path(directory)
            plan = root / "plan.json"
            state = root / "anchor.p2s"
            movie = root / "trial.p2m2"
            plan.write_text(json.dumps({
                "schema": 1,
                "segments": [
                    {"label": "neutral", "frames": 1, "left": [127, 127]},
                    {
                        "label": "raw", "frames": 2,
                        "left": [64, 192], "right": [1, 254], "buttons": ["cross"],
                    },
                ],
            }), encoding="utf-8")
            state.write_bytes(b"local-state-fixture")
            manifest = HARNESS.build_movie(plan, state, movie)
            data = movie.read_bytes()
            self.assertEqual(manifest["totalFrames"], 3)
            self.assertEqual(manifest["headerBytes"], 570)
            self.assertEqual(len(data), 570 + 3 * 36)
            self.assertEqual(data[570:576], bytes((255, 255, 127, 127, 127, 127)))
            second = 570 + 36
            self.assertEqual(data[second:second + 6], bytes((255, 191, 1, 254, 64, 192)))
            self.assertEqual(manifest["segments"][1]["buttons"], ["cross"])
            self.assertTrue(pathlib.Path(f"{movie}_SaveState.p2s").exists())

    def test_unknown_button_is_rejected(self):
        HARNESS.CAPTURES.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=HARNESS.CAPTURES) as directory:
            plan = pathlib.Path(directory) / "plan.json"
            plan.write_text(json.dumps({
                "schema": 1,
                "segments": [
                    {"frames": 1, "left": [127, 127], "buttons": ["not-a-button"]},
                ],
            }), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "unknown buttons"):
                HARNESS.load_plan(plan)

    def test_picker_helper_receives_absolute_movie_path(self):
        HARNESS.CAPTURES.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=HARNESS.CAPTURES) as directory:
            root = pathlib.Path(directory)
            movie = root / "trial.p2m2"
            runner = root / "pwsh-runner.cmd"
            helper = root / "picker.ps1"
            movie.write_bytes(b"movie")
            runner.write_text("", encoding="utf-8")
            helper.write_text("", encoding="utf-8")

            previous_runner = HARNESS.PWSH_RUNNER
            previous_helper = HARNESS.PICKER_HELPER
            try:
                HARNESS.PWSH_RUNNER = runner
                HARNESS.PICKER_HELPER = helper
                completed = mock.Mock(returncode=0, stdout="", stderr="")
                with mock.patch.object(HARNESS.subprocess, "run", return_value=completed) as run:
                    HARNESS._invoke_picker_path(1234, movie)

                argv = run.call_args.args[0]
                self.assertEqual(argv[-1], str(movie.resolve()))
                self.assertEqual(run.call_args.kwargs["cwd"], HARNESS.ROOT)
            finally:
                HARNESS.PWSH_RUNNER = previous_runner
                HARNESS.PICKER_HELPER = previous_helper

    def test_capture_paths_cannot_escape_ignored_capture_root(self):
        with self.assertRaises(ValueError):
            HARNESS.capture_path(ROOT / "research" / "raw.json")

    def test_derive_keeps_only_reduced_candidate_metadata(self):
        def row(frame, label, left, x, disp, yaw, sequence, candidate):
            return {
                "frame": frame,
                "segment": label,
                "left": left,
                "right": [127, 127],
                "sample": {
                    "pos_x": x, "pos_y": 2.0, "disp_x": disp, "disp_y": 0.0,
                    "yaw": yaw, "target_yaw": yaw, "sequence": sequence,
                    "candidate_words": {"0x000": candidate, "0x004": 99},
                },
            }
        capture = {
            "authority": "synthetic",
            "movieSha256": "deadbeef",
            "stateSha256": "cafebabe",
            "samples": [
                row(0, "neutral", [127, 127], 1.0, 0.0, 1.0, 0, 10),
                row(1, "neutral", [127, 127], 1.0, 0.0, 1.0, 0, 10),
                row(2, "raw", [127, 64], 1.25, 0.25, 1.1, 3, 11),
                row(3, "raw", [127, 64], 1.50, 0.25, 1.2, 4, 12),
            ],
        }
        report = HARNESS.derive(capture)
        self.assertEqual(report["stateSha256"], "cafebabe")
        self.assertEqual(report["sequencePath"], [0, 3, 4])
        self.assertEqual(report["segments"][1]["left"], [127, 64])
        self.assertEqual(report["segments"][1]["maxPlanarDisplacementPerUpdate"], 0.25)
        self.assertEqual(report["segments"][1]["planarDisplacementPerUpdate"], [0.25, 0.25])
        self.assertEqual(report["segments"][1]["sequenceSamples"], [3, 4])
        self.assertEqual(report["changingCandidateFields"], [
            {"offset": "0x000", "distinctWords": 3}
        ])


if __name__ == "__main__":
    unittest.main()
