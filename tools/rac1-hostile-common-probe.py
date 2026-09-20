#!/usr/bin/env python3
"""Generate a payload-free SCUS-97199 common hostile/Moby state witness."""

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
LIVE_DAMAGE_SLOT = 0xA4
LIVE_CLASS = 0xA6

PLAYER_MOBY_GLOBAL = 0x001413D0
DAMAGE_LOOKUP = 0x0025A420
DAMAGE_CONSUME = 0x0025A478
DAMAGE_CONSTRUCTOR = 0x00259BC8
TERMINALIZE_MOBY = 0x0024EB68

CLASS_UPDATES = {
    500: 0x002CF218,
    749: 0x002D4610,
    1781: 0x002E3CA8,
}
CLASS_COUNTS = {
    500: 103,
    749: 16,
    1781: 33,
}

CLASS749_PVAR_HEALTH = 0x20
CLASS749_PVAR_TARGET_DEST = 0x180
CLASS749_PVAR_TARGET_MOBY = 0x1C0
CLASS749_PVAR_STATUS = 0x1C4
CLASS749_PVAR_HOME = 0x1D0
CLASS749_STATE_TABLE = 0x001E9F20
CLASS749_EXPECTED_STATE_TARGETS = [
    0x002D46C4,
    0x002D4884,
    0x002D493C,
    0x002D49B8,
    0x002D49F0,
    0x002D4A9C,
    0x002D4B88,
    0x002D4C74,
    0x002D4DA0,
    0x002D4864,
    0x002D4E08,
    0x002D4FCC,
    0x002D501C,
]

