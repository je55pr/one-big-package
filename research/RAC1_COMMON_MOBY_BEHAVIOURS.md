# R&C1 most common missing Moby behaviours

## 22 October 2026: class-605 Metal Detector gameplay follow-up

Class **605** now has an actual engine-neutral native-target selection and
consumption session, `Rac1MetalDetectorSession`, and a Godot gameplay host.
The 358 invisible source placements, source polygon/clip-volume references,
nearest eligible site, less-than-20-unit proximity marker, **strictly
less than 1 unit** digging range, and native five-value bolt-emission
loop all have executable counterparts.

- `Rac1Paths.ContainsPlanar` evaluates the 46 source polygon-index targets;
  `Rac1ClipVolumes.Contains` evaluates the 95 clip-volume-index targets.
  The 358 retails placements were checked for bounded indices. Path and
  clip-volume restrictions are alternatives, not cumulative gates.
- Native item **27** ownership gates the gadget. Until the original R&C1
  gadget inventory wheel is hosted, **M** temporarily equips/holsters it.
  Hold the existing equipped-item key **Circle** for the native 15-frame
  activation and digging action. Full-arsenal testing mode makes item 27
  available; normal campaign play requires real item ownership.
- Extracted bolts are spawned as **physical five-value bolt pickups**, not
  granted directly to the wallet, using the shared crate/pickup simulation.
- Since the visible detector Moby and needle rig are not yet reconstructed,
  the current host readout reports target distance and relative direction.
  Source targeting uses Ratchet's body position rather than the original
  held detector's bone position. These are explicit host adaptations.
- Native target startup subtracts **5 times a saved 4-bit history counter**
  from its authored 10/50-bolt budget. That 320-byte table was verified to
  be entirely zero in the boot ELF at address 0x0014bf10. One nibble is
  advanced per five-bolt extraction, saturating at 15. This is now modeled
  in `Rac1BuriedBoltProgress` and included in OBP's optional campaign JSON
  save; missing history on older saves defaults to zero. Target command
  ordinals are assigned in authored placement order, pending direct live
  verification of source updater dispatch order.
- Source target selection still uses the host player position rather than
  the exact attached detector bone, and emission visuals/audio are partial.
  Retail fidelity beyond the recovered selection/history/drain contracts
  remains a separate validation task.

The initial rough 26/813 distinct class types and 7,673/16,232
placement exposures become approximately **27/813 (3.3%)** and
**8,031/16,232 (49.5%)** when counting the newly hosted class-605 family.
These estimates classify family-level gameplay exposure, **not** exact
feature completeness and not a systematic code-coverage measurement.

## 22 October 2026: Godot runtime follow-up

The isolated `chatgpt/moby-behaviour-atlas` branch now hosts **three new
Moby families**. These are source-backed but still have bounded omissions:

| Class | Retail placements | Integrated | Not yet source-exact |
|---:|---:|---|---|
| **505 explosive crate** | **608**, 18 levels | Native 1/5/3 fuse, blast progression and damage, original 363/364 fragment family, bolt payout, player/contact/wrench/bomb/other-weapon triggers and chain reactions | Godot contact/ray policy and tint approximate the PS2; native warning-voice mapping and draw-range rearm for the 211 repeatable crates remain incomplete |
| **1060 pulse-colour object** | **334**, 4 levels | Authored phase and recovered two-colour sine cycle at 180 degrees per second, on original Godot model/materials | PS2 per-vertex colour/alpha blending differs from Godot material tint |
| **805 checkpoint marker** | **97**, 18 levels | Original 0x80-byte clip-volume records, including oriented/scaled box admission and real entry edges; writes the recovered `Rac1LevelCheckpointSession` | Native global mode gates/conditional spawn slots are conservatively disabled; checkpoint UID serialization/revisit state remains incomplete |

**Proof in Godot:** an ordinary headless Aridia level-2 load registered five
native checkpoint markers and **naturally activated authored marker 708, clip
volume 53**, without the previous manually injected checkpoint test input.
The separately retained retail checkpoint matches its native XY and yaw. An
independent Rilgar level-5 load registered 215 explosive crates and 25 pulsing
objects. Retail tests check the original 608/334/97 placements and source
payloads, the separate 505 repeat count (211), and original 363/364 debris
model identities.

**Coverage accounting:** the previous approximate 23/813 placed classes
and 6,634/16,232 authored placements with some game-hosted behavior become
roughly **26/813 classes (3.2%)** and **7,673/16,232 placements (47.3%)**.
These are *partial-functional-exposure* numbers, not a claim that 47.3% of
gameplay is retail-perfect. Only source-backed families are counted; class
605's PVar parser alone is not an implemented Metal Detector.

### Original 2026-10-22 investigation (historical prior state)

