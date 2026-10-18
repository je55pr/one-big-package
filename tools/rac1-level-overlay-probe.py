#!/usr/bin/env python3
"""Verify Veldin's gameplay overlay and Hoven's crouch-surface branch offline.

The report contains addresses, hashes and derived behavior only. Retail payloads
remain in memory. This is static code evidence, not a live gameplay validation.
"""
import argparse
import hashlib
import importlib.util
import json
import struct
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ISO_SHA256 = "ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d"
OVERLAY_SHA256 = "eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804"
HOVEN_OVERLAY_SHA256 = "ffe8c2b5dcb8e6d02d131037472936a766cbf9aabeeeb809a17057fa69515622"
EE_SIZE = 32 * 1024 * 1024
SECTOR_SIZE = 0x800


def checked_range(offset, size, limit, label):
    if offset < 0 or size <= 0 or offset > limit or size > limit - offset:
        raise ValueError(f"{label} is outside its bounded source")


def read_at(stream, offset, size, limit):
    checked_range(offset, size, limit, "disc read")
    stream.seek(offset)
    data = stream.read(size)
    if len(data) != size:
        raise ValueError("short disc read")
    return data


def read_level_overlay(stream, disc_size, level_id):
    # These directory boundaries are independently retained in Rac1DiscIndex
    # and Rac1LevelData. Resolve native identity rather than assuming slot == ID.
    index = read_at(stream, 1500 * SECTOR_SIZE, 0x2960, disc_size)
    if struct.unpack_from("<2I", index) != (1, 0x2960):
        raise ValueError("unsupported R&C1 disc index")
    matches = []
    for slot in range(19):
        header_lba, second_word = struct.unpack_from("<2I", index, 0x28C8 + slot * 8)
        if not header_lba:
            if second_word:
                raise ValueError("present level has zero header LBA")
            continue
        header = read_at(stream, header_lba * SECTOR_SIZE, 0x28, disc_size)
        identity, header_size = struct.unpack_from("<2I", header)
        if header_size != 0x2434:
            raise ValueError("unsupported level header")
        if identity == level_id:
            matches.append(header)
    if len(matches) != 1:
        raise ValueError(f"expected exactly one level {level_id} header")
    header = matches[0]
    data_lba, data_sectors = struct.unpack_from("<2I", header, 8)
    data_offset, data_size = data_lba * SECTOR_SIZE, data_sectors * SECTOR_SIZE
    checked_range(data_offset, data_size, disc_size, "level-data range")
    checked_range(0, 0x58, data_size, "level-data directory")
    directory = read_at(stream, data_offset, 0x58, disc_size)
    overlay_offset, overlay_size = struct.unpack_from("<2i", directory)
    if overlay_offset < 0x58 or overlay_size > EE_SIZE:
        raise ValueError("invalid level overlay range")
    checked_range(overlay_offset, overlay_size, data_size, "level overlay")
    return read_at(stream, data_offset + overlay_offset, overlay_size, disc_size)


def read_veldin_overlay(stream, disc_size):
    return read_level_overlay(stream, disc_size, 0)


class Overlay:
    def __init__(self, data):
        self.data = bytes(data)
        self.sections = []
        self.entry_point = None
        offset = 0
        while offset < len(data):
            checked_range(offset, 16, len(data), "overlay section header")
            address, size, kind, entry = struct.unpack_from("<4I", data, offset)
            source = offset + 16
            checked_range(source, size, len(data), "overlay section payload")
            checked_range(address, size, EE_SIZE, "overlay section destination")
            if address % 4 or size % 4 or kind not in (1, 8):
                raise ValueError("unsupported overlay section header")
            if self.entry_point is None:
                self.entry_point = entry
            elif entry != self.entry_point:
                raise ValueError("inconsistent overlay entry point")
            if any(address < old["address"] + old["size"] and
                   old["address"] < address + size for old in self.sections):
                raise ValueError("overlapping overlay destinations")
            self.sections.append({
                "headerOffset": offset, "dataOffset": source,
                "address": address, "size": size, "type": kind,
            })
            offset = source + size
        if not self.sections:
            raise ValueError("empty overlay")
        # Unmapped runtime state is deliberately unavailable, never zero-filled.
        self.read(self.entry_point, 4)

    def read(self, address, size):
        checked_range(address, size, EE_SIZE, "mapped read")
        for section in self.sections:
            relative = address - section["address"]
            if 0 <= relative <= section["size"] - size:
                offset = section["dataOffset"] + relative
                return self.data[offset:offset + size]
        raise ValueError(f"address range 0x{address:08x}+{size} is not in one overlay section")

    def word(self, address):
        return struct.unpack("<I", self.read(address, 4))[0]


