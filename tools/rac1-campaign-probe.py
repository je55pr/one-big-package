#!/usr/bin/env python3
"""Payload-free SCUS-97199 campaign/save witness generator."""

import argparse
import hashlib
import importlib.util
import json
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

CURRENT_LEVEL = 0x0015ED84
VISITED_PLANETS = 0x0013DD40
GALACTIC_MAP = 0x0013D510
PER_LEVEL_STATE = 0x0013DD58
SELECTED_DESTINATION = 0x00184414
MAP_NAV_OWNER = 0x001602A0
PENDING_DESTINATION = 0x0015F5C0
TRAVEL_ACTIVE = 0x0015F5D8

DISC_INDEX_BASE = 0x00137B80
LEVEL_TABLE_OFFSET = 0x28C8
LEVEL_TABLE = DISC_INDEX_BASE + LEVEL_TABLE_OFFSET
LEVEL_HEADER_BUFFER = LEVEL_TABLE + 19 * 8

GAME_DESCRIPTOR_TABLE = 0x001845C0
LEVEL_VISITED_DESCRIPTOR = 0x001848C0

ADMISSION_ROUTINE = 0x002607D0
TRANSITION_CORE = 0x0024D430
TRAVEL_ROUTINE = 0x0028ED58
SCRIPT_ADMISSION_CALL = 0x00283340
DISCOVERY_EVENT_SUBTRACT = 0x002832F4
DISCOVERY_EVENT_RANGE = 0x00283330
FIRST_DISCOVERY_EVENT = 0x25
LAST_DISCOVERY_EVENT = 0x36
DISCOVERY_EVENT_OFFSET = 0x24
INITIAL_CURRENT_LEVEL_LOAD = 0x0023D160
INITIAL_CURRENT_LEVEL_ZERO_SKIP = 0x0023D164
INITIAL_ADMISSION_CALL = 0x0023D16C
SHIP_MAP_CURRENT_LOAD = 0x002762E8
SHIP_MAP_SELECTED_STORE = 0x00276320
SHIP_TRAVEL_CALL = 0x00276D38
SHIP_TRAVEL_SELECTED_LOAD = 0x00276D3C
TRAVEL_ACTIVE_SET = 0x0028ED8C
TRAVEL_CURRENT_LEVEL_LOAD = 0x0028EDE8
TRAVEL_TRANSITION_CALL = 0x0028EE78
PENDING_DESTINATION_STORE = 0x0028EE8C
TRANSITION_SOURCE_SNAPSHOT_STORE = 0x0024D51C
TRANSITION_TEMP_TARGET_STORE = 0x0024D528
TRANSITION_TEMP_VISIT_STORE = 0x0024D544
TRANSITION_VISIT_RESTORE = 0x0024D620
TRANSITION_SOURCE_RESTORE = 0x0024D628
LOADER_SELECTOR_STORE = 0x00293034
LEVEL_LOADER_ROUTINE = 0x0012F368
LEVEL_LOADER_CALL = 0x00293444
LEVEL_LOADER_ARGUMENT = 0x00293448
CURRENT_LEVEL_COMMIT = 0x00293434
PENDING_DESTINATION_COMMIT_LOAD = 0x0029341C
TRAVEL_ACTIVE_CLEAR = 0x00291DF8
LEVEL_TABLE_INDEX_SHIFT = 0x0012F380
LEVEL_TABLE_BASE_ADD = 0x0012F388
LEVEL_TABLE_ENTRY_LOAD = 0x0012F3A8
LEVEL_HEADER_SECTOR_COUNT = 0x0012F3B0
LEVEL_HEADER_BUFFER_BASE_ADD = 0x0012F3FC
LEVEL_HEADER_COPY_BOUND = 0x0012F410
COMPLETION_WRITE = 0x0029340C
COMPLETION_VALUE_SETUP = 0x00293408
COMPLETION_STATE_STORE = 0x00293414


def load_helper():
    spec = importlib.util.spec_from_file_location("rac1_savestate_helper", SAVESTATE_HELPER)
    if spec is None or spec.loader is None:
        raise RuntimeError("could not load savestate helper")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def u32(data, offset):
    return int.from_bytes(data[offset:offset + 4], "little")


