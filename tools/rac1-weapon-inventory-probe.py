#!/usr/bin/env python3
"""Generate payload-free SCUS-97199 weapon inventory/equip evidence."""

from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import struct
import zipfile
from pathlib import Path

AUTHORITY = {
    "game": "Ratchet & Clank",
    "build": "rac1-ntscu-original",
    "serial": "SCUS-97199",
    "isoSha256": "ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d",
    "exeSha256": "e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9",
}

GAME_DESCRIPTOR_TABLE = 0x001845C0
ITEM_DESCRIPTOR_TABLE = 0x001C40B0
ITEM_DESCRIPTOR_STRIDE = 0x18
ITEM_COUNT = 37

SAVE_BLOCKS = {
    "ammo": (9, 0x0013D428, 148),
    "items": (10, 0x0013D4C0, 37),
    "unlockFlags": (11, 0x0013D4E8, 37),
    "quickSelect": (13, 0x00141EA0, 32),
    "lastEquippedGadget": (21, 0x0015ED8C, 4),
    "equippedGadgets": (32, 0x00141660, 28),
}

CURRENT_ITEM_MIRRORS = (0x00140408, 0x00141424)

# Loaded-overlay instruction words that pin generic inventory semantics.
CODE_SIGNATURES = {
    "acquireItemsBase": (0x0026086C, 0x2442D4C0),
    "acquireUnlocksBase": (0x002608A0, 0x2442D4E8),
    "acquireUnlockStore": (0x002608CC, 0xA2930000),
    "acquireOwnedRead": (0x002608E4, 0x92420000),
    "acquireItemsStore": (0x00260904, 0xA2530000),
    "acquireDescriptorBase": (0x00260900, 0x246340B0),
    "acquireAmmoGate": (0x0026090C, 0x94820008),
    "acquireAmmoBase": (0x0026091C, 0x2442D428),
    "acquireInitialAmmo": (0x00260920, 0x94830012),
    "acquireQuickSelectBase": (0x00260A28, 0x24A31EA0),
    "acquireQuickSelectStore": (0x00260A60, 0xAC500000),
    "consumeCurrentItem": (0x00233DCC, 0x8C460408),
    "consumeMaxAmmo": (0x00233DE0, 0x9464000E),
    "consumeAmmoBase": (0x00233DF8, 0x2442D428),
    "addAmmoBase": (0x00233E4C, 0x2442D428),
    "addAmmoMax": (0x00233E6C, 0x94C3000E),
    "queryCurrentItem": (0x00233EAC, 0x8C450408),
    "queryAmmoBase": (0x00233ED0, 0x2442D428),
    "persistPreviousGadgetA": (0x0021047C, 0xAC24ED8C),
    "persistPreviousGadgetB": (0x002104FC, 0xAC23ED8C),
    "restoreEquippedGadgetsBase": (0x002769E4, 0x24841660),
}