def retained_movement_signatures():
    # Reuse the signatures established from loaded retail memory; do not create
    # a second, independently drifting list of expected movement instructions.
    spec = importlib.util.spec_from_file_location(
        "rac1_loaded_movement_probe", ROOT / "tools/rac1-savestate-movement-probe.py")
    probe = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(probe)
    return probe.ANALOGUE_SIGNATURES


def verify_signatures(overlay, signatures):
    for address, expected in signatures.items():
        if overlay.word(address) != expected:
            raise ValueError(f"loaded movement signature differs at 0x{address:08x}")
    return len(signatures)


def immediate_float(overlay, address):
    """Decode only the traced lui at / optional ori at / mtc1 at pattern."""
    upper = overlay.word(address)
    if upper >> 16 != 0x3C01:
        raise ValueError(f"expected float lui at 0x{address:08x}")
    bits = (upper & 0xFFFF) << 16
    following = overlay.word(address + 4)
    if following >> 16 == 0x3421:
        bits |= following & 0xFFFF
        following = overlay.word(address + 8)
    # Ignore only the destination FPR bits, not opcode/source/reserved bits.
    if following & ~0xF800 != 0x44810000:
        raise ValueError(f"expected float mtc1 after 0x{address:08x}")
    return struct.unpack("<f", struct.pack("<I", bits))[0]