def s32(data, offset):
    return int.from_bytes(data[offset:offset + 4], "little", signed=True)


def s32_array(data, address, count):
    return [s32(data, address + index * 4) for index in range(count)]


def descriptor(memory, address):
    return {
        "descriptorAddress": f"0x{address:08x}",
        "runtimeAddress": f"0x{u32(memory, address):08x}",
        "size": u32(memory, address + 4),
        "blockId": s32(memory, address + 8),
        "reserved": u32(memory, address + 12),
    }


def descriptor_by_id(memory, block_id):
    for address in range(GAME_DESCRIPTOR_TABLE, GAME_DESCRIPTOR_TABLE + 0x300, 16):
        if s32(memory, address + 8) == block_id:
            return descriptor(memory, address)
    raise RuntimeError(f"game descriptor block {block_id} was not found")


def pointer_refs(memory, target, start=0x00100000, end=0x00320000):
    refs = []
    stop = min(end, len(memory))
    for pc in range(start, stop - 28, 4):
        word = u32(memory, pc)
        if word >> 26 != 0x0F:
            continue
        register = (word >> 16) & 31
        high = word & 0xFFFF
        for delta in range(4, 28, 4):
            candidate = u32(memory, pc + delta)
            if candidate >> 26 != 9:
                continue
            if ((candidate >> 21) & 31) != register or ((candidate >> 16) & 31) != register:
                continue
            low = candidate & 0xFFFF
            low = low - 0x10000 if low & 0x8000 else low
            if ((high << 16) + low) & 0xFFFFFFFF == target:
                refs.append(pc + delta)
    return refs


def jal_callers(memory, target, start=0x00100000, end=0x00320000):
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


def signed_imm(word):
    value = word & 0xFFFF
    return value - 0x10000 if value & 0x8000 else value


def expect_i(memory, address, opcode, rs, rt, immediate, label):
    word = u32(memory, address)
    actual = (word >> 26, (word >> 21) & 31, (word >> 16) & 31, signed_imm(word))
    expected = (opcode, rs, rt, immediate)
    if actual != expected:
        raise RuntimeError(f"{label} signature mismatch at 0x{address:08x}: {actual} != {expected}")


def expect_word(memory, address, expected, label):
    actual = u32(memory, address)
    if actual != expected:
        raise RuntimeError(
            f"{label} signature mismatch at 0x{address:08x}: 0x{actual:08x} != 0x{expected:08x}"
        )


def expect_jal(memory, address, target, label):
    word = u32(memory, address)
    if word >> 26 != 3:
        raise RuntimeError(f"{label} is not JAL at 0x{address:08x}")
    resolved = ((address + 4) & 0xF0000000) | ((word & 0x03FFFFFF) << 2)
    if resolved != target:
        raise RuntimeError(f"{label} target mismatch: 0x{resolved:08x} != 0x{target:08x}")


def verify_destination_discovery(memory):
    # a0 = dispatcher value - 0x24
    expect_i(memory, DISCOVERY_EVENT_SUBTRACT, 9, 7, 4, -DISCOVERY_EVENT_OFFSET, "discovery subtract")
    # (dispatcher value - 0x25) < 0x12, i.e. inclusive 0x25..0x36.
    expect_i(memory, DISCOVERY_EVENT_RANGE, 9, 7, 2, -FIRST_DISCOVERY_EVENT, "discovery range subtract")
    expect_i(memory, DISCOVERY_EVENT_RANGE + 4, 11, 2, 2, 0x12, "discovery range width")
    expect_jal(memory, SCRIPT_ADMISSION_CALL, ADMISSION_ROUTINE, "script discovery admission")

    # Startup loads CurrentLevel, skips zero, and otherwise feeds it to the same primitive.
    expect_i(memory, INITIAL_CURRENT_LEVEL_LOAD, 35, 4, 4, -0x127C, "startup CurrentLevel load")
    expect_i(memory, INITIAL_CURRENT_LEVEL_ZERO_SKIP, 4, 4, 0, 5, "startup zero-level skip")
    expect_jal(memory, INITIAL_ADMISSION_CALL, ADMISSION_ROUTINE, "startup admission")

    # Completion is a separate per-level state-2 store, not destination discovery.
    expect_i(memory, COMPLETION_VALUE_SETUP, 9, 0, 3, 2, "completion state value")
    expect_i(memory, COMPLETION_WRITE, 9, 2, 2, -0x22A8, "completion state base")
    completion_store = u32(memory, COMPLETION_STATE_STORE)
    if (completion_store >> 26, (completion_store >> 21) & 31, (completion_store >> 16) & 31, completion_store & 0xFFFF) != (40, 2, 3, 0):
        raise RuntimeError("completion state store signature mismatch")


