# R&C1 crates, bolts and pickup behavior

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`). Public tooling is used only for terminology/corroboration; promotion decisions below come from the retail data and executable.

## Working implementation, 18 October 2026

The working tree extends the historical class500 milestone below to ordinary
wood500, Nanotech501, metal502 and ammo511. Other classes are not admitted by
numeric proximity. Production rules live in `OBP.RAC1.Gameplay`; Godot supplies
contact facts and presentation.

`tools/rac1-crate-bolt-probe.py` verifies the complete ISO hash and Veldin's
seven-section overlay before emitting
[`generated/rac1-crate-bolt-probe.json`](generated/rac1-crate-bolt-probe.json).
It retains thirteen executable block hashes, state destinations, fragment dispatch,
placed/emitted bolt scales, resting-pose constants and ammo descriptors, without retail payloads or machine paths. This pass used
offline disassembly and Godot fixtures; no fresh PCSX2 trajectory replay.

| Contract | Native witness | Current behavior |
|---|---|---|
| Crate damage | `0x2cf218` | Positive damage breaks wood/ammo/health; metal also requires flag `0x00020000`. Wrench `0x00010000` fails; Bomb Glove `0x00830000` qualifies. |
| Reward interval | `0x257470` | Centre >=8 uses nearest-integer quarter jitter, 2..7 uses one, 1 uses zero. Centre15 admits11..19. Zero emits nothing. |
| Physical partition | `0x260a88` | Native unbudgeted 1/5/20/50 denomination branch, exact value conservation. |
| Flight/settle | `0x2a5dd8`, `0x2a7438` | Z-up displacement at60 Hz; gravity10.8, random launch, surface rebound/rest, denomination-specific tumble. |
| Attraction | `0x2a7210`, `0x2219dc..0x221ac0` | Ordinary planar radius3/height1.75, unnamed special-state profiles, Bolt Grabber12/4.5; thrown wrench initiates homing. |
| Arrival credit | `0x2a67d0`, `0x2a6b70` | Curved homing, increment16/3600 capped48/60 per tick, velocity decay10/3600; one denomination credit on arrival. |
| Fragments | `0x2d09b8`, `0x2af8b0`, `0x2afa48` | Wood357/358, Nanotech348/349, metal354/355; launch/spin, gravity20, limited .75 reflection, fall-triggered shrink/fade/destruction. |
| Crate falling | `0x2d15fc..0x2d1668` | Gravity9.8/3600, downward displacement capped.3/tick; host support query lets unsupported stacks fall. |

`Rac1PickupAssets` loads active-level model packets, texture remaps and PS2
material metadata. All19 levels are checked for models13..16 and their sounds.
These have static single-frame sequences; the Moby motion rotates them.
Class13's one joint is a neutral rest anchor (Veldin posed/bind maximum delta
0.0000362). No generated replacement bolt meshes are used.

### Bolts placed in the world

The 19 levels contain **1,542 authored currency bolts**: 1,528 class13 pieces
worth one and 14 class14 pieces worth five (the latter on Pokitaru). Veldin has 29.
These reuse their original authored retail presentation and common live Moby
identity; collection does not create a replacement mesh or a crate reward.

The state0 initializer at `0x2a5e74..0x2a60b8` directly selects resting state3.
It probes from placement plus local up*.2 to placement minus local up*3. A hit
becomes the anchor and floor orientation unless PVar+54 preserves the authored
height. All 127 nonzero flag witnesses are on Batalia; they remain floating.
Missing floor hits also retain the authored anchor. Randomized X/Z angles and
spin direction drive the class-specific tilted/orbiting rest pose in
`0x2a6de8..0x2a7098`. The class13 centre rests about .12916 units along the floor
normal above its anchor. This is Moby motion around a single-frame model.

Both authored initialization and the emitted constructor replace the instance
scale with class scale times **.65/.7/.75/.6** for classes13/14/15/16. Decoding
already applies class scale, so presentation applies only these factors. This
also corrects the emitted bolts' previously omitted scale factor. State3
attraction starts with 3..7/60 displacement along the floor frame's X basis;
proximity, Bolt Grabber and thrown-wrench attraction share the homing/arrival
path above. A piece credits its exact denomination once, then terminalizes
its original authored entity to inactive presence.

Authored collection at `0x2a6a94..0x2a6b30` writes byte(live+b0+2) indexed by
signed UID(live+b2) to object-state arrays at `0x1caa24` and conditionally
`0x1cb684` before common removal. **These are separate from crate UID bitfields.**
The live+b0 sector/group bridge and map restoration are not recovered. OBP
retains collection while the level stays live, including player restart; it
does not serialize authored-bolt removal to campaign saves or promise removal
across level reload. Gold Bolts are a different collectible class and are not
part of this ordinary-currency path.

`Rac1PlacedBolt`, `Rac1PlacedBoltSession` and `Rac1BoltMotionSession` own the
source rules. Godot supplies the initialization ray and projects the motion onto
the original root. Its floor-normal alignment approximates the native Euler
frame; attachment to moving/destroyed Moby supports remains incomplete.

The fragment budget reads **free Moby slots**, global `0x15ffbc`, decremented
by the allocator. It does not measure frame rate. The host supplies an
unpressured256-slot budget: four primary and four secondary pieces. Lower
fragment budgets are modeled, but dynamic pool pressure and the optional
three-piece bolt reward budget are not simulated.

### Supply resources

Class511 PVar+cb selects the first-break preferred item. Item descriptor+3a
gives its model, ammo descriptor+0c its per-piece grant. All11 mappings are
tested against retail. There is one piece normally, two when the below-five
draw is zero. Fallback prefers owned items below capacity, then owned ammo
items; OBP descriptor order substitutes for the unresolved native twelve-slot
runtime order. Pieces use retail models, initial ballistics, delay, settling
and exact clamped grants. They wait at capacity. Their later collected-piece
expansion/disappearance states are not reconstructed.

Class501 creates806: **eight particles grant one health point**. Admission
requires a living injured player within distance10 and counts pending clouds
against missing health. The first arrival grants one point; further particles
cannot credit again. Full health retains the cloud. Ordinary health capacity
remains the existing four points; health upgrades are outside this pass.

### Sound and wallet

Core index+0x70 locates an eight-byte remap header followed by one four-byte row
per Moby class: u16 relative byte offset/u16 count, pointing to s32 SBlk ids.
Level-data slot1 holds the bank. The shared codec now lives in
`OBP.PS2.Audio.Ps2SoundBank989`, with `GcSoundBank` retaining the RAC2 API.
Veldin bolt13..16 local0 map to91/92/93/94; wood500/0 maps179, Nanotech501/1
maps181 (cloud first arrival), ammo511/0 maps182. Nanotech break501/0 maps180.
Metal explicitly calls500/0. Bolt sounds fire at
**attraction entry**, not wallet credit; the adapter throttles requests.

SoundDef+10/+14 hold the random pitch-modulation range selected at
`0x28db78..0x28dbb8`. Playback keeps that range and centre note/fine. Its
musical-ratio conversion uses128fine units/semitone and note60; positive centre
notes use44.1kHz, negative48kHz. SPU pitch-table quantization, attenuation, ADSR,
mixing and voice arbitration remain approximations. The pinned
[OpenGOAL pitch utility](https://github.com/open-goal/jak-project/blob/d04ac9d787be16e75ca1023b93efc8ad1a54d71c/game/sound/989snd/util.cpp)
and [handler defaults](https://github.com/open-goal/jak-project/blob/d04ac9d787be16e75ca1023b93efc8ad1a54d71c/game/sound/989snd/blocksound_handler.h)
corroborate those format facts; OBP's ratio formula is independently written.
The [pinned Wrench header](https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/wrenchbuild/level/level_core.h)
was the remap-layout lead. No external implementation code was copied.

The wallet survives level changes and projects into the HUD. Explicitly enabled
OBP campaign saves include optional `Bolts`; older schema2 files restore zero
and keep campaign/ammo. This is OBP save glue, not retail memory-card behavior.
Crate UID maps remain level-local and are not serialized to that file.

### Remaining boundaries and validation

Godot ray contacts approximate native swept spheres, and solid crate boxes
approximate native Moby collision. Fragment camera-quadrant choice, some
secondary scatter/orientation details and global RNG call order are incomplete.
Nanotech wisps use an explicitly named host particle presentation; native
sprite paths, dust, impact/collection sparkles and chrome passes are incomplete.
Break-transition timing is collapsed into the existing terminal event. Native
platform attachment, crushing and special respawn selectors remain outside the
ordinary path.

`tools/rac1-crate-smoke.ps1` checks Veldin and Novalis: fragments, scatter/rest/
homing, exact wallet/HUD, stack collapse, ammo, full-health cloud retention and
metal input/projectile contact/fragments. Captures are private integration artifacts, not matched
retail frames. See [interactive checks](../docs/RETAIL_TESTING.md).

`tools/rac1-placed-bolt-smoke.ps1` checks Veldin, Batalia and Pokitaru using
actual authored roots with staged contacts: resting height/rotation/scale,
floating placements, proximity entry, visible homing, attraction sound,
one-time original-entity removal, HUD credit, distant Comet-Strike and the
five-bolt denomination. Native-source tests census all 19 levels and pin the
initializer and distinct collection-state writes.

## Historical milestone evidence

The following sections retain the earlier live correlations. Their narrower
runtime-promotion limits are superseded by the working implementation above.

## Authored crate-family census

The retail authority contains the familiar class family across the 19 levels:

- class 500: 3,795 instances
- class 501: 214 instances
- class 502: 315 instances
- class 511: 431 instances

Pinned public Wrench labels these as Bolt, Nanotech, Metal and Ammo crates respectively. Those names are corroboration only.

All four families reference a 0x100-byte PVar. Every sampled/censused PVar uses native relative-pointer fixups at `+0x00` and `+0x08`, with no Moby-link fixups. The importer now preserves the exact packed 0x78 authored Moby record and its referenced PVar as opaque R&C1 payloads.

The packed placement fields `+0x0c` and `+0x10` remain globally unnamed. Wrench likewise leaves them as `unknown_c` and `unknown_10`; OBP therefore assigns semantics only for class 500 where live retail correlation proves them.
## Veldin class-500 authored/live bridge

Veldin has 103 authored class-500 crates. The loaded savestate contains the same 103 live objects using update routine `0x002cf218`.

Two packed fields bridge perfectly into live state:

- placement `+0x0c` -> live Moby `+0xb2`: authored UID, 103/103 matches;
- placement `+0x10` -> live Moby `+0xb4`: authored bolt reward centre, 103/103 matches.

Veldin's reward centres are 10 on 73 crates and 15 on 30 crates. OBP exposes the UID as neutral authored identity only for class 500; the reward centre remains interpreted by `OBP.RAC1` from the opaque authored record.

The class-500 update is a seven-state dispatch. For the ordinary Bolt Crate path the relevant witnesses are native active state 1, break transition state 3 and disabled/dead state `0xfd`. The current authority savestate has 77 class-500 objects in state 1 and 26 in `0xfd`.
## Break and persistence

For ordinary class 500 while in native state 1, the recovered collision/damage record must exist and its damage value at `+0x2c` must be strictly positive before the destruction path is taken. This does not define wrench hit windows or damage volumes; those remain separate combat work.

The ordinary path reaches `0x2d09b8`, then `0x2d0538`, and ultimately the resource/persistence routine beginning at `0x257470`. That routine reads the authored UID from live `+0xb2`, records it in native UID bitfields, then reaches the bolt emission service at `0x260a88`.

Persistence correlation is exact in the Veldin savestate:

- all 77 active state-1 crates have both observed UID persistence bits clear;
- all 26 disabled `0xfd` crates have both bits set;
- exceptions: zero.

Loading the savestate reproduces the same state, so persistence across savestate restore is proven. The later controlled Veldin environmental restart also keeps both recovered UID maps byte-identical through the death/class-0 rebuild boundary. A clear event remains **unproven** for the positive level-2 checkpoint restart and for planet reload because those transitions were not sampled for these maps; see `RAC1_CHECKPOINTS.md`.

## Reward selection and physical bolt pieces

The class-500 authored `+0x10` value is a reward **centre**, not an exact deterministic payout. Retail forms a native RNG interval around it before entering the physical-bolt service. For the representative Veldin centre 10, the recovered inclusive interval is 7..13.

Parent links on the live spawned pickups independently witness centre-10 totals 7, 8, 9, 11 and 12 in the authority savestate. OBP's deterministic test/session therefore accepts a caller-supplied native selected total only when it is inside the recovered interval; it does not invent or replace the game's RNG sequence.

`0x260a88` emits physical bolt pieces through constructor `0x2a7438`. The constructor maps denominations to native classes, and collection helper `0x2a6b70` maps the same classes back to values before incrementing the bolt counter:

| native class | bolt value |
|---:|---:|
| 13 | 1 |
| 14 | 5 |
| 15 | 20 |
| 16 | 50 |

For the representative low-value totals used by Veldin centre-10 crates, the recovered partition repeatedly emits value-5 pieces while the remaining total is at least 7, then emits value-1 pieces. Examples witnessed live include 7 = 5+1+1, 9 = 5+1+1+1+1 and 12 = 5+5+1+1. Higher-value piece-budget behavior exists in the same routine but is deliberately not generalized by this milestone.
## Pickup update and collection threshold

Bolt classes 13..16 share update routine `0x2a5dd8`. The constructor deliberately spawns a bolt into native state 1; later states handle settling/homing/collection. The final collection path calls `0x2a6b70` immediately before the pickup is destroyed and credits the exact class-backed denomination to the global bolt counter at `0x0015ed98`.

There is **not** one recovered fixed authored pickup radius. In the homing/collection path the final distance comparison reads a live pickup-PVar float at `+0x64`, and that threshold belongs to mutable pickup state. OBP therefore models collection as the proven collection event and exact denomination credit, while leaving threshold evolution/homing motion in R&C1 rather than fabricating a constant neutral radius.

## Runtime promotion

`Rac1BoltCrateSession` is an R&C1-owned class-500 controller over the shared `Rac1MobyRuntimeSession`. Registration preserves authored identity/PVar state in that common live-Moby store. Positive native damage arrives through the common damage-event transport and produces a typed class-500 admission without choosing a reward or changing lifetime. A separate class-local completion step validates the caller-provided native RNG total against the recovered centre-10 range, updates UID persistence, creates the proven low-value bolt pieces, runs the recovered active -> break-transition -> disabled states, and terminalizes the shared Moby to neutral `Inactive` presence.

Native state numbers, packed offsets, PVar bytes, UID persistence bits, reward RNG and pickup class numbers do not enter `OBP.Runtime` or Godot. The generic damage transport likewise does not own reward selection. Collection credits each outstanding pickup exactly once. The source-game UID is used to prevent duplicate payout within the deterministic session.

Payload-free evidence is frozen in `research/generated/rac1-bolt-crate-reward-loop.json`. Portable tests use synthetic authored payloads; retail assertions are gated behind `OBP_RAC1_ISO`.
