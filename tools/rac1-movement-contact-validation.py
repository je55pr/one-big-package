#!/usr/bin/env python3
"""Reduce retained R&C1 movement/contact evidence into one acceptance summary."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path


def _read(root: Path, name: str) -> dict:
    return json.loads((root / name).read_text(encoding="utf-8"))


def build_summary(generated: Path) -> dict[str, object]:
    controller = _read(generated, "rac1-ratchet-movement-controller.json")
    slope = _read(generated, "rac1-ground-contact-slope.json")
    edge = _read(generated, "rac1-ground-edge-fall.json")
    support = _read(generated, "rac1-ground-support-admission.json")
    stop = _read(generated, "rac1-ground-slope-stop.json")
    special = _read(generated, "rac1-special-surface-reachability.json")

    ground_accel = float(controller["groundPlanar"]["accelerationPerTick"])
    run_cap = float(controller["groundPlanar"]["maximumStep"])
    running_jump = controller["runningJump100ms"]
    jump_horizontal_error = abs(
        float(running_jump["modelHorizontal"]) -
        float(running_jump["observedHorizontal"])
    )
    jump_air_time_error = abs(
        float(running_jump["modelSampledAirSeconds"]) -
        float(running_jump["observedAirSeconds"])
    )

    recurrences = controller["turning"]["recurrences"]
    yaw_max_rms = max(float(row["rmsResidualRad"]) for row in recurrences.values())

    slope_error = float(slope["finalMagnitudeError"]["maxAbs"])
    edge_accel = float(edge["edgeFallAccelerationPerTick"])
    contact_limit = float(support["contactMetricLimit"])
    support_angle = float(support["ordinarySupportMaxAngleRadians"])
    stationary_frames = int(stop["stationaryFrames"])
    surface_census = special["decodedCollisionCensus"]
    veldin_surface = surface_census["veldinOrdinaryControl"]
    wade_surface = surface_census["wadeWitness"]
    mud_surface = surface_census["mudWitness"]
    ice_surface = surface_census["iceWitness"]
    conveyor_level = surface_census["conveyorLevelWitness"]

    checks = {
        "groundAccelerationMatches1Over480": abs(ground_accel - 1 / 480) < 1e-15,
        "runCapMatchesRecoveredValue": abs(run_cap - 0.09500919) < 1e-12,
        "yawRecurrenceMaxRmsBelow1e6": yaw_max_rms < 1e-6,
        "runningJumpHorizontalErrorBelow1e4": jump_horizontal_error < 1e-4,
        "runningJumpAirTimeErrorBelowOneTick": jump_air_time_error <= (1 / 60) + 1e-9,
        "slopeRunMagnitudeMaxErrorBelow1e5": slope_error < 1e-5,
        "edgeAccelerationMatches25Over3600": abs(edge_accel - 25 / 3600) < 1e-15,
        "supportAngleMatchesRetailF32FiftyDegrees": abs(
            math.degrees(support_angle) - 50.0000002530119
        ) < 1e-9,
        "contactMetricMatchesRetailF32Point02": abs(
            contact_limit - 0.019999999552965164
        ) < 1e-15,
        "stationarySlopeTailAtLeast45Ticks": stationary_frames >= 45,
        "veldinHasNoRecoveredAlternateSurfaceClass": (
            int(veldin_surface["class0"]) == 0 and
            int(veldin_surface["class3"]) == 0 and
            int(veldin_surface["class7"]) == 0
        ),
        "novalisHasDecodedWadeSurfaces": int(wade_surface["triangles"]) == 3936,
        "aridiaHasDecodedMudSurfaces": int(mud_surface["triangles"]) == 1975,
        "hovenHasDecodedIceSurfaces": int(ice_surface["triangles"]) == 2777,
        "quartuHasRecoveredClass1250Population": (
            int(conveyor_level["class1250AuthoredInstances"]) == 18
        ),
        "class1250BiasMatches1Over24": abs(
            float(conveyor_level["class1250PreTransformBiasPerTick"]) - (1 / 24)
        ) < 1e-15,
    }
    failed = [name for name, passed in checks.items() if not passed]
    if failed:
        raise RuntimeError(f"movement/contact acceptance checks failed: {failed}")

    return {
        "schema": 1,
        "authority": controller["authority"],
        "status": "passed",
        "retailVsModel": {
            "groundAccelerationPerTick": ground_accel,
            "runCapNativeUnitsPerTick": run_cap,
            "yawMaximumRmsResidualRad": yaw_max_rms,
            "runningJumpHorizontalErrorNativeUnits": jump_horizontal_error,
            "runningJumpAirTimeErrorSeconds": jump_air_time_error,
            "slopeRunMagnitudeMaximumErrorNativeUnits": slope_error,
            "edgeFallAccelerationPerTick": edge_accel,
            "ordinarySupportMaxAngleRadians": support_angle,
            "ordinarySupportMaxAngleDegrees": math.degrees(support_angle),
            "ordinaryContactMetricLimit": contact_limit,
            "stationarySupportedSlopeTailTicks": stationary_frames,
        },
        "retailReachability": {
            "veldinAlternateSurfaceClasses": [],
            "novalisWadeTriangles": int(wade_surface["triangles"]),
            "aridiaMudTriangles": int(mud_surface["triangles"]),
            "hovenIceTriangles": int(ice_surface["triangles"]),
            "quartuClass1250AuthoredInstances": int(
                conveyor_level["class1250AuthoredInstances"]
            ),
            "quartuClass1250PreTransformBiasPerTick": float(
                conveyor_level["class1250PreTransformBiasPerTick"]
            ),
        },
        "portableRuntimeContracts": {
            "dynamicSupport": (
                "Rac1DynamicSupportSession tests preserve current contact, "
                "persistent support, valid-anchor carry, support switches and rotation carry."
            ),
            "conveyor": (
                "Rac1PlayerContactRuntime tests keep class-1250 1/24 pre-transform bias "
                "separate from support carry and tangential transfer."
            ),
            "specialSurfaces": (
                "Rac1SurfaceActionRouting tests select shallow-water WADE, mud, ice "
                "and class-2/class-0xAD Magneboot intents; ordinary movement fails closed "
                "where alternate motion laws remain unrecovered."
            ),
        },
        "evidence": [
            "rac1-ratchet-movement-controller.json",
            "rac1-ground-contact-slope.json",
            "rac1-ground-edge-fall.json",
            "rac1-ground-support-admission.json",
            "rac1-ground-slope-stop.json",
            "rac1-special-surface-reachability.json",
            "Rac1GroundProjectionRetailTests",
            "Rac1SpecialSurfaceReachabilityTests",
            "Rac1DynamicSupportSessionTests",
            "Rac1PlayerContactRuntimeTests",
            "Rac1SurfaceActionRoutingTests",
        ],
        "checks": checks,
        "boundary": (
            "Godot still executes collision sweeps. MaxSlides is not claimed as a native "
            "R&C1 constant; the live LEVEL0 smoke separately proves the configured cap "
            "does not saturate on the validated ordinary traversal."
        ),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--generated",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "research" / "generated",
    )
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()

    report = build_summary(args.generated)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(args.out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
