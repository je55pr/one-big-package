#!/usr/bin/env python3
"""Generate payload-free SCUS-97199 level-entry ownership evidence."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "tools"
MOVEMENT_PROBE = TOOLS / "rac1-savestate-movement-probe.py"
CAMPAIGN_PROBE = TOOLS / "rac1-campaign-probe.py"

AUTHORITY = {
    "game": "Ratchet & Clank",
    "build": "rac1-ntscu-original",
    "serial": "SCUS-97199",
    "isoSha256": "ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d",
    "exeSha256": "e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9",
}

CURRENT_LEVEL = 0x0015ED84
VISITED_PLANETS = 0x0013DD40
GALACTIC_MAP = 0x0013D510
PER_LEVEL_VISITED = 0x0013DD58

SELECTED_DESTINATION = 0x00184414
PENDING_DESTINATION = 0x0015F5C0
TRAVEL_ACTIVE = 0x0015F5D8

GAME_DESCRIPTOR_START = 0x001845C0
GAME_DESCRIPTOR_END = 0x001848C0
LEVEL_DESCRIPTOR_START = 0x001848C0
LEVEL_DESCRIPTOR_COUNT = 11

LEVEL_INIT_START = 0x00244110
LEVEL_INIT_END = 0x00245C28
MOBY_POPULATION_START = 0x00241940
MOBY_POPULATION_END = 0x00243D38
MOBY_POPULATION_CALL = 0x00244B08
MOBY_RECORD_POINTER_LOAD = 0x00242828
LIVE_MOBY_INIT_CALL = 0x00242A7C
LIVE_MOBY_INIT = 0x0024E930
MOBY_POSITION_COPY = (0x00242AD8, 0x00242AE0, 0x00242AEC)
MOBY_ROTATION_COPY = (0x00242AF4, 0x00242AFC, 0x00242B08)
MOBY_RECORD_ADVANCE = 0x00242C48

EXPECTED_WORDS = {
    MOBY_RECORD_POINTER_LOAD: 0x8CA20044,
    MOBY_POSITION_COPY[0]: 0xE6400010,
    MOBY_POSITION_COPY[1]: 0xE6410014,
    MOBY_POSITION_COPY[2]: 0xE6420018,
    MOBY_ROTATION_COPY[0]: 0xE6400040,
    MOBY_ROTATION_COPY[1]: 0xE6410044,
    MOBY_ROTATION_COPY[2]: 0xE6400048,
    MOBY_RECORD_ADVANCE: 0x8FB1001C,
}

def load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"could not load {path.name}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def u32(memory: bytes, address: int) -> int:
    return struct.unpack_from("<I", memory, address)[0]


def s32(memory: bytes, address: int) -> int:
    return struct.unpack_from("<i", memory, address)[0]


def expect_word(memory: bytes, address: int, expected: int, label: str) -> None:
    actual = u32(memory, address)
    if actual != expected:
        raise RuntimeError(
            f"{label} signature mismatch at 0x{address:08x}: "
            f"0x{actual:08x} != 0x{expected:08x}"
        )


def expect_jal(memory: bytes, address: int, target: int, label: str) -> None:
    word = u32(memory, address)
    if word >> 26 != 3:
        raise RuntimeError(f"{label} is not JAL at 0x{address:08x}")
    resolved = ((address + 4) & 0xF0000000) | ((word & 0x03FFFFFF) << 2)
    if resolved != target:
        raise RuntimeError(
            f"{label} target mismatch at 0x{address:08x}: "
            f"0x{resolved:08x} != 0x{target:08x}"
        )


def descriptor(memory: bytes, address: int) -> dict[str, object]:
    return {
        "descriptorAddress": f"0x{address:08x}",
        "runtimeAddress": f"0x{u32(memory, address):08x}",
        "size": u32(memory, address + 4),
        "blockId": s32(memory, address + 8),
    }


def descriptors(memory: bytes, start: int, end: int) -> list[dict[str, object]]:
    rows = []
    for address in range(start, end, 16):
        row = descriptor(memory, address)
        if row["runtimeAddress"] != "0x00000000" or row["size"] != 0 or row["blockId"] != 0:
            rows.append(row)
    return rows

def descriptor_matches(
    rows: list[dict[str, object]],
    runtime_address: int,
) -> list[dict[str, object]]:
    wanted = f"0x{runtime_address:08x}"
    return [row for row in rows if row["runtimeAddress"] == wanted]


def runtime_report(savestate: Path, zstd_dll: Path) -> dict[str, object]:
    movement = load_module(MOVEMENT_PROBE, "rac1_level_entry_state_helper")
    campaign = load_module(CAMPAIGN_PROBE, "rac1_level_entry_campaign_helper")
    memory = movement.read_zip_entry(savestate, "eeMemory.bin", zstd_dll)

    expect_jal(
        memory,
        MOBY_POPULATION_CALL,
        MOBY_POPULATION_START,
        "ordinary level-init Moby population call",
    )
    expect_jal(
        memory,
        LIVE_MOBY_INIT_CALL,
        LIVE_MOBY_INIT,
        "live-Moby initializer call",
    )
    for address, expected in EXPECTED_WORDS.items():
        expect_word(memory, address, expected, "level-entry population")

    game_descriptors = descriptors(memory, GAME_DESCRIPTOR_START, GAME_DESCRIPTOR_END)
    level_descriptors = [
        descriptor(memory, LEVEL_DESCRIPTOR_START + index * 16)
        for index in range(LEVEL_DESCRIPTOR_COUNT)
    ]

    campaign_fields = {
        "currentLevel": CURRENT_LEVEL,
        "visitedPlanets": VISITED_PLANETS,
        "galacticMap": GALACTIC_MAP,
        "perLevelVisited": PER_LEVEL_VISITED,
    }
    direct_refs = {
        name: {
            "levelInit": [
                f"0x{address:08x}"
                for address in campaign.pointer_refs(
                    memory, target, LEVEL_INIT_START, LEVEL_INIT_END
                )
            ],
            "mobyPopulation": [
                f"0x{address:08x}"
                for address in campaign.pointer_refs(
                    memory, target, MOBY_POPULATION_START, MOBY_POPULATION_END
                )
            ],
        }
        for name, target in campaign_fields.items()
    }

    transient = {
        "selectedDestination": SELECTED_DESTINATION,
        "pendingDestination": PENDING_DESTINATION,
        "travelActive": TRAVEL_ACTIVE,
    }

    return {
        "savestateFile": savestate.name,
        "savestateSha256": sha256(savestate),
        "openingCampaignState": {
            "currentLevel": s32(memory, CURRENT_LEVEL),
            "visitedPlanets": list(memory[VISITED_PLANETS:VISITED_PLANETS + 20]),
            "galacticMap": [
                s32(memory, GALACTIC_MAP + index * 4)
                for index in range(20)
            ],
            "perLevelVisited": list(
                memory[PER_LEVEL_VISITED:PER_LEVEL_VISITED + 20]
            ),
        },
        "entrySeed": {
            "levelInitRoutine": (
                f"0x{LEVEL_INIT_START:08x}..0x{LEVEL_INIT_END - 1:08x}"
            ),
            "mobyPopulationCall": f"0x{MOBY_POPULATION_CALL:08x}",
            "mobyPopulationRoutine": (
                f"0x{MOBY_POPULATION_START:08x}.."
                f"0x{MOBY_POPULATION_END - 1:08x}"
            ),
            "gameplayMobyBlockPointerLoad": (
                f"0x{MOBY_RECORD_POINTER_LOAD:08x}"
            ),
            "liveMobyInitializerCall": f"0x{LIVE_MOBY_INIT_CALL:08x}",
            "liveMobyInitializer": f"0x{LIVE_MOBY_INIT:08x}",
            "positionCopies": [
                f"0x{address:08x}" for address in MOBY_POSITION_COPY
            ],
            "rotationCopies": [
                f"0x{address:08x}" for address in MOBY_ROTATION_COPY
            ],
            "recordAdvance": f"0x{MOBY_RECORD_ADVANCE:08x}",
            "campaignFieldDirectRefs": direct_refs,
        },
        "saveDescriptors": {
            "game": game_descriptors,
            "level": level_descriptors,
            "transientTravelDescriptorMatches": {
                name: descriptor_matches(game_descriptors, address)
                for name, address in transient.items()
            },
        },
    }

def memory_card_report(card_path: Path, slot_name: str) -> dict[str, object]:
    campaign = load_module(CAMPAIGN_PROBE, "rac1_level_entry_card_helper")
    full = campaign.memory_card_report(card_path, slot_name)
    return {
        "memoryCardSha256": full["memoryCardSha256"],
        "slotName": full["slotName"],
        "slotSha256": full["slotSha256"],
        "currentLevel": full["gameBlocks"]["currentLevel"]["value"],
        "visitedPlanets": full["gameBlocks"]["visitedPlanets"]["values"],
        "galacticMap": full["gameBlocks"]["galacticMap"]["values"],
        "perLevelVisited": [
            row["visited"] for row in full["levelRecords"]
        ],
        "smallestSpecificRevisit": full["smallestSpecificRevisit"],
    }


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate payload-free SCUS-97199 level-entry evidence"
    )
    parser.add_argument("--savestate", required=True, type=Path)
    parser.add_argument("--zstd-dll", required=True, type=Path)
    parser.add_argument("--memory-card", type=Path)
    parser.add_argument("--slot-name", default="save0.bin")
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()

    report: dict[str, object] = {
        "schema": 1,
        "authority": AUTHORITY,
        "runtime": runtime_report(args.savestate, args.zstd_dll),
        "notes": [
            "No ISO, executable, EE-memory, savestate, or memory-card payload bytes are emitted.",
            "The ordinary level-init path calls the same authored-Moby population routine before gameplay.",
            "That population path copies each emitted authored Moby transform into live storage and has no direct references to the four recovered campaign/progress fields.",
            "This proves the authored class-0 transform is the default player seed on a target-level load; it does not prove that a later checkpoint or level script cannot relocate Ratchet.",
            "Selected destination, pending destination, and transition-active are transient travel/session state and have no game-save descriptor in the scanned retail descriptor table.",
            "Only block 3001 in the per-level descriptor family is semantically promoted here as the recovered 0/1/2 visit state; other level blocks remain opaque.",
        ],
    }
    if args.memory_card is not None:
        report["memoryCard"] = memory_card_report(
            args.memory_card, args.slot_name
        )

    rendered = json.dumps(report, indent=2) + "\n"
    if args.out is None:
        print(rendered, end="")
    else:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(rendered, encoding="utf-8")


if __name__ == "__main__":
    main()