Authority: original US NTSC-U `SCUS-97199` in the user-provided local
game ISO, independently parsed by OBP's retail readers and overlay updater
atlas. Analysed 2026-10-22. This follow-up corrects the earlier
[registration-only atlas](RAC1_MOBY_UPDATER_ATLAS.md).

**External corroboration:** [Lombyte's matching NTSC-U decompilation](https://github.com/lombyte-project/lombyte/tree/1e40bcb)
at commit `1e40bcb`, which targets the identical original US executable hash.
Relevant matching functions include `crate_update`, the class-605 area
update, `spawn_point_update`, and `red_orb_update`. Consult the upstream
repository for reference and licence terms; **no upstream C code has been
copied into OBP**. All static class counts, authored fields, and the level-2
checkpoint placement match below were independently validated on the
locally authorised retail data.

## Main result: the common classes have distinct purposes

| oClass | Placed | Levels | Assets | What it is and what the original code actually does |
|---:|---:|---:|---|---|
| **505** | **608** | 18 | Six-packet visible Moby | **Explosive crate.** Common native update routine shared with classes 500/501/502/511. Contact or damage enters a distinct detonation state; the countdown flashes and uses audio, then bursts and damages nearby objects and Ratchet. |
| **605** | **358** | 18 | No model payload | **Buried metal-detector target.** Invisible candidate locations are selected by authored path or clip-volume eligibility; the nearest valid target drives the player Metal Detector's direction/range, and collection drains its buried resource count. |
| **1060** | **334** | 4 | One-packet visible Moby | **Pulsing-colour orb/effect.** State 0 converts the authored phase to radians; state 1 updates sine-blended colour via the native Moby colour path. **271 instances are on level 18.** This is not a broad combat/progression system. |
| **805** | **97** | 18 | No model payload | **Checkpoint trigger.** Uses authored clip volumes, mode/mission flags and persistence gates before calling the original checkpoint writer, including saving a transform and synchronising UID persistence state. |

None of these new identities is based solely on a guessed class name or model
appearance: they are anchored in actual updater registrations, direct native
source disassembly, matching reference functions, and authored data contracts.

## Class 505: an entire missing crate family

All 608 placements have a 256-byte PVar. Their registered native updater is the
*same routine* as the already-hosted wood, health, metal and ammo crate family
on all 18 levels where class 505 appears. It is nevertheless a **distinct
damage / detonation policy**, not another ordinary instantaneous break.

Recovered native states relevant to class 505:

- State **1**: active; hero standing on/touching the crate or certain contacts
  enter state **5**, the timed detonation path.
- State **5**: if command is zero, the default countdown begins at
  `3 * scale_game_frames(60) - 1`. Some incoming damage starts a shorter
  frame-scaled fuse. While active, the original flashes red and emits warning
  sounds. At the countdown boundary it runs the native crate break, bolt
  reward and explosion consequences.
- State **3**: the post-break explosive burst has a growing area query over
  approximately 20 source-scaled ticks, capable of contacting nearby objects
  and the player.
- State **6**: special authored-repeat/re-entry policy for the qualifying
  subset of crates. Do not force every class 505 to permanently disappear.

Exactly **397** of 608 class-505 authored PVars have byte `+0xc8 == 0`;
**211** have `+0xc8 == 1`. This is an especially important reason not to
add 505 indiscriminately to `Rac1BoltCrate.CrateClasses`: native repeat and
resource consequences differ.

What OBP still needs: proper contact admission, a class-505-only 60 Hz
countdown and blast controller, collision and chain reaction with existing
crate sessions, authored repeat behaviour, native sounds and host presentation.
An instantaneous wrench-break approximation would be a regression in fidelity.

## Class 605: underground metal detector targets

All 358 placements use a **32-byte PVar** and have no class mesh. The native
updater is independently registered on all 18 levels after opening Veldin.
The proven authored fields are:

| PVar | Native role | Retail observations |
|---|---|---|
| `+0x10 s32` | Optional source path index, `-1` means absent | 312 without a path; 46 with a path |
| `+0x14 s32` | Optional clip volume index, `-1` means absent | 263 without a volume; 95 with one |
| `+0x18 s32` | Initial buried-resource budget | 347 with 50; 11 with 10 |

The native class-605 routine selects the nearest eligible target by 3D
distance and stores its identity/range in global player state. Within 20
world units it sets an additional proximity marker. The independently
recovered Metal Detector's own updater checks **target class 0x25d (605)**,
orients the detector towards the selected position, and drains target
resources as the detector is used.

**Implementation boundary:** a target may be subject to its path *or* volume
restriction, and the absence of both admits it; authored PVar parsing alone
does not reconstruct the geometric path/clip-volume admission or detector
activation. The new `Rac1CommonMobyAuthored` reader keeps these distinct.

## Class 805: native checkpoint activation identified

All 97 instances across 18 levels lack geometry and have a **48-byte PVar**.
The registered updater consumes:

| PVar | Native role | Retained evidence |
|---|---|---|
| `+0x00 s32` | Native clip volume | 97 entries carry authored values, including 53 on level 2 / instance 708 |
| `+0x08 u32` | Activation gates | 89 zero; 3 value 2; 2 value 3; 2 value 16; 1 value 4 |
| `+0x10 s32` | Native rearm selector | Authored as zero or one |
| `+0x18 s32` | Optional spawn-slot gate | 95 value -1; two concrete slots |

The native update excludes disabled markers (flag 8), tests game-state
modes for flags 2/4/16, and handles first entry into its clip volume and
some persistence-triggered reactivations. It then calls the source-game
checkpoint writer, copies the marker transform after a ground probe, and
updates local UID maps. This establishes the *producer* missing from the
earlier checkpoint archaeology, rather than another speculative restart
volume.

### Closed loop against independent level-2 live evidence

The existing live loaded level-2 witness retained an **active** checkpoint
at approximately `(205.5801544, 163.0475159, 26.0592937)` with a distinct
orientation, and proved retail restarted Ratchet there after environmental
death. The authored class-805 **instance 708** is at:

`(205.5801544, 163.0475159, 26.2153225)`, clip volume **53**, flags **0**.

The exact XY match, **exact authored yaw** (0.7809665 radians) and **0.1560** Z discrepancy are consistent with the
native checkpoint writer's ground-height query; no other premise is required
to invent a nearby save point. This is a much stronger source-to-live
correlation than a simple class label. The evidence closes *writer identity
and source placement*, not the full authoring geometry or runtime admission.

To make it playable, decode the original clip volumes and evaluate marker
eligibility in the game host; then send confirmed native activations into the
existing `Rac1LevelCheckpointSession`. Do not replace source clip volumes
with guessed spherical triggers.

## Class 1060: presentation rather than gameplay priority

Class 1060 is a small native visual whose 16-byte PVar starts with an
animation phase. Its updater performs native colour interpolation/pulsing.
It is installed 21 times on level 0, 17 on level 2, 25 on level 5, and **271**
on level 18. Its frequency inflated its apparent potential gameplay
importance. This should be queued for retail presentation and effect support,
not ahead of checkpoint/Metal Detector/Crate systems.

## Next most common **modelled** unresolved gameplay candidates

| Class | Placements | Areas | Corroborating native update identity |
|---:|---:|---|---|
| 827 | 193 | Levels 6 and 10 | Frog family |
| 193 | 170 | Levels 9 and 15 | Seeker family |
| 252 | 148 | Levels 8 and 14 | Scoutbot family |
| 866 | 134 | Levels 1, 5 and 11 | Large Amoeboid family |
| 1246 | 126 | Level 11 | Puffoid family |
| 1181 | 124 | Level 9 | Additional level-specific entity; semantics not yet promoted |
| 580 | 115 | Level 2 | Sandshark; **already handled** by the Aridia enemy implementation |

These candidates should receive separate class-local AI/state studies rather
than borrowing behaviours from similarly structured hostile PVars.

## Development recommendation

1. **Class 805 checkpoint markers:** highest progression leverage, because
   the existing runtime already accepts an independently proved checkpoint
   event and the native writer is now identified. Decode clip volume 53 and
   validate level 2 / instance 708 end-to-end first.
2. **Class 505 explosive crates:** 608 visible objects, 18 levels; source's
   timing/chain reaction and class-specific reappearance policy can reuse
   the existing crate damage/pickup infrastructure.
3. **Class 605 metal-detector sites:** 358 actual authored sites; requires
   the detector held-item interaction plus native path/volume queries.
4. **Repeated enemy families:** frogs, seekers, scoutbots, amoeboids,
   puffoids. Rank by campaign blockers and ready-to-use animation assets.
5. **Class 1060 pulse effect:** presentation milestone, not gameplay-critical.

## Implementation status and reproduction

`src/OBP.RAC1/Gameplay/Rac1CommonMobyAuthored.cs` now parses *only* the
proven source fields for 605 and 805 and exposes bounded, engine-neutral
admission checks. The native selectors, clip-volume geometry, metal-detector
tool, explosion controllers and checkpoint writes are **not** yet wired into
the running Godot gameplay. This is archaeological implementation groundwork,
not a claim that those four classes now function.

`tests/OBP.Tests/Rac1CommonMobyAuthoredTests.cs` covers all four placement
counts on the authorized ISO, validates typed PVars and native global gates,
and verifies the independent level-2 / instance-708 checkpoint witness.
Tests do not commit any source game bytes.

For reference, the exact external source was checked out temporarily under
the clone's ignored `.tools` directory. That checkout is **not tracked** by
OBP or included in any commit.
