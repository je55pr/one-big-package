#!/usr/bin/env python3
"""Catalogue RAC1 Moby class registration from all 19 original NTSC-U overlays.

Rows are native (class ID, update entry, third callback) triples. The third
word deliberately remains unnamed: it is not proven to be an initializer.
No retail executable bytes or PVar payloads are written to the output.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import struct
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
AUTHORED = ROOT / "research/generated/rac1-authored-moby-census.json"
EXPECTED_ISO_SHA = "ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d"
REGISTRATION_SECTION = 3
SENTINEL = 0xffffffff


def load_overlay_module():
    spec = importlib.util.spec_from_file_location(
        "rac1_level_overlay_probe", ROOT / "tools/rac1-level-overlay-probe.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def parse_registration_rows(blob: bytes, class_ids: set[int], is_code_address):
    """Parse the fixed triple table up to its sentinel, not adjacent pointers.

    A few legitimate registered classes have no update routine. Do not mistake
    their zero update for the end of the table.
    """
    records = []
    seen = set()
    if len(blob) < 24 or len(blob) % 4:
        raise ValueError("registration section is too short or unaligned")
    for offset in range(0, len(blob) - 11, 12):
        class_id, update, third = struct.unpack_from("<3I", blob, offset)
        if class_id == SENTINEL:
            if update != 0:
                raise ValueError("class registration sentinel has a nonzero update")
            if len(records) < 8:
                raise ValueError("implausibly short class registration table")
            return records, offset
        if class_id not in class_ids:
            raise ValueError(f"class {class_id} is absent from the authored class table")
        if class_id in seen:
            raise ValueError(f"duplicate class {class_id} in registration table")
        if update != 0 and not is_code_address(update):
            raise ValueError(f"class {class_id} update 0x{update:x} is not mapped code")
        seen.add(class_id)
        records.append((class_id, update, third))
    raise ValueError("registration table has no terminating sentinel")


def authority_census():
    authored = json.loads(AUTHORED.read_text(encoding="utf-8"))
    levels = {i: {} for i in range(19)}
    for cls in authored["classes"]:
        for level in cls["levels"]:
            levels[level["level"]][cls["oClass"]] = level["instances"]
    if any(not entries for entries in levels.values()):
        raise ValueError("authored Moby census lacks a level")
    return levels


def hash_file(path: Path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def build_atlas(iso: Path, verify_sha: bool = True):
    if not iso.is_file():
        raise FileNotFoundError(iso)
    if verify_sha and hash_file(iso) != EXPECTED_ISO_SHA:
        raise ValueError("ISO is not pinned SCUS-97199 NTSC-U original authority")
    native = load_overlay_module()
    authored = authority_census()
    levels = []
    cross_level = defaultdict(lambda: {"levels": set(), "placements": 0, "updates": set()})
    totals = {
        "registeredRows": 0, "nonzeroUpdaterRows": 0, "noUpdaterRows": 0,
        "authoredClassRows": 0, "unregisteredAuthoredClassRows": 0,
        "placedInstances": 0, "placedRegisteredInstances": 0,
        "placedUnregisteredInstances": 0,
    }
    with iso.open("rb") as stream:
        stream.seek(0, 2)
        disc_size = stream.tell()
        for level in range(19):
            overlay = native.Overlay(native.read_level_overlay(stream, disc_size, level))
            section = overlay.sections[REGISTRATION_SECTION]
            if section["type"] != 1:
                raise ValueError(f"level {level}: registration section is not initialized")
            blob = overlay.read(section["address"], section["size"])
            def code_address(address):
                return address % 4 == 0 and any(
                    s["type"] == 1 and s["address"] <= address < s["address"] + s["size"] - 3
                    for s in overlay.sections)
            records, sentinel_offset = parse_registration_rows(
                blob, set(authored[level]), code_address)
            catalogued = set()
            rows = []
            for class_id, update, third in records:
                catalogued.add(class_id)
                placements = authored[level][class_id]
                rows.append({
                    "classId": class_id,
                    "update": f"0x{update:08x}" if update else None,
                    "thirdWord": f"0x{third:08x}",
                    "placements": placements,
                })
                by_class = cross_level[class_id]
                by_class["levels"].add(level)
                by_class["placements"] += placements
                if update:
                    by_class["updates"].add((level, update))
            groups = defaultdict(list)
            for class_id, update, _ in records:
                if update:
                    groups[update].append(class_id)
            shared_groups = [
                {"update": f"0x{addr:08x}", "classes": sorted(classes),
                 "placements": sum(authored[level][cls] for cls in classes)}
                for addr, classes in groups.items() if len(classes) > 1
            ]
            shared_groups.sort(key=lambda group: (-group["placements"], group["classes"]))
            unregistered = sorted(set(authored[level]) - catalogued)
            registered_placed = sum(authored[level][c] for c in catalogued)
            all_placed = sum(authored[level].values())
            totals["registeredRows"] += len(rows)
            totals["nonzeroUpdaterRows"] += sum(r["update"] is not None for r in rows)
            totals["noUpdaterRows"] += sum(r["update"] is None for r in rows)
            totals["authoredClassRows"] += len(authored[level])
            totals["unregisteredAuthoredClassRows"] += len(unregistered)
            totals["placedInstances"] += all_placed
            totals["placedRegisteredInstances"] += registered_placed
            totals["placedUnregisteredInstances"] += all_placed - registered_placed
            levels.append({
                "level": level,
                "overlaySha256": hashlib.sha256(overlay.data).hexdigest(),
                "registrationAddress": f"0x{section['address']:08x}",
                "registrationBytesThroughSentinel": sentinel_offset + 12,
                "registered": len(rows),
                "authoredClasses": len(authored[level]),
                "placedInstances": all_placed,
                "placedRegisteredInstances": registered_placed,
                "unregisteredClasses": unregistered,
                "sharedUpdateGroups": shared_groups,
                "rows": rows,
            })
    ranking = [
        {"classId": cl, "registeredLevels": len(entry["levels"]),
         "placements": entry["placements"],
         "distinctUpdateEntries": len(entry["updates"])}
        for cl, entry in cross_level.items()
    ]
    ranking.sort(key=lambda row: (-row["placements"], -row["registeredLevels"], row["classId"]))
    return {
        "schemaVersion": 1,
        "authority": "rac1-ntscu-original / SCUS-97199",
        "evidence": "native class-registration triples in level overlay section 3",
        "thirdWordSemantics": "unknown; preserved, not an assumed initializer",
        "limits": "update addresses and updater sharing do not establish PVar, animation or gameplay behaviour",
        "totals": totals,
        "topPlacedRegisteredClasses": ranking[:50],
        "levels": levels,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--iso", type=Path,
                        default=Path(os.environ.get("OBP_RAC1_ISO", "")))
    parser.add_argument("--out", type=Path, default=ROOT / "research/generated/rac1-moby-updater-atlas.json")
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--skip-sha", action="store_true",
                        help="local development only; does not verify source ISO identity")
    args = parser.parse_args()
    atlas = build_atlas(args.iso, not args.skip_sha)
    text = json.dumps(atlas, indent=2, ensure_ascii=True) + "\n"
    if args.check:
        if not args.out.is_file() or args.out.read_text(encoding="utf-8") != text:
            parser.error("native Moby atlas does not match the retained witness")
        print("retail updater atlas matches retained witness")
    else:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(text, encoding="utf-8")
        print("wrote", args.out)
    print("totals:", atlas["totals"])


if __name__ == "__main__":
    main()
