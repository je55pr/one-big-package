#!/usr/bin/env python3
"""Deterministic R&C1 camera archaeology through PCSX2 input replay + PINE.

Raw movies, savestate companions and frame samples stay under ignored captures/.
This tool intentionally treats camera addresses as evidence inputs: known
player/control fields have fixed addresses, while semantic camera fields can be
supplied by a payload-free field-map JSON as archaeology identifies them.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HARNESS_PATH = ROOT / "tools" / "rac1-analogue-movement-harness.py"
MOVEMENT_PROBE_PATH = ROOT / "tools" / "rac1-savestate-movement-probe.py"
SPEC = importlib.util.spec_from_file_location("rac1_analogue_harness", HARNESS_PATH)
HARNESS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HARNESS)

INPUT_STATE = 0x0013C940
PLAYER_STATE = 0x0013F3D0
PLAYER_MOBY = 0x01845E80
CONTROL_HEADING = 0x00166DD8
CONTROL_BASIS_BASE = 0x00166C80
CAMERA_STATE_BASE = 0x0016C058
CAMERA_POSITION_BASE = CONTROL_BASIS_BASE + 0x140
CAMERA_PITCH = CONTROL_BASIS_BASE + 0x154
CAMERA_FOLLOW_BASE = CONTROL_BASIS_BASE + 0x190
CAMERA_RAW_PLAYER_BASE = CONTROL_BASIS_BASE + 0x1F0
CAMERA_FOLLOW_RAW_Z = CAMERA_FOLLOW_BASE + 0x0C
CAMERA_FOLLOW_Z_VELOCITY = CAMERA_FOLLOW_BASE + 0x10
ACTIVE_CAMERA_OBJECT_PTR = CONTROL_BASIS_BASE + 0x180
CAMERA_PROFILE_PTR = 0x00168FC0
CAMERA_DISPATCH_TABLE = 0x001EA880
CAMERA_DISPATCH_STRIDE = 0x14
INPUT_DIRECTION_FLAGS = INPUT_STATE + 0x1A0
CHASE_Z_ACCEL = struct.unpack("<f", struct.pack("<I", 0x3BF5C28F))[0]
CHASE_Z_DAMPING = struct.unpack("<f", struct.pack("<I", 0x3E333333))[0]
CHASE_CONSTRUCTOR_RADIUS = struct.unpack("<f", struct.pack("<I", 0x40947AE1))[0]
CHASE_CONSTRUCTOR_EYE_HEIGHT = 2.0
CHASE_PROFILE_TRANSITION_ACCEL = struct.unpack("<f", struct.pack("<I", 0x3B449BA6))[0]
CHASE_EYE_HEIGHT_ACCEL = struct.unpack("<f", struct.pack("<I", 0x3B83126F))[0]
CHASE_EYE_HEIGHT_DAMPING = struct.unpack("<f", struct.pack("<I", 0x3E4CCCCD))[0]
OBSTRUCTION_PULL_IN_STEP = struct.unpack("<f", struct.pack("<I", 0x3D99999A))[0]
OBSTRUCTION_INNER_RADIUS_FLOOR = struct.unpack("<f", struct.pack("<I", 0x3E4CCCCD))[0]
OBSTRUCTION_FINAL_RADIUS_FLOOR = 1.5
OBSTRUCTION_SIDE_THRESHOLD = 0.75
OBSTRUCTION_LATERAL_STEP_RAD = struct.unpack("<f", struct.pack("<I", 0x3C8EFA35))[0]
OBSTRUCTION_PULL_IN_TIMER_TICKS = 0x884
OBSTRUCTION_MIN_RADIUS_TIMER_TICKS = 0x7D0
CHASE_SCENARIO_IDS = ("fixed-heading", "moving", "turning", "idle", "turn-release")
DEFAULT_CANDIDATE_START = 0x00166C00
DEFAULT_CANDIDATE_BYTES = 0x400
KNOWN_FIELDS = {
    "player_yaw": (PLAYER_STATE + 0x18, "f32"),
    "player_target_yaw": (PLAYER_STATE + 0x100, "f32"),
    "player_pos_x": (PLAYER_MOBY + 0x10, "f32"),
    "player_pos_y": (PLAYER_MOBY + 0x14, "f32"),
    "player_pos_z": (PLAYER_MOBY + 0x18, "f32"),
    "player_moby_yaw": (PLAYER_MOBY + 0x48, "f32"),
    "control_heading": (CONTROL_HEADING, "f32"),
    "camera_heading_sin": (CONTROL_BASIS_BASE, "f32"),
    "camera_heading_cos": (CONTROL_BASIS_BASE + 0x364, "f32"),
    "camera_forward_x": (CONTROL_BASIS_BASE + 0x08, "f32"),
    "camera_forward_y": (CONTROL_BASIS_BASE + 0x18, "f32"),
    "camera_forward_z": (CONTROL_BASIS_BASE + 0x28, "f32"),
    "camera_position_x": (CAMERA_POSITION_BASE + 0x00, "f32"),
    "camera_position_y": (CAMERA_POSITION_BASE + 0x04, "f32"),
    "camera_position_z": (CAMERA_POSITION_BASE + 0x08, "f32"),
    "camera_pitch": (CAMERA_PITCH, "f32"),
    "camera_follow_x": (CAMERA_FOLLOW_BASE + 0x00, "f32"),
    "camera_follow_y": (CAMERA_FOLLOW_BASE + 0x04, "f32"),
    "camera_follow_z": (CAMERA_FOLLOW_BASE + 0x08, "f32"),
    "camera_follow_raw_z": (CAMERA_FOLLOW_RAW_Z, "f32"),
    "camera_follow_z_velocity": (CAMERA_FOLLOW_Z_VELOCITY, "f32"),
    "camera_raw_player_x": (CAMERA_RAW_PLAYER_BASE + 0x00, "f32"),
    "camera_raw_player_y": (CAMERA_RAW_PLAYER_BASE + 0x04, "f32"),
    "camera_raw_player_z": (CAMERA_RAW_PLAYER_BASE + 0x08, "f32"),
    "input_direction_flags": (INPUT_DIRECTION_FLAGS, "u32"),
    "right_conditioned_x": (INPUT_STATE + 0x100, "f32"),
    "right_conditioned_y": (INPUT_STATE + 0x104, "f32"),
    "left_conditioned_x": (INPUT_STATE + 0x108, "f32"),
    "left_conditioned_y": (INPUT_STATE + 0x10C, "f32"),
}
CAMERA_PRODUCER_SIGNATURES = {
    0x001F3AA4: 0x3C030014,  # input-state high half
    0x001F3ABC: 0x2470C940,  # s0 = 0x0013c940
    0x001F3AD4: 0x8E0201A0,  # directional flags
    0x001F3AE4: 0x3C030017,  # camera-state high half
    0x001F3AE8: 0x2471C058,  # s1 = 0x0016c058
    0x001F3E18: 0x3043A000,  # heading-step direction mask
    0x001F3E20: 0x30428000,  # heading-step sign select
    0x001F3E2C: 0x3C013B03,  # 0.002f increment high half
    0x001F3E30: 0x3421126F,  # 0.002f increment low half
    0x001F3E60: 0x3C013D23,  # 0.04f clamp high half
    0x001F3E64: 0x3421D70A,  # 0.04f clamp low half
    0x001F3F24: 0x26106C80,  # s0 = camera/control basis
    0x001F3F28: 0xC44D0080,  # load camera state +0x80
    0x001F3F2C: 0x0C08004C,  # WrapPi difference helper
    0x001F3F30: 0xC60C0158,  # load control heading
    0x001F3F38: 0xE6000158,  # store control heading
    0x001F3F40: 0x3C013FC0,  # release divisor 1.5f
    0x00200130: 0x460D6001,  # f0 = f12 - f13
}
CHASE_FOLLOW_SIGNATURES = {
    0x001ECA90: 0x3C170016,  # s7 high half for camera globals
    0x001ECAA0: 0x26F56E10,  # s5 = filtered follow target
    0x001ECC6C: 0x8E822284,  # state-dependent follow-mode discriminator
    0x001ECC78: 0x8E832084,  # secondary mode discriminator
    0x001ECCA0: 0xE6E16E10,  # filtered target X = player X
    0x001ECCA4: 0x3C013BF5,  # 0.0075 acceleration high half
    0x001ECCA8: 0x3421C28F,  # 0.0075 acceleration low half
    0x001ECCB0: 0x3C013E33,  # 0.175 damping high half
    0x001ECCB4: 0x34213333,  # 0.175 damping low half
    0x001ECCBC: 0x0C07AC90,  # call damped follow helper 0x001eb240
    0x001ECCC0: 0xE6A00004,  # filtered target Y = player Y
    0x001ECCC8: 0xE6A00008,  # store filtered target Z
    0x001ECCD0: 0xE6A1000C,  # retain raw target Z
    0x001EB260: 0x46156D01,  # delta = target - current
    0x001EB26C: 0x46147382,  # accel * delta
    0x001EB270: 0x46017BC2,  # damping * velocity
    0x001EB274: 0x460F7381,  # accel term - damping term
    0x001EB278: 0x460E0840,  # velocity += correction
    0x001EB280: 0xE6010000,  # store updated velocity
    0x001EB30C: 0x4600A800,  # return current + velocity
}
CHASE_FRAMING_SIGNATURES = {
    0x001ED108: 0x0C07B29C,  # update filtered player follow target first
    0x001ED110: 0x0C07AE88,  # dispatch active camera object update
    0x001ED164: 0x26050140,  # global eye destination = camera globals +0x140
    0x001EBA2C: 0x8C506E00,  # active camera object pointer at 0x00166e00
    0x001EBAD4: 0x8C51000C,  # per-type update callback from 20-byte dispatch entry
    0x001EBAE0: 0x0220F809,  # invoke type-specific camera update
    0x002E9C88: 0x0C0BA6EC,  # type-0 ordinary camera core pipeline
    0x002E5EBC: 0xC60C002C,  # current preferred radius state+0x15c
    0x002E5EC0: 0xC66D002C,  # profile/override radius target
    0x002E5EC4: 0xC60E0048,  # radius transition acceleration state+0x178
    0x002E5EC8: 0x0C07AC90,  # damp preferred radius through 0x001eb240
    0x002E5ED0: 0xE600002C,  # store preferred radius state+0x15c
    0x002E5EEC: 0xC60C0030,  # current preferred eye height state+0x160
    0x002E5EF0: 0xC66D0030,  # profile/override eye-height target
    0x002E5EF4: 0xC60E0054,  # height transition acceleration state+0x184
    0x002E5EFC: 0x0C07AC90,  # damp preferred eye height through 0x001eb240
    0x002E5F04: 0xE6000030,  # store preferred eye height state+0x160
    0x002E6E20: 0x3C014094,  # constructor 4.64 radius high half
    0x002E6E24: 0x34217AE1,  # constructor 4.64 radius low half
    0x002E6E3C: 0x3C014000,  # constructor 2.0 eye height
    0x002E97E4: 0x0C07AC90,  # damp current offset magnitude toward preferred radius
    0x002E97E8: 0x26840158,  # radial velocity state+0x158
    0x002E9A40: 0x27C40030,  # final eye destination = camera object +0x30
    0x002E9A48: 0x0C07FC9E,  # vector add helper 0x001ff278
    0x002E9A4C: 0x26A60010,  # final offset source = state+0x140
    0x002E91B8: 0x3C013B83,  # final height-follow 0.004 accel high half
    0x002E91BC: 0x3421126F,  # final height-follow 0.004 accel low half
    0x002E91C4: 0x3C013E4C,  # final height-follow 0.2 damping high half
    0x002E91C8: 0x3421CCCD,  # final height-follow 0.2 damping low half
    0x002E91E8: 0x0C07AC90,  # damp current eye height toward preferred height
    0x002E9210: 0x0C07AC90,  # damp current look height toward its target
}
CAMERA_OBSTRUCTION_SIGNATURES = {
    0x002E9BEC: 0x0C0B9F48,  # type-0 core calls obstruction/contact stage
    0x002E7218: 0xC780B0B8,  # load 0.075 pull-in step
    0x002E721C: 0x24040884,  # arm 2180-tick release timer
    0x002E7234: 0xA6620034,  # store release timer at state+0x204
    0x002E723C: 0xC78CB0C8,  # load 0.2 inner radial floor
    0x002E736C: 0x3C013F40,  # +0.75 lateral-side threshold
    0x002E7378: 0x3C01BF40,  # -0.75 lateral-side threshold
    0x002E7FB0: 0x3C01BC8E,  # -1 degree rotation high half
    0x002E7FB4: 0x3421FA35,  # -1 degree rotation low half
    0x002E7FD0: 0x3C013C8E,  # +1 degree rotation high half
    0x002E7FD4: 0x3421FA35,  # +1 degree rotation low half
    0x002E8064: 0xC781B0D0,  # load 1.5 final effective-radius floor
    0x002E8080: 0x240407D0,  # 2000-tick minimum-radius release timer
    0x002E8088: 0x0C07FBC8,  # convert native tick duration through 0x001fef20
    0x002E8090: 0xA6420034,  # store converted timer at state+0x204
    0x002E8108: 0x0C07FBE6,  # decrement signed-16 release timer
    0x002E8150: 0x4600A383,  # remaining ticks / duration
    0x002E8158: 0x0C095F96,  # cosine-ease helper 0x00257e58
    0x002E8164: 0xE6400030,  # store eased radial correction state+0x200
    0x002E8200: 0x0C07BF1C,  # final world-contact query 0x001efc70
    0x00257EA4: 0x3C014049,  # pi high half for cosine easing
    0x00257EB0: 0x0C07FDFA,  # cosine helper 0x001ff7e8
}


def wrap_pi(value: float) -> float:
    return (value + math.pi) % (2.0 * math.pi) - math.pi


def _movement_probe():
    spec = importlib.util.spec_from_file_location("rac1_movement_probe", MOVEMENT_PROBE_PATH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def probe_camera_producer(savestate: Path, zstd_dll: Path) -> dict[str, object]:
    probe = _movement_probe()
    memory = probe.read_zip_entry(savestate, "eeMemory.bin", zstd_dll)
    mismatches: list[dict[str, str]] = []
    signatures = {
        **CAMERA_PRODUCER_SIGNATURES,
        **CHASE_FOLLOW_SIGNATURES,
        **CHASE_FRAMING_SIGNATURES,
        **CAMERA_OBSTRUCTION_SIGNATURES,
    }
    for address, expected in signatures.items():
        actual = struct.unpack_from("<I", memory, address)[0]
        if actual != expected:
            mismatches.append({
                "address": f"0x{address:08x}",
                "expected": f"0x{expected:08x}",
                "actual": f"0x{actual:08x}",
            })
    if mismatches:
        raise RuntimeError(f"camera producer signatures changed: {mismatches}")

    camera_mode = struct.unpack_from("<H", memory, PLAYER_STATE + 0x288)[0]
    heading_step = struct.unpack_from("<f", memory, CAMERA_STATE_BASE + 0x80)[0]
    control_heading = struct.unpack_from("<f", memory, CONTROL_HEADING)[0]
    active_camera = struct.unpack_from("<I", memory, ACTIVE_CAMERA_OBJECT_PTR)[0]
    camera_type = struct.unpack_from("<H", memory, active_camera + 0x8C)[0]
    camera_state = struct.unpack_from("<I", memory, active_camera + 0x70)[0]
    dispatch_entry = CAMERA_DISPATCH_TABLE + camera_type * CAMERA_DISPATCH_STRIDE
    init_callback = struct.unpack_from("<I", memory, dispatch_entry + 0x08)[0]
    update_callback = struct.unpack_from("<I", memory, dispatch_entry + 0x0C)[0]
    camera_profile = struct.unpack_from("<I", memory, CAMERA_PROFILE_PTR)[0]

    def f32(address: int) -> float:
        return struct.unpack_from("<f", memory, address)[0]

    def vec3(address: int) -> list[float]:
        return [f32(address + offset) for offset in (0, 4, 8)]

    object_eye = vec3(active_camera + 0x30)
    global_eye = vec3(CAMERA_POSITION_BASE)
    player = vec3(CAMERA_RAW_PLAYER_BASE)
    eye_anchor = vec3(camera_state + 0x90)
    radial_offset = vec3(camera_state + 0x140)
    composed_eye = [eye_anchor[index] + radial_offset[index] for index in range(3)]
    radial_magnitude = math.sqrt(sum(value * value for value in radial_offset))
    return {
        "schema": 1,
        "authority": "R&C1 NTSC-U SCUS-97199 loaded EE savestate",
        "savestateSha256": HARNESS.sha256(savestate),
        "loadedOverlaySignaturesVerified": len(signatures),
        "addresses": {
            "inputState": f"0x{INPUT_STATE:08x}",
            "inputDirectionFlags": f"0x{INPUT_DIRECTION_FLAGS:08x}",
            "cameraStateBase": f"0x{CAMERA_STATE_BASE:08x}",
            "cameraControlBasisBase": f"0x{CONTROL_BASIS_BASE:08x}",
            "cameraPositionBase": f"0x{CAMERA_POSITION_BASE:08x}",
            "cameraPitch": f"0x{CAMERA_PITCH:08x}",
            "cameraFollowBase": f"0x{CAMERA_FOLLOW_BASE:08x}",
            "cameraRawPlayerBase": f"0x{CAMERA_RAW_PLAYER_BASE:08x}",
            "activeCameraObjectPointer": f"0x{ACTIVE_CAMERA_OBJECT_PTR:08x}",
            "cameraProfilePointer": f"0x{CAMERA_PROFILE_PTR:08x}",
            "cameraDispatchTable": f"0x{CAMERA_DISPATCH_TABLE:08x}",
            "controlHeading": f"0x{CONTROL_HEADING:08x}",
        },
        "savestateWitness": {
            "cameraModeHalfword": camera_mode,
            "headingStep": heading_step,
            "controlHeading": control_heading,
            "activeCameraObject": f"0x{active_camera:08x}",
            "activeCameraType": camera_type,
            "activeCameraState": f"0x{camera_state:08x}",
            "dispatchEntry": f"0x{dispatch_entry:08x}",
            "initCallback": f"0x{init_callback:08x}",
            "updateCallback": f"0x{update_callback:08x}",
            "cameraProfile": f"0x{camera_profile:08x}",
            "objectEye": object_eye,
            "globalEye": global_eye,
            "maxObjectGlobalEyeError": max(abs(left - right) for left, right in zip(object_eye, global_eye)),
        },
        "directionalHeadingStepBranch": {
            "routine": "0x001f3aa0..0x001f3f7c",
            "directionFlags": "I+0x1a0",
            "stepField": "cameraState+0x80",
            "directionMask": "0x0000a000",
            "negativeDirectionBit": "0x00008000",
            "stepIncrementRad": struct.unpack("<f", struct.pack("<I", 0x3B03126F))[0],
            "stepClampAbsRad": struct.unpack("<f", struct.pack("<I", 0x3D23D70A))[0],
            "releaseDivisor": 1.5,
            "headingUpdate": "WrapPi(controlHeading - stepField)",
            "wrapDifferenceHelper": "0x00200130",
        },
        "ordinaryChaseFollowBranch": {
            "producerRoutine": "0x001eca70..0x001ecde8",
            "dampedStepHelper": "0x001eb240..0x001eb320",
            "filteredTarget": "cameraControlBasis+0x190",
            "rawPlayerCopy": "cameraControlBasis+0x1f0",
            "ordinaryXY": "filtered X/Y copy player X/Y directly",
            "verticalAcceleration": CHASE_Z_ACCEL,
            "verticalDamping": CHASE_Z_DAMPING,
            "verticalStep": "v += accel * (target - current) - damping * v; clamp v to remaining delta; current += v",
            "modeBranch": "state+0x2284 == 0x50 and state+0x2084 != 0x11 takes an alternate follow path",
        },
        "ordinaryChaseFramingProducer": {
            "mainUpdateRoutine": "0x001ed0a8..0x001ed2cc",
            "cameraDispatcher": "0x001eba20..0x001ebb30",
            "cameraType": camera_type,
            "type0InitCallback": "0x002e6d60",
            "type0UpdateCallback": "0x002e9c28",
            "corePipeline": "0x002e9bb0",
            "framingRoutine": "0x002e9720..0x002e9a9c",
            "profileTransitionRoutine": "0x002e5e38..0x002e5ffc",
            "finalHeightRoutine": "0x002e9010..0x002e944c",
            "linearDampedStepHelper": "0x001eb240..0x001eb320",
            "wrappedAngularStepHelper": "0x001eb328..0x001eb440",
            "vectorAddHelper": "0x001ff278",
            "persistentState": f"0x{camera_state:08x}",
            "stateWitness": {
                "currentLookHeight": f32(camera_state + 0x24),
                "currentEyeHeight": f32(camera_state + 0x28),
                "lookHeightVelocity": f32(camera_state + 0x2C),
                "eyeHeightVelocity": f32(camera_state + 0x30),
                "eyeAnchor": eye_anchor,
                "radialOffset": radial_offset,
                "radialOffsetMagnitude": radial_magnitude,
                "radialVelocity": f32(camera_state + 0x158),
                "preferredRadius": f32(camera_state + 0x15C),
                "preferredEyeHeight": f32(camera_state + 0x160),
                "radiusOverrideFlag": struct.unpack_from("<H", memory, camera_state + 0x16C)[0],
                "eyeHeightOverrideFlag": struct.unpack_from("<H", memory, camera_state + 0x16E)[0],
                "preferredRadiusVelocity": f32(camera_state + 0x174),
                "radiusTransitionAcceleration": f32(camera_state + 0x178),
                "preferredEyeHeightVelocity": f32(camera_state + 0x180),
                "eyeHeightTransitionAcceleration": f32(camera_state + 0x184),
                "constructorRadiusReference": f32(camera_state + 0x188),
                "unobstructedRadialCorrection": f32(camera_state + 0x200),
                "profileRadiusWitness": f32(camera_profile + 0x15C),
                "profileEyeHeightWitness": f32(camera_profile + 0x160),
            },
            "geometryWitness": {
                "player": player,
                "eyeAnchorPlusRadialOffset": composed_eye,
                "maxComposedGlobalEyeError": max(abs(left - right) for left, right in zip(composed_eye, global_eye)),
                "preferredRadiusMinusOffsetMagnitude": f32(camera_state + 0x15C) - radial_magnitude,
            },
            "constructorDefaults": {
                "radius": CHASE_CONSTRUCTOR_RADIUS,
                "eyeHeight": CHASE_CONSTRUCTOR_EYE_HEIGHT,
                "profileTransitionAcceleration": CHASE_PROFILE_TRANSITION_ACCEL,
            },
            "finalHeightFollow": {
                "acceleration": CHASE_EYE_HEIGHT_ACCEL,
                "damping": CHASE_EYE_HEIGHT_DAMPING,
                "eyeHeightCurrent": "state+0x28",
                "eyeHeightTarget": "state+0x160",
                "eyeHeightVelocity": "state+0x30",
                "lookHeightCurrent": "state+0x24",
                "lookHeightVelocity": "state+0x2c",
            },
            "radialFollow": [
                "0x002e5e38 damps state+0x15c preferred radius toward a profile/override source using state+0x178 acceleration and state+0x174 velocity",
                "0x002e9720 damps the magnitude of state+0x140 toward state+0x15c minus state+0x200, using coefficients selected by 0x002e9518 and state+0x158 velocity",
                "0x002e9a40..0x002e9a4c composes camera object +0x30 from the resolved anchor plus state+0x140; the main update then publishes object +0x30 to global eye 0x00166dc0",
            ],
        },
        "cameraObstructionProducer": {
            "routine": "0x002e7d20..0x002e8368",
            "pipelineOrder": "type-0 core 0x002e9bb0 calls obstruction 0x002e7d20 before radial follow 0x002e9720",
            "persistentState": {
                "radialCorrection": "state+0x200",
                "releaseTimer": "state+0x204 signed16",
                "lateralSide": "state+0x208 signed16",
                "clearWitnessCorrection": f32(camera_state + 0x200),
                "clearWitnessReleaseTimer": struct.unpack_from("<h", memory, camera_state + 0x204)[0],
                "clearWitnessLateralSide": struct.unpack_from("<h", memory, camera_state + 0x208)[0],
            },
            "radialPullIn": {
                "stepPerCameraUpdate": OBSTRUCTION_PULL_IN_STEP,
                "frameScaleAddress": "0x0015ed60",
                "frameScaleWitness": f32(0x0015ED60),
                "activeContactTimerTicksAtUnitScale": OBSTRUCTION_PULL_IN_TIMER_TICKS,
                "innerSolverRadiusFloor": OBSTRUCTION_INNER_RADIUS_FLOOR,
                "finalEffectiveRadiusFloor": OBSTRUCTION_FINAL_RADIUS_FLOOR,
                "minimumRadiusTimerTicksAtUnitScale": OBSTRUCTION_MIN_RADIUS_TIMER_TICKS,
                "effectiveRadius": "preferredRadius(state+0x15c) - radialCorrection(state+0x200)",
            },
            "clearLineRecovery": {
                "timerHelper": "0x001fef20",
                "timerTickHelper": "0x001fef98",
                "easingHelper": "0x00257e58",
                "cosineHelper": "0x001ff7e8",
                "durationTicksAtUnitScale": OBSTRUCTION_MIN_RADIUS_TIMER_TICKS,
                "step": "decrement releaseTimer; t=releaseTimer/duration; correction=CosineEase(0, correction, t)",
                "cosineEase": "a + (b-a) * 0.5 * (1 - cos(pi*t))",
            },
            "lateralCorrection": {
                "sideField": "state+0x208 signed16",
                "positiveDotThreshold": OBSTRUCTION_SIDE_THRESHOLD,
                "negativeDotThreshold": -OBSTRUCTION_SIDE_THRESHOLD,
                "sideEncoding": "+1 above +0.75, -1 below -0.75, otherwise 0",
                "angularStepRad": OBSTRUCTION_LATERAL_STEP_RAD,
                "angularStepDegrees": 1.0,
                "rotationHelper": "0x0025fc08",
            },
            "contactGeometry": {
                "finalQuery": "0x001efc70",
                "visibleCallShape": "two position pointers plus query context and owner/exclusion object; no explicit scalar camera radius argument",
                "broadPhase": "0x00259a88/0x00259888 paths build nearby/contact candidate records before the final query",
                "boundary": "the call site is segment-like, but 0x001efc70 internals are not decoded enough to claim an infinitesimal ray versus swept/expanded geometry",
            },
            "gameplayCollisionSeparation": [
                "camera obstruction is a dedicated stage in the camera type-0 pipeline, not the player movement/contact controller",
                "the routine temporarily clears Moby +0x94 on selected candidates, retains them in a scratch list, and restores the field before return",
                "the hard-coded class exceptions to that temporary masking are 0x72, 0x0b, 0x2c, 0x9a, 0xff, 0x392, 0x452 and 0x353",
            ],
        },
        "uncertainty": [
            "This branch explains direction-flag-driven heading change, but it is not the complete manual-camera producer.",
            "Live right-stick input changes controlHeading while I+0x1a0 remains on the left-stick forward flag, so right-stick production occurs elsewhere.",
            "The constructor/profile radius witness is 4.64 while this authority snapshot has a settled preferred radius near 6.0; do not promote 4.64 as the ordinary chase distance without resolving the intervening profile/mode source.",
            "No dedicated physical wall-contact savestate is retained here, so exact helper-internal ray/sweep geometry and full lateral convergence remain intentionally unpromoted.",
        ],
    }


def scenario_specs() -> list[dict[str, object]]:
    neutral = [127, 127]
    forward = [127, 0]
    right = [255, 127]
    return [
        {
            "id": "fixed-heading",
            "purpose": "stationary no-input baseline for heading/camera stability",
            "segments": [{"label": "baseline", "frames": 120, "left": neutral}],
        },
        {
            "id": "moving",
            "purpose": "steady forward locomotion with neutral right stick",
            "segments": [
                {"label": "settle", "frames": 20, "left": neutral},
                {"label": "move", "frames": 180, "left": forward},
            ],
        },
        {
            "id": "turning",
            "purpose": "player-facing turn without deliberate right-stick input",
            "segments": [
                {"label": "prelude", "frames": 50, "left": forward},
                {"label": "turn", "frames": 120, "left": right},
            ],
        },
        {
            "id": "idle",
            "purpose": "long neutral chase/follow drift witness",
            "segments": [{"label": "idle", "frames": 240, "left": neutral}],
        },
        {
            "id": "manual-idle-left",
            "purpose": "stationary horizontal right-stick-left response and release tail",
            "segments": [
                {"label": "settle", "frames": 30, "left": neutral},
                {"label": "right-stick-left", "frames": 36, "left": neutral, "right": [0, 127]},
                {"label": "release", "frames": 100, "left": neutral},
            ],
        },
        {
            "id": "manual-idle-right",
            "purpose": "stationary horizontal right-stick-right response and release tail",
            "segments": [
                {"label": "settle", "frames": 30, "left": neutral},
                {"label": "right-stick-right", "frames": 36, "left": neutral, "right": [255, 127]},
                {"label": "release", "frames": 100, "left": neutral},
            ],
        },
        {
            "id": "recenter",
            "purpose": "forward movement, horizontal right-stick pulse, then recenter",
            "segments": [
                {"label": "prelude", "frames": 40, "left": forward},
                {"label": "right-stick-left", "frames": 36, "left": forward, "right": [0, 127]},
                {"label": "recenter", "frames": 150, "left": forward},
            ],
        },
        {
            "id": "turn-release",
            "purpose": "neutral-right-stick chase after a deliberate player turn",
            "segments": [
                {"label": "prelude", "frames": 40, "left": forward},
                {"label": "turn", "frames": 60, "left": right},
                {"label": "follow", "frames": 120, "left": forward},
            ],
        },
        {
            "id": "obstruction",
            "purpose": "forward approach from a dedicated obstruction anchor",
            "requiresObstructionState": True,
            "segments": [
                {"label": "settle", "frames": 20, "left": neutral},
                {"label": "approach", "frames": 180, "left": forward},
                {"label": "idle-after-contact", "frames": 80, "left": neutral},
            ],
        },
    ]


def plan_for(spec: dict[str, object]) -> dict[str, object]:
    return {"schema": 1, "segments": spec["segments"]}


def parse_address(value: object) -> int:
    if isinstance(value, int):
        result = value
    elif isinstance(value, str):
        result = int(value, 0)
    else:
        raise ValueError(f"address must be int or string, got {value!r}")
    if result < 0 or result > 0x01FFFFFF:
        raise ValueError(f"EE address outside ordinary RAM: 0x{result:08x}")
    if result & 3:
        raise ValueError(f"field address must be 4-byte aligned: 0x{result:08x}")
    return result


def load_field_map(path: Path | None) -> tuple[dict[str, tuple[int, str]], list[tuple[int, int]]]:
    fields: dict[str, tuple[int, str]] = {}
    ranges = [(DEFAULT_CANDIDATE_START, DEFAULT_CANDIDATE_BYTES)]
    if path is None:
        return fields, ranges

    raw = json.loads(path.read_text(encoding="utf-8"))
    if raw.get("schema") != 1:
        raise ValueError("camera field map must use schema 1")
    for name, spec in raw.get("fields", {}).items():
        kind = str(spec.get("kind", "f32"))
        if kind not in ("f32", "u32"):
            raise ValueError(f"unsupported field kind for {name}: {kind}")
        if name in KNOWN_FIELDS:
            raise ValueError(f"field map cannot override known field {name}")
        fields[str(name)] = (parse_address(spec["address"]), kind)

    if "candidateRanges" in raw:
        ranges = []
        for item in raw["candidateRanges"]:
            start = parse_address(item["start"])
            size = int(item["bytes"])
            if size <= 0 or size % 4:
                raise ValueError("candidate range bytes must be positive and 4-byte aligned")
            ranges.append((start, size))
    return fields, ranges


def candidate_addresses(ranges: list[tuple[int, int]]) -> list[int]:
    result: list[int] = []
    seen: set[int] = set()
    for start, size in ranges:
        for address in range(start, start + size, 4):
            if address not in seen:
                seen.add(address)
                result.append(address)
    return result


def decode(raw: int, kind: str) -> float | int:
    if kind == "u32":
        return raw
    return struct.unpack("<f", struct.pack("<I", raw))[0]


def sample_frame(
    pine: object,
    extra_fields: dict[str, tuple[int, str]],
    ranges: list[tuple[int, int]],
) -> dict[str, object]:
    fields = {**KNOWN_FIELDS, **extra_fields}
    candidates = candidate_addresses(ranges)
    addresses = [address for address, _ in fields.values()] + candidates
    values = pine.read32(addresses)
    sample: dict[str, object] = {}
    for (name, (_, kind)), raw in zip(fields.items(), values[: len(fields)]):
        sample[name] = decode(raw, kind)
    sample["candidate_words"] = {
        f"0x{address:08x}": raw
        for address, raw in zip(candidates, values[len(fields):])
    }
    return sample


def capture_scenario(
    pid: int,
    port: int,
    plan_path: Path,
    movie: Path,
    raw_out: Path,
    extra_fields: dict[str, tuple[int, str]],
    ranges: list[tuple[int, int]],
    settle: float,
    reload_wait: float,
) -> dict[str, object]:
    raw_out = HARNESS.capture_path(raw_out)
    movie = HARNESS.capture_path(movie)
    manifest_path = movie.with_suffix(movie.suffix + ".json")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    plan = HARNESS.load_plan(plan_path)
    frames = HARNESS.expand_plan(plan)
    main = HARNESS.start_movie_replay(pid, movie)
    HARNESS.time.sleep(reload_wait)
    pine = HARNESS.Pine("127.0.0.1", port)
    try:
        initial = sample_frame(pine, extra_fields, ranges)
        samples: list[dict[str, object]] = []
        for frame_index, command in enumerate(frames):
            HARNESS.frame_advance(main, settle)
            observed = sample_frame(pine, extra_fields, ranges)
            samples.append({
                "frame": frame_index,
                "segment": command["label"],
                "segment_index": command["segment_index"],
                "local_frame": command["local_frame"],
                "left": list(command["left"]),
                "right": list(command["right"]),
                "buttons": list(command["buttons"]),
                "sample": observed,
            })
    finally:
        pine.close()

    capture = {
        "schema": 1,
        "authority": "R&C1 NTSC-U SCUS-97199 / fixed savestate + input recording + PINE",
        "movieSha256": manifest["movieSha256"],
        "stateSha256": manifest.get("stateSha256"),
        "fieldMap": {
            name: {"address": f"0x{address:08x}", "kind": kind}
            for name, (address, kind) in extra_fields.items()
        },
        "candidateRanges": [
            {"start": f"0x{start:08x}", "bytes": size}
            for start, size in ranges
        ],
        "initial": initial,
        "samples": samples,
    }
    raw_out.parent.mkdir(parents=True, exist_ok=True)
    raw_out.write_text(json.dumps(capture, indent=2) + "\n", encoding="utf-8")
    return capture


def normalized_segments(plan: list[dict[str, object]]) -> list[dict[str, object]]:
    return [
        {
            "label": segment["label"],
            "frames": segment["frames"],
            "left": list(segment["left"]),
            "right": list(segment["right"]),
            "buttons": list(segment["buttons"]),
        }
        for segment in plan
    ]


def existing_capture_is_current(
    raw_path: Path,
    movie: Path,
    savestate: Path,
    plan: list[dict[str, object]],
    extra_fields: dict[str, tuple[int, str]],
    ranges: list[tuple[int, int]],
) -> bool:
    manifest_path = movie.with_suffix(movie.suffix + ".json")
    if not raw_path.exists() or not movie.exists() or not manifest_path.exists():
        return False
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        raw = json.loads(raw_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return False
    expected_field_map = {
        name: {"address": f"0x{address:08x}", "kind": kind}
        for name, (address, kind) in extra_fields.items()
    }
    expected_ranges = [
        {"start": f"0x{start:08x}", "bytes": size}
        for start, size in ranges
    ]
    return (
        manifest.get("stateSha256") == HARNESS.sha256(savestate)
        and raw.get("stateSha256") == manifest.get("stateSha256")
        and raw.get("movieSha256") == manifest.get("movieSha256")
        and manifest.get("segments") == normalized_segments(plan)
        and raw.get("fieldMap") == expected_field_map
        and raw.get("candidateRanges") == expected_ranges
    )


def capture_suite(
    pid: int,
    port: int,
    savestate: Path,
    obstruction_savestate: Path | None,
    out_dir: Path,
    field_map_path: Path | None,
    settle: float,
    reload_wait: float,
) -> None:
    out_dir = HARNESS.capture_path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    extra_fields, ranges = load_field_map(field_map_path)
    specs = scenario_specs()

    for index, spec in enumerate(specs, start=1):
        state = obstruction_savestate if spec.get("requiresObstructionState") else savestate
        if state is None:
            print(json.dumps({
                "scenario": spec["id"],
                "index": index,
                "total": len(specs),
                "status": "skipped-no-obstruction-savestate",
            }), flush=True)
            continue
        trial = out_dir / str(spec["id"])
        trial.mkdir(exist_ok=True)
        plan_path = trial / "plan.json"
        plan_path.write_text(json.dumps(plan_for(spec), indent=2) + "\n", encoding="utf-8")
        movie = trial / "trial.p2m2"
        raw = trial / "raw.json"
        plan = HARNESS.load_plan(plan_path)
        if existing_capture_is_current(raw, movie, state, plan, extra_fields, ranges):
            print(json.dumps({
                "scenario": spec["id"],
                "index": index,
                "total": len(specs),
                "status": "existing-capture",
            }), flush=True)
            continue
        HARNESS.build_movie(plan_path, state, movie)
        capture = capture_scenario(
            pid,
            port,
            plan_path,
            movie,
            raw,
            extra_fields,
            ranges,
            settle,
            reload_wait,
        )
        print(json.dumps({
            "scenario": spec["id"],
            "index": index,
            "total": len(specs),
            "samples": len(capture["samples"]),
            "stateSha256": capture["stateSha256"],
        }), flush=True)


def numeric_summary(values: list[float | int]) -> dict[str, object]:
    numeric = [float(value) for value in values]
    changes = sum(a != b for a, b in zip(values, values[1:]))
    return {
        "start": numeric[0],
        "end": numeric[-1],
        "min": min(numeric),
        "max": max(numeric),
        "range": max(numeric) - min(numeric),
        "changes": changes,
    }


def angular_summary(values: list[float | int]) -> dict[str, object]:
    wrapped = [float(value) for value in values]
    unwrapped = [wrapped[0]]
    for value in wrapped[1:]:
        unwrapped.append(unwrapped[-1] + wrap_pi(value - unwrapped[-1]))
    changes = sum(a != b for a, b in zip(wrapped, wrapped[1:]))
    return {
        "start": wrapped[0],
        "end": wrapped[-1],
        "delta": unwrapped[-1] - unwrapped[0],
        "unwrappedRange": max(unwrapped) - min(unwrapped),
        "changes": changes,
    }


def discrete_path(values: list[int]) -> list[str]:
    result: list[str] = []
    for value in values:
        rendered = f"0x{int(value):08x}"
        if not result or result[-1] != rendered:
            result.append(rendered)
    return result


def movement_target_error(rows: list[dict[str, object]]) -> dict[str, object]:
    same_frame_errors: list[float] = []
    previous_heading_errors: list[float] = []
    for index, row in enumerate(rows):
        sample = row["sample"]
        x = float(sample["left_conditioned_x"])
        y = float(sample["left_conditioned_y"])
        if math.hypot(x, y) < 0.25:
            continue
        stick_term = math.atan2(-x, -y)
        target = float(sample["player_target_yaw"])
        expected = wrap_pi(float(sample["control_heading"]) + stick_term)
        same_frame_errors.append(abs(wrap_pi(target - expected)))
        if index:
            previous_heading = float(rows[index - 1]["sample"]["control_heading"])
            expected_previous = wrap_pi(previous_heading + stick_term)
            previous_heading_errors.append(abs(wrap_pi(target - expected_previous)))

    previous_sorted = sorted(previous_heading_errors)
    p95_index = min(len(previous_sorted) - 1, int(len(previous_sorted) * 0.95)) if previous_sorted else 0
    return {
        "activeSamples": len(same_frame_errors),
        "maxAbsErrorRad": max(same_frame_errors) if same_frame_errors else None,
        "previousHeadingSamples": len(previous_heading_errors),
        "previousHeadingMaxAbsErrorRad": max(previous_heading_errors) if previous_heading_errors else None,
        "previousHeadingP95AbsErrorRad": previous_sorted[p95_index] if previous_sorted else None,
        "previousHeadingSamplesWithin1eMinus5Rad": sum(
            error <= 1e-5 for error in previous_heading_errors
        ),
    }


def camera_framing_summary(rows: list[dict[str, object]]) -> dict[str, object] | None:
    required = {
        "control_heading", "player_pos_x", "player_pos_y", "player_pos_z",
        "camera_position_x", "camera_position_y", "camera_position_z", "camera_pitch",
        "camera_forward_x", "camera_forward_y", "camera_forward_z",
        "camera_follow_x", "camera_follow_y", "camera_follow_z",
        "camera_follow_raw_z", "camera_follow_z_velocity",
        "camera_raw_player_x", "camera_raw_player_y", "camera_raw_player_z",
    }
    if not rows or not required.issubset(rows[0]["sample"]):
        return None

    distances: list[float] = []
    eye_heights: list[float] = []
    yaw_errors: list[float] = []
    forward_errors: list[float] = []
    pitch_values: list[float] = []
    look_height_over_filtered: list[float] = []
    follow_planar_errors: list[float] = []
    raw_player_errors: list[float] = []
    z_lag: list[float] = []
    camera_steps: list[float] = []
    vertical_law_errors: list[float] = []

    for index, row in enumerate(rows):
        sample = row["sample"]
        heading = float(sample["control_heading"])
        pitch = float(sample["camera_pitch"])
        dx = float(sample["player_pos_x"]) - float(sample["camera_position_x"])
        dy = float(sample["player_pos_y"]) - float(sample["camera_position_y"])
        distance = math.hypot(dx, dy)
        distances.append(distance)
        eye_heights.append(float(sample["camera_position_z"]) - float(sample["player_pos_z"]))
        yaw_errors.append(abs(wrap_pi(math.atan2(dy, dx) - heading)))
        pitch_values.append(pitch)

        cos_pitch = math.cos(pitch)
        expected_forward = (
            math.cos(heading) * cos_pitch,
            math.sin(heading) * cos_pitch,
            -math.sin(pitch),
        )
        actual_forward = tuple(float(sample[f"camera_forward_{axis}"]) for axis in "xyz")
        forward_errors.append(max(abs(a - b) for a, b in zip(actual_forward, expected_forward)))
        planar_forward = math.hypot(actual_forward[0], actual_forward[1])
        if planar_forward:
            look_z = float(sample["camera_position_z"]) + distance * actual_forward[2] / planar_forward
            look_height_over_filtered.append(look_z - float(sample["camera_follow_z"]))

        follow_planar_errors.append(math.hypot(
            float(sample["camera_follow_x"]) - float(sample["player_pos_x"]),
            float(sample["camera_follow_y"]) - float(sample["player_pos_y"]),
        ))
        raw_player_errors.append(math.sqrt(sum(
            (float(sample[f"camera_raw_player_{axis}"]) - float(sample[f"player_pos_{axis}"])) ** 2
            for axis in "xyz"
        )))
        z_lag.append(float(sample["camera_follow_raw_z"]) - float(sample["camera_follow_z"]))

        if index:
            previous = rows[index - 1]["sample"]
            camera_steps.append(math.hypot(
                float(sample["camera_position_x"]) - float(previous["camera_position_x"]),
                float(sample["camera_position_y"]) - float(previous["camera_position_y"]),
            ))
            previous_z = float(previous["camera_follow_z"])
            previous_velocity = float(previous["camera_follow_z_velocity"])
            target_z = float(sample["camera_follow_raw_z"])
            delta = target_z - previous_z
            velocity = previous_velocity + CHASE_Z_ACCEL * delta - CHASE_Z_DAMPING * previous_velocity
            if abs(velocity) > abs(delta):
                velocity = math.copysign(abs(delta), velocity)
            vertical_law_errors.append(abs(previous_z + velocity - float(sample["camera_follow_z"])))

    return {
        "planarDistance": numeric_summary(distances),
        "eyeHeightAbovePlayer": numeric_summary(eye_heights),
        "pitchRad": numeric_summary(pitch_values),
        "maxEyeRayYawErrorRad": max(yaw_errors),
        "maxForwardBasisComponentError": max(forward_errors),
        "lookHeightAboveFilteredTarget": numeric_summary(look_height_over_filtered),
        "maxFollowPlanarErrorToPlayer": max(follow_planar_errors),
        "maxRawPlayerCopyError": max(raw_player_errors),
        "filteredZLag": numeric_summary(z_lag),
        "planarCameraStep": numeric_summary(camera_steps) if camera_steps else None,
        "verticalFollowLaw": {
            "acceleration": CHASE_Z_ACCEL,
            "damping": CHASE_Z_DAMPING,
            "samples": len(vertical_law_errors),
            "maxAbsResidual": max(vertical_law_errors) if vertical_law_errors else None,
        },
    }


def segment_summary(rows: list[dict[str, object]]) -> dict[str, object]:
    heading_minus_yaw = [
        wrap_pi(float(row["sample"]["control_heading"]) - float(row["sample"]["player_yaw"]))
        for row in rows
    ]
    return {
        "label": rows[0]["segment"],
        "frames": len(rows),
        "left": list(rows[0]["left"]),
        "right": list(rows[0]["right"]),
        "controlHeading": angular_summary([row["sample"]["control_heading"] for row in rows]),
        "playerYaw": angular_summary([row["sample"]["player_yaw"] for row in rows]),
        "headingMinusPlayerYaw": angular_summary(heading_minus_yaw),
        "inputDirectionFlagPath": discrete_path([
            int(row["sample"]["input_direction_flags"]) for row in rows
        ]),
        "targetHeadingRelation": movement_target_error(rows),
        "cameraFraming": camera_framing_summary(rows),
    }


def candidate_summary(rows: list[dict[str, object]]) -> list[dict[str, object]]:
    if not rows:
        return []
    keys = list(rows[0]["sample"]["candidate_words"].keys())
    report: list[dict[str, object]] = []
    for key in keys:
        words = [int(row["sample"]["candidate_words"][key]) for row in rows]
        changes = sum(a != b for a, b in zip(words, words[1:]))
        distinct = len(set(words))
        floats = [float(decode(word, "f32")) for word in words]
        finite = [value for value in floats if math.isfinite(value) and abs(value) < 1e8]
        item: dict[str, object] = {
            "address": key,
            "distinctWords": distinct,
            "changes": changes,
            "finitePlausibleF32Samples": len(finite),
        }
        if finite:
            item["f32Min"] = min(finite)
            item["f32Max"] = max(finite)
            item["f32Range"] = max(finite) - min(finite)
        report.append(item)
    return report


def derive_scenario(
    scenario_id: str,
    capture: dict[str, object],
    include_candidates: bool = True,
) -> dict[str, object]:
    rows = list(capture["samples"])
    if not rows:
        raise ValueError(f"{scenario_id} capture contains no samples")
    present = rows[0]["sample"]
    field_names = [
        name for name in [*KNOWN_FIELDS.keys(), *capture.get("fieldMap", {}).keys()]
        if name in present
    ]
    fields = {
        name: numeric_summary([row["sample"][name] for row in rows])
        for name in field_names
    }
    right_commands: list[list[int]] = []
    segment_labels: list[str] = []
    for row in rows:
        right = list(row["right"])
        if not right_commands or right_commands[-1] != right:
            right_commands.append(right)
        label = str(row["segment"])
        if not segment_labels or segment_labels[-1] != label:
            segment_labels.append(label)

    segments = [
        segment_summary([row for row in rows if row["segment"] == label])
        for label in segment_labels
    ]
    basis_sin_error = max(
        abs(float(row["sample"]["camera_heading_sin"]) - math.sin(float(row["sample"]["control_heading"])))
        for row in rows
    )
    basis_cos_error = max(
        abs(float(row["sample"]["camera_heading_cos"]) - math.cos(float(row["sample"]["control_heading"])))
        for row in rows
    )

    player_start = [fields[f"player_pos_{axis}"]["start"] for axis in "xyz"]
    player_end = [fields[f"player_pos_{axis}"]["end"] for axis in "xyz"]
    planar_delta = math.hypot(
        player_end[0] - player_start[0],
        player_end[1] - player_start[1],
    )
    return {
        "id": scenario_id,
        "samples": len(rows),
        "stateSha256": capture.get("stateSha256"),
        "movieSha256": capture.get("movieSha256"),
        "rightStickCommandPath": right_commands,
        "conditionedRightStick": {
            "x": fields["right_conditioned_x"],
            "y": fields["right_conditioned_y"],
        },
        "conditionedLeftStick": {
            "x": fields["left_conditioned_x"],
            "y": fields["left_conditioned_y"],
        },
        "inputDirectionFlagPath": discrete_path([
            int(row["sample"]["input_direction_flags"]) for row in rows
        ]),
        "controlHeading": fields["control_heading"],
        "controlHeadingAngular": angular_summary([
            row["sample"]["control_heading"] for row in rows
        ]),
        "playerYaw": fields["player_yaw"],
        "playerTargetYaw": fields["player_target_yaw"],
        "movementTargetRelation": movement_target_error(rows),
        "cameraHeadingBasis": {
            "sinAddress": f"0x{CONTROL_BASIS_BASE:08x}",
            "cosAddress": f"0x{CONTROL_BASIS_BASE + 0x364:08x}",
            "maxSinError": basis_sin_error,
            "maxCosError": basis_cos_error,
        },
        "segments": segments,
        "playerPosition": {
            "start": player_start,
            "end": player_end,
            "netPlanarDelta": planar_delta,
            "zRange": fields["player_pos_z"]["range"],
        },
        "cameraFields": {
            name: fields[name]
            for name in field_names
            if name.startswith("camera_")
        },
        "cameraFraming": camera_framing_summary(rows),
        "candidateFields": candidate_summary(rows) if include_candidates else [],
    }


def derive_suite(capture_dir: Path, include_candidates: bool = True) -> dict[str, object]:
    scenarios: list[dict[str, object]] = []
    missing: list[str] = []
    for spec in scenario_specs():
        raw_path = capture_dir / str(spec["id"]) / "raw.json"
        if not raw_path.exists():
            missing.append(str(spec["id"]))
            continue
        raw = json.loads(raw_path.read_text(encoding="utf-8"))
        scenarios.append(derive_scenario(
            str(spec["id"]),
            raw,
            include_candidates=include_candidates,
        ))

    by_address: dict[str, dict[str, int]] = {}
    for scenario in scenarios:
        for field in scenario["candidateFields"]:
            by_address.setdefault(field["address"], {})[scenario["id"]] = int(field["changes"])
    interesting = [
        {"address": address, "changesByScenario": changes}
        for address, changes in by_address.items()
        if changes.get("recenter", 0) > changes.get("idle", 0)
        or changes.get("turning", 0) > changes.get("fixed-heading", 0)
        or changes.get("obstruction", 0) > changes.get("idle", 0)
    ]
    interesting.sort(
        key=lambda item: (-sum(item["changesByScenario"].values()), item["address"])
    )
    return {
        "schema": 1,
        "authority": "R&C1 NTSC-U SCUS-97199 / fixed savestate + input recording + PINE",
        "sampleCadence": "one batched PINE sample after each PCSX2 FrameAdvance(1)",
        "knownFields": {
            name: {"address": f"0x{address:08x}", "kind": kind}
            for name, (address, kind) in KNOWN_FIELDS.items()
        },
        "scenarios": scenarios,
        "missingScenarios": missing,
        "candidateReduction": "included" if include_candidates else "omitted",
        "candidateShortlist": interesting,
        "evidenceBoundary": [
            "rightStickCommandPath is literal DS2 movie input; conditionedRightStick is native I+0x100/+0x104 state",
            "controlHeading is the proven ordinary target-construction source at 0x00166dd8",
            "0x00166c80/0x00166fe4 are retained horizontal basis witnesses because live values match sin/cos(controlHeading)",
            "movementTargetRelation checks P+0x100 against controlHeading plus the conditioned left-stick term",
            "the direction-flag heading-step branch is statically recovered, but the exact right-stick producer feeding camera orientation remains unresolved",
            "cameraFields contains promoted native camera fields plus independently supplied field-map additions; the default candidate span is not otherwise semantically labeled",
            "raw words and per-frame captures remain under ignored captures/; this report retains only derived summaries",
        ],
    }


def derive_chase_suite(capture_dir: Path) -> dict[str, object]:
    scenarios: list[dict[str, object]] = []
    missing: list[str] = []
    for scenario_id in CHASE_SCENARIO_IDS:
        raw_path = capture_dir / scenario_id / "raw.json"
        if not raw_path.exists():
            missing.append(scenario_id)
            continue
        raw = json.loads(raw_path.read_text(encoding="utf-8"))
        derived = derive_scenario(scenario_id, raw, include_candidates=False)
        scenarios.append({
            "id": derived["id"],
            "samples": derived["samples"],
            "stateSha256": derived["stateSha256"],
            "movieSha256": derived["movieSha256"],
            "controlHeadingAngular": derived["controlHeadingAngular"],
            "playerPosition": derived["playerPosition"],
            "cameraFraming": derived["cameraFraming"],
            "segments": [{
                "label": segment["label"],
                "frames": segment["frames"],
                "left": segment["left"],
                "right": segment["right"],
                "controlHeading": segment["controlHeading"],
                "cameraFraming": segment["cameraFraming"],
            } for segment in derived["segments"]],
        })
    return {
        "schema": 1,
        "authority": "R&C1 NTSC-U SCUS-97199 / fixed savestate + input recording + PINE",
        "scope": "ordinary unobstructed chase framing/follow only; manual right-stick and obstruction behavior excluded",
        "sampleCadence": "one batched PINE sample after each PCSX2 FrameAdvance(1)",
        "scenarios": scenarios,
        "missingScenarios": missing,
        "nativeFollowContract": {
            "producerRoutine": "0x001eca70..0x001ecde8",
            "dampedStepHelper": "0x001eb240..0x001eb320",
            "verticalAcceleration": CHASE_Z_ACCEL,
            "verticalDamping": CHASE_Z_DAMPING,
            "ordinaryXY": "filtered target X/Y copy current player X/Y directly",
            "verticalStep": "v += accel * (target - current) - damping * v; clamp v to remaining delta; current += v",
            "alternateModeGate": "state+0x2284 == 0x50 and state+0x2084 != 0x11",
        },
        "evidenceBoundary": [
            "stationary ordinary framing settles near six planar units behind Ratchet with pitch near 0.084 rad",
            "the eye-to-Ratchet planar ray and forward basis use controlHeading; no separate yaw-lagged eye ray was observed",
            "movement changes radial distance and pitch, so stationary distance/pitch are not global invariant constants",
            "vertical follow is executable-backed and dynamically validated; the horizontal type-0 radial producer is traced through its two-stage stateful smoother and final eye composition",
            "the profile/mode source that moves preferred radius away from the 4.64 constructor/profile witness, plus the GP-relative preferred-radius damping, remain evidence-gated",
            "raw movies and per-frame samples remain below ignored captures/",
        ],
    }


def write_chase_derived(capture_dir: Path, out: Path) -> dict[str, object]:
    report = derive_chase_suite(capture_dir)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return report


def write_derived(
    capture_dir: Path,
    out: Path,
    include_candidates: bool = True,
) -> dict[str, object]:
    report = derive_suite(capture_dir, include_candidates=include_candidates)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return report


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    scenarios = sub.add_parser("scenarios")
    scenarios.add_argument("--out", type=Path)

    capture = sub.add_parser("capture")
    capture.add_argument("--pid", type=int, required=True)
    capture.add_argument("--pine-port", type=int, default=28099)
    capture.add_argument("--savestate", type=Path, required=True)
    capture.add_argument("--obstruction-savestate", type=Path)
    capture.add_argument("--out-dir", type=Path, required=True)
    capture.add_argument("--field-map", type=Path)
    capture.add_argument("--settle", type=float, default=0.05)
    capture.add_argument("--reload-wait", type=float, default=1.0)

    derive = sub.add_parser("derive")
    derive.add_argument("--capture-dir", type=Path, required=True)
    derive.add_argument("--out", type=Path, required=True)
    derive.add_argument("--semantic-only", action="store_true")

    chase = sub.add_parser("derive-chase")
    chase.add_argument("--capture-dir", type=Path, required=True)
    chase.add_argument("--out", type=Path, required=True)

    probe = sub.add_parser("probe-producer")
    probe.add_argument("--savestate", type=Path, required=True)
    probe.add_argument("--zstd-dll", type=Path, required=True)
    probe.add_argument("--out", type=Path)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    if args.command == "scenarios":
        result = {"schema": 1, "scenarios": scenario_specs()}
        rendered = json.dumps(result, indent=2) + "\n"
        if args.out:
            args.out.parent.mkdir(parents=True, exist_ok=True)
            args.out.write_text(rendered, encoding="utf-8")
        else:
            print(rendered, end="")
        return 0

    if args.command == "capture":
        capture_suite(
            args.pid,
            args.pine_port,
            args.savestate,
            args.obstruction_savestate,
            args.out_dir,
            args.field_map,
            args.settle,
            args.reload_wait,
        )
        return 0

    if args.command == "derive-chase":
        report = write_chase_derived(args.capture_dir, args.out)
        print(json.dumps({
            "out": str(args.out),
            "scenarios": len(report["scenarios"]),
            "missingScenarios": report["missingScenarios"],
        }, indent=2))
        return 0

    if args.command == "probe-producer":
        report = probe_camera_producer(args.savestate, args.zstd_dll)
        rendered = json.dumps(report, indent=2) + "\n"
        if args.out:
            args.out.parent.mkdir(parents=True, exist_ok=True)
            args.out.write_text(rendered, encoding="utf-8")
        else:
            print(rendered, end="")
        return 0

    report = write_derived(
        args.capture_dir,
        args.out,
        include_candidates=not args.semantic_only,
    )
    print(json.dumps({
        "out": str(args.out),
        "scenarios": len(report["scenarios"]),
        "missingScenarios": report["missingScenarios"],
        "candidateShortlist": len(report["candidateShortlist"]),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