def ordinary_locomotion_report(overlay, gp, tick_seconds):
    """Retain the dataflow manually traced in RAC1_LOCOMOTION_ANIMATION.md."""
    blocks = {
        "entry": (0x2232D4, 0x223598),
        "update": (0x22B434, 0x22C2E4),
        "selectorAndRate": (0x228C18, 0x228FA8),
        "scaledTickCount": (0x1FEF20, 0x1FEF48),
        "frameAdvance": (0x232670, 0x2329D0),
        "regularUpdateOrder": (0x207184, 0x2071D0),
        "inputMovementCounterOrder": (0x211670, 0x211830),
        "entryBlendHistory": (0x2234C0, 0x223560),
        "speedTable": (0x17BDB8, 0x17BDD8),
        "phaseTables": (gp - 0x7538, gp - 0x7520),
    }
    table = struct.unpack("<8f", overlay.read(0x17BDB8, 32))
    offsets = struct.unpack("<4i", overlay.read(gp - 0x7538, 16))
    frames = struct.unpack("<2i", overlay.read(gp - 0x7528, 8))
    f32 = lambda value: struct.unpack("<f", struct.pack("<f", value))[0]
    rates = [{
        "sequence": sequence,
        "speedMultiplier": immediate_float(overlay, multiplier),
        "minimum": immediate_float(overlay, minimum),
        "maximum": immediate_float(overlay, maximum),
    } for sequence, multiplier, minimum, maximum in (
        (3, 0x228EFC, 0x228F04, 0x228F28),
        (4, 0x228F3C, 0x228F44, 0x228F6C),
    )]
    update_calls = verified_update_calls(overlay)
    return {
        "scope": "mode 0, ordinary action 2, substate 0/1; caller eligibility already satisfied",
        "entryDispatchTarget": f"0x{overlay.word(0x1E8030 + 2 * 4):08x}",
        "updateDispatchTarget": f"0x{overlay.word(0x1E82B0 + 2 * 4):08x}",
        "blocks": {name: {
            "range": [f"0x{start:08x}", f"0x{end:08x}"],
            "sha256": hashlib.sha256(overlay.read(start, end - start)).hexdigest(),
        } for name, (start, end) in blocks.items()},
        "speedTableAddress": "0x0017bdb8",
        "speedThresholds": {
            "sequence3To4StrictlyAbove": table[1],
            "sequence4To3StrictlyBelow": table[4],
            "secondsPerUpdateAtBoot": tick_seconds,
            "sequence3To4StepF32": f32(table[1] * tick_seconds),
            "sequence4To3StepF32": f32(table[4] * tick_seconds),
        },
        "guard": "P+0x1a0 > scaledTickCount(4) and signed16(P+0x3bc) == 0",
        "rateField": "P+0xa90 = clamp(P+0x194 * multiplier, minimum, maximum)",
        "rates": rates,
        "entryNextFrames": {"3": frames[0], "4": frames[1]},
        "updateOrder": [
            {"call": f"0x{address:08x}", "target": f"0x{target:08x}", "role": role}
            for address, target, role in update_calls[:4]
        ],
        "inputCounterCall": {"call": f"0x{update_calls[4][0]:08x}",
                             "target": f"0x{update_calls[4][1]:08x}"},
        "inputMovementCall": {"call": f"0x{update_calls[5][0]:08x}",
                              "target": f"0x{update_calls[5][1]:08x}"},
        "defaultEntryBlendTicks": 8,
        "entryBlendOverridesInOrder": [
            {"condition": "P+0x22a8 == 1", "ticks": 12},
            {"condition": "P+0x209c == 0x23", "ticks": 15},
            {"condition": "P+0x209c in [6, 0x2d]", "ticks": 18},
            {"condition": "P+0x20a0 == 4 and P+0x418 > scaledTickCount(5)", "ticks": 13},
        ],
        "transitionFrameOffsets": [list(offsets[:2]), list(offsets[2:])],
        "transitionNextFrame": "((oldNextFrame * newFrameCount) / oldFrameCount + offset) % newFrameCount; integer arithmetic",
        "loopPolicy": "wrap within sequence; entry clears subrange loop; no timed 3-to-4 promotion",
        "limits": [
            "Static dataflow recovery, not execution of the native routine or a fresh retail trace.",
            "Threshold products use host binary32; this is not a complete R5900 FPU model.",
            "Boot timing values do not prove all later runtime timing configurations.",
            "Caller support/control guards and entry blending must be preserved by any integration.",
        ],
    }


def verified_update_calls(overlay):
    calls = (
        (0x207184, 0x232670, "animation advance using the previously assigned multiplier"),
        (0x20718C, 0x211670, "input, movement and counter update; calls movement at 0x211728, counters at 0x211814"),
        (0x2071B4, 0x229B70, "action update, including ordinary animation selection/rate assignment"),
        (0x2071C8, 0x216F10, "delayed-action timer processing; not the movement executor"),
        (0x211814, 0x2215C8, "counter update including substate age"),
        (0x211728, 0x217970, "movement executor before action update/animation selection"),
    )
    for address, target, _ in calls:
        if overlay.word(address) != 0x0C000000 | (target >> 2):
            raise ValueError(f"regular update call differs at 0x{address:08x}")
    return calls


