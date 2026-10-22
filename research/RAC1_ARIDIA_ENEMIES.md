# RAC1 Aridia enemies: retail evidence and remaining gaps

**Status: UNFINISHED / experimental.** This Aridia enemy work was integrated into `dev` to make its partial implementation available for continued development, **not** because retail gameplay is complete. Ordinary Sandshark emergence, pursuit and melee behaviour are provisional; the six Sandmouse-linked Sandsharks intentionally stay dormant because the native Sandmouse cinematic remains unconnected to Godot (although its source-gated engine-neutral transitions are now tested); nest releases are limited to proven state-26 eligible sharks from the authored group, with exact timing and gameplay contact still provisional. Constructorbot attacks, animations, collision, effects and sounds also require more retail reconstruction and in-game validation.

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

The native class-612 routine has **18 state slots**, including a particle emitter in states 8/9 (around `0x2d7ecc..0x2d7f8c`) and a distinct **joint-0 contact query** in native state **11** (`0x2d812c..0x2d8228`, zero-based dispatcher), which runs each update; animation completion advances directly to state 13 and selects sequence 8 or 10. The contact routine builds a native hit descriptor. Thus the current host flame-cycle and player-hit timing are **not** an exact translation of retail Constructorbot behaviour. Do not label the guessed contact sweep as native.

Sandshark's dispatcher has 30 slots, some repeated/unused. The current buried, emergence, pursuit, biting and host physics transitions remain approximate. Source state 27 / animation 11 is the one recovered and implemented specifically for nest releases.

## What remains provisional

- The nest's *native class-group selector* (Moby `+0x21`) and state-26 retirement/reuse are now integrated. The exact native global group counters, hit/death timing, physical return and reactivation conditions remain provisional. Ordinary buried sharks must still not be treated as candidates.
- Native nest animation gate checks `moby_anim_time == 10`; Godot now passes its retail clip's animation position, with a documented host sampling tolerance and fixture fallback. The nest is additionally conservatively gated to a living player within 12 host units with LOS, not a recovered native predicate. Following nest cooldown timing is provisional.
- Source-accurate Sandshark state machine, locomotion/pathing, return home, launch gravity/landing, melee socket queries and native damage flags.
- The **Sandmouse's own class-1818 native state chain** is represented in a bounded engine-neutral session, and both original scripted actors register as native state 1 in Godot. The host still has no original cinematic/hero and path-43 movement adapters, so it never invents state 5. Consequently the six state-29 sharks remain correctly dormant in ordinary Godot gameplay. Post-unlock emergence still follows a provisional host approximation.
- Constructorbot's full 18-state AI, moving/turning/pathing, flame emission texture/particle parameters, native state-11 joint-contact volume and timing.
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

## Follow-up class-604 controller and live actor registration (22 October 2026)

**Source: `L2:0x2d7190..0x2d74d0`, class-604 native updater.** Retail class604 actor #526 lies beside the Sandmouse #756 and has an authored 16-byte PVar. Its nine-slot state dispatcher is controlled by the **live Moby control byte at `+0xbc`**, NOT the PVar; its animation-state check is `+0x53`, and completion flag is `+0x70 & 2`. The native transitions now modelled by `Rac1AridiaClass604Session` are: 0→1/clip0 startup; 1 (command 3/4/5)→2/clip1; 2 (animation complete)→3/clip2; 3 (command 4/5)→4/clip3 or (command 1/7)→7/clip6; 4 (complete)→5/clip4; 5 (command 1/3/6/7)→6/clip5; 6 (complete)→7/clip6 if command 1/7, else→3/clip2; 7 (complete)→1/clip0. Slot 8 is unused. Unrecognized commands do not force state transitions.

**Sandmouse issues the class604 command.** Its native `0x2f13e0` writes class604 control `1` or `3` based on the scripted point-in-box condition while waiting for the scene entry, and `0x2f159c` writes command **5** to the linked class604 control when the cinematic/hero entry actually succeeds. The engine-neutral Sandmouse controller now exposes an explicit, one-time command-5 intent on the transition to state 2. It is not emitted for just a global mode match. The paired regression proves class604 must animate to state 5, then class1818 reaches state 5 only after additional route and cinematic conditions, then and only then do all six class580 sharks awaken.