def sha256_path(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def u16(data: bytes, address: int) -> int:
    return int.from_bytes(data[address:address + 2], "little")


def u32(data: bytes, address: int) -> int:
    return int.from_bytes(data[address:address + 4], "little")


def s32(data: bytes, address: int) -> int:
    return int.from_bytes(data[address:address + 4], "little", signed=True)


def read_zip_entry(path: Path, name: str, zstd_dll: Path) -> bytes:
    with zipfile.ZipFile(path) as archive:
        info = archive.getinfo(name)
        if info.compress_type != 93:
            return archive.read(name)

    with path.open("rb") as stream:
        stream.seek(info.header_offset)
        header = stream.read(30)
        if header[:4] != b"PK\x03\x04":
            raise RuntimeError("invalid local ZIP header")
        name_len, extra_len = struct.unpack_from("<HH", header, 26)
        stream.seek(name_len + extra_len, 1)
        compressed = stream.read(info.compress_size)

    lib = ctypes.CDLL(str(zstd_dll))
    lib.ZSTD_decompress.restype = ctypes.c_size_t
    lib.ZSTD_decompress.argtypes = [
        ctypes.c_void_p,
        ctypes.c_size_t,
        ctypes.c_void_p,
        ctypes.c_size_t,
    ]
    lib.ZSTD_isError.restype = ctypes.c_uint
    lib.ZSTD_isError.argtypes = [ctypes.c_size_t]

    source = ctypes.create_string_buffer(compressed)
    target = ctypes.create_string_buffer(info.file_size)
    result = lib.ZSTD_decompress(target, info.file_size, source, len(compressed))
    if lib.ZSTD_isError(result):
        raise RuntimeError(f"zstd decompression failed with code {result}")
    return target.raw[:result]


def descriptor_by_id(memory: bytes, block_id: int) -> dict[str, object]:
    for address in range(GAME_DESCRIPTOR_TABLE, GAME_DESCRIPTOR_TABLE + 0x340, 16):
        if s32(memory, address + 8) != block_id:
            continue
        return {
            "descriptorAddress": f"0x{address:08x}",
            "runtimeAddress": f"0x{u32(memory, address):08x}",
            "size": u32(memory, address + 4),
            "blockId": block_id,
        }
    raise RuntimeError(f"save block {block_id} descriptor was not found")


def verify_runtime_layout(memory: bytes) -> dict[str, object]:
    descriptors: dict[str, object] = {}
    for name, (block_id, runtime_address, size) in SAVE_BLOCKS.items():
        found = descriptor_by_id(memory, block_id)
        if found["runtimeAddress"] != f"0x{runtime_address:08x}" or found["size"] != size:
            raise RuntimeError(f"{name} descriptor does not match recovered layout")
        descriptors[name] = found

    signatures: dict[str, str] = {}
    for name, (address, expected) in CODE_SIGNATURES.items():
        actual = u32(memory, address)
        if actual != expected:
            raise RuntimeError(
                f"{name} signature mismatch at 0x{address:08x}: "
                f"0x{actual:08x} != 0x{expected:08x}"
            )
        signatures[name] = f"0x{address:08x}"

    return {"saveDescriptors": descriptors, "codeSignatures": signatures}


def item_descriptors(memory: bytes) -> list[dict[str, object]]:
    rows = []
    for item_id in range(ITEM_COUNT):
        address = ITEM_DESCRIPTOR_TABLE + item_id * ITEM_DESCRIPTOR_STRIDE
        rows.append({
            "itemId": item_id,
            "descriptorAddress": f"0x{address:08x}",
            "ammoGate": u16(memory, address + 0x08),
            "maxAmmo": u16(memory, address + 0x0E),
            "firstAcquisitionAmmoFloor": u16(memory, address + 0x12),
        })
    return rows


def runtime_inventory(memory: bytes) -> dict[str, object]:
    return {
        "ammo": [s32(memory, SAVE_BLOCKS["ammo"][1] + index * 4) for index in range(ITEM_COUNT)],
        "items": list(memory[SAVE_BLOCKS["items"][1]:SAVE_BLOCKS["items"][1] + ITEM_COUNT]),
        "unlockFlags": list(memory[SAVE_BLOCKS["unlockFlags"][1]:SAVE_BLOCKS["unlockFlags"][1] + ITEM_COUNT]),
        "quickSelect": [
            s32(memory, SAVE_BLOCKS["quickSelect"][1] + index * 4)
            for index in range(8)
        ],
        "lastEquippedGadget": s32(memory, SAVE_BLOCKS["lastEquippedGadget"][1]),
        "equippedGadgets": [
            s32(memory, SAVE_BLOCKS["equippedGadgets"][1] + index * 4)
            for index in range(7)
        ],
        "currentItemMirrors": [s32(memory, address) for address in CURRENT_ITEM_MIRRORS],
    }


def extract_card_slot(card_path: Path, slot_name: str) -> tuple[bytes, dict[str, object]]:
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


def parse_block_stream(data: bytes, start: int) -> list[tuple[int, int, int, bytes]]:
    cursor = start + 8
    blocks: list[tuple[int, int, int, bytes]] = []
    while True:
        block_id = s32(data, cursor)
        size = u32(data, cursor + 4)
        cursor += 8
        if block_id == -1:
            return blocks
        payload = data[cursor:cursor + size]
        blocks.append((block_id, size, cursor, payload))
        cursor += (size + 3) & ~3


def card_inventory(card_path: Path, slot_name: str) -> dict[str, object]:
    slot, directory = extract_card_slot(card_path, slot_name)
    blocks = parse_block_stream(slot, 8)
    by_id = {block_id: (size, offset, payload) for block_id, size, offset, payload in blocks}

    for name, (block_id, _runtime, expected_size) in SAVE_BLOCKS.items():
        if block_id not in by_id or by_id[block_id][0] != expected_size:
            raise RuntimeError(f"memory-card {name} block {block_id} has unexpected size")

    def ints(block_id: int) -> list[int]:
        payload = by_id[block_id][2]
        return [
            int.from_bytes(payload[offset:offset + 4], "little", signed=True)
            for offset in range(0, len(payload), 4)
        ]

    return {
        "memoryCardSha256": sha256_path(card_path),
        "slotName": slot_name,
        "slotSha256": hashlib.sha256(slot).hexdigest(),
        "directory": directory,
        "ammo": ints(9),
        "items": list(by_id[10][2]),
        "unlockFlags": list(by_id[11][2]),
        "quickSelect": ints(13),
        "lastEquippedGadget": ints(21)[0],
        "equippedGadgets": ints(32),
        "blockOffsets": {
            name: f"0x{by_id[block_id][1]:x}"
            for name, (block_id, _runtime, _size) in SAVE_BLOCKS.items()
        },
    }


def savestate_report(path: Path, zstd_dll: Path) -> tuple[dict[str, object], bytes]:
    memory = read_zip_entry(path, "eeMemory.bin", zstd_dll)
    if len(memory) != 32 * 1024 * 1024:
        raise RuntimeError(f"expected 32 MiB EE memory, got {len(memory)}")
    return {
        "savestateFile": path.name,
        "savestateSha256": sha256_path(path),
        "inventory": runtime_inventory(memory),
    }, memory


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--opening-savestate", type=Path, required=True)
    parser.add_argument("--resume-savestate", type=Path, required=True)
    parser.add_argument("--zstd-dll", type=Path, required=True)
    parser.add_argument("--memory-card", type=Path, required=True)
    parser.add_argument("--slot-name", default="save0.bin")
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()

    opening, opening_memory = savestate_report(args.opening_savestate, args.zstd_dll)
    resume, _resume_memory = savestate_report(args.resume_savestate, args.zstd_dll)

    layout = verify_runtime_layout(opening_memory)
    descriptors = item_descriptors(opening_memory)

    report = {
        "schema": 1,
        "authority": AUTHORITY,
        "notes": [
            "No ISO, executable, EE-memory, savestate, or memory-card payload bytes are emitted.",
            "Save block names are source-specific roles recovered from their retail access patterns.",
            "Current-item mirrors are runtime/session witnesses and are intentionally not promoted as save blocks.",
            "Descriptor +0x0e is the generic ammo cap/query field; +0x12 seeds first-acquisition ammo when +0x08 is nonzero.",
        ],
        "layout": layout,
        "itemDescriptors": descriptors,
        "openingSavestate": opening,
        "resumeSavestate": resume,
        "memoryCard": card_inventory(args.memory_card, args.slot_name),
    }

    rendered = json.dumps(report, indent=2) + "\n"
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
