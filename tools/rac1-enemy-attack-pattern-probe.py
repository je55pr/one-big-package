#!/usr/bin/env python3
"""Generate payload-free SCUS-97199 enemy attack-pattern evidence.

The retained report deliberately separates proven class-749 outgoing melee
semantics from class-1440's independently recovered damage intake/contact
query.  Raw retail memory and savestate payloads are never emitted.
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import importlib.util
import json
import math
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SAVESTATE_HELPER = ROOT / "rac1-savestate-movement-probe.py"

AUTHORITY = {
    "game": "Ratchet & Clank",
    "build": "rac1-ntscu-original",
    "serial": "SCUS-97199",
    "isoSha256": "ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d",
    "exeSha256": "e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9",
}

LIVE_POOL = 0x01845E80
LIVE_STRIDE = 0x100
LIVE_SCAN_SLOTS = 0x300
LIVE_STATE = 0x20
LIVE_UPDATE = 0x74
LIVE_PVAR = 0x78
LIVE_CLASS = 0xA6
PLAYER_MOBY_GLOBAL = 0x001413D0

CLASS749 = 749
CLASS749_UPDATE = 0x002D4610
CLASS749_ATTACK_EMITTER = 0x002599E8
CLASS749_ATTACK_CALL = 0x002D4CB4
CLASS749_STATE_TABLE = 0x001E9F20
CLASS749_STATE_TARGETS = [
    0x002D46C4, 0x002D4884, 0x002D493C, 0x002D49B8, 0x002D49F0,
    0x002D4A9C, 0x002D4B88, 0x002D4C74, 0x002D4DA0, 0x002D4864,
    0x002D4E08, 0x002D4FCC, 0x002D501C,
]

CLASS1440 = 1440
CLASS1440_UPDATE = 0x002E0B88
CLASS1440_UPDATE_END = 0x002E1DF0
CLASS1440_PVAR_HEALTH = 0x20
CLASS1440_PVAR_TARGET = 0x110
CLASS1440_STATE_TABLE = 0x001EA230
CLASS1440_STATE_TARGETS = [
    0x002E0C84, 0x002E0D6C, 0x002E0E24, 0x002E0FBC, 0x002E110C,
    0x002E11A4, 0x002E1494, 0x002E14F8, 0x002E1544,
]
DAMAGE_CONSTRUCTOR = 0x00259BC8
DAMAGE_LOOKUP = 0x0025A420
DAMAGE_CONSUME = 0x0025A478
VU_QUERY = 0x001EFC70

SIGNATURES = [
    (0x002D4C74, 0x3C014208, "class-749 state 7 materializes native marker 34.0"),
    (0x002D4C7C, 0x0C098636, "class-749 state 7 tests the native animation marker"),
    (0x002D4C8C, 0x3C013F80, "class-749 attack path materializes 1.0"),
    (0x002D4C94, 0x3C013EAA, "class-749 attack volume scalar begins as raw 0x3eaa7efa"),
    (0x002D4CA4, 0x24060001, "class-749 attack emitter argument a2 is 1"),
    (0x002D4CB0, 0x24080001, "class-749 attack emitter argument t0 is 1"),
    (0x002D4CB4, 0x0C09667A, "class-749 state 7 calls attack emitter 0x2599e8"),
    (0x002E0BB8, 0x0C0B859E, "class-1440 update calls its pre-dispatch damage/status helper"),
    (0x002E0C5C, 0x92630020, "class-1440 dispatch reads live Moby state byte +0x20"),
    (0x002E16E8, 0x0C096908, "class-1440 pre-dispatch calls common damage lookup 0x25a420"),
    (0x002E1710, 0x0C09691E, "class-1440 pre-dispatch calls common damage consumer 0x25a478"),
    (0x002E1774, 0xC6200020, "class-1440 loads PVar +0x20 health"),
    (0x002E1784, 0x46060001, "class-1440 subtracts returned native damage from health"),
    (0x002E17BC, 0xE6200020, "class-1440 stores PVar +0x20 health"),
    (0x002E1290, 0x3C020001, "class-1440 state 5 descriptor materializes 0x00010000"),
    (0x002E1294, 0xAFA20084, "class-1440 state 5 stores 0x00010000 at descriptor +0x84"),
    (0x002E129C, 0xAFB30080, "class-1440 state 5 stores source Moby at descriptor +0x80"),
    (0x002E12A4, 0xAFB20090, "class-1440 state 5 stores integer 1 at descriptor +0x90"),
    (0x002E12AC, 0xE7B4008C, "class-1440 state 5 stores 1.0 at descriptor +0x8c"),
    (0x002E12BC, 0x966200A6, "class-1440 state 5 loads source native class from Moby +0xa6"),
    (0x002E12E0, 0xA7A2008A, "class-1440 state 5 stores native class at descriptor +0x8a"),
    (0x002E1348, 0x0C07BF1C, "class-1440 state 5 performs first VU-backed query"),
    (0x002E1364, 0x8C653E58, "class-1440 first query path loads query-owned Moby result"),
    (0x002E1368, 0x8C8213D0, "class-1440 first query path loads current player Moby"),
    (0x002E1374, 0x0C0B8810, "class-1440 player-contact branch calls class-local helper 0x2e2040"),
    (0x002E1388, 0x10820008, "class-1440 player-contact branch checks transition to native state 6"),
    (0x002E13BC, 0x0C07BF1C, "class-1440 state 5 performs second VU-backed query"),
    (0x002E13E0, 0x8C64FFF8, "class-1440 second query path loads the same query-owned Moby result"),
    (0x002E13E4, 0x8C4313D0, "class-1440 second query path loads current player Moby"),
    (0x002E13F0, 0x0C0B8810, "class-1440 second player-contact branch calls class-local helper 0x2e2040"),
    (0x002E1404, 0x10820008, "class-1440 second player-contact branch checks transition to native state 6"),
]


def load_helper():
    spec = importlib.util.spec_from_file_location("rac1_savestate_helper", SAVESTATE_HELPER)
    if spec is None or spec.loader is None:
        raise RuntimeError("could not load savestate helper")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def u8(memory: bytes, address: int) -> int:
    return memory[address]


def u16(memory: bytes, address: int) -> int:
    return int.from_bytes(memory[address:address + 2], "little")


def u32(memory: bytes, address: int) -> int:
    return int.from_bytes(memory[address:address + 4], "little")


def f32(memory: bytes, address: int) -> float:
    return struct.unpack_from("<f", memory, address)[0]


def jal_callers(memory: bytes, target: int, start: int = 0x00100000, end: int = 0x00320000) -> list[int]:
    callers: list[int] = []
    stop = min(end, len(memory))
    for pc in range(start, stop - 4, 4):
        word = u32(memory, pc)
        if word >> 26 != 3:
            continue
        resolved = ((pc + 4) & 0xF0000000) | ((word & 0x03FFFFFF) << 2)
        if resolved == target:
            callers.append(pc)
    return callers


def live_class(memory: bytes, native_class: int) -> list[int]:
    return [
        LIVE_POOL + slot * LIVE_STRIDE
        for slot in range(LIVE_SCAN_SLOTS)
        if u16(memory, LIVE_POOL + slot * LIVE_STRIDE + LIVE_CLASS) == native_class
    ]


def verify_signatures(memory: bytes) -> list[dict[str, object]]:
    result = []
    for address, expected, meaning in SIGNATURES:
        actual = u32(memory, address)
        if actual != expected:
            raise RuntimeError(
                f"instruction drift at 0x{address:08x}: 0x{actual:08x} != 0x{expected:08x}"
            )
        result.append({
            "address": f"0x{address:08x}",
            "word": f"0x{actual:08x}",
            "meaning": meaning,
        })
    return result


def verify_state_table(memory: bytes, base: int, expected: list[int], label: str) -> dict[str, str]:
    actual = [u32(memory, base + index * 4) for index in range(len(expected))]
    if actual != expected:
        raise RuntimeError(f"{label} state table drifted: {[hex(value) for value in actual]}")
    return {str(index): f"0x{target:08x}" for index, target in enumerate(actual)}


def inspect_live_class(memory: bytes, native_class: int, expected_update: int) -> tuple[list[int], dict[str, object]]:
    instances = live_class(memory, native_class)
    if not instances:
        raise RuntimeError(f"no live class {native_class} witnesses")
    update_histogram = collections.Counter(u32(memory, address + LIVE_UPDATE) for address in instances)
    if set(update_histogram) != {expected_update}:
        raise RuntimeError(
            f"class {native_class} update drift: {[hex(value) for value in sorted(update_histogram)]}"
        )
    state_histogram = collections.Counter(u8(memory, address + LIVE_STATE) for address in instances)
    return instances, {
        "count": len(instances),
        "updateRoutine": f"0x{expected_update:08x}",
        "stateHistogram": {f"0x{state:02x}": count for state, count in sorted(state_histogram.items())},
    }


def inspect_class1440(memory: bytes, player_moby: int) -> dict[str, object]:
    instances, common = inspect_live_class(memory, CLASS1440, CLASS1440_UPDATE)
    health_values: list[float] = []
    target_kinds: list[str] = []
    for address in instances:
        pvar = u32(memory, address + LIVE_PVAR)
        if pvar == 0 or pvar + CLASS1440_PVAR_TARGET + 4 > len(memory):
            raise RuntimeError(f"class 1440 live Moby 0x{address:08x} has invalid PVar")
        health = f32(memory, pvar + CLASS1440_PVAR_HEALTH)
        if not math.isfinite(health):
            raise RuntimeError(f"class 1440 non-finite PVar +0x20 at 0x{pvar:08x}")
        health_values.append(health)
        target = u32(memory, pvar + CLASS1440_PVAR_TARGET)
        target_kinds.append("none" if target == 0 else "player" if target == player_moby else "other")
    if set(health_values) != {2.0}:
        raise RuntimeError(f"unexpected class-1440 fixed-witness health values {sorted(set(health_values))}")
    if "other" in target_kinds:
        raise RuntimeError("class-1440 fixed witness has a +0x110 pointer to a non-player Moby")

    update_emitter_calls = [
        pc for pc in jal_callers(memory, CLASS749_ATTACK_EMITTER, CLASS1440_UPDATE, CLASS1440_UPDATE_END)
    ]
    update_constructor_calls = [
        pc for pc in jal_callers(memory, DAMAGE_CONSTRUCTOR, CLASS1440_UPDATE, CLASS1440_UPDATE_END)
    ]
    if update_emitter_calls or update_constructor_calls:
        raise RuntimeError("class-1440 update unexpectedly gained a direct outgoing damage-emitter call")

    common.update({
        "pvarPlus20HealthValues": sorted(set(health_values)),
        "pvarPlus110TargetMobyHistogram": dict(sorted(collections.Counter(target_kinds).items())),
        "directCallsWithinUpdateRange": {
            "class749AttackEmitter0x2599e8": [],
            "damageConstructor0x259bc8": [],
        },
    })
    return common


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate R&C1 enemy attack-pattern evidence")
    parser.add_argument("--savestate", type=Path, required=True)
    parser.add_argument("--zstd-dll", type=Path, required=True)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()

    helper = load_helper()
    memory = helper.read_zip_entry(args.savestate, "eeMemory.bin", args.zstd_dll)
    if len(memory) != 32 * 1024 * 1024:
        raise RuntimeError(f"expected 32 MiB EE memory, got {len(memory)}")

    signatures = verify_signatures(memory)
    player_moby = u32(memory, PLAYER_MOBY_GLOBAL)
    if player_moby != LIVE_POOL or u16(memory, player_moby + LIVE_CLASS) != 0:
        raise RuntimeError(
            f"player Moby witness drift: 0x{player_moby:08x}, expected class-0 pool slot 0"
        )

    class749_instances, class749_live = inspect_live_class(memory, CLASS749, CLASS749_UPDATE)
    if len(class749_instances) != 16:
        raise RuntimeError(f"class 749 live count {len(class749_instances)} != 16")

    attack_callers = jal_callers(memory, CLASS749_ATTACK_EMITTER)
    if attack_callers != [CLASS749_ATTACK_CALL]:
        raise RuntimeError(
            f"loaded attack-emitter callers drifted: {[hex(address) for address in attack_callers]}"
        )

    report = {
        "schema": 1,
        "authority": AUTHORITY,
        "savestate": {
            "file": args.savestate.name,
            "sha256": sha256(args.savestate),
            "eeMemorySize": len(memory),
        },
        "class749Melee": {
            **class749_live,
            "stateDispatch": verify_state_table(
                memory, CLASS749_STATE_TABLE, CLASS749_STATE_TARGETS, "class-749"
            ),
            "recoveredContract": {
                "targetedState": 6,
                "attackState": 7,
                "returnHomeState": 8,
                "attackSequence": 5,
                "attackEntryDistanceExclusive": 2.0,
                "attackRetainDistanceInclusive": 1.5,
                "attackFacingErrorRawBits": "0x3e32b8c2",
                "animationMarker": 34.0,
                "markerNativeUpdate": 68,
                "sequenceNativeUpdates": 102,
                "nativeDamage": 1.0,
            },
            "loadedEmitter": {
                "routine": "0x002599e8",
                "directCallersInLoadedImage": [f"0x{address:08x}" for address in attack_callers],
                "attackVolumeScalarRawBits": "0x3eaa7efa",
                "attackVolumeScalar": struct.unpack("<f", (0x3EAA7EFA).to_bytes(4, "little"))[0],
                "boundary": "the loaded image proves class-749's marker-gated emitter call; the surrounding geometry helper is not promoted as a universal enemy melee shape",
            },
        },
        "class1440Candidate": {
            **inspect_class1440(memory, player_moby),
            "stateDispatch": verify_state_table(
                memory, CLASS1440_STATE_TABLE, CLASS1440_STATE_TARGETS, "class-1440"
            ),
            "damageIntake": {
                "health": "PVar+0x20 f32",
                "lookupCall": "0x002e16e8 -> 0x0025a420",
                "consumeCall": "0x002e1710 -> 0x0025a478",
                "consequence": "subtract returned native damage from PVar+0x20",
            },
            "state5ContactQuery": {
                "entry": "0x002e11a4",
                "queryRoutine": f"0x{VU_QUERY:08x}",
                "queryCalls": ["0x002e1348", "0x002e13bc"],
                "descriptor": {
                    "+0x80": "source Moby",
                    "+0x84": "0x00010000",
                    "+0x8a": "source native class",
                    "+0x8c": "1.0",
                    "+0x90": "1",
                },
                "playerContactGate": "query-owned Moby result is compared with current player Moby before class-local helper 0x2e2040 and native state-6 transition",
                "outgoingDamageBoundary": "unresolved: state 5 does not directly call 0x2599e8 or 0x259bc8, and descriptor field similarity is insufficient to name +0x8c as damage",
            },
        },
        "crossFamilyConclusion": {
            "sharedProven": [
                "live Moby native-state/update dispatch envelope",
                "native damage-record lookup/consumption",
                "class-owned PVar health subtraction after native damage consumption",
            ],
            "notSharedWithoutFurtherEvidence": [
                "attack cadence or animation marker",
                "attack entry/retention range",
                "facing threshold",
                "contact geometry",
                "outgoing damage scalar",
                "projectile semantics",
            ],
            "projectileChildRequired": False,
            "reason": "no separately identifiable weapon-like enemy projectile was recovered from this fixed loaded overlay",
        },
        "instructionEvidence": signatures,
        "notes": [
            "No ISO, executable, savestate, EE-memory or PVar payload bytes are emitted.",
            "Class 1440 is retained as a second targeting/damageable candidate, not promoted to hostile solely from resemblance.",
            "Only class 749 has a recovered outgoing attack cadence and damage emission path in this witness.",
        ],
    }

    rendered = json.dumps(report, indent=2) + "\n"
    if args.out is None:
        print(rendered, end="")
    else:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(rendered, encoding="utf-8")


if __name__ == "__main__":
    main()
