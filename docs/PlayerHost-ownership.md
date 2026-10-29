# PlayerHost boundary audit (#383)

Reviewed against `dev` at `c8dbfd4` (29 October 2026). Scope: the 18
`PlayerHost` partial declarations in `game/scripts`; `PlayerHostProfiles.cs`
is a separate profile declaration, not a nineteenth partial. **File size is
not an ownership criterion.**

## Boundary and concrete ownership

`PlayerHost : CharacterBody3D` is a **scene-lifetime engine adapter**, not
the owner of campaign progress, equipment inventory, actors, native traversal
rules, or diagnostic scenarios. `_PhysicsProcess` in
[`PlayerHost.cs`](../game/scripts/PlayerHost.cs) samples `_rawInput.Read`,
applies the optional `PlaytestInputFilter`, calls
`StepRetailDerivedMovement`, then `MoveRac1Contact`/`MoveAndSlide`,
records resulting contact, updates presentation, and steps the camera.
The native tick/scene conversion stays at 60 Hz; do not reorder this path.

| Partial | Legitimate bridge responsibility and concrete witness | Source / other owner |
| --- | --- | --- |
| [`PlayerHost.cs`](../game/scripts/PlayerHost.cs) | `_Ready`, `_UnhandledInput`, `_PhysicsProcess`, `_rawInput`, `Velocity`, `SafeMargin`, camera nodes and lifecycle; `PlaytestInputFilter` and `ScriptedInputProvider` are optional input ports | `Rac1RatchetMovementController`/`Rac1RatchetYawController` determine ordinary movement; [`GodotPlaytestHost`](../game/scripts/GodotPlaytestHost.cs) owns playtest filtering |
| [`PlayerHost.Capabilities.cs`](../game/scripts/PlayerHost.Capabilities.cs) | `ControlProfile` and `HasNativeTraversal` etc. are capability binding queries | [`PlayerHostProfiles`](../game/scripts/PlayerHostProfiles.cs) defines supported profiles |
| [`PlayerHost.Camera.cs`](../game/scripts/PlayerHost.Camera.cs) | `ApplyRac1CameraState` applies native snapshot; `UpdateCameraDistance` makes Godot obstruction ray; fallback yaw/pitch are presentation only | `Rac1OrdinaryCameraController.Step` selects recovered camera behavior |
| [`PlayerHost.CameraGeometry.cs`](../game/scripts/PlayerHost.CameraGeometry.cs) | `CameraGeometry.Sphere`/`Segment` call `DirectSpaceState` and filter Godot RIDs | `IRac1CameraGeometry` is a query contract, not gameplay policy |
| [`PlayerHost.Animation.cs`](../game/scripts/PlayerHost.Animation.cs) | `UpdateAnimationState` projects controller phase and body velocity into `PlayerAnimationFacts`; `TrackJump` measures diagnostics | `PlayerAnimationStateMachine.Update` and source animation selectors own decisions |
| [`PlayerHost.Diagnostics.cs`](../game/scripts/PlayerHost.Diagnostics.cs) | `UpdateHud`/`BuildInputDiagnostics` format observed inputs and source/controller state | No scenario orchestration; diagnostics belong in `game/diagnostics` |
| [`PlayerHost.MovementRepro.cs`](../game/scripts/PlayerHost.MovementRepro.cs) | `CaptureMovementRepro`/`RestoreMovementRepro` marshal local Godot pose, floor cache, camera and controller snapshots | [`MovementReproductionService`](../game/scripts/MovementReproductionService.cs) owns save/open orchestration and [`GodotMovementStateDiagnosticFixture`](../game/diagnostics/GodotMovementStateDiagnosticFixture.cs) drives fixture |
| [`PlayerHost.Rac1Equipment.cs`](../game/scripts/PlayerHost.Rac1Equipment.cs) | `ProbeRac1MovementContext` queries Godot rays; `ApplyPlayerEquipment` mirrors per-scene `Rac1MovementCapabilities` and selected pack; development preview is transient | [`Rac1PlayerGameplayBinding`](../game/scripts/Rac1PlayerGameplayBinding.cs) and [`PlayerEquipmentFacade`](../game/scripts/PlayerEquipmentFacade.cs) own selection/inventory |
| [`PlayerHost.Rac1Movement.cs`](../game/scripts/PlayerHost.Rac1Movement.cs) | `StepRetailDerivedMovement` packages `PlayerControlIntent` and engine contact; `RecordRac1OrdinaryContactMotion` feeds back measured displacement | `_rac1Movement.StepExtended`/`Step` and `Rac1OrdinaryGroundContactMotion` own native recurrence |
| [`PlayerHost.Rac1SmallSteps.cs`](../game/scripts/PlayerHost.Rac1SmallSteps.cs) | `TryMoveRac1SmallStep` does actual `PhysicsServer3D.BodyTestMotion` and rounded-capsule sweep; `Rac1HostMaximumStepRise` is explicitly a Godot contact envelope | Source support angle/contact profile supply bounds, not a second native stair rule |
| [`PlayerHost.Rac1Surfaces.cs`](../game/scripts/PlayerHost.Rac1Surfaces.cs) | Converts contact and input into `_rac1SurfaceMotion.Step` then applies `Velocity` | `Rac1SurfaceMovementSession` owns ice traversal motion |
| [`PlayerHost.Rac1Swimming.cs`](../game/scripts/PlayerHost.Rac1Swimming.cs) | `ProbeRac1WaterContact` uses Godot ray queries; `TryStepRac1Swimming` projects movement/sound results | `Rac1WaterMovementSession.Step` owns swim and drowning decisions |
| [`PlayerHost.Rac1LedgeProbe.cs`](../game/scripts/PlayerHost.Rac1LedgeProbe.cs) | Maps physical body queries to native probe geometry | `Rac1LedgeProbe` owns candidate geometry/admission rules |
| [`PlayerHost.Rac1WallProbe.cs`](../game/scripts/PlayerHost.Rac1WallProbe.cs) | Samples scene walls and maps hits into source wall facts | Native wall traversal admission belongs to R&C1 player contracts |
| [`PlayerHost.Rac1Ledges.cs`](../game/scripts/PlayerHost.Rac1Ledges.cs) | `ProbeRac1Ledge` supplies scene facts, `_rac1Ledge.Step` produces motion and `Velocity` receives it | [`Rac1LedgeTraversalSession`](../src/OBP.RAC1/Player/Traversal/Rac1LedgeTraversalSession.cs) now owns jump-press age, grab cooldown, and exit reset |
| [`PlayerHost.Rac1Swingshot.cs`](../game/scripts/PlayerHost.Rac1Swingshot.cs) | Per-world authored target cache, `Rac1TetherLineOfSight` ray, rope mesh, native-to-scene motion | `Rac1TraversalEquipmentSwing`, `Rac1TraversalEquipmentPull` and authored target contracts select/move |
| [`PlayerHost.Rac1Wrench.cs`](../game/scripts/PlayerHost.Rac1Wrench.cs) | `TryStepRac1Wrench` translates `Rac1WrenchAttackSession.Frame` into body velocity and animation presentation | `Rac1WrenchAttackSession.Step` owns action decisions |
| [`PlayerHost.Rac1TaxiWalk.cs`](../game/scripts/PlayerHost.Rac1TaxiWalk.cs) | `Rac1TaxiWalkIntent` synthesizes host stick intent toward a supplied scene target; `PresentRac1TaxiWalkFacing` is an explicit temporary presentation correction | Authored taxi target, walk-to admission and state transitions live in the RAC1 taxi owner |