def crouch_and_stop_report(overlay, tick_seconds):
    blocks = {
        "crouchEntry": (0x2238E0, 0x223A1C),
        "crouchRelease": (0x22C45C, 0x22C4C0),
        "crouchTurn": (0x217BF0, 0x217E08),
        "crouchDecayCaller": (0x217F4C, 0x217FCC),
        "residualMotionDecay": (0x216710, 0x2167CC),
        "contactMotionProjection": (0x213AE0, 0x213B5C),
        "separateExternalTransport": (0x213C64, 0x213D08),
        "vectorSetLength": (0x1FF500, 0x1FF550),
        "vectorDot": (0x1FF398, 0x1FF3B8),
        "vectorMagnitudes": (0x1FF3D8, 0x1FF430),
        "loopRangeSetter": (0x2325E0, 0x232670),
        "blendSelector": (0x2323B8, 0x2325E0),
        "standUpCurve": (0x17BE54, 0x17BE94),
        "defaultStopSelector": (0x2237C8, 0x223840),
        "ordinaryRelease": (0x22B934, 0x22BAE8),
        "recentInputReader": (0x266E80, 0x266EF0),
        "delayedInputReader": (0x211830, 0x21186C),
        "delayedInputOrdering": (0x211690, 0x2116C0),
        "historyMagnitudeBase": (0x26724C, 0x267258),
        "historyMagnitudeWriter": (0x267650, 0x26769C),
        "historyAdvance": (0x2677BC, 0x267808),
        "stopUpdate": (0x22C2E4, 0x22C45C),
        "evaluatedAnimationTime": (0x24EDC8, 0x24EEBC),
        "ordinaryStandingCurve": (0x17BDF0, 0x17BE1C),
        "restartEntryMotion": (0x2232D4, 0x2233A4),
        "idleRestartInput": (0x22A720, 0x22A750),
    }
    f32 = lambda value: struct.unpack("<f", struct.pack("<f", value))[0]
    threshold = immediate_float(overlay, 0x217CC8)
    divisor = immediate_float(overlay, 0x217D4C)
    return {
        "scope": "mode 0, ordinary action-2 release, action-3 stop/restart and action 4; special equipment paths excluded",
        "blocks": {name: {
            "range": [f"0x{start:08x}", f"0x{end:08x}"],
            "sha256": hashlib.sha256(overlay.read(start, end - start)).hexdigest(),
        } for name, (start, end) in blocks.items()},
        "crouch": {
            "entryDispatchTarget": f"0x{overlay.word(0x1E8040):08x}",
            "updateDispatchTarget": f"0x{overlay.word(0x1E82C0):08x}",
            "entry": {"sequence": 13, "nextFrame": 2,
                      "blendTicksFromStandingPreviousPose": 9, "blendTicksOtherwise": 14},
            "requestedLoop": [3, 18],
            "loopLimit": "setter rejects end >= decoded frame count; sequence 13 has 15 frames",
            "turnEntryMagnitudeStrictlyAbove": immediate_float(overlay, 0x217C08),
            "turnAngularVelocityMagnitudeStrictlyAbove": f32(threshold * tick_seconds),
            "turnRateDivisor": f32(divisor * tick_seconds),
            "turnRateClamp": [immediate_float(overlay, 0x217D58), immediate_float(overlay, 0x217D84)],
            "turnSequences": {"positiveYawVelocity": 14, "negativeYawVelocity": 15, "zeroYawVelocity": 13},
            "turnBlendTicks": 5, "returnToCrouchBlendTicks": 7,
            "smallNonzeroYawVelocity": "retain sequence and multiplier",
            "residualMotion": {
                "coefficient": immediate_float(overlay, 0x217F78),
                "iceCoefficient": immediate_float(overlay, 0x217F64),
                "surfaceSelector": "P+0x12e2: zero uses ordinary coefficient; nonzero uses ice coefficient",
                "scale": "multiply by native squared-update scalar at GP-0x7e90",
                "snapMagnitude": immediate_float(overlay, 0x216740),
                "entry": "copy P+0x110 into P+0x150; cap vector length at 7 * secondsPerUpdate",
                "decay": "zero vertical carry, subtract coefficient * timing scalar from planar magnitude; preserve heading",
                "hostLimit": "OBP retains measured 0.001802944 per-update decay; static nominal value is about 0.0018",
            },
            "contactMotion": {
                "inputRequest": "P+0xe0",
                "primaryDisplacement": "position after primary contact minus position at pass entry",
                "output": "P+0x110 = normalized(request) * max(dot(normalized(primaryDisplacement), request), 0)",
                "directionSource": "0x213b58 delay slot passes request (s4), not normalized displacement (s1)",
                "zeroLength": "set-length helper returns zero when its vector length is zero",
                "externalTransport": "P+0x90 applied separately after P+0x110 construction; final P+0x100 includes that pass",
                "entryCarry": "action 4 copies P+0x110 into P+0x150; later P+0x110 updates do not reseed held crouch",
            },
            "release": {"actionAgeStrictlyAbove": 11, "sequence": 0, "nextFrame": 0,
                        "blendArgument": immediate_float(overlay, 0x22C4B0),
                        "curveLength": overlay.word(0x15F6E4),
                        "curve": list(struct.unpack("<16f", overlay.read(0x17BE54, 64)))},
        },
        "defaultStop": {
            "sequence5": "previous pose sequence Moby+0x52 == 4 and 3 < P+0xaa8 < 16; otherwise sequence 6",
            "lowerExclusive": immediate_float(overlay, 0x2237EC),
            "upperExclusive": immediate_float(overlay, 0x223808),
            "nextFrame": 4, "blendTicks": 9,
            "timestamp": "P+0xaa8 is evaluated animation time, not an integer observer frame",
        },
        "ordinaryReleaseAndRestart": {
            "releaseInputStrictlyBelow": immediate_float(overlay, 0x22B948),
            "inputTiming": "P+0x229c is the prior P+0x1d20 vector magnitude capped at 1; computed before current axes are copied",
            "contactMagnitude": "P+0x160 is the 3D length of primary contact-derived P+0x110, not the controller scalar",
            "lowMotionCoefficient": immediate_float(overlay, 0x22B968),
            "highMotionCoefficient": immediate_float(overlay, 0x22B998),
            "motionScale": "multiply coefficients by seconds per update",
            "middleStop": "above low threshold and either below high threshold or history(4) < 0.8f: sequence 20, next frame 7, blend 9",
            "defaultStop": "above high threshold with history(4) >= 0.8f: ordinary action-3 entry selects 5/6, next frame 4, blend 9",
            "recentInputThreshold": immediate_float(overlay, 0x22B9C4),
            "history": "30 entries of unclamped conditioned-stick magnitude; write then advance head; read head - min(available, lookback)",
            "slowRelease": "at or below low threshold, action age > 25 and ordinary caller guard clear: standing 0 with preset -1",
            "standingCurve": list(struct.unpack("<11f", overlay.read(0x17BDF0, 44))),
            "restartInputStrictlyAbove": immediate_float(overlay, 0x22C384),
            "movingRestartInputStrictlyAbove": immediate_float(overlay, 0x22C438),
            "idleRestartInputStrictlyAbove": immediate_float(overlay, 0x22A720),
            "zeroScalarRestart": "action 0 with animation selection suppressed; ordinary idle update can re-enter locomotion",
            "terminalStop": "animation end-cycle bit enters standing before input restart checks",
            "evaluatedTime": "interpolate signed frame timestamp / 16 normally; snapshot blend uses target time + fraction; wrap uses current time + fraction",
            "limits": [
                "Ordinary callers only: equipment, aiming, combat and other action overrides excluded.",
                "Host contact data and scalar reconstruction remain approximations; movement release tables are unchanged.",
                "Restart entry also seeds/caps native scalar and clears it for an opposed-facing stop history; full motion ownership is not integrated by presentation.",
            ],
        },
    }


