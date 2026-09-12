# R&C1 crates, bolts and pickup behavior

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`). Public tooling is used only for terminology/corroboration; promotion decisions below come from the retail data and executable.

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

Loading the savestate reproduces the same state, so persistence across savestate restore is proven. The event that clears these native bitfields is **not** proven: death, checkpoint reload and planet reload are not assigned reset semantics by OBP until a retail witness distinguishes them.
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

`Rac1BoltCrateSession` is an R&C1-owned deterministic host for the admitted representative loop. It takes a neutral `RuntimeEntityState`, validates class-500 authored authority, applies only positive recovered damage, validates a caller-provided native RNG total against the recovered centre-10 range, creates the proven low-value bolt pieces, and projects the destroyed crate to neutral `Inactive` presence.

Native state numbers, packed offsets, PVar bytes, UID persistence bits, reward RNG and pickup class numbers do not enter `OBP.Runtime` or Godot. Collection credits each outstanding pickup exactly once. The source-game UID is used to prevent duplicate payout within the deterministic session.

Payload-free evidence is frozen in `research/generated/rac1-bolt-crate-reward-loop.json`. Portable tests use synthetic authored payloads; retail assertions are gated behind `OBP_RAC1_ISO`.