**Godot wiring boundary:** Both retail dynamic actor meshes are present in Aridia's original imported scene, and `ConfigureRac1AridiaScriptActors` registers exactly one class604 plus one class1818 in the same live `Rac1MobyRuntimeSession` as the 137 hostile actors, using their original identity and PVar. Their source-backed state0 initialization advances each to state1. The model identities and native states are now visible to the linked shark resolver during actual Godot play and reset with the Aridia enemy host. However the host does **not yet** simulate the original camera/hero cinematic, scene globals, exact animation wrap for class604, scripted `+0xbc` updates or Sandmouse path 43. Consequently both remain conservatively in native state1 and the six sharks remain dormant in normal Godot gameplay. This is intentionally NOT a playable completion claim.

## Class-612 Constructorbot source-state admission (22 October 2026, further specialist pass)

A closer native dispatcher read corrects the earlier misidentification: the attack routine at **LEVEL2:0x2d812c is native state 11**, **not** 12. The native indexed table in `constructorbot_update_l2` (`0x2d7748`) maps slots `0,2,3,4,5,6,7,8,9,10,11,12,13,14` plus an inert slot 1; unlisted upper slots fail closed. State 11 calls the joint-0 contact solver on **every native tick**, using a native contact descriptor; `Moby+0x70 & 2` then advances to native state **13** and picks animation **8 or 10** randomly. State **10** instead sets state **12**, animation **9**, when `+0x70 & 2` indicates animation completion. Host interaction must not conflate these separate states, and no invented hit is emitted merely because an animation is playing.

The native state-0 initializer branches on authored PVar `+0x194` (signed integer):
- `0` → state **7**, animation **0** (`L2:0x2d7918/0x2d7c00`)
- `1` → state **10**, animation **0** (`L2:0x2d7930`)
- `2` → state **8**, animation **2** (`L2:0x2d7958`)
- `3` → state **9**, animation **15** (`L2:0x2d797c`)

Retail Aridia's 15 placed Constructorbots contain exactly **4 mode-0, 1 mode-1, 1 mode-2, and 9 mode-3** PVars. All fifteen are now registered into the shared native runtime at their correct startup state instead of generic state 1, and initial source sequence is used when available. The provisional host combat session also preserves the relevant source startup sequence while the bot is outside attack range, rather than silently replacing it with generic idle sequence 1. Initializers reset on level transitions. `Rac1AridiaConstructorbotNative` separately models the source-backed aim/contact transitions as deterministic intents, and tests cover all four modes, invalid PVars, state numbers, sequence changes, and absence of guessed contact damage.

**Still not implemented:** the native path/physics state loop, per-mode transitions after initial spawn, original source-particle texture/audio/arc settings, actual transformed joint query, contact target solver and damage flags. The existing close-range provisional host attack adapter is NOT equivalent to native states 8–13. A headless load cannot count as combat/FX acceptance.

## Constructorbot state-7 → state-11 playable host bridge and source particle gate (22 October 2026)

**Native evidence:** `L2:0x2d7c98..0x2d7dd4` first checks the selected native path-node distance to the target against Constructorbot PVar `+0x180` (retail examples have radius **18**), target/body vertical separation less than **4**, body within **1** of selected waypoint, then `angle_dist` to the target below **0x3db2b8c2** (~5°). If all pass, the original native update enters **state 11** and plays **animation 8**. The currently loaded original path-node selector is PVar `+0x188`; OBP's host still lacks that native waypoint mapping and therefore substitutes the **authored spawn position** for its anchor when admitting state 11. This is explicitly a host fallback, **not** a retail-equivalent path solver; it admits only all-four-source-gate success.