## Finding and targeted repair

The concrete foreign responsibility was the `_rac1LedgeRegrabDelay` and
`_rac1LedgeJumpPressAge` recurrence in `PlayerHost.Rac1Ledges.cs`, including
a hardcoded 20-tick cooldown and 1,000-tick saturation. These were source
traversal admission rules, not engine collision queries. They are now
`Rac1LedgeTraversalSession.AdvanceInput`, `JumpPressAgeTicks`,
`CanProbeForGrab`, and the exit/reset transitions of that existing session.
The host **still** obtains Godot contact data and advances the native session
at the same callsite and tick order. Session lifetime is the scene player
lifetime, reset by `ResetRac1LedgeTraversal`; no world-global lease was added.

The remaining possible-looking state is bounded: `_rac1TetherTargets`
and `_rac1SwingTargets` are per-scene authored target caches,
`Rac1MovementEquipment`/`Rac1ActiveBackpack` are snapshots copied from the
persistent player owner, and `_lastJump`/`DevelopmentProfile` are measurements.
`PlayerHost` does not save campaign progress or coordinate diagnostic
scenarios. Development input remains injectable only through the existing
`GodotPlaytestHost` and scripted fixture seam. The temporary taxi yaw
correction is explicitly presentation policy, not a recovered native formula.

## Regression policy

[`PlayerHostBoundaryTests`](../tests/OBP.Tests/PlayerHostBoundaryTests.cs)
scans every `PlayerHost` class partial with Roslyn, not file size or partial
filename alone. It rejects unowned persistent campaign/equipment/actor
state declarations, new collection-backed actor managers, standalone numeric
gameplay formulas, and new scenario/fixture orchestrators. Mutation tests
demonstrate rejections; positive fixtures allow Godot collision rays, vector
adapters, and contact/debug counters. This is a narrow review tripwire, **not
a complete dataflow or taint proof**. Existing ephemeral source snapshots
have explicit exceptions; revisit the exception list rather than silently
adding broader categories. The separate R&C1 ledge regression covers tick
semantics independently of Godot.
