# RAC1 Aridia enemies: retail evidence and remaining gaps

Worktree: `C:\Repos\obp-aridia`, branch `chatgpt/aridia`. This task never launches PCSX2. All original data was read directly from the configured NTSC-U Ratchet & Clank ISO.

## Authority

Reference project: [rac1-native-game](https://github.com/daino-selvatico/rac1-native-game). Its [Aridia class hooks](https://github.com/daino-selvatico/rac1-native-game/blob/main/src/game/ovl/aridia_mobys.cpp) identify these native class routines:

| Native class | Enemy | LEVEL2 update entry | Authored placements |
| --- | --- | --- | ---: |
| 580 | Sandshark | `0x002d3e50` | 115 |
| 612 | Constructorbot | `0x002d7748` | 15 |
| 668 | Sandshark nest | `0x002dcb38` | 7 |

The reference repository's `tools/codegen/gen_mkall.py gen aridia` was run **outside the OBP worktree** using the original NTSC-U ISO, after generating the level-2 relocation map. This regenerated `aridia_classes_tr.cpp`, a machine translation of the native enemy routines. Its generated code was used for fact extraction only, not committed to OBP or substituted for an actual managed gameplay implementation.

## Confirmed native mechanics integrated into OBP

1. **Health:** Sandshark 1, Constructorbot 3, nest 3. In the native routines, initialization sets the PVar health float at `+0x20` (Sandshark `0x2d3fbc`, Constructorbot `0x2d78d0`, nest `0x2dcc0c`).
2. **Nest initial timer:** Native nest code reads the authored signed integer at PVar `+0xd4` and passes it to `frame_scale_i` (`0x2dcc28`). The source disc contains 5 nests with value 60 and 2 with value 0; OBP reads the individual value instead of supplying a universal cooldown. The continuation of the native cycle is still approximated.
3. **Sandshark nest release:** Nest routine `0x2dcd3c..0x2dcdf0` searches for an existing eligible class-580 Moby via `0x2dd2a8` (native waiting state `26`), relocates it to **nest Z+1.5**, sets a planar heading towards its prior authored position (magnitude `2 * native frame scalar`), and calls `0x2d6118` to enter native state **27**, animation **11**. The host release method reuses an authored shark identity, relocates it, and plays that animation. **Crucial safeguard (2026-10-19):** the original code requires native state **26**, not merely a buried/invisible shark. None of OBP's ordinary authored sharks initially qualify. Until a real state-26 transition is reconstructed, automatic nest release is intentionally disabled rather than spawning surprise sharks at level entry.
4. **Bolts:** Sandshark `0x2d54e0` and Constructorbot `0x2d8430` call the shared `moby_drop_bolts(moby, 0, -1)`. OBP uses the authored placement reward centre at offset `+0x10`, rather than a made-up value. Original Aridia placement centre values are `0/1/13` for Sandsharks and `38` for Constructorbots. The nest death path emits explosion/debris, not an ordinary bolt drop in its translated update routine.
5. **Original art:** all 137 authored enemy models are loaded; usable source clips, skeletons and class sound cues are retained. Invalid forward-only clips are omitted instead of being assigned invented rates.

## Native Constructorbot clues not yet fully ported

The native class-612 routine has **18 state slots**, including a particle emitter in states 8/9 (around `0x2d7ecc..0x2d7f8c`) and a distinct animation-gated **joint-0 contact attack** in state 12 (`0x2d812c..0x2d8228`), which builds a common damage/contact descriptor and progresses into state 13. Thus the current host flame-cycle and player-hit timing are **not** an exact translation of retail Constructorbot behaviour. Do not label the guessed contact sweep as native.

Sandshark's dispatcher has 30 slots, some repeated/unused. The current buried, emergence, pursuit, biting and host physics transitions remain approximate. Source state 27 / animation 11 is the one recovered and implemented specifically for nest releases.

## What remains provisional

- Sandshark eligibility for each nest's *native class-group selector* (Moby `+0x21`), state-26 transition/retirement and exact global group counters. Only explicitly admitted native state-26 sharks are eligible; OBP currently doesn't synthesise them. If such a shark is eventually admitted, a nearest-candidate fallback within 30 host units remains in place pending group matching. **30 units is not a recovered retail predicate.**
- Native nest animation clock gate checks `moby_anim_time == 10`; host currently uses ten simulation ticks, which does **not** assert the same animation time. The nest is additionally conservatively gated to a living player within 12 host units with LOS, not a recovered native range. Following nest cooldown timing is provisional.
- Source-accurate Sandshark state machine, locomotion/pathing, return home, launch gravity/landing, melee socket queries and native damage flags.
- Constructorbot's full 18-state AI, moving/turning/pathing, flame emission texture/particle parameters, native state-12 contact volume and timing.
- Damage reactions, debris and explosion effects, exact animation/transition timing, and full bolt-drop spawn trajectories. Only the shared bolt reward centre/range are source backed.
- Aridia's original sound routing and attack/death cue timings, plus human visual/gameplay comparisons.
- Godot's current capsule collision, ray LOS and hit recovery are explicitly host adapters, not PS2 narrow-phase/physics equivalents.

## Performance / unexpected spawns regression fixed 2026-10-19

The first implementation treated *every buried* class-580 shark as eligible for a nest, instead of the specific native state 26. Its 180-frame Aridia smoke launched **57** supposedly waiting sharks while Ratchet remained at the start, even though they were not eligible. This also activated unnecessary motion, physics and animation workloads, contributing to severe frame drops.

The repair:

- Ordinary class-580 placements **do not** begin in state 26. The nest selector now requires explicit state-26 eligibility. No fake native state-26 transitions have been added. Therefore nests correctly launch **zero** sharks in the startup smoke until real state-26 logic is implemented.
- The nest timer will not run unless Ratchet is within 12 host units, alive and visible to its LOS adapter. This is explicitly a protective host distance, **not** recovered retail timing.
- Buried sharks start hidden, non-colliding and unanimated; no ground-contact sweep is performed until they emerge. This removes 115 unnecessary per-frame vertex interpolation and GPU mesh-buffer uploads while they are dormant.
- All Aridia entities beyond 24 units freeze AI/animation/host physics; expensive animated vertex-buffer updates happen only within 18 units. Static Constructorbot/nest meshes remain visible. These bounded culling thresholds apply to Aridia only and do not alter Veldin.
- Same Godot 180-frame headless test: **zero** surprise nest releases; previous run ~18.8 seconds versus corrected initial test ~10.5 seconds, although those wall times include start-up and are **not** user-visible FPS benchmarks.

The nest spawning mechanism is currently *conservatively dormant*, not retail-complete; a future native state-26 implementation must reconstruct its actual eligibility/return lifecycle without allowing the original mass-spawn regression.

A negative rate (`-0.125`) occurs in original Sandshark sequence 10. The forward-only `Rac1MobySequenceClock` cannot represent the underlying signed timing; that clip is omitted only for LEVEL2. Missing semantic attachment socket zero on some LEVEL2 classes uses root joint **for pose presentation only**.

## Validation

- `dotnet build game/OneBigPackage.csproj`
- `dotnet test tests/OBP.Tests/OBP.Tests.csproj`
- Optional disc-backed evidence: set `OBP_RAC1_ISO` before the Aridia retail unit test; absent the setting, only that test skips.
- Godot 4.7.2 .NET headless smoke with `--destination rac1:LEVEL2` and the mounted ISO, no PCSX2. Check that it registers 115+15+7 enemy placements and produces **no** offscreen nest release at startup.
- Human playtest with wrench/Bomb Glove against all three families, in particular weapon damage, normal emerge/attack, Constructorbot flames, death pickups and respawn. Nests will not launch until the native state-26 lifecycle is supported. Check for wall clipping, enemies sunk into geometry, and frame pacing during dense encounters.

Keep Aridia changes isolated until reviewed; do not modify parallel Veldin or Novalis worktrees.