def verify_planet_travel(memory):
    # Ship/map entry seeds the UI selection from CurrentLevel. The launch call
    # then forwards that selected destination unchanged as a0 to the travel routine.
    expect_i(memory, SHIP_MAP_CURRENT_LOAD, 35, 5, 5, -0x127C, "ship map CurrentLevel load")
    expect_i(memory, SHIP_MAP_SELECTED_STORE, 43, 2, 5, 0x4414, "ship map selected store")
    expect_jal(memory, SHIP_TRAVEL_CALL, TRAVEL_ROUTINE, "ship selected travel")
    expect_i(memory, SHIP_TRAVEL_SELECTED_LOAD, 35, 16, 4, 0x224, "ship selected destination load")

    # Different-level travel is a two-phase handoff. Setup raises an active flag,
    # snapshots source state, temporarily swaps target context, then restores source.
    expect_i(memory, TRAVEL_ACTIVE_SET, 43, 1, 3, -0xA28, "travel active set")
    expect_i(memory, TRAVEL_CURRENT_LEVEL_LOAD, 35, 2, 2, -0x127C, "travel CurrentLevel load")
    expect_jal(memory, TRAVEL_TRANSITION_CALL, TRANSITION_CORE, "travel transition core")
    expect_i(memory, PENDING_DESTINATION_STORE, 43, 1, 16, -0xA40, "pending destination store")
    expect_i(memory, TRANSITION_SOURCE_SNAPSHOT_STORE, 43, 5, 2, 0xC8, "source snapshot store")
    expect_i(memory, TRANSITION_TEMP_TARGET_STORE, 43, 1, 17, -0x127C, "temporary target CurrentLevel store")
    expect_i(memory, TRANSITION_TEMP_VISIT_STORE, 40, 3, 2, 0, "temporary target visit store")
    expect_i(memory, TRANSITION_VISIT_RESTORE, 40, 2, 18, 0, "target visit restore")
    expect_i(memory, TRANSITION_SOURCE_RESTORE, 43, 1, 4, -0x127C, "source CurrentLevel restore")

    # The later handoff copies pending target into the loader selector, commits
    # CurrentLevel only after load admission, and clears the transfer-active flag.
    expect_i(memory, LOADER_SELECTOR_STORE, 43, 28, 2, -0x7E7C, "loader selector store")
    expect_i(memory, PENDING_DESTINATION_COMMIT_LOAD, 35, 2, 2, -0xA40, "pending destination commit load")
    expect_i(memory, CURRENT_LEVEL_COMMIT, 43, 1, 2, -0x127C, "CurrentLevel commit")
    expect_jal(memory, LEVEL_LOADER_CALL, LEVEL_LOADER_ROUTINE, "native level header load")
    expect_i(memory, LEVEL_LOADER_ARGUMENT, 35, 28, 4, -0x7E7C, "native level selector argument")
    expect_i(memory, TRAVEL_ACTIVE_CLEAR, 43, 1, 0, -0xA28, "travel active clear")

    # LEVELn is not a guessed filename convention: the loader indexes the final
    # 19 disc-index pairs by selector * 8, reads that pair's LBA, reads five sectors,
    # then copies the exact 0x2434-byte native header to the buffer after the table.
    expect_word(memory, LEVEL_TABLE_INDEX_SHIFT, 0x000420C0, "level table selector shift")
    expect_i(memory, LEVEL_TABLE_BASE_ADD, 9, 2, 2, -0x5BB8, "level table base")
    expect_i(memory, LEVEL_TABLE_ENTRY_LOAD, 35, 17, 4, 0, "level table LBA load")
    expect_i(memory, LEVEL_HEADER_SECTOR_COUNT, 9, 0, 5, 5, "level header sector count")
    expect_i(memory, LEVEL_HEADER_BUFFER_BASE_ADD, 9, 2, 6, -0x5B20, "level header buffer")
    expect_i(memory, LEVEL_HEADER_COPY_BOUND, 11, 5, 2, 0x2434, "level header copy bound")