def crouch_surface_report(veldin, hoven, squared_update_scale):
    # The source blocks are traced separately: Hoven's surface-flag producer
    # differs from Veldin's. Only the coefficient-selection block is identical.
    levels = []
    for level_id, overlay, expected_hash, flag_start, flag_end, decay, carry, zero_fill in (
        (0, veldin, OVERLAY_SHA256, 0x20B960, 0x20BBA4, 0x217F4C, 0x216710, 0x1FF068),
        (12, hoven, HOVEN_OVERLAY_SHA256, 0x22DE30, 0x22DFB0, 0x238ED4, 0x2378A8, 0x221F78),
    ):
        if hashlib.sha256(overlay.data).hexdigest() != expected_hash:
            raise ValueError(f"level {level_id} overlay differs from retained authority")
        ordinary = immediate_float(overlay, decay + 0x2C)
        ice = immediate_float(overlay, decay + 0x18)
        if ordinary != 6.480000019073486 or ice != 1.7000000476837158:
            raise ValueError("crouch surface coefficients differ")
        # Both levels load the same native flag; do not infer it from a surface name.
        if overlay.word(decay + 0xC) != 0x920212E2:
            raise ValueError("crouch ice-flag reader differs")
        blocks = {
            "surfaceFlagSetup": (flag_start, flag_end),
            "coefficientSelection": (decay, decay + 0x54),
            "residualCarryDecay": (carry, carry + 0xBC),
            "zeroFillHelper": (zero_fill, zero_fill + 0x28),
        }
        levels.append({
            "levelId": level_id, "overlaySha256": expected_hash,
            "blocks": {name: {
                "range": [f"0x{start:08x}", f"0x{end:08x}"],
                "sha256": hashlib.sha256(overlay.read(start, end - start)).hexdigest(),
            } for name, (start, end) in blocks.items()},
            "ordinaryCoefficient": ordinary, "iceCoefficient": ice,
        })
    if levels[0]["blocks"]["coefficientSelection"]["sha256"] != levels[1]["blocks"]["coefficientSelection"]["sha256"]:
        raise ValueError("Veldin/Hoven crouch coefficient branches differ")
    f32 = lambda value: struct.unpack("<f", struct.pack("<f", value))[0]
    return {
        "scope": "action-4 residual carry braking; not the complete ice-slide action controller",
        "surfaceClass": 7, "iceFlag": "P+0x12e2",
        "flagProducer": "clear 16 bytes from P+0x12e0, then set P+0x12e2 to 1 for preceding surface class 7",
        "normalDefault": "zero ice flag selects 6.48f; no additional material-indexed coefficient in this branch",
        "coefficientRatioOrdinaryToIce": levels[0]["ordinaryCoefficient"] / levels[0]["iceCoefficient"],
        "nominalDecayPerUpdateF32": {
            "ordinary": f32(levels[0]["ordinaryCoefficient"] * squared_update_scale),
            "ice": f32(levels[0]["iceCoefficient"] * squared_update_scale),
        },
        "levels": levels,
        "limits": [
            "Both mapped overlays and their local branches are verified independently; addresses are level-specific.",
            "Hoven class-7 floor reachability is independently retained in rac1-special-surface-reachability.json.",
            "Other surface actions and contact corrections can change travel; the coefficient ratio is not a measured distance ratio.",
            "OBP already uses the ordinary measured decay; ice action execution remains unsupported.",
            "No extra ordinary friction multiplier was found in this branch; the full contact executor remains incomplete.",
        ],
    }


