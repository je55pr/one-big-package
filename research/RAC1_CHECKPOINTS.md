# R&C1 checkpoint and environmental-restart boundary

Authority: NTSC-U original retail (SCUS-97199, build rac1-ntscu-original). This note retains both the negative Veldin checkpoint boundary and a positive level-2 checkpoint/restart witness. It distinguishes the proven restart placement/consumer from the still-unrecovered gameplay event that activates or populates the checkpoint record.

The aggregate payload-free witness is research/generated/rac1-checkpoint-boundary.json. The level-2 loaded-state reduction is independently reproducible as research/generated/rac1-level2-checkpoint.json.

## Result for the retained Veldin route

The retained early-Veldin route does **not** demonstrate activation of a mid-route checkpoint. A controlled environmental death from the later .05 authority state, with Ratchet already at native position approximately (106.5274, 230.3423, 34.4037), still rebuilds him at the opening authored class-0 placement:

- native position (132.09, 115.48, 31.4266167) after retail grounding;
- native yaw 0.6627014875 rad, exactly the authored class-0 Z rotation;
- Nanotech transitions from the .05 witness value 1 to 0 at the death boundary and then to 4 at restart.

The restart yaw is now independently witnessed, rather than inferred from the authored placement.

This changes the interpretation of the earlier .03/.04 route differences: they are not evidence of a stored checkpoint transform. That negative result remains scoped to Veldin and does not conflict with the positive level-2 witness below.

## Positive level-2 checkpoint restart witness

A populated save was brought into loaded level 2 by a synthetic harness handoff into the already-recovered native level loader. That entry method is **not** evidence for ordinary ship-travel sequencing. Once level 2 was initialized, however, the checkpoint and death/restart observations below are direct retail runtime state and an ordinary controlled environmental-death transition.

The loaded live-Moby pool pointer at `0x0015ffd8` resolves to `0x01cec780`. Live index 0 is native class 0 and matches the initial level-2 player transform exactly: position `(210.5431518555, 170.2037963867, 25.3327236176)`, yaw `2.3095138073` rad. This is the level's default authored class-0 restart baseline.

Before death, a separate active record exists at `0x001bb830`. Its active word is `1`; its transform at `0x001bb840` is position `(205.5801544189, 163.0475158691, 26.0592937469)`, with yaw `0.7809665203` rad at record `+0x28`. The checkpoint position is about `8.7391` native units from the class-0 position, so the two restart candidates are unambiguously distinct.

The controlled death used the same bounded environmental stimulus as the retained Veldin witness: only player-state Z and the corresponding live class-0 Moby Z were set to `20.0`, after which retail owned the transition. The observed boundary was:

- Nanotech `4 -> 0`, with player state cleared at the death boundary;
- retail first rebuilt class 0 near `(210.4100036621, 170.3500061035, 25.3448276520)`, yaw `2.3099956512`;
- Nanotech returned to `4`;
- roughly 11 ms later, retail redirected both player and live Moby to `(205.5801544189, 163.0475158691, 26.0592937469)`, yaw `0.7809665203`, exactly matching the pre-existing `0x001bb830` record.

This satisfies the earlier acceptance criterion for a checkpoint witness: the native death restart demonstrably finishes at a transform different from the target level's authored class-0 placement. It proves a level-2 checkpoint/restart placement. It does **not** yet identify the gameplay trigger, writer, script policy, or selector that originally activated/populated the record.

## Native level-2 checkpoint consumer

Loaded level-2 routine `0x00286520` closes the record-to-player dataflow. It reads the active word at `0x001bb830`; when nonzero, `0x0028657c..0x00286598` copies record `+0x10..+0x2f` into player state `0x0013f3d0..+0x1f`. The same routine later reloads the live-Moby pool through `0x0015ffd8` and carries additional record metadata into reset setup. Small retained instruction signatures in `tools/rac1-level2-checkpoint-probe.py` fail closed if this loaded path changes.

The exact writer/activation event for `0x001bb830` is still unrecovered. Accordingly, this evidence supports checkpoint **restart placement and consumption**, not a guessed trigger volume or mission-script activation rule.

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

This does not prove its reset event on planet reload, campaign transition, or the newly recovered level-2 checkpoint restart. The level-2 trial did not sample these UID maps. Equipped weapon, ammo, Bolt wallet, transient pickups, hostile state and broader level-script state also remain outside this checkpoint contract until separately measured.

## Reproduction

Run:

py -3.12 tools/rac1-checkpoint-boundary-probe.py --savestate "<authorized SCUS-97199 Veldin state>" --zstd-dll "<zstd.dll>" --out "<payload-free report.json>"

py -3.12 tools/rac1-level2-checkpoint-probe.py --savestate "<authorized SCUS-97199 level-2 state>" --zstd-dll "<zstd.dll>" --out "<payload-free level2-report.json>"

The Veldin probe verifies its loaded reset signatures plus the level-13 gate-consumer signatures. The level-2 probe independently verifies the active checkpoint record, dynamic live-Moby pool/class-0 transform and the loaded `0x00286520` placement consumer. Both emit only hashes, addresses, scalars and derived state. `Rac1CheckpointRetailTests` independently replays the gate-class authored census from the authorized retail ISO. Raw savestates, EE memory and controlled PINE traces remain under ignored local capture storage.

The current .05 Veldin authority savestate SHA-256 is 58cf20650d47fe2ed6940b4b508e3354d032d77f477e0d2f5ea16776dcbb52ec. The retained level-2 loaded-state savestate SHA-256 is 74bd2afd45e8e0796f5bf056aeb30f2a71b76fd0a9977e0cfd61a2dc911c775a; its synthetic level-entry provenance must not be promoted as ordinary ship travel.

## OBP engine-neutral checkpoint/progress boundary

`Rac1LevelCheckpointSession` now carries only the placement state justified by
these witnesses. A full R&C1 level entry creates a fresh session from the decoded
authored class-0 `RuntimeSpawn`. `Activate` accepts an already-identified native
checkpoint event/record and never tries to infer a trigger from position, cuboids,
mission state or campaign progress. `ResolveEnvironmentalRestart` selects the
active record when present and otherwise returns authored class 0, matching the
retained level-2 positive witness and Veldin negative witness respectively.

The session is deliberately absent from `Rac1CampaignSaveEnvelope`. Schema 2
continues to persist only the recovered campaign and weapon owners. Because
checkpoint serialization and planet-reload/revisit lifetime are still unrecovered,
OBP starts a fresh inactive checkpoint session on every full level load. That
reload/revisit clearing is an explicit conservative host default, not a retail
claim. Same-world state such as class-500 UID bits, ammo, Bolts, hostiles, pickups
and scripts is not folded into the checkpoint model without its own restart
witness.

## Remaining checkpoint boundary

A level-2 checkpoint record, its loaded player-placement consumer, and a controlled death restart to that non-class-0 transform are now proven. The retained Veldin route remains a valid negative witness: it has no demonstrated mid-route checkpoint activation and restarts from authored class 0 while preserving the measured class-500 UID maps.

Still unresolved are the generic R&C1 checkpoint definition/selector, the gameplay event or writer that activates/populates `0x001bb830`, and whether other levels share this exact storage/consumer shape. Checkpoint-specific semantics for weapon/ammo, Bolts, pickups, enemies, UID maps and broader script state likewise require their own controlled before/death/restart measurements. The level-2 evidence must therefore not be stretched into a generic activation policy.
