#!/usr/bin/env python3
"""Payload-free static evidence for the opening Veldin enemy implementation."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("overlay_probe", Path(__file__).with_name("rac1-level-overlay-probe.py"))
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)

BLOCKS = {
    "class749Update": (0x2D4610, 0x2D5160),
    "class749Damage": (0x2D5160, 0x2D549C),
    "class749Navigation": (0x2D54C8, 0x2D56DC),
    "class749IdleRoaming": (0x261630, 0x261858),
    "class1440Update": (0x2E0B88, 0x2E1678),
    "class1440Damage": (0x2E1678, 0x2E1AA8),
    "class1440AttackSlots": (0x2E1F28, 0x2E2110),
    "meleeDescriptor": (0x2598B0, 0x259BC8),
    "queryDamageRecordBridge": (0x1F0C40, 0x1F0D60),
    "scalarSteering": (0x25B970, 0x25BB38),
    "angularSteering": (0x25BE00, 0x25BFDC),
    "sequenceSelection": (0x257880, 0x257B50),
    "playerDamageIntake": (0x210CE8, 0x211248),
    "ordinaryPlayerHurtEntry": (0x2265E0, 0x22668C),
    "playerNativeTimerUpdate": (0x221604, 0x2216B0),
    "integerTimerDecrement": (0x1FEF68, 0x1FEF98),
    "nativeDistanceHelpers": (0x1FF450, 0x1FF4B8),
    "nativeVectorLength": (0x1FF3D8, 0x1FF408),
    "reactionLaunch": (0x25C558, 0x25C698),
    "reactionStep": (0x25C698, 0x25C758),
    "class749ReactionParameters": (0x161970, 0x161980),
    "class1440ReactionParameters": (0x161BD4, 0x161BE4),
}

SIGNATURES = {
    0x2D46F8: (0x24050001, "class749 initialization selects rest sequence1"),
    0x2D4ADC: (0x24050004, "class749 state5 selects locomotion4 for idle roaming"),
    0x2D4B14: (0x0C09858C, "class749 state5 invokes the roaming movement routine"),
    0x2D4FEC: (0x24030001, "class749 return-home completion selects rest sequence1"),
    0x2E0D20: (0x24630010, "class1440 initialization addresses entrance point0 after the path header"),
    0x2E0D24: (0x78620000, "class1440 initialization loads entrance point0"),
    0x2E0D28: (0x7E220000, "class1440 initialization replaces the Moby position with entrance point0"),
    0x2E0F2C: (0x0C07FCF6, "class1440 waypoint arrival uses full XYZ vector length"),
    0x2E0FA4: (0x02E0202D, "class1440 entrance destination is the live Moby position pointer"),
    0x2E0FAC: (0x0C07FC9E, "class1440 entrance directly adds displacement to position without a contact solver"),
    0x2D51CC: (0x3C050033, "class749 native damage mask 0x00330000"),
    0x2E16E4: (0x3C050033, "class1440 uses the same weapon damage mask"),
    0x2D5288: (0x46070001, "class749 health subtracts consumed native damage"),
    0x2D5334: (0x0C095D1C, "class749 emits its authored bolt reward on lethal intake"),
    0x2598BC: (0x00A0B02D, "melee emitter preserves the query origin pointer in s6"),
    0x25998C: (0x02C0202D, "sphere query receives the preserved joint origin, not the descriptor impulse vector"),
    0x2599A8: (0x0C07CA1A, "melee emitter calls the sphere query at the joint origin"),
    0x259A04: (0x03A0302D, "joint wrapper writes its world position to the stack"),
    0x259A5C: (0x03A0282D, "joint wrapper passes that position as the emitter origin"),
    0x2E0CB4: (0xE6800020, "class1440 script initializes health, overriding the authored placement"),
    0x2E1784: (0x46060001, "class1440 subtracts consumed damage"),
    0x2E1850: (0x0C095D1C, "class1440 emits its authored reward on lethal intake"),
    0x2E1238: (0x0C093DCA, "class1440 resolves semantic socket0"),
    0x2E12AC: (0xE7B4008C, "class1440 stores the one-point descriptor scalar"),
    0x2E1310: (0xAFA20084, "class1440 admits active contact after animation position1"),
    0x1F0CCC: (0x7A240000, "query loads descriptor first quadword"),
    0x1F0CD0: (0x7A250010, "query loads descriptor second quadword"),
    0x1F0CDC: (0x7C640010, "query stores descriptor at damage record+0x10"),
    0x1F0CE0: (0x7C650020, "query stores descriptor second quadword at record+0x20"),
    0x22666C: (0x0C07FBC8, "ordinary hurt entry converts the protection duration"),
    0x22665C: (0x2404004D, "ordinary hurt duration77"),
    0x226678: (0xAE0201C0, "ordinary hurt stores protection at P+0x1c0"),
    0x210D34: (0x8CC201C0, "common player intake reads the protection timer"),
    0x210D40: (0x1440013B, "nonzero protection timer rejects the incoming descriptor"),
    0x22163C: (0x0C07FBDA, "ordinary update decrements an integer timer"),
    0x221640: (0x260401C0, "the decremented timer is P+0x1c0"),
    0x1FEF78: (0x2021FFFF, "integer timer decrements by one per native update"),
    0x1FF490: (0x4B82086C, "class1440 distance helper subtracts native XY components"),
}


def build_report(path):
    with Path(path).open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
        if digest != PROBE.ISO_SHA256:
            raise ValueError("ISO differs from NTSC-U authority")
        data = PROBE.read_veldin_overlay(stream, stream.seek(0, 2))
    if hashlib.sha256(data).hexdigest() != PROBE.OVERLAY_SHA256:
        raise ValueError("Veldin overlay differs from authority")
    overlay = PROBE.Overlay(data)
    for address, (word, _) in SIGNATURES.items():
        if overlay.word(address) != word:
            raise ValueError(f"Instruction signature differs at {address:#x}: {overlay.word(address):#x}")
    return {
        "schema": 1,
        "authority": {"build": "rac1-ntscu-original", "levelId": 0, "isoSha256": digest, "overlaySha256": PROBE.OVERLAY_SHA256},
        "blocks": {name: {"start": f"0x{start:08x}", "end": f"0x{end:08x}", "sha256": hashlib.sha256(overlay.read(start, end - start)).hexdigest()}
                   for name, (start, end) in BLOCKS.items()},
        "signatures": [{"address": f"0x{address:08x}", "word": f"0x{word:08x}", "meaning": meaning}
                       for address, (word, meaning) in SIGNATURES.items()],
        "class749": {"placements": list(range(143, 159)), "health": 1, "rewardCentre": 7, "visualScale": .75,
                     "restSequence": 1, "roamingSequence": 4,
                     "pursuitSpeedPerTick": 4 / 60, "accelerationPerTick": 8 / 3600, "decelerationPerTick": 6 / 3600,
                     "attackSequence": 5, "attackPosition": 34, "contactRadius": .333, "contactOriginSemanticJoint": 0,
                     "descriptorPlanarImpulse": 1, "descriptorVerticalImpulse": 1, "damage": 1,
                     "lethalSequences": [6, 7]},
        "class1440": {"placements": list(range(189, 198)), "authoredHealth": 1, "scriptHealth": 2, "rewardCentre": 22,
                      "entranceInitialPosition": "path point0 replaces editor placement during script initialization",
                      "entranceFirstDestinationIndex": 1, "waypointArrivalDistance": "native XYZ Euclidean distance",
                      "entrancePositionUpdate": "direct XYZ position assignment; no shared navigation contact query",
                      "acquisitionPlanarDistanceInclusive": 24, "sharedAttackSlots": 3, "entranceSpeedPerTick": 12 / 60,
                      "accelerationPerTick": 8 / 3600, "decelerationPerTick": 16 / 3600,
                      "windupSequence": 5, "sweepSequence": 6, "contactAfterPositionExclusive": 1,
                      "contactLength": 5, "damage": 1, "descriptorFlags": "0x00010001", "maximumFireCycles": 4,
                      "contactRecoverySequence": 7, "timeoutRecoverySequence": 12, "hurtSequence": 8,
                      "lethalSequence": 11, "deathFragmentClasses": [1933, 1934, 1935, 1963]},
        "limits": ["Static executable evidence, not a fresh live emulator replay.",
                   "OBP uses retail rest sequence1 for stationary class749; the native state5 roaming motion remains unimplemented.",
                   "Godot capsules, wall sliding, skeletal vertex interpolation, particle presentation and random call order are adapters.",
                   "Full native reaction physics and Ratchet hurt presentation/combat restart are separate remaining work."]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--iso", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    report = build_report(args.iso)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("Verified opening Veldin enemy source evidence.")