def boot_context(iso_path):
    # Use the existing boot reader only for ABI/small-data values outside the
    # overlay. Never substitute its same-VA instructions for gameplay code.
    spec = importlib.util.spec_from_file_location(
        "rac1_boot_movement_probe", ROOT / "tools/rac1-movement-probe.py")
    probe = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(probe)
    exe = probe.read_root_iso_file(Path(iso_path), "SCUS_971.99")
    digest = hashlib.sha256(exe).hexdigest()
    if digest != probe.EXE_SHA256:
        raise ValueError("boot executable differs from the retained authority")
    gp = probe.read_reginfo_gp(exe)
    segments = probe.load_segments(exe)
    tick_word = probe.word_at_vaddr(exe, segments, 0x15ED6C)
    scale_word = probe.word_at_vaddr(exe, segments, gp - 0x7E98)
    squared_word = probe.word_at_vaddr(exe, segments, gp - 0x7E90)
    if (gp != 0x166C00 or tick_word != 0x3C888889 or scale_word != 0x3F800000 or
            squared_word != 0x3991A2B4):
        raise ValueError("boot ABI/timing constants differ from the traced context")
    return {
        "executableSha256": digest,
        "reginfoGp": f"0x{gp:08x}",
        "secondsPerUpdateAddress": "0x0015ed6c",
        "secondsPerUpdateInitial": struct.unpack("<f", struct.pack("<I", tick_word))[0],
        "tickScaleAddress": f"0x{gp - 0x7E98:08x}",
        "tickScaleInitial": 1.0,
        "squaredUpdateScaleAddress": f"0x{gp - 0x7E90:08x}",
        "squaredUpdateScaleInitial": struct.unpack("<f", struct.pack("<I", squared_word))[0],
    }


