# Going Commando crate archaeology

**Authority:** `rac2-ntscu-v1.01` / `SCUS-97268`  
**Retail ISO SHA-256:** `9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5`

This document narrows the first interactive-object milestone to the native crate family. Retail bytes and the **loaded level overlay** are authority; public archaeology is corroboration only.

## Confidence

- **CONFIRMED** — direct retail-authority relationship.
- **CORROBORATED** — retail agrees with pinned public archaeology.
- **INFERRED** — strong interpretation, not yet behaviour-proved or symbol-named.
- **UNKNOWN** — unresolved.

## Explosive crates: class 505

**CONFIRMED:** class **505** is the timed explosive crate. The same exact-authority
disc was hashed locally before this recovery. The class has 159 authored instances
across seven level files; every one has PVar `+0xC8=0`:

| Level file | Class-505 instances |
| --- | ---: |
| 2 / Maktar | 66 |
| 3 | 32 |
| 7 | 10 |
| 9 | 14 |
| 13 | 33 |
| 14 | 3 |
| 17 | 1 |

Oozla contains the shared code but **no authored explosive crates**. Maktar's
`lvl.vtbl` entry at `0x2A6348` maps class 505 to update `0x3A52B8`. The following
Oozla instruction addresses refer to the loaded overlay, never the boot image.
The public OpenRAC `UpdateCrateMoby`/stack-handler notes and
`src/game/0030F7B0-update.c` provide corroborating leads; implementation was
derived independently from the authorized retail instructions, not translated C.

### Fuse, touch and blast

- `0x391240..0x3912D8`: class 505 accepts standing, side, ledge and explicit
  `damageFlags=0x01000000` touch records. Ordinary touch enters state 5 with
  command zero. The following state-5 update replaces zero with **179**, then
  decrements it at the end of the update: the approximately three-second fuse.
- `0x3912DC..0x391370`: state 1 retains the common mask `0x05830001` and positive
  damage magnitude check. A hit containing **0x20000** starts command **1**;
  other hits draw a float in **[5,15)**, then use `CVT.W.S` to select a **5..15**
  tick fuse. `0x310580` consumes the high 15 bits of newlib `rand`, multiplies
  by `(15-5)/32768`, then adds five; `0x2E45B8` converts the result. The source
  session uses the existing RAC2 conditioned newlib generator and EE arithmetic.
  Its stream is not a replay of other native global RNG consumers.
- `0x39174C..0x39187C`: a further queried hit while command is at least 16
  replaces the remaining long fuse with another 5..15 draw. This state-5 branch
  does not repeat state 1's positive-magnitude check. Beep SoundDef **1** runs
  when command `%60==57` or command is **13**. Flash RGB is **200/64/64** when
  command `%60>40` or command `<15`; other ticks restore the saved colour.
- `0x392724..0x392774`: detonation hides the crate, clears its collider, writes
  state **3** and initializes PVar `+0xCA=1`. The common stack update also has
  an immediate underside-break path; host underside contacts feed that path.
- `0x3913C8..0x3914E0`: steps **1..20** expand a Moby sphere by **0.1 units/tick**
  and a player sphere by **0.15 units/tick**, ending at **2** and **3** units.
  The centre is the original crate position plus half its up vector. The Moby
  descriptor carries **2 damage**, flags **1**, and the owning class-505 Moby.
  The separate player helper at `0x312D78` receives **4 damage**, flags
  **0x10000**; `0x312E10/14` writes those into the native damage record.
  These envelopes are intentionally separate. When `+CA` reaches 21, `+C8=0`
  follows the existing deactivation route.

The Maktar loaded body corroborates these transitions and envelopes. Its
reward helper `0x3A6390` takes the common Bolt path when `+C6=0`; authored
class-505 Bolt values are retained in the existing fresh-session payout policy.
The resulting physical objects retain their real denomination classes 13..19,
source build, crate UID provenance and one-time wallet receipts.

### Original sounds and the recovered flash slice

Maktar class-505 local SoundDefs **0/1** map to SBlk **73/74** (explosion/beep).
Both programs decode through the existing SoundDef/989snd path. All seven
authored levels admit two original class-505 cues.

The break helper's fragment switch at `0x39263C..0x392670` has **no class-505
mask**. Explosive crates therefore contribute recent-break pressure but emit
**no wooden detachable pieces**. Their dedicated explosion call at
`0x392778..0x3927B4` creates class **5024** with scale one. The first six flash
sprites at `0x3F8F68..0x3F914C` (Maktar `0x41F958`) use the already implemented
type-85 constructor/callback: stationary size **3 -> 4**, requested life **30**,
zero gravity/spin, initial opacity 100 for the first pair and 127 for four more,
end opacity zero. Colours are `0x388EF7` and `0x0063F7`. The caller replaces
the texture with definition **92**, frames **0/5/6**, in counts **2/2/2**.
These resolve to active-level Part textures **70/74/75** in the inspected banks.
Every authored level decoded this original flash slice successfully.

This implementation reuses that native type-85 simulation and source-qualified
Part bank. World-size billboard projection remains an OBP presentation adapter;
the remaining explosion particle families, native draw/depth sorting, full
camera shake, moving attachments and native repeat/global-action variants are
not claimed. No RAC1 explosion assets or recipes are substituted.

### Host and player boundaries

Class 505 is promoted from welded scenery into an individual source-qualified
actor. Godot supplies physical touch/underside contacts, closest-AABB blast
queries and solid-world visibility; those are host projections, not PS2 narrow
phase equivalence. Blast visibility excludes registered actor bodies, while
falling/support queries retain them. Native chain damage reaches admitted RAC2
crate/hostile controllers with the actual explosive owner, never a fabricated
weapon key. Additional foreign-actor blast conversions remain outside this slice.

The persistent player's explicitly admitted conversion receives the native
integer amount through its existing health controller; a destination does not
select a health provider or avatar. The hidden state-3 crate remains a registered
damage producer for its 20-step sphere, but is no longer a weapon target or solid
support. A source-frame/target ledger prevents repeated applied hits; the player
controller retains its own protection/death rules. A foreign physical weapon
blast maps explicitly to the RAC2 fast-fuse category; foreign flag bits are not
reinterpreted as RAC2 flags. Player knockback remains the selected controller's
existing reaction rather than a recovered RAC2 movement rule.

Local ordinary Maktar input verified touch beeps and flashing, a three-crate
chain, nearby ordinary crate destruction, wrench-shortened fuses, four explosive
crates triggered by a Bomb Glove blast, player defeat at close range, and actual
rendered RAC2 fire/flash sprites with Bolt models. Local probes confirmed all
159 authored crates' non-repeat fields and seven banks' sound/flash admission.
A temporary solid-wall control blocked both nearby explosive neighbours while
the unshielded control chained them; collecting subsequent physical Bolts raised
the same persistent wallet from 10 to 42 without duplicate credit.
Captures, observers, native payloads and diagnostic executables remain local;
no automated tests or test infrastructure were added.

## Physical Bolt rotation: classes 13..19