# Exact loaded-Veldin instruction witnesses.  These are words, not copied retail
# payload blocks, and are retained only to make semantic claims reproducible.
SIGNATURES = [
    (0x002D4630, 0x0C0B5458, "class-749 update calls its pre-dispatch damage/status helper"),
    (0x002D4634, 0x8E320078, "class-749 update loads live Moby +0x78 as its PVar pointer"),
    (0x002D469C, 0x92230020, "class-749 dispatch reads live Moby state byte +0x20"),
    (0x002D4B10, 0x26450240, "state 5 supplies PVar +0x240 to its class-local roaming/search helper"),
    (0x002D4B14, 0x0C09858C, "state 5 calls loaded helper 0x261630"),
    (0x002D4B1C, 0x8E4301C4, "state 5 reads PVar +0x1c4 status"),
    (0x002D4B94, 0x0C0B5532, "state 6 calls class-749 locomotion helper 0x2d54c8"),
    (0x002D4B98, 0x26450180, "state 6 supplies PVar +0x180 target destination"),
    (0x002D4C74, 0x3C014208, "state 7 materializes native attack marker 34.0"),
    (0x002D4C7C, 0x0C098636, "state 7 tests the attack marker"),
    (0x002D4CB4, 0x0C09667A, "state 7 calls the generic damage-record creation path"),
    (0x002D4DAC, 0x0C0B5532, "state 8 reuses the class-749 locomotion helper"),
    (0x002D4DB0, 0x264501D0, "state 8 supplies PVar +0x1d0 home position"),
    (0x002D51D8, 0x0C096908, "class-749 pre-dispatch path calls common damage lookup 0x25a420"),
    (0x002D51EC, 0x26260020, "class-749 passes PVar +0x20 to the common damage consumer"),
    (0x002D5200, 0x0C09691E, "class-749 calls common damage consumer 0x25a478"),
    (0x002D5274, 0xC6200020, "class-749 loads its PVar +0x20 health scalar"),
    (0x002D5288, 0x46070001, "class-749 subtracts returned native damage from health"),
    (0x002D52A4, 0xE6200020, "class-749 stores the resulting PVar health"),
    (0x002D5320, 0xA2550020, "class-749 enters native hit-reaction state 12"),
    (0x002D5344, 0xA24200A4, "class-749 clears live Moby damage-record slot +0xa4 to 0xff"),
    (0x002D53E4, 0x8E2201C0, "class-749 loads PVar +0x1c0 target Moby"),
    (0x002D53E8, 0x14400007, "non-null class-749 target Moby skips target initialization"),
    (0x002D53EC, 0x3C030014, "class-749 target initialization starts player-global address"),
    (0x002D53F0, 0x2463F350, "class-749 target initialization forms base 0x0013f350"),
    (0x002D53F4, 0x8C642080, "class-749 target initialization loads 0x001413d0"),
    (0x002D53FC, 0xAE2401C0, "class-749 stores the player Moby into PVar +0x1c0"),
    (0x002CF2EC, 0x0C096908, "class-500 update also calls common damage lookup 0x25a420"),
    (0x002CF340, 0x0C09691E, "class-500 update also calls common damage consumer 0x25a478"),
    (0x002CF820, 0x0C093ADA, "class-500 update calls common Moby terminalizer 0x24eb68"),
    (0x002D5060, 0x0C093ADA, "class-749 state 12 calls common Moby terminalizer 0x24eb68"),
    (0x0024EB88, 0x240200FD, "common terminalizer selects native state 0xfd on one pool side"),
    (0x0024EB8C, 0x240200FE, "common terminalizer selects native state 0xfe on the other pool side"),
    (0x0024EB90, 0xA0620020, "common terminalizer writes the chosen value to Moby +0x20"),
    (0x00259C0C, 0x924300A4, "damage constructor reads victim Moby +0xa4 slot"),
    (0x00259C64, 0xE62C002C, "damage constructor writes record +0x2c damage"),
    (0x00259C68, 0xAE320034, "damage constructor writes record +0x34 victim Moby"),
    (0x00259CC4, 0xA25300A4, "damage constructor assigns the record slot to victim Moby +0xa4"),
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


def s32(memory: bytes, address: int) -> int:
    return int.from_bytes(memory[address:address + 4], "little", signed=True)


def f32(memory: bytes, address: int) -> float:
    return struct.unpack_from("<f", memory, address)[0]


def point3(memory: bytes, address: int) -> tuple[float, float, float]:
    point = tuple(f32(memory, address + index * 4) for index in range(3))
    if not all(math.isfinite(value) for value in point):
        raise RuntimeError(f"non-finite point at 0x{address:08x}")
    return point


def jal_callers(memory: bytes, target: int, start: int = 0x00100000, end: int = 0x00320000) -> list[int]:
    refs = []
    stop = min(end, len(memory))
    for pc in range(start, stop - 4, 4):
        word = u32(memory, pc)
        if word >> 26 != 3:
            continue
        resolved = ((pc + 4) & 0xF0000000) | ((word & 0x03FFFFFF) << 2)
        if resolved == target:
            refs.append(pc)
    return refs


def histogram(values) -> dict[str, int]:
    counts = collections.Counter(values)
    return {f"0x{value:02x}": counts[value] for value in sorted(counts)}


def live_class(memory: bytes, native_class: int) -> list[int]:
    hits = []
    for slot in range(LIVE_SCAN_SLOTS):
        address = LIVE_POOL + slot * LIVE_STRIDE
        if u16(memory, address + LIVE_CLASS) == native_class:
            hits.append(address)
    return hits


def verify_signatures(memory: bytes) -> list[dict]:
    evidence = []
    for address, expected, meaning in SIGNATURES:
        actual = u32(memory, address)
        if actual != expected:
            raise RuntimeError(
                f"loaded-Veldin signature drift at 0x{address:08x}: "
                f"0x{actual:08x} != 0x{expected:08x}"
            )
        evidence.append({
            "address": f"0x{address:08x}",
            "word": f"0x{actual:08x}",
            "meaning": meaning,
        })
    return evidence


def inspect_class(memory: bytes, native_class: int) -> dict:
    instances = live_class(memory, native_class)
    expected_count = CLASS_COUNTS[native_class]
    if len(instances) != expected_count:
        raise RuntimeError(
            f"class {native_class} live count {len(instances)} != expected {expected_count}"
        )
    update_values = sorted({u32(memory, address + LIVE_UPDATE) for address in instances})
    expected_update = CLASS_UPDATES[native_class]
    if update_values != [expected_update]:
        raise RuntimeError(
            f"class {native_class} update pointers drifted: "
            f"{[hex(value) for value in update_values]}"
        )
    pvars = [u32(memory, address + LIVE_PVAR) for address in instances]
    nonzero_pvars = [pointer for pointer in pvars if pointer != 0]
    if any(pointer >= len(memory) for pointer in nonzero_pvars):
        raise RuntimeError(f"class {native_class} has an out-of-range live +0x78 pointer")
    if native_class in (500, 749) and len(nonzero_pvars) != len(instances):
        raise RuntimeError(f"class {native_class} unexpectedly has a null live +0x78 PVar pointer")
    if native_class == 1781 and nonzero_pvars:
        raise RuntimeError("class 1781 unexpectedly uses live +0x78 in the fixed witness")
    return {
        "count": len(instances),
        "firstLiveAddress": f"0x{instances[0]:08x}",
        "updateRoutine": f"0x{expected_update:08x}",
        "stateHistogram": histogram(u8(memory, address + LIVE_STATE) for address in instances),
        "damageSlotHistogram": histogram(
            u8(memory, address + LIVE_DAMAGE_SLOT) for address in instances
        ),
        "livePlus78": {
            "nonzeroCount": len(nonzero_pvars),
            "pointerRange": (
                [f"0x{min(nonzero_pvars):08x}", f"0x{max(nonzero_pvars):08x}"]
                if nonzero_pvars else None
            ),
        },
    }


def inspect_class749(memory: bytes, player_moby: int) -> dict:
    instances = live_class(memory, 749)
    health = []
    status = []
    target_kinds = []
    home_distances = []
    terminal_health = []
    nonterminal_player_distances_by_status = collections.defaultdict(list)
    player_position = point3(memory, player_moby + 0x10)

    for address in instances:
        pvar = u32(memory, address + LIVE_PVAR)
        if pvar + CLASS749_PVAR_HOME + 12 > len(memory):
            raise RuntimeError(f"class 749 PVar 0x{pvar:08x} is outside EE memory")
        hp = f32(memory, pvar + CLASS749_PVAR_HEALTH)
        if not math.isfinite(hp):
            raise RuntimeError(f"class 749 non-finite health at PVar 0x{pvar:08x}")
        health.append(hp)
        status_value = s32(memory, pvar + CLASS749_PVAR_STATUS)
        status.append(status_value)

        target = u32(memory, pvar + CLASS749_PVAR_TARGET_MOBY)
        if target == 0:
            target_kinds.append("none")
        elif target == player_moby:
            target_kinds.append("player")
        else:
            target_kinds.append("other")

        pos = point3(memory, address + 0x10)
        home = point3(memory, pvar + CLASS749_PVAR_HOME)
        home_distances.append(math.dist(pos, home))

        state = u8(memory, address + LIVE_STATE)
        if state in (0xFD, 0xFE):
            terminal_health.append(hp)
        else:
            nonterminal_player_distances_by_status[status_value].append(
                math.dist(pos, player_position)
            )

    if any(value not in (0.0, 1.0) for value in health):
        raise RuntimeError(f"unexpected class-749 fixed-witness health values {sorted(set(health))}")
    if any(value != 0.0 for value in terminal_health):
        raise RuntimeError("terminal class-749 witness has non-zero health")
    if "other" in target_kinds:
        raise RuntimeError("class-749 fixed witness targets a Moby other than player/null")

    return {
        "healthHistogram": {
            str(value): health.count(value) for value in sorted(set(health))
        },
        "statusHistogram": {
            str(value): status.count(value) for value in sorted(set(status))
        },
        "targetMobyHistogram": dict(sorted(collections.Counter(target_kinds).items())),
        "homeDistanceRange": [min(home_distances), max(home_distances)],
        "nonTerminalPlayerDistanceRangeByStatus": {
            str(value): [min(distances), max(distances)]
            for value, distances in sorted(nonterminal_player_distances_by_status.items())
        },
        "terminalHealthValues": sorted(set(terminal_health)),
    }


def main():
    parser = argparse.ArgumentParser(
        description="Generate payload-free R&C1 common hostile/Moby runtime evidence"
    )
    parser.add_argument("--savestate", type=Path, required=True)
    parser.add_argument("--zstd-dll", type=Path, required=True)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()

    helper = load_helper()
    memory = helper.read_zip_entry(args.savestate, "eeMemory.bin", args.zstd_dll)
    if len(memory) != 32 * 1024 * 1024:
        raise RuntimeError(f"expected 32 MiB EE memory, got {len(memory)}")

    signatures = verify_signatures(memory)

    state_targets = [u32(memory, CLASS749_STATE_TABLE + index * 4) for index in range(13)]
    if state_targets != CLASS749_EXPECTED_STATE_TARGETS:
        raise RuntimeError(
            "class-749 state table drifted: "
            f"{[hex(address) for address in state_targets]}"
        )

    player_moby = u32(memory, PLAYER_MOBY_GLOBAL)
    if player_moby != LIVE_POOL:
        raise RuntimeError(
            f"player Moby global 0x{PLAYER_MOBY_GLOBAL:08x} -> 0x{player_moby:08x}, "
            f"expected live pool slot 0 at 0x{LIVE_POOL:08x}"
        )
    if u16(memory, player_moby + LIVE_CLASS) != 0:
        raise RuntimeError("player Moby witness is not native class 0")

    class_reports = {
        str(native_class): inspect_class(memory, native_class)
        for native_class in CLASS_UPDATES
    }
    class_reports["749"]["classSpecific"] = inspect_class749(memory, player_moby)

    helper_callers = {
        DAMAGE_LOOKUP: jal_callers(memory, DAMAGE_LOOKUP),
        DAMAGE_CONSUME: jal_callers(memory, DAMAGE_CONSUME),
        TERMINALIZE_MOBY: jal_callers(memory, TERMINALIZE_MOBY),
        0x00261630: jal_callers(memory, 0x00261630),
        0x002D54C8: jal_callers(memory, 0x002D54C8),
    }
    required_callers = {
        DAMAGE_LOOKUP: {0x002CF2EC, 0x002D51D8},
        DAMAGE_CONSUME: {0x002CF340, 0x002D5200},
        TERMINALIZE_MOBY: {0x002CF820, 0x002D5060},
        0x00261630: {0x002D4B14},
        0x002D54C8: {0x002D4B94, 0x002D4DAC},
    }
    for target, required in required_callers.items():
        missing = required.difference(helper_callers[target])
        if missing:
            raise RuntimeError(
                f"missing callers for 0x{target:08x}: {[hex(address) for address in sorted(missing)]}"
            )

    # PVar +0x1c4 controls class-749 state transitions, but the loaded main
    # update body only reads it.  Retain that negative evidence so no generic
    # aggro-radius writer is invented from the transition consumer alone.
    status_accesses = []
    for pc in range(0x002D4610, 0x002D5160, 4):
        word = u32(memory, pc)
        if (word & 0xFFFF) != CLASS749_PVAR_STATUS:
            continue
        opcode = word >> 26
        if opcode in {0x20, 0x21, 0x23, 0x24, 0x25, 0x31}:
            kind = "load"
        elif opcode in {0x28, 0x29, 0x2B, 0x39}:
            kind = "store"
        else:
            kind = "other"
        status_accesses.append({"address": f"0x{pc:08x}", "kind": kind})
    if any(access["kind"] == "store" for access in status_accesses):
        raise RuntimeError("class-749 main update unexpectedly stores PVar +0x1c4")

    report = {
        "schema": 1,
        "authority": AUTHORITY,
        "savestate": {
            "file": args.savestate.name,
            "sha256": sha256(args.savestate),
            "eeMemorySize": len(memory),
        },
        "liveMobyCommon": {
            "poolBase": f"0x{LIVE_POOL:08x}",
            "stride": LIVE_STRIDE,
            "fields": {
                "stateByte": "+0x20",
                "classUpdateRoutine": "+0x74",
                "damageRecordSlot": "+0xa4",
                "nativeClass": "+0xa6",
            },
            "crossClassWitnesses": class_reports,
            "livePlus78Boundary": "class 500 and class 749 use +0x78 as a PVar pointer in this witness; class 1781 leaves it null, so +0x78 is not promoted as a universal Moby field",
        },
        "engineCommonMechanisms": {
            "damageRecord": {
                "constructor": f"0x{DAMAGE_CONSTRUCTOR:08x}",
                "lookup": f"0x{DAMAGE_LOOKUP:08x}",
                "consumer": f"0x{DAMAGE_CONSUME:08x}",
                "lookupCallerCountInLoadedImage": len(helper_callers[DAMAGE_LOOKUP]),
                "consumerCallerCountInLoadedImage": len(helper_callers[DAMAGE_CONSUME]),
                "sharedWitnessCallers": {
                    "class500": ["0x002cf2ec", "0x002cf340"],
                    "class749": ["0x002d51d8", "0x002d5200"],
                },
                "recordFields": {
                    "damage": "+0x2c f32",
                    "victimMoby": "+0x34 pointer",
                    "victimSlot": "Moby+0xa4 u8; 0xff means no queued record",
                },
            },
            "terminalizeMoby": {
                "routine": f"0x{TERMINALIZE_MOBY:08x}",
                "callerCountInLoadedImage": len(helper_callers[TERMINALIZE_MOBY]),
                "sharedWitnessCallers": {
                    "class500": "0x002cf820",
                    "class749": "0x002d5060",
                },
                "result": "writes native state 0xfd or 0xfe to Moby+0x20, then performs common cleanup",
            },
        },
        "class749Script": {
            "updateRoutine": f"0x{CLASS_UPDATES[749]:08x}",
            "stateDispatch": {
                str(index): f"0x{target:08x}"
                for index, target in enumerate(state_targets)
            },
            "recoveredStates": {
                "5": "class-local search/roam state; calls 0x261630 with PVar+0x240 and reads PVar+0x1c4",
                "6": "pursues PVar+0x180 destination through class-local helper 0x2d54c8 and gates attack on range/facing",
                "7": "attack state; marker 34.0 enters the generic damage-record path",
                "8": "returns toward PVar+0x1d0 home through the same class-local locomotion helper",
                "12": "hit/death reaction state reached after class-specific PVar health subtraction",
            },
            "targetAcquisition": {
                "playerMobyGlobal": f"0x{PLAYER_MOBY_GLOBAL:08x}",
                "playerMoby": f"0x{player_moby:08x}",
                "targetPointer": "PVar+0x1c0",
                "behavior": "if target pointer is null, class-749 pre-dispatch code stores the current player Moby",
            },
            "activationBoundary": {
                "statusField": "PVar+0x1c4",
                "mainUpdateAccesses": status_accesses,
                "directStoresInMainUpdate": 0,
                "conclusion": "state consumers are proven, but the writer/meaning of this status and any universal aggro radius remain evidence-gated",
            },
            "locomotionBoundary": {
                "state5Helper": "0x00261630",
                "state5HelperCallerCountInLoadedImage": len(helper_callers[0x00261630]),
                "state6And8Helper": "0x002d54c8",
                "state6And8HelperCallerCountInLoadedImage": len(helper_callers[0x002D54C8]),
                "conclusion": "these helpers are local to the loaded class-749 overlay; do not promote their tuning as engine-common hostile motion",
            },
            "healthDamageBoundary": {
                "health": "PVar+0x20 f32 for class 749",
                "hitReactionState": 12,
                "behavior": "common damage record is consumed first; class 749 then subtracts returned damage from its own PVar health and enters state 12",
            },
        },
        "instructionEvidence": signatures,
        "notes": [
            "No ISO, executable, EE-memory, savestate, or PVar payload bytes are emitted.",
            "Class 500 and class 1781 are non-hostile controls for live-Moby field ownership; their update pointers independently match existing retail archaeology.",
            "Moby +0x20/+0x74/+0xa4/+0xa6 and the damage/terminal helpers are engine/common mechanisms. Live +0x78 is class-bound, not universal; class-749 state numbers, PVar layout, health subtraction, targeting and locomotion policy remain class-specific.",
            "Native 0xfd/0xfe are written by common terminalizer 0x24eb68. Calling them terminal/inactive states is justified; immediate allocation/free timing is not inferred.",
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