def runtime_report(savestate, zstd_dll):
    helper = load_helper()
    memory = helper.read_zip_entry(savestate, "eeMemory.bin", zstd_dll)
    map_pointer = u32(memory, MAP_NAV_OWNER)
    if map_pointer != GALACTIC_MAP:
        raise RuntimeError(
            f"map navigation owner points to 0x{map_pointer:08x}, expected 0x{GALACTIC_MAP:08x}"
        )

    game_descriptors = {
        "currentLevel": descriptor_by_id(memory, 0),
        "visitedPlanets": descriptor_by_id(memory, 14),
        "galacticMap": descriptor_by_id(memory, 20),
    }
    level_descriptor = descriptor(memory, LEVEL_VISITED_DESCRIPTOR)
    if level_descriptor["blockId"] != 3001:
        raise RuntimeError("per-level descriptor at 0x001848c0 is not block 3001")

    verify_destination_discovery(memory)
    verify_planet_travel(memory)
    level_table = [
        {
            "index": index,
            "headerLba": u32(memory, LEVEL_TABLE + index * 8),
            "rawSecondWord": u32(memory, LEVEL_TABLE + index * 8 + 4),
        }
        for index in range(19)
    ]

    return {
        "savestateFile": savestate.name,
        "savestateSha256": sha256(savestate),
        "gameDescriptors": game_descriptors,
        "perLevelVisitedDescriptor": level_descriptor,
        "openingState": {
            "currentLevel": s32(memory, CURRENT_LEVEL),
            "visitedPlanets": list(memory[VISITED_PLANETS:VISITED_PLANETS + 20]),
            "galacticMap": s32_array(memory, GALACTIC_MAP, 20),
            "perLevelState": list(memory[PER_LEVEL_STATE:PER_LEVEL_STATE + 20]),
            "selectedDestination": s32(memory, SELECTED_DESTINATION),
            "pendingDestinationStorage": s32(memory, PENDING_DESTINATION),
            "travelActive": s32(memory, TRAVEL_ACTIVE),
        },
        "levelLoader": {
            "discIndexBase": f"0x{DISC_INDEX_BASE:08x}",
            "levelTableOffset": f"0x{LEVEL_TABLE_OFFSET:04x}",
            "levelTableAddress": f"0x{LEVEL_TABLE:08x}",
            "levelHeaderBuffer": f"0x{LEVEL_HEADER_BUFFER:08x}",
            "loadedHeaderNativeLevelId": s32(memory, LEVEL_HEADER_BUFFER),
            "loadedHeaderSize": u32(memory, LEVEL_HEADER_BUFFER + 4),
            "levelTable": level_table,
        },
        "runtimeOwners": {
            "selectedDestination": f"0x{SELECTED_DESTINATION:08x}",
            "galacticMapNavigationPointer": {
                "owner": f"0x{MAP_NAV_OWNER:08x}",
                "value": f"0x{map_pointer:08x}",
            },
        },
        "code": {
            "admissionRoutine": f"0x{ADMISSION_ROUTINE:08x}",
            "admissionCallers": [f"0x{x:08x}" for x in jal_callers(memory, ADMISSION_ROUTINE)],
            "destinationDiscovery": {
                "eventSubtractAddress": f"0x{DISCOVERY_EVENT_SUBTRACT:08x}",
                "eventRangeAddress": f"0x{DISCOVERY_EVENT_RANGE:08x}",
                "admissionCall": f"0x{SCRIPT_ADMISSION_CALL:08x}",
                "firstEvent": FIRST_DISCOVERY_EVENT,
                "lastEvent": LAST_DISCOVERY_EVENT,
                "destinationOffset": DISCOVERY_EVENT_OFFSET,
                "events": [
                    {"event": event, "destination": event - DISCOVERY_EVENT_OFFSET}
                    for event in range(FIRST_DISCOVERY_EVENT, LAST_DISCOVERY_EVENT + 1)
                ],
            },
            "initialAdmission": {
                "currentLevelLoad": f"0x{INITIAL_CURRENT_LEVEL_LOAD:08x}",
                "zeroSkipBranch": f"0x{INITIAL_CURRENT_LEVEL_ZERO_SKIP:08x}",
                "admissionCall": f"0x{INITIAL_ADMISSION_CALL:08x}",
            },
            "transitionCore": f"0x{TRANSITION_CORE:08x}",
            "transitionCallers": [f"0x{x:08x}" for x in jal_callers(memory, TRANSITION_CORE)],
            "travelRoutine": f"0x{TRAVEL_ROUTINE:08x}",
            "travelCallers": [f"0x{x:08x}" for x in jal_callers(memory, TRAVEL_ROUTINE)],
            "planetTravel": {
                "shipMapCurrentLevelLoad": f"0x{SHIP_MAP_CURRENT_LOAD:08x}",
                "shipMapSelectedStore": f"0x{SHIP_MAP_SELECTED_STORE:08x}",
                "shipTravelCall": f"0x{SHIP_TRAVEL_CALL:08x}",
                "shipTravelSelectedLoad": f"0x{SHIP_TRAVEL_SELECTED_LOAD:08x}",
                "travelActiveSet": f"0x{TRAVEL_ACTIVE_SET:08x}",
                "pendingDestinationStore": f"0x{PENDING_DESTINATION_STORE:08x}",
                "sourceSnapshotStore": f"0x{TRANSITION_SOURCE_SNAPSHOT_STORE:08x}",
                "temporaryTargetCurrentLevelStore": f"0x{TRANSITION_TEMP_TARGET_STORE:08x}",
                "sourceCurrentLevelRestore": f"0x{TRANSITION_SOURCE_RESTORE:08x}",
                "loaderSelectorStore": f"0x{LOADER_SELECTOR_STORE:08x}",
                "pendingCommitLoad": f"0x{PENDING_DESTINATION_COMMIT_LOAD:08x}",
                "currentLevelCommit": f"0x{CURRENT_LEVEL_COMMIT:08x}",
                "levelLoaderCall": f"0x{LEVEL_LOADER_CALL:08x}",
                "travelActiveClear": f"0x{TRAVEL_ACTIVE_CLEAR:08x}",
            },
            "completionWrite": f"0x{COMPLETION_WRITE:08x}",
            "completionValueSetup": f"0x{COMPLETION_VALUE_SETUP:08x}",
            "completionStateStore": f"0x{COMPLETION_STATE_STORE:08x}",
            "directRefs": {
                "visitedPlanets": [f"0x{x:08x}" for x in pointer_refs(memory, VISITED_PLANETS)],
                "galacticMap": [f"0x{x:08x}" for x in pointer_refs(memory, GALACTIC_MAP)],
                "perLevelState": [f"0x{x:08x}" for x in pointer_refs(memory, PER_LEVEL_STATE)],
            },
        },
    }