**CONFIRMED:** the loaded Oozla constructor `0x378270`, update `0x376D58`,
ground-pose helper `0x377D10` and attraction entry `0x378098` use distinct
rotation rules for flight, landing and collection. The previous OBP presenter
invented two Euler rotations from one airborne angular rate and kept applying
them on the floor. That presentation has been replaced with source-owned poses.

- `0x37850C..0x3785A8` chooses a turn direction, two initial ground angles,
  and an airborne angular rate in **[0,720) degrees/second**. The quaternion
  axis is the normalized cross product of launch velocity with
  **owner position minus pickup position**. Updates at `0x3770D0..0x3770E4`
  compose this increment with the current basis, rather than rotating two axes
  independently.
- `0x377E8C..0x377FF0` constructs a local X/Y/Z Euler pose in the support's
  normal-derived frame. The X and Z angular steps have opposite signs,
  selected by the constructor's direction bit. The Z magnitude is **0.0125
  radians/tick**; the X rate and fixed Y tilt depend on denomination:

| Native class | Value | X step, radians/tick | Y tilt, radians | Normal offset | First-basis offset |
| --- | ---: | ---: | ---: | ---: | ---: |
| 13 | 1 | 0.04 | -0.25 | 0.04875 | 0.325 |
| 14 | 5 | 0.03 | -0.5 | 0.0245 | 0.35 |
| 15 | 20 | 0.025 | -0.4 | 0.05625 | 0.375 |
| 16..19 | 50/100/500/1000 | 0.0175 | -0.35 | 0.135 | 0.3 |

- `0x377FF4..0x378034` positions the model pivot at the raw contact anchor
  plus the support up vector times the normal offset, plus the posed first
  basis vector times the first-basis offset. Holding the pivot at a fixed
  contact-radius clearance while turning the off-centre model is incorrect.
- `0x3773CC..0x3774BC` blends the saved airborne quaternion toward the moving
  ground pose with slerp weight **1 / remaining first-hop ticks**. Position
  similarly blends from contact-anchor Z plus **0.25** toward the compensated
  ground pivot using the remaining/total fraction, then adds the existing
  parabolic first hop. State 3 uses the ground pose directly, with its smaller
  second hop.
- `0x378174..0x3781F8` gives a resting pickup an initial velocity along the
  support up vector of **8/60 units/tick**, then builds a new quaternion about
  `cross(velocity, target position - pickup position)` at a fixed **720
  degrees/second**. That rate belongs to attraction, not to floor rotation.

The host transforms the native Z-up quaternion into the scene coordinate frame
without changing the original **0.5** model scale. Contact points and the player
socket remain explicit Godot adapters. Random samples reproduce these domains,
not the retail global RNG history; moving supports, camera-side steering and
pickup sparkles remain unavailable. A local manual pose probe covered all seven
denominations on flat and sloped contacts, first-hop settling and attraction;
an ordinary Maktar run also confirmed the presented scale stays at 0.5 through
rotation and the landed pickups still attract and credit the wallet. Retail
instruction dumps, captures and probe executables remain outside the repository.

## Nanotech and ammunition crates: playable shared family

The current authority pass closes the Oozla identities and first-break paths:

- **501 = Nanotech crate:** 13 authored Oozla instances. The common initializer
  at `0x3911FC..0x391224` creates class **806** through `0x394E90` with the
  ordinary-health flag zero. The cloud starts attached to its owning crate;
  collection becomes available after the owner breaks. Class 512 uses the
  alternate flag and is outside this implementation.
- **511 = ammo crate:** 26 authored Oozla instances. Initialization writes
  PVar `+0xC6 = 100`; the reward helper takes the indexed-resource branch.
- **Shared damage/lifetime:** `0x3912DC..0x391394` admits both through the
  common queried flags and positive damage magnitude. Neither gets a new HP
  threshold. State 3 with `+0xC8=0` reaches `0x306250` at `0x391544`.
  Nonzero respawn fields remain explicitly unsupported.
- **Own fragments:** `0x392628..0x3926C4` gives class 511 the wood crate's
  masks `1/5/0x25`. Class 501 instead uses contiguous masks
  `(1 << (budget-1))-1`, yielding parts `0/1/2` at the highest budget.
  Each type loads its own original meshes, pivots, contact vertices, textures
  and authored-instance lighting. Falling, support removal and generic fragment
  contact/settling use the existing crate machinery.

**Ammo amount/selection:** `0x30F570` prefers enabled indexed resources with
remaining capacity, otherwise chooses among enabled ammo resources. It returns
two physical objects when `RNG(5)==0`, one otherwise (`0x30F77C..0x30F798`).
The class-3291 constructor reads per-object amount from mapped descriptor
`+0x8C` (`0x3D3FC0..0x3D3FE4`), while collection caps at `+0x8E` and reports
the actual increase (`0x3D3A68..0x3D3AE4`). Oozla's slot 30 maps to base
Lancer class 1490, **25 rounds per object**, **200 capacity**. The existing
base Lancer profile and this descriptor are authority for that implemented
recipient; upgraded weapons/runtime remapping are not claimed. Other level
overlays relocate their descriptor tables, so the reader deliberately uses
the recovered Oozla descriptor and the *active level's* class-3291 **part-0** visuals.
Local extraction confirmed the model/descriptor combination for levels 0/1/2/8.

**Ammo visibility correction:** the ordinary class-3291 mesh is a four-vertex,
two-triangle, untextured placeholder, only about **0.001 units** across. Earlier
manual lifetime/collection observations did not establish that mesh's visibility.
The loaded constructor at `0x3D3F80..0x3D3F94` instead calls `0x2D11B8` with
**type 2, class 3291, part 0**. Its initializer at `0x2D1500..0x2D1524` enables
the detachable-part mask and reads that part's kernel. `Rac2SupplyAssets` now
decodes `ReadBangle(class3291, 0)` and applies the same class-table texture remap
used by other detachable parts, rather than treating the parent mesh as a
visible pickup. Oozla's original part has **86 vertices, 96 triangles**, remaps
slot 0 to **Moby texture 166**, and measures about **0.557 x 0.612 x 0.643 units**
in host X/Y/Z. No enlargement or replacement geometry is applied. A rendered
ordinary-game observation confirmed the blue, white-emblem canisters above
the Oozla floor with their original texture; its image remains local.

**Nanotech admission/credit:** `0x395BD0..0x395D04` requires living health,
room after accounting for pending clouds, distance below **10** and an
unobstructed world query through `0x3956E8`. Eight particle slots are
initialized at `0x3958A4..0x3958C8`; the first arriving particle performs
the one-time health credit and sets `+0x56` so the others cannot credit again
(`0x396374..0x396454`). Ordinary flag zero grants **one** health unit and
clamps to current capacity. The alternate/challenge-mode paths are not
implemented by this ordinary slice.

**Original audio:** Oozla Nanotech class 501 local SoundDefs 0/1 map to
bank sounds **70/71** (break/arrival); ammo class 511 uses local **1**,
bank **73**, for its break; class 3291 local 0 maps to bank **151** for
collection. The shared SoundDef reader now handles multiple local definitions
and bounded pitch-bend ranges. It retains the corrected PB versus PM semantics.

