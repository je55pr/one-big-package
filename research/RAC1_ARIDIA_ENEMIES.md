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
3. **Sandshark nest release:** Nest routine `0x2dcd3c..0x2dcdf0` searches for an existing eligible class-580 Moby via `0x2dd2a8` (native waiting state `26`), relocates it to **nest Z+1.5**, sets a planar heading towards its prior authored position (magnitude `2 * native frame scalar`), and calls `0x2d6118` to enter native state **27**, animation **11**. The host now reuses an authored shark identity, relocates it, and plays that animation. It never fabricates an additional native placement or immediately offers a released shark for another launch.
4. **Bolts:** Sandshark `0x2d54e0` and Constructorbot `0x2d8430` call the shared `moby_drop_bolts(moby, 0, -1)`. OBP uses the authored placement reward centre at offset `+0x10`, rather than a made-up value. Original Aridia placement centre values are `0/1/13` for Sandsharks and `38` for Constructorbots. The nest death path emits explosion/debris, not an ordinary bolt drop in its translated update routine.
5. **Original art:** all 137 authored enemy models are loaded; usable source clips, skeletons and class sound cues are retained. Invalid forward-only clips are omitted instead of being assigned invented rates.

## Native Constructorbot clues not yet fully ported

The native class-612 routine has **18 state slots**, including a particle emitter in states 8/9 (around `0x2d7ecc..0x2d7f8c`) and a distinct animation-gated **joint-0 contact attack** in state 12 (`0x2d812c..0x2d8228`), which builds a common damage/contact descriptor and progresses into state 13. Thus the current host flame-cycle and player-hit timing are **not** an exact translation of retail Constructorbot behaviour. Do not label the guessed contact sweep as native.

Sandshark's dispatcher has 30 slots, some repeated/unused. The current buried, emergence, pursuit, biting and host physics transitions remain approximate. Source state 27 / animation 11 is the one recovered and implemented specifically for nest releases.

## What remains provisional

- Sandshark eligibility for each nest's *native class-group selector* (Moby `+0x21`) and the exact global group counters. Current selection uses the nearest waiting authored shark within an explicit 30-unit host bound; **30 units is not a recovered retail predicate**.
- Native nest animation clock gate checks `moby_anim_time == 10`; host currently uses ten simulation ticks, which does **not** assert the same animation time. Following nest cooldown timing is provisional.
- Source-accurate Sandshark state machine, locomotion/pathing, return home, launch gravity/landing, melee socket queries and native damage flags.
- Constructorbot's full 18-state AI, moving/turning/pathing, flame emission texture/particle parameters, native state-12 contact volume and timing.
- Damage reactions, debris and explosion effects, exact animation/transition timing, and full bolt-drop spawn trajectories. Only the shared bolt reward centre/range are source backed.
- Aridia's original sound routing and attack/death cue timings, plus human visual/gameplay comparisons.
- Godot's current capsule collision, ray LOS and hit recovery are explicitly host adapters, not PS2 narrow-phase/physics equivalents.

A negative rate (`-0.125`) occurs in original Sandshark sequence 10. The forward-only `Rac1MobySequenceClock` cannot represent the underlying signed timing; that clip is omitted only for LEVEL2. Missing semantic attachment socket zero on some LEVEL2 classes uses root joint **for pose presentation only**.

## Validation

- `dotnet build game/OneBigPackage.csproj`
- `dotnet test tests/OBP.Tests/OBP.Tests.csproj`
- Optional disc-backed evidence: set `OBP_RAC1_ISO` before the Aridia retail unit test; absent the setting, only that test skips.
- Godot 4.7.2 .NET headless smoke with `--destination rac1:LEVEL2` and the mounted ISO, no PCSX2. Check that it registers 115+15+7 enemy placements and that nests no longer relaunch the same sharks in a tight loop.
- Human playtest with wrench/Bomb Glove against all three families, in particular weapon damage, nest launch, flames, death pickups and respawn. Check for wall clipping and enemies sunk into geometry.

Keep Aridia changes isolated until reviewed; do not modify parallel Veldin or Novalis worktrees.