def extract_card_slot(card_path, slot_name):
    raw = card_path.read_bytes()
    if len(raw) % 528 != 0:
        raise RuntimeError("expected a raw 528-byte-page PS2 memory card image")
    logical = b"".join(raw[offset:offset + 512] for offset in range(0, len(raw), 528))
    page_size = int.from_bytes(logical[0x28:0x2A], "little")
    pages_per_cluster = int.from_bytes(logical[0x2A:0x2C], "little")
    allocation_offset = u32(logical, 0x34)
    name_offset = logical.find(slot_name.encode("ascii"))
    if name_offset < 0:
        raise RuntimeError(f"{slot_name} not found in memory-card directory")
    entry_offset = name_offset - 0x40
    size = u32(logical, entry_offset + 4)
    start_cluster = u32(logical, entry_offset + 0x10)
    cluster_size = page_size * pages_per_cluster
    start = (allocation_offset + start_cluster) * cluster_size
    return logical[start:start + size], {
        "entryOffset": f"0x{entry_offset:x}",
        "size": size,
        "startCluster": f"0x{start_cluster:x}",
    }


def parse_block_stream(data, start):
    checksum_size = u32(data, start)
    cursor = start + 8
    blocks = []
    while True:
        block_id = s32(data, cursor)
        size = u32(data, cursor + 4)
        cursor += 8
        if block_id == -1:
            break
        blocks.append((block_id, size, cursor, data[cursor:cursor + size]))
        cursor += (size + 3) & ~3
    return blocks, cursor, checksum_size


