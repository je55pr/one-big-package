import importlib.util
import json
import pathlib
import struct
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-analogue-movement-harness.py"
SPEC = importlib.util.spec_from_file_location("rac1_analogue_harness", SCRIPT)
HARNESS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HARNESS)


class Rac1AnalogueHarnessTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        patcher = mock.patch.object(HARNESS, "CAPTURES", pathlib.Path(directory.name))
        patcher.start()
        self.addCleanup(patcher.stop)

    def movie_fixture(self, root):
        plan = root / "plan.json"
        state = root / "anchor.p2s"
        movie = root / "trial.p2m2"
        plan.write_text(json.dumps({
            "schema": 1,
            "segments": [{"frames": 2, "left": [127, 60]}],
        }), encoding="utf-8")
        state.write_bytes(b"synthetic-anchor-not-retail")
        HARNESS.build_movie(plan, state, movie)
        return plan, state, movie

    def test_verify_movie_checks_actual_bytes_and_companion(self):
        with tempfile.TemporaryDirectory(dir=HARNESS.CAPTURES) as directory:
            plan, state, movie = self.movie_fixture(pathlib.Path(directory))
            verified = HARNESS.verify_movie_artifacts(plan, movie)
            self.assertEqual(verified["artifactConsistency"], "passed")
            self.assertEqual(verified["movieSha256"], HARNESS.sha256(movie))
            self.assertEqual(verified["stateSha256"], HARNESS.sha256(state))
            self.assertEqual(verified["planSha256"], HARNESS.sha256(plan))
            self.assertNotIn("validation", verified)

    def test_replaced_companion_is_rejected_before_emulator_input(self):
        with tempfile.TemporaryDirectory(dir=HARNESS.CAPTURES) as directory:
            root = pathlib.Path(directory)
            plan, _, movie = self.movie_fixture(root)
            pathlib.Path(f"{movie}_SaveState.p2s").write_bytes(b"different-anchor")
            with mock.patch.object(HARNESS, "start_movie_replay") as replay:
                with self.assertRaisesRegex(ValueError, "companion savestate changed"):
                    HARNESS.capture_trial(1, 28099, plan, movie, root / "raw.json", 0, 0)
                replay.assert_not_called()

    def test_modified_movie_is_rejected_even_with_unchanged_filename(self):
        with tempfile.TemporaryDirectory(dir=HARNESS.CAPTURES) as directory:
            plan, _, movie = self.movie_fixture(pathlib.Path(directory))
            data = bytearray(movie.read_bytes())
            data[-1] ^= 1
            movie.write_bytes(data)
            with self.assertRaisesRegex(ValueError, "movie bytes changed"):
                HARNESS.verify_movie_artifacts(plan, movie)

    def test_same_length_but_different_plan_is_rejected(self):
        with tempfile.TemporaryDirectory(dir=HARNESS.CAPTURES) as directory:
            plan, _, movie = self.movie_fixture(pathlib.Path(directory))
            data = json.loads(plan.read_text(encoding="utf-8"))
            data["segments"][0]["left"] = [127, 0]
            plan.write_text(json.dumps(data), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "input differs from plan at frame 0"):
                HARNESS.verify_movie_artifacts(plan, movie)

    def test_movie_with_matching_hash_but_truncated_frame_is_rejected(self):
        with tempfile.TemporaryDirectory(dir=HARNESS.CAPTURES) as directory:
            plan, _, movie = self.movie_fixture(pathlib.Path(directory))
            movie.write_bytes(movie.read_bytes()[:-1])
            sidecar = movie.with_suffix(movie.suffix + ".json")
            manifest = json.loads(sidecar.read_text(encoding="utf-8"))
            manifest["movieSha256"] = HARNESS.sha256(movie)
            sidecar.write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "frame count/length"):
                HARNESS.verify_movie_artifacts(plan, movie)

    def test_empty_plan_cannot_be_a_successful_experiment(self):
        with tempfile.TemporaryDirectory(dir=HARNESS.CAPTURES) as directory:
            plan = pathlib.Path(directory) / "empty.json"
            plan.write_text('{"schema":1,"segments":[]}', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "nonempty"):
                HARNESS.load_plan(plan)

    def test_invalid_or_unsupported_capture_cannot_be_reduced(self):
        for status in ("invalid", "unsupported", "passed", None):
            with self.subTest(status=status):
                with self.assertRaisesRegex(ValueError, "cannot derive movement evidence"):
                    HARNESS.derive({"validation": {"status": status}, "samples": []})

    def pine_fixture(self, replies):
        stream = bytearray(b"".join(replies))
        sock = mock.Mock()

        def receive(count):
            # Exercise socket fragmentation as well as the protocol fields.
            count = min(count, 3)
            result = bytes(stream[:count])
            del stream[:count]
            return result

        sock.recv.side_effect = receive
        with mock.patch.object(HARNESS.socket, "create_connection", return_value=sock):
            return HARNESS.Pine("127.0.0.1", 28099), sock

    def pine_reply(self, payload):
        return struct.pack("<I", len(payload) + 5) + b"\0" + payload

    def test_pine_identity_uses_only_read_only_protocol_commands(self):
        values = ("PCSX2 v2.6.3", "Ratchet & Clank", "SCUS-97199", "12345678", "1.00")
        replies = [self.pine_reply(struct.pack("<I", 1))]
        for value in values:
            encoded = value.encode("utf-8") + b"\0"
            replies.append(self.pine_reply(struct.pack("<I", len(encoded)) + encoded))
        pine, sock = self.pine_fixture(replies)
        identity = pine.identity()
        self.assertEqual(identity["status"], "paused")
        self.assertEqual(identity["serial"], "SCUS-97199")
        self.assertEqual(
            [call.args[0][4] for call in sock.sendall.call_args_list],
            [15, 8, 11, 12, 13, 14],
        )

    def test_pine_shutdown_does_not_request_unavailable_game_metadata(self):
        pine, sock = self.pine_fixture([self.pine_reply(struct.pack("<I", 2))])
        self.assertEqual(pine.identity(), {"status": "shutdown"})
        sock.sendall.assert_called_once()

    def test_pine_rejects_malformed_lengths_and_truncated_reads(self):
        replies = (
            struct.pack("<I", 4),
            struct.pack("<I", 450001),
            self.pine_reply(b"\0\0"),
            self.pine_reply(bytes(8)),
        )
        for reply in replies:
            with self.subTest(reply=reply):
                pine, _ = self.pine_fixture([reply])
                with self.assertRaises(RuntimeError):
                    pine.read32([HARNESS.PLAYER_STATE])

    def test_pine_disconnect_and_error_cannot_look_like_zero_memory(self):
        for reply, error in ((struct.pack("<I", 9), ConnectionError),
                             (struct.pack("<I", 5) + b"\xff", RuntimeError)):
            with self.subTest(reply=reply):
                pine, _ = self.pine_fixture([reply])
                with self.assertRaises(error):
                    pine.read32([HARNESS.PLAYER_STATE])

    def test_wrong_game_or_running_vm_is_rejected_before_replay(self):
        for identity in ({"serial": "SLES-50916", "status": "paused"},
                         {"serial": "SCUS-97199", "status": "running"},
                         {"status": "shutdown"}):
            with self.subTest(identity=identity), tempfile.TemporaryDirectory(dir=HARNESS.CAPTURES) as directory:
                root = pathlib.Path(directory)
                plan, _, movie = self.movie_fixture(root)
                pine = mock.Mock()
                pine.identity.return_value = identity
                with mock.patch.object(HARNESS, "Pine", return_value=pine), \
                     mock.patch.object(HARNESS, "start_movie_replay") as replay:
                    with self.assertRaisesRegex(ValueError, "requires paused SCUS-97199"):
                        HARNESS.capture_trial(1, 28099, plan, movie, root / "raw.json", 0, 0)
                    replay.assert_not_called()
                    pine.close.assert_called_once()

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

    def test_contact_probe_fields_pin_recovered_player_layout(self):
        self.assertEqual(
            HARNESS.KNOWN_FIELDS["contact_orientation_x"],
            (HARNESS.PLAYER_BASE + 0x270, "f32"),
        )
        self.assertEqual(
            HARNESS.KNOWN_FIELDS["contact_orientation_y"],
            (HARNESS.PLAYER_BASE + 0x274, "f32"),
        )
        self.assertEqual(
            HARNESS.KNOWN_FIELDS["contact_orientation_z"],
            (HARNESS.PLAYER_BASE + 0x278, "f32"),
        )
        self.assertEqual(
            HARNESS.KNOWN_FIELDS["current_dynamic_contact"],
            (HARNESS.PLAYER_BASE + 0x2FC, "u32"),
        )
        self.assertEqual(
            HARNESS.KNOWN_FIELDS["contact_slot_300"],
            (HARNESS.PLAYER_BASE + 0x300, "u32"),
        )
        self.assertEqual(
            HARNESS.KNOWN_FIELDS["contact_counters_30c"],
            (HARNESS.PLAYER_BASE + 0x30C, "u32"),
        )
        self.assertEqual(
            HARNESS.KNOWN_FIELDS["persistent_support"],
            (HARNESS.PLAYER_BASE + 0x360, "u32"),
        )
        self.assertEqual(
            HARNESS.KNOWN_FIELDS["support_anchor_state"],
            (HARNESS.PLAYER_BASE + 0x364, "u32"),
        )
        self.assertEqual(
            HARNESS.KNOWN_FIELDS["action_state"],
            (HARNESS.PLAYER_BASE + 0x2084, "u32"),
        )

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
        self.assertEqual(report["validation"]["status"], "unsupported")
        self.assertEqual(report["sequencePath"], [0, 3, 4])
        self.assertEqual(report["segments"][1]["left"], [127, 64])
        self.assertEqual(report["segments"][1]["maxPlanarDisplacementPerUpdate"], 0.25)
        self.assertEqual(report["segments"][1]["planarDisplacementPerUpdate"], [0.25, 0.25])
        self.assertEqual(report["segments"][1]["sequenceSamples"], [3, 4])
        self.assertEqual(report["changingCandidateFields"], [
            {"offset": "0x000", "distinctWords": 3}
        ])

    def test_derive_keeps_repeated_segment_labels_separate(self):
        capture = {
            "authority": "synthetic",
            "movieSha256": "synthetic",
            "samples": [
                {
                    "frame": index,
                    "segment_index": index,
                    "segment": label,
                    "left": [127, 127],
                    "right": [127, 127],
                    "sample": {
                        "pos_x": 0.0, "pos_y": 0.0,
                        "disp_x": float(index), "disp_y": 0.0,
                        "yaw": 0.0, "target_yaw": 0.0,
                        "sequence": index, "candidate_words": {},
                    },
                }
                for index, label in enumerate(("neutral", "move", "neutral", "neutral"))
            ],
        }
        report = HARNESS.derive(capture)
        self.assertEqual([segment["frames"] for segment in report["segments"]], [1, 1, 1, 1])
        self.assertEqual(
            [segment["sequenceSamples"] for segment in report["segments"]],
            [[0], [1], [2], [3]],
        )
        # Legacy captures lack segment indexes, but separated runs must still
        # not be merged merely because they reuse a label.
        for row in capture["samples"]:
            row.pop("segment_index")
        report = HARNESS.derive(capture)
        self.assertEqual([segment["frames"] for segment in report["segments"]], [1, 1, 2])


if __name__ == "__main__":
    unittest.main()
