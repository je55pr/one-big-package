#!/usr/bin/env python3
"""Generate payload-free SCUS-97199 projectile/contact/damage evidence."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
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

ITEM10_WEAPON_UPDATE = 0x002C26C8
ITEM10_CONSTRUCTOR = 0x002ACE70
ITEM10_LAUNCH = 0x002ACFD8
ITEM10_UPDATE = 0x002ADB30
ITEM10_UPDATE_END = 0x002AF400
CLASS4A_UPDATE = 0x002A84C8
CLASS4A_UPDATE_END = 0x002A96F4
WORLD_QUERY = 0x001EFC70
CONTACT_QUERY = 0x001F2868
SPLASH_DISTRIBUTOR = 0x0025A9F8
SPLASH_VICTIM_WRITER = 0x00259A88
DIRECT_DAMAGE_WRITER = 0x00259BC8
FRAME_SCALE_ADDRESS = 0x0015ED70

LIVE_POOL = 0x01845E80
LIVE_STRIDE = 0x100
LIVE_SCAN_SLOTS = 0x300
LIVE_STATE = 0x20
LIVE_CLASS = 0xA6

SIGNATURES = [
    (0x0024E9B8, 0xA63000A6, "allocator stores requested native class at Moby+0xa6"),
    (0x002C28D0, 0x0C08CF6E, "item-10 fire path consumes one current-weapon ammo"),
    (0x002C28D8, 0x24030003, "accepted item-10 fire selects weapon state 3"),
    (0x002C3004, 0x8E850050, "weapon state 3 loads staged projectile from PVar+0x50"),
    (0x002C3014, 0x0C0AB39C, "item-10 weapon calls class-0x79 constructor"),
    (0x002C3024, 0x0C0AB3F6, "item-10 weapon calls class-0x79 launch helper"),
    (0x002C30CC, 0xAE800050, "launch path clears weapon PVar+0x50"),
    (0x002C30D0, 0x0C07FBC8, "launch path calls native time helper for fire gate"),
    (0x002C316C, 0x0C0AB39C, "weapon tail can stage a replacement class-0x79 object"),
    (0x002C3174, 0xAE820050, "weapon tail stores replacement at PVar+0x50"),
    (0x002ACE8C, 0x24040079, "item-10 constructor requests native class 0x79"),
    (0x002ACE98, 0x0C093A0E, "item-10 constructor calls the common Moby allocator"),
    (0x002ACF28, 0xAE120050, "class-0x79 PVar+0x50 stores firing weapon Moby"),
    (0x002ACFFC, 0x2404001E, "launch helper requests native time value 30"),
    (0x002AD01C, 0x0C07FBC8, "launch helper calls time helper for short countdown"),
    (0x002AD024, 0xA642006A, "short countdown is stored at projectile PVar+0x6a"),
    (0x002AD334, 0x2404012C, "launch helper requests native time value 300"),
    (0x002AD338, 0x0C07FBC8, "launch helper calls time helper for long countdown"),
    (0x002AD340, 0xA6420054, "long countdown is stored at projectile PVar+0x54"),
    (0x002AD370, 0x0C07BF1C, "launch helper calls shared world query"),
    (0x002AE014, 0x03C0202D, "state-1 motion uses Moby position as vector-add destination"),
    (0x002AE018, 0x03C0282D, "state-1 motion uses current Moby position as vector-add lhs"),
    (0x002AE01C, 0x0C07FC9E, "state-1 motion calls common vector-add helper"),
    (0x002AE020, 0x02C0302D, "state-1 motion uses projectile PVar base as step vector"),
    (0x002AE034, 0xC420ED70, "state-1 gravity reads native frame-scale source"),
    (0x002AE038, 0x3C014130, "state-1 gravity materializes scalar 11.0"),
    (0x002AE040, 0xC6C10008, "state-1 gravity reads vertical step from PVar+0x08"),
    (0x002AE044, 0x46020002, "state-1 gravity multiplies frame scale by 11.0"),
    (0x002AE048, 0x46000841, "state-1 gravity subtracts that delta from vertical step"),
    (0x002AE050, 0xE6C10008, "state-1 gravity stores vertical step back to PVar+0x08"),
    (0x002AE1B0, 0x0C07BF1C, "class-0x79 state-1 update calls shared world query"),
    (0x002AE444, 0x0C07FBE6, "class-0x79 update calls signed-16 countdown helper"),
    (0x002AE448, 0x26C40054, "that countdown helper receives projectile PVar+0x54"),
    (0x002AE4F0, 0x0C07CA1A, "class-0x79 contact path calls common gameplay contact routine"),
    (0x002AE500, 0x3C014000, "item-10 contact path materializes native damage scalar 2.0"),
    (0x002AE518, 0x0280202D, "item-10 contact path retains projectile Moby as source"),
    (0x002AE520, 0x3C090083, "item-10 contact path materializes damage flag high word 0x0083"),
    (0x002AE530, 0x0C096A7E, "item-10 contact path calls splash distributor 0x25a9f8"),
    (0x002AE53C, 0xA2820020, "contact path writes projectile native state 2"),
    (0x002AF274, 0x0C093ADA, "class-0x79 update calls common Moby terminalizer"),
    (0x0025AA78, 0x8E030000, "splash distributor loads one candidate Moby"),
    (0x0025AA7C, 0x5077001F, "splash distributor skips candidate equal to retained source"),
    (0x0025AA94, 0xE7B7002C, "splash descriptor retains caller damage scalar"),
    (0x0025AAE0, 0x8E040000, "splash distributor passes candidate Moby as victim"),
    (0x0025AAF0, 0x0C0966A2, "splash distributor calls per-victim writer 0x259a88"),
    (0x00259B48, 0x8E630010, "per-victim writer reads retained source Moby"),
    (0x00259B58, 0xAE430020, "per-victim damage record +0x20 stores source Moby"),
    (0x00259B5C, 0x8E620014, "per-victim writer reads retained damage flags"),
    (0x00259B7C, 0xC660001C, "per-victim writer reads retained damage scalar"),
    (0x00259B8C, 0xAE420030, "per-victim record stores caller field at +0x30"),
    (0x00259B90, 0xAE540034, "per-victim record +0x34 stores victim Moby"),
    (0x001F28AC, 0x20D20000, "common contact routine retains source Moby a2"),
    (0x001F29F0, 0x1312FFF7, "common contact scan skips candidate equal to source Moby"),
    (0x002A8C6C, 0x3C013F80, "separate class-0x4a path materializes direct damage 1.0"),
    (0x002A8C74, 0x0260282D, "class-0x4a direct path supplies projectile as source"),
    (0x002A8C7C, 0x3C060001, "class-0x4a direct path supplies flags 0x00010000"),
    (0x002A8C84, 0x0C0966F2, "class-0x4a direct path calls writer 0x259bc8"),
]
MEMORY_OPS = {0x20, 0x21, 0x23, 0x24, 0x25, 0x28, 0x29, 0x2B, 0x31, 0x39}

CONTROLLED_LIVE_WITNESS = {
    "authority": "managed PCSX2 replay from fixed retained SCUS-97199 Veldin state",
    "equippedItemTransition": [8, 10],
    "ammoTransition": [6, 5],
    "launchedMoby": "0x01858b80",
    "nativeClass": 0x79,
    "stateTransitions": [
        {"frame": 41, "state": 1, "longCountdown": 300, "shortCountdown": 30,
         "position": [155.40013122558594, 121.16759490966797, 29.975194931030273]},
        {"frame": 107, "state": 2, "longCountdown": 235, "shortCountdown": 0,
         "position": [159.54039001464844, 129.53054809570312, 29.484375]},
        {"frame": 122, "state": 0xFE, "longCountdown": 235, "shortCountdown": 0,
         "position": [159.54039001464844, 129.53054809570312, 29.484375]},
    ],
    "firstState1Step": [0.06286147236824036, 0.12695619463920593, 0.09166651964187622],
    "secondState1Step": [0.06286147236824036, 0.12695619463920593, 0.08861096203327179],
    "sourceMoby": "0x01858a80",
}


def load_helper():
    spec = importlib.util.spec_from_file_location("rac1_state_helper", SAVESTATE_HELPER)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load {SAVESTATE_HELPER}")
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


def u16(memory: bytes, address: int) -> int:
    return struct.unpack_from("<H", memory, address)[0]


def f32(memory: bytes, address: int) -> float:
    return struct.unpack_from("<f", memory, address)[0]


def round_f32(value: float) -> float:
    return struct.unpack("<f", struct.pack("<f", value))[0]


def f32_word(value: float) -> int:
    return struct.unpack("<I", struct.pack("<f", value))[0]


def direct_jal_callers(memory: bytes, start: int, end: int, target: int) -> list[str]:
    callers: list[str] = []
    for address in range(start, end, 4):
        word = u32(memory, address)
        if word >> 26 != 0x03:
            continue
        resolved = ((address + 4) & 0xF0000000) | ((word & 0x03FFFFFF) << 2)
        if resolved == target:
            callers.append(f"0x{address:08x}")
    return callers


def verify_signatures(memory: bytes) -> list[dict[str, str]]:
    report = []
    for address, expected, meaning in SIGNATURES:
        actual = u32(memory, address)
        if actual != expected:
            raise RuntimeError(
                f"Instruction mismatch at 0x{address:08x}: "
                f"expected 0x{expected:08x}, got 0x{actual:08x}"
            )
        report.append({
            "address": f"0x{address:08x}",
            "word": f"0x{expected:08x}",
            "meaning": meaning,
        })
    return report
def count_live_class(memory: bytes, native_class: int) -> tuple[int, dict[str, int]]:
    count = 0
    states: dict[str, int] = {}
    for slot in range(LIVE_SCAN_SLOTS):
        address = LIVE_POOL + slot * LIVE_STRIDE
        if u16(memory, address + LIVE_CLASS) != native_class:
            continue
        count += 1
        state = memory[address + LIVE_STATE]
        key = f"0x{state:02x}"
        states[key] = states.get(key, 0) + 1
    return count, states


def build_report(memory: bytes, savestate: Path) -> dict[str, object]:
    signatures = verify_signatures(memory)
    class79_count, class79_states = count_live_class(memory, 0x79)
    class4a_count, class4a_states = count_live_class(memory, 0x4A)
    frame_scale = f32(memory, FRAME_SCALE_ADDRESS)
    vertical_delta = round_f32(frame_scale * 11.0)

    return {
        "schema": 2,
        "authority": AUTHORITY,
        "savestate": {
            "file": savestate.name,
            "sha256": sha256(savestate),
            "eeMemorySize": len(memory),
            "liveClass0x79Count": class79_count,
            "liveClass0x79StateHistogram": class79_states,
            "liveClass0x4aCount": class4a_count,
            "liveClass0x4aStateHistogram": class4a_states,
        },
        "item10Bomb": {
            "weaponNativeClass": "0x00c0",
            "weaponUpdate": f"0x{ITEM10_WEAPON_UPDATE:08x}",
            "projectileNativeClass": "0x0079",
            "constructor": f"0x{ITEM10_CONSTRUCTOR:08x}",
            "launchRoutine": f"0x{ITEM10_LAUNCH:08x}",
            "projectileUpdate": f"0x{ITEM10_UPDATE:08x}",
            "sourceOwnership": "projectile PVar+0x50 retains the firing weapon Moby",
            "launchState": 1,
            "contactState": 2,
            "observedTerminalState": "0xfe",
            "replacementStaging": "weapon tail can stage a replacement in the same accepted-fire update",
            "fireGateTicks": 20,
        },
        "motionAndExpiryBoundary": {
            "positionStep": "Moby position = Moby position + projectile PVar[0x00..0x08]",
            "frameScaleAddress": f"0x{FRAME_SCALE_ADDRESS:08x}",
            "frameScaleInAuthority": frame_scale,
            "verticalStepDeltaAtAuthorityScale": vertical_delta,
            "verticalStepDeltaFloat32Word": f"0x{f32_word(vertical_delta):08x}",
            "verticalStepLaw": "PVar+0x08 -= float32(frameScale * 11.0) each state-1 update",
            "longCountdown": {"offset": "+0x54", "launchValue": 300, "decrementHelper": "0x001fef98"},
            "shortCountdown": {"offset": "+0x6a", "launchValue": 30},
            "lifetimeConclusion": (
                "unresolved: the 30-derived countdown reaches zero while flight continues, and the "
                "controlled shot contacts at long-countdown 235 before any natural expiry is witnessed"
            ),
        },
        "damageHandoffs": {
            "item10Splash": {
                "contactRoutine": f"0x{CONTACT_QUERY:08x}",
                "distributor": f"0x{SPLASH_DISTRIBUTOR:08x}",
                "perVictimWriter": f"0x{SPLASH_VICTIM_WRITER:08x}",
                "nativeDamage": 2.0,
                "nativeDamageFlags": "0x00830000",
                "source": "class-0x79 projectile Moby",
                "victims": "candidate Mobies discovered by common contact query; source candidate skipped",
            },
            "separateDirectRepresentative": {
                "nativeClass": "0x004a",
                "writer": f"0x{DIRECT_DAMAGE_WRITER:08x}",
                "nativeDamage": 1.0,
                "nativeDamageFlags": "0x00010000",
                "source": "class-0x4a projectile Moby",
                "victim": "preselected Moby",
                "boundary": "this is not the item-10 Bomb carrier",
            },
        },
        "ownershipAndFriendlyFiltering": {
            "item10SourcePvarOffset": "+0x50",
            "commonSelfFilter": "contact candidates equal to source Moby are skipped",
            "friendlyFilterBoundary": (
                "no broader team/faction/ally immunity is promoted; remaining bitmask/filter logic "
                "inside the common routine is unresolved"
            ),
        },
        "environmentCollisionPattern": {
            "sharedWorldQuery": f"0x{WORLD_QUERY:08x}",
            "item10LaunchCallers": direct_jal_callers(
                memory, ITEM10_LAUNCH, ITEM10_UPDATE, WORLD_QUERY
            ),
            "item10UpdateCallers": direct_jal_callers(
                memory, ITEM10_UPDATE, ITEM10_UPDATE_END, WORLD_QUERY
            ),
            "class0x4aUpdateCallers": direct_jal_callers(
                memory, CLASS4A_UPDATE, CLASS4A_UPDATE_END, WORLD_QUERY
            ),
            "boundary": (
                "call sites prove shared world-contact querying, not whether the primitive is "
                "a ray, segment, sweep, or expanded shape"
            ),
        },
        "genericContract": {
            "shared": [
                "source-owned native damage transport",
                "direct preselected-victim damage records",
                "source-aware contact-volume handoff with source-self exclusion",
                "shared world-contact query pattern",
                "position-plus-step ballistic recurrence where the owning projectile supplies the step",
            ],
            "callerOwnedOrUnresolved": [
                "projectile launch-vector initialization",
                "terminal lifetime/expiry law",
                "contact geometry/radius",
                "damage scalar and flags",
                "team/faction/friendly filtering beyond source-self exclusion",
                "weapon-specific state machines and unusual effects",
            ],
        },
        "controlledLiveWitness": CONTROLLED_LIVE_WITNESS,
        "instructionEvidence": signatures,
        "notes": [
            "No ISO, executable, EE-memory, savestate, Moby, or PVar payload bytes are emitted.",
            "Instruction witnesses are isolated 32-bit words retained only to make semantic claims reproducible.",
            "The managed live witness stores only derived scalar/state observations and addresses.",
            "Class 0x4a remains useful as a direct-damage representative but is not the item-10 Bomb projectile.",
        ],
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--savestate", required=True, type=Path)
    parser.add_argument("--zstd-dll", required=True, type=Path)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()

    helper = load_helper()
    memory = helper.read_zip_entry(args.savestate, "eeMemory.bin", args.zstd_dll)
    report = build_report(memory, args.savestate)
    rendered = json.dumps(report, indent=2) + "\n"
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")


if __name__ == "__main__":
    main()