def memory_card_report(card_path, slot_name):
    slot, directory = extract_card_slot(card_path, slot_name)
    game_data_size = u32(slot, 0)
    level_data_size = u32(slot, 4)
    game_blocks, _, game_checksum_size = parse_block_stream(slot, 8)
    by_id = {block_id: (size, offset, payload) for block_id, size, offset, payload in game_blocks}

    current_level = int.from_bytes(by_id[0][2], "little", signed=True)
    visited_planets = list(by_id[14][2])
    galactic_map = [
        int.from_bytes(by_id[20][2][index:index + 4], "little", signed=True)
        for index in range(0, by_id[20][0], 4)
    ]

    levels = []
    cursor = 8 + game_data_size
    while cursor + 8 <= len(slot):
        blocks, end, checksum_size = parse_block_stream(slot, cursor)
        level_by_id = {block_id: (size, offset, payload) for block_id, size, offset, payload in blocks}
        visited = level_by_id.get(3001)
        levels.append({
            "record": len(levels),
            "streamOffset": f"0x{cursor:x}",
            "streamSize": end - cursor,
            "checksumPayloadSize": checksum_size,
            "visitedOffset": None if visited is None else f"0x{visited[1]:x}",
            "visited": None if visited is None else visited[2][0],
        })
        cursor += level_data_size

    admitted = [destination for destination in galactic_map if destination != 0]
    revisit_other = next((destination for destination in admitted if destination != current_level), None)

    return {
        "memoryCardSha256": sha256(card_path),
        "slotName": slot_name,
        "slotSha256": hashlib.sha256(slot).hexdigest(),
        "directory": directory,
        "slotHeader": {
            "gameDataSize": game_data_size,
            "levelDataSize": level_data_size,
            "levelRecordCount": len(levels),
        },
        "gameBlocks": {
            "currentLevel": {"id": 0, "size": by_id[0][0], "offset": f"0x{by_id[0][1]:x}", "value": current_level},
            "visitedPlanets": {"id": 14, "size": by_id[14][0], "offset": f"0x{by_id[14][1]:x}", "values": visited_planets},
            "galacticMap": {"id": 20, "size": by_id[20][0], "offset": f"0x{by_id[20][1]:x}", "values": galactic_map},
        },
        "levelRecords": levels,
        "gameChecksumPayloadSize": game_checksum_size,
        "smallestSpecificRevisit": (
            [current_level, revisit_other, current_level]
            if revisit_other is not None
            else None
        ),
    }


def main():
    parser = argparse.ArgumentParser(
        description="Generate payload-free SCUS-97199 campaign archaeology witnesses"
    )
    parser.add_argument("--savestate", type=Path)
    parser.add_argument("--zstd-dll", type=Path)
    parser.add_argument("--memory-card", type=Path)
    parser.add_argument("--slot-name", default="save0.bin")
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()

    if args.savestate is None and args.memory_card is None:
        parser.error("provide --savestate and/or --memory-card")
    if args.savestate is not None and args.zstd_dll is None:
        parser.error("--zstd-dll is required with --savestate")

    report = {
        "schema": 1,
        "authority": AUTHORITY,
        "notes": [
            "No ISO, executable, EE-memory, savestate, or memory-card payload bytes are emitted.",
            "Runtime descriptors and code references are read from the authorized SCUS-97199 savestate.",
            "The recovered loader bridge indexes the retail 19-pair disc level table directly by campaign destination id.",
            "The pending target slot is only authoritative while the separately recovered travel-active flag is set.",
        ],
    }
    if args.savestate is not None:
        report["runtime"] = runtime_report(args.savestate, args.zstd_dll)
    if args.memory_card is not None:
        report["memoryCard"] = memory_card_report(args.memory_card, args.slot_name)

    rendered = json.dumps(report, indent=2) + "\n"
    if args.out is None:
        print(rendered, end="")
    else:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(rendered, encoding="utf-8")


if __name__ == "__main__":
    main()
