import importlib.util
import json
import math
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
            [
                "fixed-heading",
                "moving",
                "turning",
                "idle",
                "manual-idle-left",
                "manual-idle-right",
                "recenter",
                "turn-release",
                "obstruction",
            ],
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
                    "camera_probe_position_x": {"address": "0x00167000", "kind": "f32"},
                    "camera_probe_forward_x": {"address": "0x00167010", "kind": "f32"},
                },
                "candidateRanges": [
                    {"start": "0x00167100", "bytes": 16},
                ],
            }), encoding="utf-8")
            fields, ranges = CAMERA.load_field_map(path)
            self.assertEqual(fields["camera_probe_position_x"], (0x00167000, "f32"))
            self.assertEqual(fields["camera_probe_forward_x"], (0x00167010, "f32"))
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
            "camera_probe_position_x": (0x00167000, "f32"),
            "camera_probe_yaw": (0x00167004, "f32"),
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

        self.assertAlmostEqual(sample["camera_probe_position_x"], 12.5)
        self.assertAlmostEqual(sample["camera_probe_yaw"], -0.25)
        self.assertEqual(sample["candidate_words"]["0x00167100"], 0x12345678)
        self.assertIn(CAMERA.CONTROL_HEADING, pine.addresses)
        self.assertIn(CAMERA.INPUT_STATE + 0x100, pine.addresses)
        self.assertEqual(len(pine.addresses), len(CAMERA.KNOWN_FIELDS) + 4)

    def test_producer_probe_verifies_directional_heading_step_contract(self):
        memory = bytearray(0x002E9C8C + 4)
        signatures = {
            **CAMERA.CAMERA_PRODUCER_SIGNATURES,
            **CAMERA.CHASE_FOLLOW_SIGNATURES,
            **CAMERA.CHASE_FRAMING_SIGNATURES,
            **CAMERA.CAMERA_OBSTRUCTION_SIGNATURES,
        }
        for address, word in signatures.items():
            struct.pack_into("<I", memory, address, word)
        struct.pack_into("<H", memory, CAMERA.PLAYER_STATE + 0x288, 0)
        struct.pack_into("<f", memory, CAMERA.CAMERA_STATE_BASE + 0x80, 0.0)
        struct.pack_into("<f", memory, CAMERA.CONTROL_HEADING, -2.0)
        struct.pack_into("<f", memory, 0x0015ED60, 1.0)

        active_camera = 0x3000
        camera_state = 0x4000
        camera_profile = 0x5000
        struct.pack_into("<I", memory, CAMERA.ACTIVE_CAMERA_OBJECT_PTR, active_camera)
        struct.pack_into("<I", memory, active_camera + 0x70, camera_state)
        struct.pack_into("<H", memory, active_camera + 0x8C, 0)
        struct.pack_into("<I", memory, CAMERA.CAMERA_DISPATCH_TABLE + 0x08, 0x002E6D60)
        struct.pack_into("<I", memory, CAMERA.CAMERA_DISPATCH_TABLE + 0x0C, 0x002E9C28)
        struct.pack_into("<I", memory, CAMERA.CAMERA_PROFILE_PTR, camera_profile)
        for base, values in (
            (active_camera + 0x30, (4.0, 6.0, 3.0)),
            (CAMERA.CAMERA_POSITION_BASE, (4.0, 6.0, 3.0)),
            (CAMERA.CAMERA_RAW_PLAYER_BASE, (1.0, 2.0, 1.0)),
            (camera_state + 0x90, (1.0, 2.0, 3.0)),
            (camera_state + 0x140, (3.0, 4.0, 0.0)),
        ):
            for index, value in enumerate(values):
                struct.pack_into("<f", memory, base + index * 4, value)
        for offset, value in (
            (0x24, 1.5), (0x28, 2.0), (0x2C, 0.0), (0x30, 0.0),
            (0x158, 0.0), (0x15C, 5.0), (0x160, 2.0), (0x174, 0.0),
            (0x178, 0.003), (0x180, 0.0), (0x184, 0.003), (0x188, 4.64),
            (0x200, 0.0),
        ):
            struct.pack_into("<f", memory, camera_state + offset, value)
        struct.pack_into("<f", memory, camera_profile + 0x15C, 4.64)
        struct.pack_into("<f", memory, camera_profile + 0x160, 2.0)

        class FakeProbe:
            @staticmethod
            def read_zip_entry(_state, _entry, _zstd):
                return bytes(memory)

        original_probe = CAMERA._movement_probe
        original_sha = CAMERA.HARNESS.sha256
        CAMERA._movement_probe = lambda: FakeProbe
        CAMERA.HARNESS.sha256 = lambda _path: "state"
        try:
            report = CAMERA.probe_camera_producer(
                pathlib.Path("state.p2s"),
                pathlib.Path("zstd.dll"),
            )
        finally:
            CAMERA._movement_probe = original_probe
            CAMERA.HARNESS.sha256 = original_sha

        branch = report["directionalHeadingStepBranch"]
        self.assertEqual(report["loadedOverlaySignaturesVerified"], len(signatures))
        self.assertAlmostEqual(branch["stepIncrementRad"], 0.0020000000949949026)
        self.assertAlmostEqual(branch["stepClampAbsRad"], 0.03999999910593033)
        self.assertEqual(branch["releaseDivisor"], 1.5)
        self.assertEqual(branch["headingUpdate"], "WrapPi(controlHeading - stepField)")
        vertical = report["ordinaryManualVerticalRelease"]
        self.assertEqual(vertical["selectorRoutine"], "0x002e89b0..0x002e8bb0")
        self.assertEqual(vertical["verticalFallbackAngle"], "state+0x1cc")
        self.assertAlmostEqual(vertical["verticalSpanRadians"], 0.69813168, places=7)
        self.assertEqual(
            vertical["fallbackWriterCallers"],
            ["0x002ed0a4", "0x002ed858"],
        )
        chase = report["ordinaryChaseFollowBranch"]
        self.assertAlmostEqual(chase["verticalAcceleration"], 0.0075)
        self.assertAlmostEqual(chase["verticalDamping"], 0.175)
        self.assertEqual(chase["dampedStepHelper"], "0x001eb240..0x001eb320")
        framing = report["ordinaryChaseFramingProducer"]
        self.assertEqual(report["savestateWitness"]["activeCameraType"], 0)
        self.assertEqual(report["savestateWitness"]["updateCallback"], "0x002e9c28")
        self.assertAlmostEqual(framing["stateWitness"]["radialOffsetMagnitude"], 5.0)
        self.assertAlmostEqual(framing["stateWitness"]["preferredRadius"], 5.0)
        self.assertAlmostEqual(framing["geometryWitness"]["maxComposedGlobalEyeError"], 0.0)
        self.assertEqual(framing["constructorDefaults"]["radius"], CAMERA.CHASE_CONSTRUCTOR_RADIUS)
        self.assertAlmostEqual(framing["constructorDefaults"]["profileTransitionAcceleration"], 0.003)
        self.assertAlmostEqual(framing["finalHeightFollow"]["acceleration"], 0.004)
        self.assertAlmostEqual(framing["finalHeightFollow"]["damping"], 0.2)
        obstruction = report["cameraObstructionProducer"]
        self.assertEqual(obstruction["persistentState"]["clearWitnessReleaseTimer"], 0)
        self.assertEqual(obstruction["persistentState"]["clearWitnessLateralSide"], 0)
        self.assertAlmostEqual(obstruction["radialPullIn"]["stepPerCameraUpdate"], 0.075)
        self.assertEqual(obstruction["radialPullIn"]["activeContactTimerTicksAtUnitScale"], 0x884)
        self.assertEqual(obstruction["radialPullIn"]["minimumRadiusTimerTicksAtUnitScale"], 0x7D0)
        self.assertAlmostEqual(obstruction["radialPullIn"]["innerSolverRadiusFloor"], 0.2)
        self.assertAlmostEqual(obstruction["radialPullIn"]["finalEffectiveRadiusFloor"], 1.5)
        self.assertAlmostEqual(obstruction["radialPullIn"]["frameScaleWitness"], 1.0)
        self.assertEqual(obstruction["lateralCorrection"]["sideEncoding"], "+1 above +0.75, -1 below -0.75, otherwise 0")
        self.assertAlmostEqual(obstruction["lateralCorrection"]["angularStepRad"], math.pi / 180.0)
        self.assertIn("segment-like", obstruction["contactGeometry"]["boundary"])

    def test_camera_framing_reduces_native_geometry_and_vertical_follow_law(self):
        pitch = 0.1
        forward = (math.cos(pitch), 0.0, -math.sin(pitch))
        def row(frame, player_z, follow_z, raw_z, velocity):
            sample = {
                "control_heading": 0.0,
                "player_pos_x": 0.0, "player_pos_y": 0.0, "player_pos_z": player_z,
                "camera_position_x": -6.0, "camera_position_y": 0.0, "camera_position_z": player_z + 2.0,
                "camera_pitch": pitch,
                "camera_forward_x": forward[0], "camera_forward_y": forward[1], "camera_forward_z": forward[2],
                "camera_follow_x": 0.0, "camera_follow_y": 0.0, "camera_follow_z": follow_z,
                "camera_follow_raw_z": raw_z, "camera_follow_z_velocity": velocity,
                "camera_raw_player_x": 0.0, "camera_raw_player_y": 0.0, "camera_raw_player_z": player_z,
            }
            return {"frame": frame, "sample": sample}

        rows = [
            row(0, 0.0, 0.0, 0.0, 0.0),
            row(1, 1.0, CAMERA.CHASE_Z_ACCEL, 1.0, CAMERA.CHASE_Z_ACCEL),
        ]
        report = CAMERA.camera_framing_summary(rows)
        self.assertAlmostEqual(report["planarDistance"]["min"], 6.0)
        self.assertLess(report["maxEyeRayYawErrorRad"], 1e-12)
        self.assertLess(report["maxForwardBasisComponentError"], 1e-12)
        self.assertEqual(report["maxRawPlayerCopyError"], 0.0)
        self.assertLess(report["verticalFollowLaw"]["maxAbsResidual"], 1e-12)

    def test_reducer_keeps_camera_summaries_but_not_raw_candidate_words(self):
        def row(frame, right, heading, yaw, px, camera_x, candidate):
            sample = {
                "player_yaw": yaw,
                "player_target_yaw": heading,
                "player_pos_x": px,
                "player_pos_y": 2.0,
                "player_pos_z": 3.0,
                "player_moby_yaw": yaw,
                "control_heading": heading,
                "camera_heading_sin": math.sin(heading),
                "camera_heading_cos": math.cos(heading),
                "input_direction_flags": 0x1000,
                "right_conditioned_x": 0.0 if right[0] == 127 else -1.0,
                "right_conditioned_y": 0.0,
                "left_conditioned_x": 0.0,
                "left_conditioned_y": -1.0,
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
        self.assertAlmostEqual(report["cameraHeadingBasis"]["maxSinError"], 0.0)
        self.assertAlmostEqual(report["cameraHeadingBasis"]["maxCosError"], 0.0)
        self.assertAlmostEqual(report["movementTargetRelation"]["maxAbsErrorRad"], 0.0)
        self.assertEqual(report["segments"][0]["inputDirectionFlagPath"], ["0x00001000"])
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
                        "player_target_yaw": 0.0,
                        "player_pos_x": 0.0,
                        "player_pos_y": 0.0,
                        "player_pos_z": 0.0,
                        "player_moby_yaw": 0.0,
                        "control_heading": 0.0,
                        "camera_heading_sin": 0.0,
                        "camera_heading_cos": 1.0,
                        "input_direction_flags": 0,
                        "right_conditioned_x": 0.0,
                        "right_conditioned_y": 0.0,
                        "left_conditioned_x": 0.0,
                        "left_conditioned_y": 0.0,
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
