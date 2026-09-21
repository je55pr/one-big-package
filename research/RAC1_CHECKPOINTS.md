# R&C1 checkpoint and environmental-restart boundary

Authority: NTSC-U original retail (SCUS-97199, build rac1-ntscu-original). This note records the checkpoint evidence that survives controlled Veldin death/restart experiments. It deliberately distinguishes a proven default player-start restart from an inferred checkpoint system.

The payload-free machine-readable witness is research/generated/rac1-checkpoint-boundary.json.

## Result for the retained Veldin route

The retained early-Veldin route does **not** demonstrate activation of a mid-route checkpoint. A controlled environmental death from the later .05 authority state, with Ratchet already at native position approximately (106.5274, 230.3423, 34.4037), still rebuilds him at the opening authored class-0 placement:

- native position (132.09, 115.48, 31.4266167) after retail grounding;
- native yaw 0.6627014875 rad, exactly the authored class-0 Z rotation;
- Nanotech transitions from the .05 witness value 1 to 0 at the death boundary and then to 4 at restart.

The restart yaw is now independently witnessed, rather than inferred from the authored placement.

This changes the interpretation of the earlier .03/.04 route differences: they are not evidence of a stored checkpoint transform. Any true checkpoint definition or activation mechanism beyond this default Veldin restart remains unrecovered.

## Authored trigger/data exclusions

Retail Veldin gameplay contains 14 authored cuboids in the gameplay +0x60 block. Correct inverse-transform segment tests find no cuboid intersecting either retained player route segment .03 -> .04 or .04 -> .05. The route therefore does not support a simple "cross this cuboid to activate checkpoint" interpretation.

The rare/singleton authored Mobies were also checked as possible invisible checkpoint controllers. The identifiable class-834 Clank cutscene trigger is authored at approximately (171.43, 270.94, 29.257), well beyond the retained activation slice, and its relevant state was already stable before that slice. The other singleton placements likewise do not provide a nearby authored checkpoint object. These are exclusions for this Veldin witness, not a claim that no R&C1 level can implement checkpoint policy in a script or object.

## Native reset dataflow

Loaded Veldin routine 0x00204c60 owns the player rebuild used by the controlled restart. Small retained instruction signatures establish this path:

- 0x00204d40 reads live Moby+0xa6 while scanning for native class 0;
- the selected class-0 Moby is passed through helper 0x00259698, whose result can adjust live Moby Z before placement;
- 0x00204da4/0x00204da8 load the selected Moby position at +0x10 and copy it into player state at 0x0013f3d0;
- the same initialization path carries the selected class-0 Moby yaw from +0x48; the controlled restart returns exactly the authored 0.6627014875 rad yaw.

This composes directly with the separately retained level-population evidence in RAC1_VELDIN_SPAWN.md: authored gameplay instance 0 / class 0 seeds the live class-0 Moby transform. For this Veldin restart, authored class 0 is therefore the proven placement authority.

## The 0x0013e090 record is not checkpoint authority

A clean 32-byte record at 0x0013e090..0x0013e0af contains the grounded opening position and yaw even while Ratchet is far away. Static archaeology shows why: after rebuilding player state, 0x00204dcc..0x00204dd8 copies the first 32 player-state bytes into this record when global 0x00160540 is zero.

A controlled causality test changed only this record to a different XYZ/yaw, then triggered the same environmental death. Retail overwrote the edited record back to the authored Veldin values at the restart boundary and placed Ratchet at those authored values. The edited record never redirected the restart.

Accordingly this record is a downstream reset snapshot/output. It must not be promoted as a checkpoint transform. The 0x00160540 gate is zero in retained states .01 through .06 and a second loaded consumer further argues against naming it as a generic checkpoint selector: 0x00243670..0x002436cc only reaches the gate when CurrentLevel is 13 and the reset/context halfword at +0x26 equals 2, then uses that selector to read native class 0x215 from the table at 0x00160548. A full retail authored-Moby census finds the table's nonzero class family (0x213, 0x214, 0x215, 0x217, 0x218, 0x219) collapses to exactly one authored match anywhere on disc: level 13 instance 801, class 0x215, at approximately (467.16425, 584.96655, 316.71402) with a 16-byte PVar. The broader level-13 behavior remains unrecovered, but this is level-specific object logic rather than evidence for a cross-level checkpoint selector.

## Local state across the exact restart

The two independently recovered class-500 UID persistence maps are:

- level-indexed map at 0x0014c190 + level * 0x100;
- parallel local/session map at 0x001ba4d0.

In the .05 authority state both maps are identical and contain 46 set bits. High-frequency sampling across the exact death/restart transition shows both maps remain byte-identical while Nanotech reaches zero and while Ratchet is rebuilt at class 0. Thus destroyed-crate UID persistence is **preserved across this controlled Veldin environmental restart**.

This does not prove its reset event on planet reload, campaign transition, or a future independently recovered checkpoint activation. Equipped weapon, ammo, Bolt wallet, transient pickups, hostile state and broader level-script state also remain outside this checkpoint contract until separately measured.

## Reproduction

Run:

py -3.12 tools/rac1-checkpoint-boundary-probe.py --savestate "<authorized SCUS-97199 Veldin state>" --zstd-dll "<zstd.dll>" --out "<payload-free report.json>"

It verifies the loaded reset instruction signatures plus the level-13 gate-consumer signatures, identifies the player, class-0 Moby, reset snapshot and UID-map addresses, and emits only hashes, scalars and derived state. `Rac1CheckpointRetailTests` independently replays the gate-class authored census from the authorized retail ISO. Raw savestates, EE memory and controlled PINE traces remain under ignored local capture storage.

The current .05 authority savestate SHA-256 is 58cf20650d47fe2ed6940b4b508e3354d032d77f477e0d2f5ea16776dcbb52ec.

## Remaining checkpoint boundary

No generic R&C1 checkpoint definition, selector, activation condition or checkpoint-specific spawn table is promoted by this work. The strongest result is instead negative and specific: the retained Veldin route has no proven mid-route checkpoint activation, and its controlled environmental restart uses the authored class-0 start while preserving the proven class-500 UID maps.

A future checkpoint witness must first demonstrate a death restart to a transform different from the target level's authored class-0 placement. Only then should differences around the activation event be attributed to a checkpoint rather than ordinary mission/script activity.
