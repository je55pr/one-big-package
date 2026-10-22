# RAC1 Aridia enemies: retail evidence and remaining gaps

**Status: UNFINISHED / experimental.** This Aridia enemy work was integrated into `dev` to make its partial implementation available for continued development, **not** because retail gameplay is complete. Ordinary Sandshark emergence, pursuit and melee behaviour are provisional; the six Sandmouse-linked Sandsharks intentionally stay dormant because the Sandmouse native state machine has not been implemented; nest releases are intentionally disabled until the native Sandshark state-26 lifecycle is recovered. Constructorbot attacks, animations, collision, effects and sounds also require more retail reconstruction and in-game validation.

The original behaviour and data below were investigated using the NTSC-U Ratchet & Clank ISO and [rac1-native-game](https://github.com/daino-selvatico/rac1-native-game), without PCSX2. Source-backed findings and temporary host safeguards are distinguished explicitly.

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
6. **Sandmouse-linked Sandsharks:** native Sandshark init (`L2:0x2d4110..0x2d4160`) enters native **state 29** if PVar `+0x258` names an authored Moby. State 29 specifically waits until that referenced Moby's native state is at least **5** before re-entering ordinary initialization. Retail Aridia PVars identify **six sharks, #440–445, all linked to Moby #756 (class 1818, Sandmouse)**. The three at initial OBP player spawn are **#443–445**. Host initialization now records those links, preserves state 29 in the shared runtime and suppresses emergence even at point-blank distance until the linked Sandmouse state is positively established as `>=5`. No guessed timer or distance trigger substitutes for that gate.

## Native Constructorbot clues not yet fully ported

The native class-612 routine has **18 state slots**, including a particle emitter in states 8/9 (around `0x2d7ecc..0x2d7f8c`) and a distinct animation-gated **joint-0 contact attack** in state 12 (`0x2d812c..0x2d8228`), which builds a common damage/contact descriptor and progresses into state 13. Thus the current host flame-cycle and player-hit timing are **not** an exact translation of retail Constructorbot behaviour. Do not label the guessed contact sweep as native.

Sandshark's dispatcher has 30 slots, some repeated/unused. The current buried, emergence, pursuit, biting and host physics transitions remain approximate. Source state 27 / animation 11 is the one recovered and implemented specifically for nest releases.

## What remains provisional

- Sandshark eligibility for each nest's *native class-group selector* (Moby `+0x21`), state-26 transition/retirement and exact global group counters. Only explicitly admitted native state-26 sharks are eligible; OBP currently doesn't synthesise them. If such a shark is eventually admitted, a nearest-candidate fallback within 30 host units remains in place pending group matching. **30 units is not a recovered retail predicate.**
- Native nest animation clock gate checks `moby_anim_time == 10`; host currently uses ten simulation ticks, which does **not** assert the same animation time. The nest is additionally conservatively gated to a living player within 12 host units with LOS, not a recovered native range. Following nest cooldown timing is provisional.
- Source-accurate Sandshark state machine, locomotion/pathing, return home, launch gravity/landing, melee socket queries and native damage flags.
- The **Sandmouse's own class-1818 state-machine** is not yet simulated. It is not silently registered as native state 5. Therefore the six state-29 sharks stay correctly dormant for now; they can emerge later once a real Sandmouse controller advances the linked state. Post-unlock emergence still follows a provisional host approximation.
- Constructorbot's full 18-state AI, moving/turning/pathing, flame emission texture/particle parameters, native state-12 contact volume and timing.
- Damage reactions, debris and explosion effects, exact animation/transition timing, and full bolt-drop spawn trajectories. Only the shared bolt reward centre/range are source backed.
- Aridia's original sound routing and attack/death cue timings, plus human visual/gameplay comparisons.
- Godot's current capsule collision, ray LOS and hit recovery are explicitly host adapters, not PS2 narrow-phase/physics equivalents.

## Aridia start-encounter regression fixed 2026-10-20

Godot's Aridia initial player position was measured at `(-210.41, 25.35, 170.35)` in its Y-up coordinates. The authored source has three sharks almost on top of it: #444 at ~0.83 unit, #443 at ~4.66, #445 at ~5.63. Previous OBP logic woke **every** buried shark within 8 units, disregarding those three authored placements' PVar `+0x258 = 756`. That was the immediate triple-aggro bug, independent of nest over-release.

The original Aridia overlay takes such placements through state **29**, waiting for the linked Sandmouse (#756) to reach native state **5**. The host now retains the state-29 gate for the six affected sharks and refuses to wake them until the source-backed prerequisite is known to have happened. Source values were verified directly against the NTSC-U ISO, no PCSX2. This is a deliberately incomplete *later* activation until the Sandmouse is implemented, rather than pretending that starting proximity proves the event occurred.

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

## 22 October 2026: source-backed group and recycling reconstruction (partial)

Retail evidence below was regenerated directly from the user's NTSC-U R&C1 disc using the separate native-game codegen, without checking generated source or disc payloads into OBP.

- **Authentic group source:** authored Moby placement record offset `+0x48` is read as the low byte into live Moby `+0x21`; `0xff` means ungrouped. The gameplay block at header offset `+0x48` carries per-group ordered halfword Moby IDs, terminated by bit 15. Retail level loader `level_load.cpp` around `0x001ab840`; native `group_first/group_next` at `0x2591d0`/`0x2592b8`. OBP now reads original ordering from the retail source at runtime and does not choose by nearest distance.
- **Native shark state-26 admission:** Aridia overlay `L2:0x2d54e8..0x2d5534` follows death/falling physics and calls group selector `0x2d5658`. It selects an eligible *live class-668 nest in the same group*, then transitions the shark into state 26, a recyclable pool. OBP now mirrors this after its existing provisional 36-tick death presentation; its exact physics, timing, bolts, and health restoration remain host stand-ins.
- **Nest candidate:** `L2:0x2dd2a8..0x2dd360` tests the nest's original group roster and selects **class 580 already in state 26**. No arbitrary buried shark or neighbour within 30 units qualifies. `L2:0x2d6118` relocates that very Moby into state 27, animation 11. The host now sets that state explicitly and preserves identity.
- **Native nest timing still pending:** `L2:0x2dcce0..` triggers at animation time 10, calculates subsequent wait from its PVar interval plus a capped group-size-dependent term, then launches. The OBP ten-tick windup, 12-unit activation gate and simple cooldown are not yet those native conditions.
- **Sandmouse unlock evidence:** class 1818 uses update `L2:0x2f0f88` (not the separate class-437 update `0x2ccc08`). Its eight-plus-state cinematic/controller path requires level/hero conditions and volume/interaction logic. Native state 4 transitions to 5 at `L2:0x2f18f8..0x2f1980`; the six authored sharks linked to #756 must not wake before that. OBP still has no verified host equivalent for the preceding cinematic, so the state-29 safety gate intentionally stays closed during ordinary gameplay.
- **Constructorbot still provisional:** its update `L2:0x2d7748` branches over 18 states, including state-8/9 emission and state-12 attachment/contact paths. Existing Godot attack detection, emitters, audio timing and collision remain host approximations, not claimed native parity.

Added deterministic group, recycling, opening and retry regressions plus disc-backed group format assertions. No generated executable/decompiled source, textures, music, captures or private file paths are committed.

## Follow-up native Sandmouse chain and nest animation gate (22 October 2026)

Retail-linked instance #756, native class 1818, has **208 PVar bytes**. Its PVar `+0x40/+0x44` contains authored path selectors `43/43`, and `+0x48/+0x4c` links to actor **#526, class 604** (the linked actor is a distinct class, not another Sandmouse). Verified by decoding the original level-2 instance roster and PVars, without committing data payloads.

Native machine translation `L2:0x2f0f88` reveals a source-ordered dependency chain: class-1818 state 0 enters state 1 (`0x2f131c`); state 1 waits for nonzero global scene flag `0x13d4c5` and mode `0x1404a8 == 5`, then performs additional hero/cinematic interactions; state 2 (`0x2f15f0`) waits for linked class-604 state **exactly 5**, not `>=5`; state 3 (`0x2f16b8..0x2f1878`) performs original path/contact movement, state 4 (`0x2f18f8..0x2f1980`) commits native state 5. Native class-604 `0x2d7190` independently responds to its `+0xbc` scripted control byte and advances through its own animation-gated states. The six linked class-580 sharks may unlock at Sandmouse state 5, not at earlier transition states.

`Rac1AridiaSandmouseSession` now represents this **narrow state order** with deterministic proof of six-shark unlocking and retry reset. The scene-entry and travel-completion facts are intentionally external: OBP's Godot cutscene/hero path source does not yet supply them, so this session is **not** proof that normal gameplay reaches the unlock. No proximity/timer synthetic trigger is inserted.

Nest native update `L2:0x2dcce0` tests animation-time **10** in winding-up state 2. The Aridia host now supplies the original clip's runtime animation position to the controller, with a documented fractional crossing tolerance to bridge host clock samples and a tick-only fallback for non-rendering fixtures. The windup-entering frame cannot spuriously fire on the preceding idle clip's timestamp. The native cooldown calculation and source trigger volume remain unfinished.