`L2:0x2d812c..0x2d8228`: native **state 11** runs the semantic joint-0 contact query each tick, builds the original contact descriptor and, once `Moby+0x70 & 2` confirms animation completion, transitions to **state 13**, selecting animation **8** or **10**. OBP's host now uses the decoded animation's semantic joint-0 socket and a guarded Godot ray to check the player collider, not the previous sixteen-tick periodic cone hit in this state. The **original contact-geometry solver** `lv2_264e70`/`lv2_2651d0`, descriptor flags and target-filtering rules remain pending; the ray is explicitly a substitute. The host uses `Rac1MobySequenceClock.Wrapped` as an approximation for native completion. State **10** advances to **12**/animation9 on that clip-completion condition. State12's original target/path solver is not recovered and deliberately doesn't silently auto-advance.

`L2:0x2d7ecc..0x2d7f8c`: native state **8**, and **only state 8**, spawns source particle program **15** from joint0 if `(global 0x15f5cc & 3)==0` and `Moby+0x31!=0`. State 9 shares the subsequent pursuit/detection routine but skips emitter spawn. A source-gated particle intent is now computed at the 60Hz host update cadence, and the existing Godot GPU-flame implementation is **only a temporary appearance proxy**, not the recovered native particle15 art, emission distribution, or timing. The exact update phase of the global counter and state8 exit behavior remain unknown.

**Godot validation:** A new opt-in Aridia branch of the existing `--rac1-enemy-smoke` harness stages the original, fully imported Constructorbot **#548** (retail PVar mode0), Ratchet's real playable capsule, and native actor state. It confirmed **state7 → state11 and sequence8** while respecting the host's spawn-waypoint fallback. It then staged the player at the elevated original joint0 animation socket and the Godot ray/contact adapter applied real player damage (**Nanotech 4 → 3**). This is a staged scene verification, **not** an unassisted ground-level retail encounter; normal gameplay contact, recovery to state13 and all modes' complete motion/particles/audio are still incomplete.

**Regression scope:** Deterministic tests assert the four independent state7 range/height/waypoint/heading gates, boundary refusal, invalid radius rejection, and the 4-tick particle15 gate including hidden Mobies and state9 exclusion. Aridia headless smoke preserves the original population and Sandmouse startup safeguards.
Native `L2:0x2d7c98..0x2d7dd4` admits state-7 Constructorbots into **state 11 / clip 8** only if their target is inside the PVar `+0x180` engagement radius (18 on retail bots), target height is within 4 of the Moby, the Moby is within 1 of its selected path waypoint, and facing error is less than 0x3db2b8c2 (~5 degrees). Original waypoint selector PVar `+0x188` refers to the level path-node array. The Godot bridge temporarily uses the **authored spawn position** for the waypoint, not a reconstructed source path lookup. Four retail mode-0 bots now use these gates, not the provisional timed flamethrower, for state-7 → 11.

Native `L2:0x2d812c..0x2d8228` queries semantic joint 0 on every state-11 tick. Godot now queries Ratchet's real capsule from the decoded animation contact socket using a **provisional Godot ray**. The original contact solver, damage descriptor flags and target filters remain to be translated. Clip completion (`Moby+0x70 & 2`) routes state 11 → 13 / sequence 8 or 10, while state 10 → 12 / sequence 9 has its distinct animation-completion path. Godot samples `Animation.Wrapped` for completion; the exact native completion-bit behavior still needs comparison.

Native `L2:0x2d7ecc..0x2d7f8c` spawns particle program **15** only in **state 8**, if global counter `0x15f5cc & 3 == 0` and Moby `+0x31 != 0`. State 9 shares the subsequent detection routine but never runs the emitter itself. OBP now gates the event by an approximate 60Hz host global-frame counter; its existing GPU flame is an **explicit temporary visual proxy**, not verified retail particle art or exact cadence.

The real Godot `--rac1-enemy-smoke --destination rac1:LEVEL2` staging fixture uses original Constructorbot **#548**, its source animation, native runtime identity, collision body and playable Ratchet. It has demonstrated native **state 7 → 11**, clip 8 and live joint-socket hit detection reducing player Nanotech **4 → 3**. The fixture repositions Ratchet near the elevated joint socket and is NOT proof of natural ground-level combat. Full path motion, state8/9 exit logic, original emitter parameters, state13 recovery and per-mode end-to-end fights are outstanding.