def build_report(iso_path):
    with Path(iso_path).open("rb") as stream:
        iso_hash = hashlib.file_digest(stream, "sha256").hexdigest()
        if iso_hash != ISO_SHA256:
            raise ValueError("ISO does not match the original NTSC-U authority")
        disc_size = stream.seek(0, 2)
        data = read_veldin_overlay(stream, disc_size)
        hoven_data = read_level_overlay(stream, disc_size, 12)
    overlay_hash = hashlib.sha256(data).hexdigest()
    if overlay_hash != OVERLAY_SHA256:
        raise ValueError("Veldin overlay hash differs from the retained authority")
    overlay = Overlay(data)
    signatures = retained_movement_signatures()
    verified = verify_signatures(overlay, signatures)
    boot = boot_context(iso_path)
    sections = [{
        "headerOffset": f"0x{s['headerOffset']:08x}",
        "dataOffset": f"0x{s['dataOffset']:08x}",
        "address": f"0x{s['address']:08x}",
        "size": s["size"], "type": s["type"],
    } for s in overlay.sections]
    return {
        "schema": 4,
        "authority": {"build": "rac1-ntscu-original", "serial": "SCUS-97199",
                      "isoSha256": iso_hash, "levelId": 0},
        "source": "disc index -> level 0 outer range 0 -> inner range 0",
        "overlaySha256": overlay_hash,
        "overlaySize": len(data),
        "entryPoint": f"0x{overlay.entry_point:08x}",
        "sections": sections,
        "loadedMovementSignaturesVerified": verified,
        "signatureAddresses": [f"0x{address:08x}" for address in sorted(signatures)],
        "bootContext": boot,
        "ordinaryLocomotion": ordinary_locomotion_report(
            overlay, int(boot["reginfoGp"], 16), boot["secondsPerUpdateInitial"]),
        "crouchAndStop": crouch_and_stop_report(overlay, boot["secondsPerUpdateInitial"]),
        "crouchSurfaceBraking": crouch_surface_report(
            overlay, Overlay(hoven_data), boot["squaredUpdateScaleInitial"]),
        "playerAnimation": {
            "selectorStore": "0x00232494",
            "advanceRoutine": "0x00232670",
            "defaultFrameWrapRange": ["0x00232930", "0x00232998"],
            "defaultFrameWrapSha256": hashlib.sha256(
                overlay.read(0x232930, 0x232998 - 0x232930)).hexdigest(),
        },
        "limits": [
            "Static initialized code/data only; runtime player state and current inputs are unavailable.",
            "The twelve signatures match retained loaded-memory evidence; no new gameplay trial was run.",
            "The default animation frame-wrap path does not alone establish every caller's playback policy.",
        ],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--iso", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if args.iso.resolve() == args.out.resolve():
        parser.error("the report must not overwrite its retail source")
    report = build_report(args.iso)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Verified Veldin overlay: {len(report['sections'])} sections, "
          f"{report['loadedMovementSignaturesVerified']} loaded movement signatures; "
          "Hoven crouch-surface branch verified")


if __name__ == "__main__":
    main()