**Explicit OBP adapters:** the normal persistent player's controller receives
one unit per ordinary Nanotech cloud; the world never selects a health provider
or capacity. At the user's request, ordinary ammo drops are **unassigned until
collection**, including drops created while the arsenal is full. Fusion's
`PlayerAmmoSupplyPolicy` chooses an owned, implemented, attached-source
recipient that is missing ammo at that moment, and each
source supplies its own native grant amount (RAC1 crate ammo definitions, the
RAC2 Lancer descriptor, RAC3 Shock Cannon's four-round profile). This is an
OBP cross-game conversion, not a claim that retail RAC2 knows foreign items.
Pickup identities retain `SourceWeaponKey`; no native item-number aliases or
second player inventory are created. If every weapon is full, pickups remain
on the ground. Pickup admission uses RAC2's recovered ordinary reach: a
**4.25-unit horizontal radius** and **2.25-unit absolute vertical allowance**,
plus an OBP twelve-tick drop grace period and world visibility ray. The former
1.25/1.25 host limits were too short and are removed. A successful collection credits one
recipient's clamped native pickup amount, then consumes that object's receipt;
other drops reevaluate the updated inventory. This collection-time policy is
an intentional departure from retail's constructor-owned resource index, not
a new claim about `0x30F570`. Full pickups wait for capacity, and
dead/retired player/world leases cannot receive grants.

**Range authority:** Oozla's class-3291 update at `0x3D3944..0x3D3990` compares
absolute height difference with player `+0x24A8` and planar distance with
`+0x24A4`. The planar helper `0x2E3650` ends with a VU square root, so its
threshold is a linear radius, not a squared distance. Player initialization
at `0x2A7024..0x2A7048` temporarily writes 2.125/1.25; the ordinary update at
`0x2BE700..0x2BE714` writes **4.25/2.25**. A separate availability-byte branch
at `0x2BE6D8..0x2BE6FC` writes 14/4.5; that upgrade/context branch is not
silently granted by the ordinary OBP ammo item. These constants stay in RAC2,
and do not mutate the persistent player's equipment or pickup capabilities.
An ordinary Oozla observation held the player 4.35 units horizontally from
the leftmost settled pickup while Bomb Glove was missing one round: all three
drops remained. Moving to 4.15 units collected that pickup once, credited +1
and left the other two drops available. The temporary observer/profile stays
outside the repository; no automated tests were added.

**Requested RAC1 sphere donor:** Nanotech presentation reuses
`Rac1NanotechOrbAssets`, `Rac1NanotechCluster`, `Rac1ParticleSession`,
`Rac1FxMaterials` and the existing particle renderer. Assets are authenticated
against the attached **RAC1 NTSC-U Veldin (level 0)** tables; the RAC2 destination
ID never selects the donor. The intact crate hosts the glowing sphere/halo and
orbiting sprites; breaking transfers that same cluster into its resting state.
Collection uses the donor's curved eight-orb flight and healing trail, with the
first orb arrival applying the RAC2-owned single health credit and arrival sound.
This is deliberate cross-game presentation as requested, not recovered RAC2
particle/GS parity. The former blue host spheres and fixed 45-tick flight are
removed. World retirement disposes riders, waiting clouds, flights, materials
and particles together.
The selected donor requires an attached, verified RAC1 source. If it is missing,
the existing atomic crate-admission boundary reports that failure and cleans up
partial crate state; it does not silently substitute another effect. Ordinary
trilogy startup already requires all three sources. Per-type partial admission
for source-isolated diagnostics is not implemented by this world owner.

The ammo
pickup uses original class-3291 part 0 with a bounded scatter/homing host
projection and world sweeps; its stopped-on-contact translation is explicitly
not the complete native generic contact manifold. Ground sweeps retain the
model's decoded bottom clearance rather than placing its centre on the floor.
The exact PS2 RNG history,
upgrade/challenge state, arena respawn and cross-world pickup persistence remain
outside this slice.

Manual ordinary Oozla observations used temporary external SceneTree observers
and normal input, with no committed observer or automated tests: wrench hits
broke ammo crates 235/236 and credited actual recipient amounts (Bomb Glove
three rounds and Blaster twenty); breaking Nanotech crate 222 from a temporary
injured player profile raised health **2/4 -> 3/4**, with one arrival cue and
no remaining cloud; the same break at full **4/4** retained its cloud without
crediting health. Two ordinary Bomb Glove throws from beside ammo crate 235
broke ammo 235/236 and Nanotech 222, with the same one-time supply receipts.
Repeated contacts did not duplicate rewards. The original
seven-crate bolt-cluster Bomb Glove observation still completed successfully.

Follow-up manual observations with the RAC1 donor recorded the actual two-pass
sphere/halo mesh (1,053 submitted triangle vertices) and its orb/sparkle sprites
on intact and resting RAC2 crates. The injured profile again healed **2/4 -> 3/4**
once using the curved first-arrival event; at full health the sphere remained.
An all-full arsenal broke two ammo crates and retained three physical pickups;
firing one Bomb Glove round **40 -> 39** while five units away left all three
available. Approaching one then credited **+1** to Bomb Glove (clamped to 40),
retired that pickup and left the other two on the ground. The recipient therefore
depends on inventory at collection, not on what was missing when the crate broke.
Observers and temporary profiles remain outside the repository.

## Identity and authored structural family

`oClass 500 = Bolt Crate` is **CORROBORATED** by pinned Wrench commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb` (`docs/asset_system.md`) and the retail footprint.

Classes `500/501/505/511/512` are **CONFIRMED** to share the authored `0x110`-byte PVar layout, mode `0x20`, and relative-pointer fixups at PVar `+0x00/+0x08`. Class 500 is authored 1,979 times across **18** retail level files. See [`GC_PVARS.md`](GC_PVARS.md).

The packed GC Moby instance field at `+0x14` is the authored Bolt reward value. The authority build gives class 500 **24 distinct values from 1 through 1000**; on Oozla, 189 of 190 class-500 instances carry `13` and one carries `14`. Oozla's loaded static-Moby population path directly copies this field into live Moby `+0xB4` (details below), so OBP exposes it as `MobyInstance.Bolts`. Pinned Wrench independently names the same authored field `bolts`.

Class `502` is an important negative result: loaded crate code explicitly retains a class-502 branch, but the exact-authority census finds class 502 **not listed and not authored in any of the 27 retail gameplay files**. It is therefore a **CONFIRMED dormant/shared-code branch in this build's authored data**; dynamic reachability remains UNKNOWN.

### Static instance -> live Moby bridge

Oozla's loaded retail population code closes the authored/runtime identity directly. It reads gameplay pointer `+0x4C`, enters the Moby block, consumes the `0x10`-byte block header, and then walks the packed static records. In the per-record path:

- `0x002F73C0`: `lw $s4,0($s2)` reads authored record `+0x10` (`uid`);
- `0x002F73C8`: `lw $v0,0($s2)` reads authored record `+0x14`, then `0x002F73D4` preserves it in `$s1`;
- the runtime Moby address is formed from the live pool base plus `index << 8`, confirming a `0x100`-byte live-Moby stride;
- `0x002F769C` calls live-Moby initializer `0x00305FE0`;
- `0x002F76A8`: `sh $s4,0xB2($s3)` writes the authored UID into live `+0xB2`;
- `0x002F76C8`: `sh $s1,0xB4($s3)` writes the authored `+0x14` value into live `+0xB4`.

Thus the mapping is **CONFIRMED retail dataflow**. The loader stores the low 16 bits into the live halfword fields; all observed class-500 UID/reward values used here fit that representation. Separately, live-Moby initialization at `0x0030608C` writes the native class ID to live `+0xAA`.

## Loaded runtime authority: one crate-family update routine per level

The old fixed-address boot-ELF route was wrong for gameplay because the level overlay replaces those regions. The correct route is `lvl.vtbl` -> loaded `.text`.

Exact-authority vtable probes confirm that authored members of the crate family share one update routine inside each loaded level:

| loaded level | authored family members checked | shared loaded update |
|---|---|---:|
| LEVEL1 / Oozla | 500, 501, 511 | `0x00391080` |
| LEVEL2 / Maktar | 500, 501, 505, 511 | `0x003A52B8` |
| LEVEL8 / Endako | 500, 501, 511, 512 | `0x003AB1E8` |

The changing address is expected: each level supplies its own executable overlay. Do **not** treat `0x00391080` as a universal GC crate address.

The Oozla copy is a **CONFIRMED seven-state Moby state machine**:

- Moby `+0x20` is used as the state byte and dispatched through seven cases (`0..6`);
- Moby `+0x68` supplies the PVar pointer;
- the routine explicitly tests class IDs `500/501/502/505/511/512`;
- class-specific transitions/timers and linked-Moby state are handled inside the shared routine.

The exact gameplay names of states `0..6` remain **UNKNOWN**.

### Class-500 break predicate

The common update calls loaded `0x00312B68(moby, 0x05830001, 0)` before dispatching the state. That helper resolves an owned collision-damage record and returns it only when `(damageFlags & 0x05830001) != 0`. The caller then explicitly discards the exact damage-flag word `0x01000000`. GC retail dataflow fixes the consumed flag word at record `+0x24`; later public PS2 Ratchet structures corroborate the semantic name `damageFlags`.

In state 1, the normal class-500 path performs one further test: `0.0 < record[+0x2C]`. GC's descriptor-to-record pipeline independently fixes this as the damage magnitude field, and later public structures corroborate the name `damageHp`. If that comparison succeeds the crate immediately calls `0x003922B0` and then `0x00392158` in the same update. There is no class-500 HP decrement or multi-hit threshold in this state. Therefore the native break predicate is **CONFIRMED** as qualifying damage flags plus positive `damageHp`. See `GC_WRENCH.md` for the recovered player state-20 damage specimen that satisfies this predicate.

`0x003922B0` later writes Moby state `3` and timer byte `15`. On the next state-3 update, normal class 500 follows authored PVar `+0xC8`; all 190 Oozla Bolt Crates have `+0xC8 == 0`, which routes directly to loaded `0x00306250`. That helper moves the Moby into native sentinel state `0xFD` or `0xFE` and runs its deactivation path. Thus Oozla's normal Bolt Crate is **CONFIRMED to leave active gameplay immediately after the short break transition**, although the exact debris/effect presentation is not yet reconstructed.

## Runtime PVar field use

Loaded retail code upgrades several formerly structural fields into behaviour-backed facts:

| PVar offset | retail runtime evidence | status |
|---:|---|---|
| `+0xA0/+0xA4` | linked Moby pointers are read, copied, released and traversed by the crate-family helpers | **CONFIRMED runtime links** |
| `+0xAC` | flag word manipulated during transitions | **CONFIRMED runtime state** |
| `+0xB4` | counter/timing-like word incremented/read in the state machine | **CONFIRMED runtime state**, exact meaning UNKNOWN |
| `+0xC6` | runtime halfword read by helpers; class 511 initializes it to `100` | **CONFIRMED runtime field**, exact meaning UNKNOWN |
| `+0xC8` | authored `0/1` field read by loaded native crate code | **CONFIRMED live authored input**, exact meaning UNKNOWN |
| `+0xCA` | written to `1` on one class-505 transition path | **CONFIRMED mutable runtime flag**, exact meaning UNKNOWN |
| `+0xCB` | read by the resource-emission helper and mapped to candidate type values | **CONFIRMED runtime selector input**, exact meaning UNKNOWN |
| `+0xCC` | authored `0/1` field read by the loaded Maktar crate update during state initialization | **CONFIRMED live authored input**, exact meaning UNKNOWN |

Thus the arena-correlated `+0xC8/+0xCC` bytes are no longer merely statistical curiosities: both are consumed by native runtime code. Their precise challenge/arena semantics are still UNKNOWN.

## Reward paths: Bolt Crates are separate from indexed resources

Loaded Oozla code now proves that `0x00392158` contains **two distinct reward paths**. Class 500's normal Bolt Crate path does **not** construct class 3291 when PVar `+0xC6 == 0`.

### Class-500 Bolt reward path

For the class-500 branch, loaded `0x00392158` computes a small integer from a progression-like global plus `0x003104E8(2)`, then calls:

```text
0x00392158
  -> 0x0030F920(crate, 0x100, pieceBudget)
  -> 0x003177C8(..., pieceBudget)
  -> 0x00378270(..., denomination)
  -> creates one denomination-specific runtime pickup object
```

`0x003104E8(n)` is **CONFIRMED** to be an integer modulo-RNG helper: it obtains the native RNG value, masks it to 15 bits, divides by `n`, and returns the remainder. Therefore the call with `2` returns exactly `0` or `1`.

The input at `gp + 0x3AC` is **CONFIRMED as recent break-fragment pressure** by its decay/increment path in `0x003922B0`, recovered below. Its historical name in the reward API is retained. Its effect on the third argument is:

| progression-like input | base | `pieceBudget` after RNG(2) |
|---:|---:|---:|
| `< 11` | 2 | 2 or 3 |
| `11..20` | 1 | 1 or 2 |
| `>= 21` | 0 | 0 or 1 |

The important correction is what this value controls. `0x003177C8` first chooses the reward value, then — when the class-500 path's `0x8` flag is set — uses `pieceBudget` to cap how many **physical denomination pickup objects** it emits. It greedily partitions the selected value using denominations:

`1000, 500, 100, 50, 20, 5, 1`.

For each denomination it emits at most the remaining physical-piece budget. Any value left after that budget is exhausted is added to `0x001A7A14`. A later reward event drains `ceil(pool / 50)` from that global, adds the drained amount into its new reward magnitude, and stores `pool - drained` back. Therefore `0x001A7A14` is **CONFIRMED as a deferred-reward reservoir**; it is not an immediate direct-credit path.

Thus recent break pressure and RNG determine the physical pickup-count budget. Rapid breaks reduce physical pieces; this is unrelated to campaign progression.

`0x00378270` is the denomination-object constructor used by this path. Its denomination input selects seven runtime object resources/classes through a generic object allocator:

| denomination | allocator selector |
|---:|---:|
| 1 | 13 |
| 5 | 14 |
| 20 | 15 |
| 50 | 16 |
| 100 | 17 |
| 500 | 18 |
| 1000 | 19 |

The selector/value relationship and per-object initialization are **CONFIRMED retail dataflow**. Interpreting 13..19 specifically as Bolt-denomination pickup identities is **INFERRED with strong native support** from their exclusive denomination mapping in this reward path.

#### Base reward scaling

Loaded helper `0x002D9528` establishes the normal reward scaler. Retail code reads a packed 4-bit selector from `0x0019B4A8 + contextIndex*0x400 + floor(uid/2)` (low/high nibble chosen by UID parity). Selector bit 3 chooses one of two 8-byte percentage banks; the low three bits choose the percentage within that bank. The helper returns the integer quotient:

`scaledValue = percentage[selector & 7] * mobyRewardValue / 100`.

Retail independently proves live Moby `+0xAA` is `oClass`. The scaler consumes the nearby live fields `+0xB2` as its UID selector input and `+0xB4` as the reward-value input. The static-to-live population path is now traced directly: authored `+0x10` (`uid`) is copied to live `+0xB2`, and authored `+0x14` (`bolts`) is copied to live `+0xB4`. Therefore the scaler's inputs are **CONFIRMED** to be the authored UID and Bolt reward value.

The authored-value percentage banks are loaded `.lit` bytes at `0x001A89C8` and `0x001A89D0`: `[100,50,40,30,25,20,15,10]` and eight `100` values. The earlier emission branch uses the adjacent banks at `0x001A89D8` and `0x001A89E0`: `[100,50,40,30,25,20,15,10]` and `[100,30,10,10,10,10,10,10]`. All 27 retail level overlays contain the same 32 bytes. Oozla callers at `0x0030FD64/0x0030FD74` select the authored pair before calling `0x002D9528`; `0x0030FB48/0x0030FB64` select the emission pair.

This corrects an earlier MIPS address-decoding error. `addiu` sign-extends its 16-bit immediate, so `lui 0x1A; addiu -0x4B58` resolves to `0x0019B4A8`, not `0x001AB4A8`, and `lui 0x1B; addiu -0x7638` resolves to `0x001A89C8`, not `0x001B89C8`. The real selector region is zero-filled in the retail executable image and is mutated at runtime. The previous `144/190` Oozla selector census sampled unrelated bytes and is discarded. Exact runtime selector-state initialization and context identity remain **UNKNOWN**.

A further local layer check closes a misleading boot-code lead: when Oozla is loaded, virtual address `0x0029B3E0` lies inside the level overlay `.data`, while `0x002DF3F8` is overwritten by the level overlay `.text`. Boot-image routines at those addresses therefore cannot be treated as live in-level selector initializers. OBP's current fresh-showcase path deliberately chooses selector `0` because it is a native-valid encoding backed by the zero-filled executable image; this is an **OBP harness initial condition**, not a claim about selector state for arbitrary retail saves or revisit history.

The scaled value is additionally multiplied by `max(1, byte[0x001A7A32])` in the normal path. Retail code advances that byte up to a maximum of 20 using a subordinate counter at `0x001A7A33`. The multiplier mechanics are **CONFIRMED**; interpreting these two bytes as GC's Challenge Mode bolt multiplier/sublevel is **INFERRED with strong behavioural support** until a retail symbol or independent GC structure names them.

After the deferred-reservoir release is added, `0x0030F920` converts the resulting magnitude into an integer centre and chooses a symmetric random range: values below 2 have zero spread; values from 2 to under 8 use spread 1; values at least 8 use approximately 25% spread (converted with the native float-to-int helper). `0x003177C8` then selects inclusively from `[centre-spread, centre+spread]` using the native modulo RNG. The control flow is **CONFIRMED**; exact host reproduction of the PS2 FPU conversion mode is intentionally not asserted yet.

### Generic indexed-resource branch

When the Bolt-specific condition does not apply, `0x00392158` follows the separate indexed-resource path:

```text
crate-family loaded state/transition path
  -> 0x003922B0   transition/effect/link helper (semantic name INFERRED)
  -> 0x00392158   indexed-resource branch
  -> 0x0030F570   choose an indexed resource with remaining capacity
  -> 0x003D3E28   dynamic resource-object constructor
  -> creates oClass 3291
  -> installs update 0x003D37A0
  -> class-3291 update
  -> 0x002C9738(index, amount)
  -> bounded indexed counter increment
```

This branch reads PVar `+0xCB`, maps candidate resource values, and repeatedly calls `0x003D3E28`. Calling it an indexed-resource emission/drop branch is **INFERRED** from the complete dataflow. Class 3291 is **not** the class-500 Bolt pickup path.

### `0x003922B0`

**CONFIRMED structure:** this routine manipulates linked Mobies, PVar flags/state, effects and Moby state, and has class-specific paths for the crate family. It sets one transition state and participates immediately before reward emission.

A destruction/break-transition role is **INFERRED**, not yet promoted to CONFIRMED because its incoming hit/event semantics are not fully named.

## Runtime-only class 3291

The static census and loaded code agree:

- class `3291` is listed in every retail level's Moby class table;
- it has zero authored static instances and zero authored static PVars;
- Oozla `lvl.vtbl` resolves its loaded update to `0x003D37A0`;
- loaded function `0x003D3E28` dynamically creates oClass 3291 through the runtime Moby allocator;
- constructor PVar `+0x60` carries an **amount** and `+0x68` carries an **index** into the indexed count system;
- the class-3291 update later passes those same values to `0x002C9738`.

The collection path is now more specific. Immediately before the add/clamp call, the update reads the selected count from `0x00139688 + index*4`. It calls `0x002C9738(index, amount)`, reads that same slot again, and produces collection feedback only if the value increased. The feedback path receives the actual `after-before` delta.

This is **CONFIRMED** dataflow. A generic indexed collectible/resource interpretation remains **INFERRED with strong native support**; do not universally label class 3291 as a bolt pickup.

## 56-slot indexed resource system

Exact-authority Oozla disassembly at `0x002C96B0..0x002C981C` and `0x0030F570..` establishes the table structure used by class 3291 and crate resource selection:

- current 32-bit counts begin at `0x00139688`, indexed by `index * 4`;
- a parallel byte mapping begins at `0x00139568`;
- each mapping byte selects a descriptor record of size `0xE0` at base `0x002637A0`;
- descriptor halfword `+0x8E` is the per-entry maximum/capacity used by add/subtract/select logic;
- the selector scans **56 indices (`0..55`)**;
- another per-index byte table at `0x001A7AF8` gates candidates;
- the selector only counts/selects enabled entries whose current count is below the mapped maximum;
- effective exclusions visible in the 0..55 scan include indices `31` and `45` (the code also compares against `61`/`77`, which are outside this loop's range).

`0x002C9738` adds an amount to a selected count and clamps to the mapped descriptor maximum when nonzero. Nearby loaded code implements the complementary bounded subtraction/get paths.

Pinned Wrench `data/memcard/types_gc.h` independently describes `Ammo[56]` alongside 56-entry gadget/item arrays. The exact 56-entry match is **CORROBORATION** for an ammo/inventory-resource interpretation of this runtime table, not a retail symbol name. The exact slot-to-weapon/resource mapping is still UNKNOWN.

### Correction: `index = -1`

Earlier archaeology considered `index=-1` a possible bolt sentinel because it appeared adjacent to the indexed count table. The loaded helper bodies disprove that simple interpretation.

Multiple count helpers explicitly test `index == -1` and replace it with the runtime value stored at `0x0019B068` before indexing the tables. Therefore:

- **CONFIRMED:** `-1` means “use a runtime-selected/current index” in these helpers;
- **NOT CONFIRMED:** `-1 = bolts`;
- any Bolt Crate reward identity/amount must be proved from the crate emission helper's actual constructor arguments or a separate bolt-specific path.

This correction supersedes all earlier adjacency-based `-1 = bolts` speculation.

## Resource selection service

Loaded `0x0030F570` scans the 56-slot system described above, chooses among enabled entries with remaining capacity, and writes a selected index to the caller-provided output. It also uses loaded RNG helper `0x003104E8` while choosing among candidates.

The selection mechanics are **CONFIRMED**. Calling the table an ammo table is **CORROBORATED/INFERRED**, while other resource names and broader crate-family selector rules remain UNKNOWN; the playable supply section above closes base Lancer slot 30.

## Critical boot-ELF correction retained

Earlier archaeology followed fixed addresses in `SCUS_972.68`. That was not runtime authority because the selected level's custom executable overlay overwrites non-core `.lit`, `.bss`, `.data`, `lvl.vtbl`, `lvl.camvtbl`, `lvl.sndvtbl` and `.text` regions.

The following old boot-image interpretations remain explicitly superseded and must not be used as implementation specifications:

- `0x003120C8` as a crate destruction transition;
- `0x00311F70` as a crate reward/drop helper;
- `0x003202E8` as the runtime class-3291 constructor;
- `0x0031FC60` as the runtime class-3291 update;
- `0x0026F740` as the runtime resource service.

The loaded addresses documented above replace those stale runtime claims.

## Exact-authority provenance

All runs below verified the full retail ISO SHA-256 in the same job:

- pipeline `2827600082`, job `16354081451` — Oozla crate-family full loaded update body;
- pipeline `2827602355`, job `16354095087` — transition/emission/3291 loaded helper slices;
- pipeline `2827606692`, job `16354123617` — complete `0x003918D0` callback + class-3291 constructor bridge;
- pipeline `2827607142`, job `16354126201` — all-level census including dormant class 502;
- pipeline `2827609920`, job `16354148745` — Maktar/Endako family vtable confirmation and `+0xCC` read;
- pipeline `2827610817`, job `16354155119` — indexed resource add/clamp service and resource selector;
- pipeline `2827647584`, job `16354385800` — post-main-sync bounded Oozla count/selector/class-3291 disassembly, including the 56-slot structure and `-1` correction.

On 2026-09-08, the static-to-live Moby bridge was re-derived locally on Jess-Laptop from the same exact authority build using the bounded level-overlay tooling. The retail bytes remain local; only instruction addresses, field relationships and derived facts are recorded here.

## Local deterministic interaction proof

The Godot development harness can focus the first preserved class-500 object and feed it the known-good debug event (`flags=0x00000001`, scalar `1.0`). Target acquisition is explicitly a host/debug convenience; the break decision itself goes through `GcCrateInteraction.ShouldBreakClass500`.

On Jess-Laptop, the original paired LEVEL1 captures at frame 120 target `level:1:moby:31` / UID `73`:

- `tools/capture.ps1 -CrateFocus ...` records `Visible=true`, `Broken=false`;
- `tools/capture.ps1 -CrateFocus -CrateAutoStrike ...` records `PvarC8=0`, `Route=Deactivate`, `Visible=false`, `Broken=true`.

A later deterministic fresh-showcase capture extends that path through reward planning and collection. For UID `73`, authored `bolts=13`, explicit selector `0`, reward-multiplier byte `0` (native `max(1, byte)` therefore gives effective multiplier `1`), progression-like input `0`, and native-valid RNG-domain choice `1`, RAC2 gameplay uses the recovered reward centre `13` as the deterministic showcase value and partitions it as `5+5+1 physical, 2 deferred`. The Godot host renders those three physical denominations as deliberately non-native gold placeholder orbs; the scripted debug player walks through their `Area3D` triggers and the RAC2 session records `11` collected, `0` outstanding, `2` deferred. The capture harness now evaluates metadata after frame settling, so its gameplay state matches the photographed frame.

This proves a complete development loop from preserved retail crate identity through the recovered class-500 break predicate, authored reward value, percentage/denomination/deferred rules, physical host pickups and collection accounting. The gold-orb appearance, generous trigger radius, neutral reward-multiplier byte, deterministic progression/RNG inputs, reward-centre choice and selector-zero initialization are **OBP showcase choices**. Native Bolt pickup model/scatter/magnet presentation, exact PS2 RNG sequence, arbitrary-save selector context/history, and real game wallet/save integration remain unreconstructed.

## What is proved now

### Ordinary Oozla contact and pickup slice

The ordinary actor registry now admits Oozla's 190 class-500 crates. The existing
weapon/wrench contact service forwards a source-qualified hit to the RAC2 break
predicate, retaining the source damage amount. The cross-source damage envelope
is an explicit OBP adapter; foreign weapon class numbers and flags are not
reinterpreted as native GC attacks. Repeat contacts with an inactive crate cannot
emit another payout. Godot convex contacts are generated from each retail crate
mesh and disabled on break; this is host physics, not the PS2 narrow phase.

Bomb Glove area damage already reaches these registered actors through the
shared blast adapter. Original R&C2 crate bodies were missing from the weapon
visibility-ray exclusions, however: those bodies could shield their own bounds
or adjacent crates before the damage adapter ran. `ActorWorldHost` now supplies
an occlusion-specific list covering original-world actor bodies as well as
factory-created bodies. Aim/blast visibility uses it, while crate-support and
pickup physics retain their existing exclusions so stacked crates remain solid.
Terrain and other non-actor walls still occlude damage. This is an OBP contact
adapter correction; the R&C1 Bomb Glove's native damage/radius are unchanged.

A temporary external observer drove ordinary equipped-use input in Oozla with
the selected R&C1 player and fixed seed 1440. Before the correction, two throws
left crate 31 alive and broke three neighbours. The same input after the change
broke all seven nearby crates 31..37, each once. Adding a solid wall between
the player and cluster left all 190 level crates alive. No observer is committed.

`Rac2BoltAssets` reads the active level's original Moby classes 13..19 and their
texture remaps. Oozla's triangle counts are respectively 52, 48, 96, 192, 162, 174
and 204. The constructor at loaded `0x00378270` selects those classes by
denomination and loads seven scale factors of `0.5` at `0x001AEBB0..0x001AEBC8`.
Its `lwc1` witnesses are `0x00378378/38C/3A0/3B4/3C8/3DC/3E8`; Oozla's `$gp`
is `0x001AEFF0`. The decoder already applies the model's own class scale.
Local bounded reads verified geometry, texture availability and those original
instruction/data values without retaining asset payloads. The recovery followed
each original denomination comparison into its allocator call and checked
Oozla texture IDs 0/23/24/25 using decoded-payload SHA-256 fingerprints.

The gold placeholder spheres and fixed scatter ring are replaced with these
models and a bounded source-owned motion session. Loaded pickup update
`0x00376D58`, constructor `0x00378270`, and emission `0x003177C8` provide the
per-tick gravity `0.003`, planar launch components `0..3 / 60`, upward launch
`3.7..6 / 60`, contact radius `0.25`, rebound intervals `30..40` and `10..20`,
and attraction acceleration ramp `0.006666667` up to `0.53333336` per tick.
The host supplies floor contacts, the selected player's attraction dimensions,
and an explicit feet-to-socket offset. Collection, rather than proximity alone,
credits each denomination through the existing player-session receipt port.
The host ray-to-centre clearance uses the contacted unit normal, including on
slopes. The catch-up limit is 12 native ticks per host frame, and ordinary launch
randomness uses a runtime seed unless a diagnostic/replay fixed seed is supplied.
The Godot owner runs in the Physics phase and uses the existing actor-body ray
exclusions. Wallet-capacity overflow retains the exact receipt with hidden
presentation and retries only when the wallet balance changes. There is no
invented pickup expiry: this lease admits only 190 non-respawning crates, at most
three pieces each, and teardown retires their nodes and receipts.
Falling and first-hop states latch PVar `+0x42` when the player enters the pickup
volume; leaving the volume does not clear it. Oozla reads it at `0x37703C` and
`0x37737C`, then writes one at `0x377080/84` and `0x3773C0/C4`. The retail witness
checks both writes and their count, and the motion regression enters range,
leaves before landing and still attracts. The first hop also retains its final
position sample at the state-3 transition; it does not borrow R&C1's anchor reset.

The local recovery trial exercised the ordinary actor/query route, absence of
marker cubes, collision removal, duplicate-hit isolation, original pickup models
and wallet collection with the selected R&C1 player and attached R&C2 source.
It inspected spawned 14/14/13 classes, 48/48/52-triangle ArrayMeshes and actual
material textures, then observed the 5/5/1 payout and wallet credit. The temporary
automated fixture is removed in keeping with the project's gameplay-first policy.

### Falling stacks, original fragments and sounds

Loaded crate physics `0x003927E8` traverses supports bottom-up with a per-tick
visit guard. The unsupported recurrence at `0x003929B8` subtracts float bits
`0x3B326750` (9.8 / 3600), clamps velocity to -0.3, then advances height.
`0x00392C08..18` uses the lower crate's root height plus exactly 1.0. Landing
clears velocity. Break helper `0x003922B0` unlinks supports and marks the remaining
stack to update. `Rac2CrateFallSession` owns the recurrence; the host supplies
stationary support contacts and moves both neutral presentation state and an
AnimatableBody3D derived from the original mesh. Native moving-parent motion,
chain identity and the exact PS2 contact solver remain outside this port.

The break helper decays `gp+0x3AC` by elapsed ticks >> 2, or resets it after
240 ticks. Available fragment slots and recent pressure choose budget 4/3/2/1/0
at free-slot thresholds >250/>200/>150/>100 and pressure thresholds <5/<10/<15/<20.
Class 500 emits original mask 0x25/0x05/0x01/0 for budget >=3/2/1/0,
respectively; pressure increases by the chosen budget, including 4 for mask
0x25. This also feeds the reward-piece budget. OBP explicitly uses a host pool
of 256 fragment slots, not the retail allocator's history.

Class header +0x2C locates the detachable-part packet directory (units of 16),
and +0x2E locates the corncob pivot table. Parts 0/2/5 decode to 32/20/20
triangles and use Oozla texture 40. Original pivots are retained. This layout is
independently corroborated by pinned Wrench `src/engine/moby_low.h` and
`moby_packet.h`; implementation follows bounded retail reads, without copying
Wrench code. Generic emitter `0x0031BF10` supplies planar/upward launch 3..6 / 60,
spin 90..270 degrees per second and gravity 10..20 / 3600. Constructor
`0x002D1400` supplies speed-loss fraction 0.45, minimum loss 0.033333335 and
lifetime 180. Lifetime advances every fourth tick while the support count is
zero, then every tick after a support is anchored. The contact-state recovery
below replaces the initial centre-ray/free-spin approximation. Exact native
collision predicates and angular/RNG ordering remain outside the port.

#### Contact points and settling

The pinned [OpenRAC generic-motion reference](https://github.com/OpenRAC/rac2-decomp/blob/40e6fafd5c9edbab103513fa8046d24275ec3ca5/src/game/00273B70-rc2.c)
dispatches generic motion using PVar +0x5D and gates
the lifetime update on +0x5C. The init body in `00273978.c` corroborates the
field layout (its introductory camera comment does not identify this actual
fragment caller). Loaded Oozla bytes establish the concrete call chain:

- `0x390CF8` calls generic update `0x2D15F8` for the emitted mode-2 parts.
- `0x2D17C4..0x2D17EC` chooses free flight for +0x5C == 0 and supported motion
  for counts 1/2; count >= 3 returns finished before advancing the pose.
- `0x2D21F0..0x2D2228` appends a supporting vertex index. `0x2D2298` prunes
  the support set with contact-plane predicates; arbitrary three impacts do
  not constitute a stable support.
- `0x2D1D70` rotates about a supporting point, or the midpoint of a supporting
  edge. Gravity supplies the lever/up cross-product correction via +0x4C
  (`0.0017453295` radians); `0x2D25E0` constrains rotation to the edge.
  Angular correction at `0x2D2510` bounds its increment and resulting step.
- `0x2D2630` reflects velocity. For floor-like normals with up dot > 0.7,
  it moves speed toward zero by max(speed * 0.45, 0.033333335), clamped at zero.
  The old multiply-by-0.45 response did not reproduce this minimum loss.
- Finished generic motion starts the fragment's fade state at `0x390D44`.
  The fade state still calls the generic update, then subtracts two from
  native alpha at `0x390D68..0x390D88`. Settled parts retain their pose.

Helper `0x3068B8` reads each part's corncob sphere followed by packed s16 XYZ
contact points, scaled by runtime model scale / 1024. The fourth short of the
first packed point is the count. Oozla parts 0/2/5 have **16/12/12 original
contact points**. The importer retains them relative to the same part centre
used by rendering, rather than colliding a single point at the centre.

OBP now distinguishes free flight, point support, edge support and settled
motion. Supported motion pivots about its anchors instead of applying residual
linear translation; three stable supports freeze translation and orientation
before fading. Godot supplies swept corner contacts. Its deepest-hit choice,
2mm ray skin and projected-centre edge/triangle support adapter remain explicit
host choices, rather than claims to reproduce the PS2 narrow phase. Moving
supports, nonstandard gravity and exact native quaternion/plane arithmetic
remain unrecovered.

A local inspection dropped all three original hulls onto a supplied flat plane
using 16 fixed seeds per hull. All 48 trajectories acquired stable three-point
support and retired; sampled settled velocities were exactly zero. This is a
bounded contact-adapter exercise, not a PS2 frame-equivalence claim.
An external, temporary SceneTree observer also recorded the actual Godot Oozla
crate-31 break with fixed seed 1440: the three pieces became stationary by
frames 67/91/113 of the observation and retired at 127/151/173. Each retained
exactly the same position and orientation through its final 64 observed frames.
No observer or automated fixture is added to production.

The intact class-500 box also uses texture 40. Its imported surfaces multiply
that texture by computed per-instance ambient/directional/point-light colours.
The initial fragment path omitted those colours, making debris render at full
brightness. Fragments now use the same source-owned lighting helper and original
instance/light records as the intact box, with their own original vertex normals.
Godot retains those colours in the fragment mesh and enables vertex-colour
modulation. This retains the existing OBP baked-lighting approximation; spinning
fragments are not dynamically relit by a recovered native VU lighting program.

The crate call at `0x0039235C/60` selects class-local SoundDef 0 through helper
`0x00355780`, throttled to once per two ticks. Bolt arrival also selects its own
class SoundDef 0 (flat playback flag 32). The level +0x70 remap directory has an
eight-byte prefix and a leading absent direct slot before local SoundDef 0.
Oozla class 500 maps to SBlk sound 69; Bolt classes 13..17 map to sound 53.
SoundDef initial pitch bend is 0 / -2540 and maximum gain 1 / 921/2048, respectively. Shared
989snd rendering executes their tone/random-choice/pitch-bend programs and ADSR
at 48 kHz. Twelve seeded variants cache the randomized arrival program.
Zero-SoundDef classes remain silent, matching the native guard. This replaces
the upgrade cue on the ordinary crate/pickup path; hostile audio is unchanged.
Oozla classes 18/19 (500/1000 Bolts) have zero SoundDefs and intentionally remain
silent; no class-13 cue is substituted.

The SoundDef value is a signed pitch-bend controller, not a pitch modifier in
1/128-semitone units. Oozla `0x35574C` stores it at slot +0x84;
`0x35520C` reads it, and `0x355378` forwards it in t1 (PB), separately from
t0 (PM) at `0x355370`. PB normalizes by 32768 below zero / 32767 above zero and
uses each tone's bend limits. Grain 27 replaces the initial controller with its
random bend. Passing -2540 as PM had lowered pickup playback by about 19.84
semitones. The corrected seed-0 bank-53 render is 33,925 frames / 0.7068 seconds,
versus the incorrect 75,829 frames / 1.5798 seconds; bank 69 is unchanged.

Crate setup admits each family separately at the lease boundary. Nanotech loads
only its sphere donor; ammo loads its class-3291 model and Lancer descriptor.
Fragment assets and sounds are restricted to the family and its pickup classes.
If a family's original models, textures, audio programs or body setup are
unavailable, the host logs an explicit family-admission error, retires its partial
supply presentation and removes its new crate bodies and registrations. Other
crate families and world gameplay may still activate; unsupported crates are not
advertised as working actors. This isolation is OBP host policy, not a recovered
retail failure path.

The crate readers and both single-class and bulk `Rac2MobyClasses` paths validate
count, offset and the complete 64-bit table extent before scanning native rows.
Physical Bolt collection offers the outstanding denomination to player reward
intake before committing source-session accounting.
The host provisionally marks collection for reward admission and restores that
flag on rejection or exception, leaving the pickup outstanding for a later retry.
The audio variant generator is deliberately seeded and non-cryptographic: its
only role is cosmetic pitch variation, with no security decision involved.

The review follow-up's local manual probes rejected oversized, negative and
truncated class tables before enumeration. Unregistered, stale-lease and throwing
wallet credits retained both outstanding pickup accounting and the provisional
actor flag; retry credited one denomination once, and duplicate collection was
rejected. Family-scoped Oozla audio retained identical PCM for all 11 cues. A
damaged copy of the class-3291 asset offset rejected ammo decoding while leaving
Nanotech's two cues available. The in-engine Oozla run admitted all 229 crate
contacts, collected eight physical Bolts for 32 units, broke a Nanotech crate and
healed one unit. A separate ammo run retained three drops at planar distance 4.35,
then collected exactly one after moving inside to 4.15. No automated fixture or
retail payload is added to the repository.

Local authorised-source inspection verified original parts, sound programs and
loaded instruction witnesses. The recovery's engine trial broke a real stack
support and observed a one-unit fall, settling, updated state/combat geometry,
original textured parts, movement/spin/retirement and audio-host acceptance of
both mapped cues.

This remains a bounded improvement. Fresh-session selector/centre reward choices
are explicit OBP initial conditions. Native RNG order, difficulty-dependent
immediate attraction, camera-side steering, moving supports, sparkle effects,
full native debris collision/mixing and campaign reload persistence remain
unrecovered.

**CONFIRMED:** authored crate-family PVar structure; class-500 population; per-level shared crate-family update vtables; seven-state loaded state machine; class-500 damage-flag mask/exclusion and positive-`damageHp` break predicate; class-500 break transition to state 3 and Oozla `+0xC8==0` deactivation path; runtime consumption of authored `+0xC8/+0xCC`; runtime `+0xC6/+0xCA/+0xCB` use; live `Moby+0xAA = oClass`; static Moby `+0x10 -> live +0xB2` UID mapping; static Moby `+0x14 -> live +0xB4` Bolt-reward mapping; class-500's separate `0x0030F920 -> 0x003177C8` reward path; percentage-scaling helper `0x002D9528`; four overlay-backed reward percentage banks at `0x001A89C8..0x001A89E7`; packed selector addressing at `0x0019B4A8 + contextIndex*0x400 + floor(uid/2)` and UID-parity nibble selection; zero-filled selector bytes in the retail executable image; deferred reservoir add/release at `0x001A7A14`; modulo RNG at `0x003104E8`; recent-break-pressure effect on the class-500 physical-piece budget; denomination decomposition `1000/500/100/50/20/5/1`; denomination-to-selector mapping `19..13`; dynamic class-3291 creation on the separate indexed-resource branch; class-3291 amount/index PVar fields; 56-slot indexed count structure; add/subtract/capacity mechanics; class-3291 successful-collection delta check; `-1` runtime-index substitution; class-502 authored absence.

**CORROBORATED:** `oClass 500 = Bolt Crate`; public `bolts` naming for the now retail-proved `+0x14` reward field; public TargetVars structural lead; public `Ammo[56]` matches the runtime selector's 56-slot domain.

**INFERRED:** `0x003922B0` is a break/destruction transition helper; selectors `13..19` are Bolt-denomination pickup identities; `0x001A7A32/+0x33` are the Challenge Mode bolt multiplier/sublevel; class 3291 is a generic indexed collectible/resource object; the 56-slot counter system is ammo/inventory-resource state.

Still **UNKNOWN:** broader crate-family state names and `+0xCC/+0xC6/+0xCB` semantics outside the proved paths; broader meaning of `+0xC8` outside the proved ordinary deactivation branch; exact runtime population/history of the packed reward-selector blocks and semantic identity of the active `contextIndex`; exact 56-slot resource names; full native fragment collision/angular behavior and sparkle effects; save/reload/persistence/respawn rules; complete class-512 alternate Nanotech behaviour. Class 505 and its `+0xCA` expanding-blast counter are resolved above.
