# R&C1 Amoeboid encounter reconstruction

**Scope:** original NTSC-U `SCUS-97199`, native class **572 / 865 / 866**
on **Novalis (level 1), Rilgar (level 5) and Pokitaru (level 11)**.

## Recovered source facts

The same native updater `FUN_L01_002edca0` handles the large (572),
medium (865) and small (866) variants. Its damage helper
`FUN_L01_002eec68` subtracts **one** from the PVar+0x20 native health
per valid nonzero hit and calls `FUN_L01_002ef350` up to twice when
the remaining health is positive. The split helper searches the
parent's original **Moby.group** native list and selects only children
whose original native state is **12** and class is the next stage
(572 → 865 → 866). It **does not clone** or change the class of a Moby.
Each activated child inherits the parent's current location, rotation,
health budget and encounter scaling; it starts in native state **8**,
using the source trooper knockback helper. The original parent is removed.

The original 0x78-byte authored instance record stores the Moby group
index at **+0x48**, with `-1` meaning the native `0xff` none-group
sentinel. Original Novalis split helper **0x002ef350** reads native
`Moby.group` at **+0x21**, indexes the group-head table at
**0x001abcc0**, and walks unsigned **16-bit entries in stored order**.
Each entry's lower 15 bits select the Moby index (multiplied by the
native **0x100-byte Moby stride**), and the top bit marks the **last
entry**. An engine-neutral decoder and original-executable instruction
test preserve this exact packed representation, including nonsequential
member order and a terminal entry that is itself a member.

The retail **loader's group-list construction order is not yet proven**:
the Godot session still scans dormant members in **ascending instance
index** as a documented fallback, not a claim of recovered runtime
iteration order. No authored reserve is reused or sourced from another
group. The split helper **also copies
PVar +0x224** (the parent's current detection radius) into the activated
child, not just HP and world transform. A regression test specifically
checks inherited radius when authored parent/child values differ.

### Recovered native route selection

The source `FUN_L01_00276fe8` follows a visibility-first rule: it
tries a direct segment through the authored obstacle-region ID
(PVar +0x240), probing two sideways clearance rays for nonzero radius.
If obstructed, it searches candidate vertices from the original
path (PVar +0x244), builds visible-source and visible-destination
masks, and expands the destination mask across the path's original
per-vertex connection words, up to 32 iterations
(`FUN_L01_00276c40`). It chooses the connected vertex with minimum
distance to the destination, **or returns failure** if none is
reachable. The engine-neutral `Rac1AmoeboidPathRouter` now implements
these mask/waypoint choices, without inventing a route when blocked.
Godot supplies the segment-obstruction predicate by its world ray
query; the original PS2 `FUN_L00_00261968` region collision service
has not been ported bit-for-bit.

The original three planets' authored Amoeboid path indices reference
only 2–4-vertex source paths. The retail-backed test verifies all
**227** path indices and unobstructed selections; unit cases check
clearance probes, neighbor-mask expansion, blocked routes and first
reachable waypoint. `follow_path` now owns the path-following and
return navigation decisions when a route is available, while the
separate direct-chase state retains its native direct steering.

Initialization sets native health to **3** except the +0x258 force-active
branch, which sets **1**; the actor enters native state 1 if +0x230
is nonzero or +0x258 forces activation. Otherwise it enters 12
(dormant reserve, PVar+0x25c=-1) or 13 (awaiting original 3D
clip volume). State-13 entry raises the hidden actor from the source
Z-20 offscreen position on first admission. Godot instead hides the
actor at its **unchanged authored Z**, then reveals it on admission,
so it must **not** apply an extra +20 height offset (fixed and tested
after the initial PR). No hidden actor is solid, animated or ticked
in Godot while it remains waiting.

State map in the native updater: **1** acquire path, **2** approach the
stored home, **3** await entry animation, **4** follow path to target,
**5** return home, **6** lunge and source hit at animation frame **15.5**,
**7** attack exit, **8** knockback, **9** recovery animation, **12**
reserved, **13** activation-volume gate, **15** direct chase,
**16** blocked. The host reproduces the source one-hit-per-lunge
admission using animation frame >=15.5 with the original owner-backed
contact descriptor (`0x10001`, **1** damage). Source group allocation,
health decrement, and active vs reserve lifetime live entirely in
`Rac1AmoeboidFamilySession`, never in Godot.

### Original Rilgar target candidate groups

Native `FUN_L01_002ef478` scans PVar **+0x248**'s indexed
group list, selecting the nearest live **class-623 / 0x26f** Moby
within **120 native units**. Native state must be below 0x7f and
source flags include 0x1000. The updater only swaps from its current
target if the candidate is inside the current detection range and is
closer than the current target. This is a distinct relationship from
Amoeboid split groups at authored record +0x48.

The Rilgar class-623 retail updater is at **0x00301f48**. Its
state dispatch reads the native state byte at Moby **+0x20** via the
jump table at **0x002186b0**. State 0 first sets Moby.flags
**+0x34 |= 0x1000** at **0x00302080–0x00302090**. The PVar
**+0x2b4** nonzero branch (0x0030215c–0x00302194) enters native state
**12** and clears flag 0x1000; otherwise, if the full eight-byte
PVar **+0x2b8** field differs from **-1**, the branch at
0x0030219c–0x003021c0 enters state **15** and clears 0x1000.
Only the remaining -1 branch enters state **1**, retaining 0x1000.
A retail-ISO test asserts each branch's instruction words.

All 56 Rilgar class-623 PVars are **0x2e0 bytes**, with +0x2b4
equal to zero. **44** have a non--1 eight-byte gate and begin in
state 15, **12** begin in state 1 and retain the targetable flag.
Of those 12, **eight are original instances 1009–1016 in group 101**;
the other four are ungrouped instances 1002–1005. The initially
inactive target groups **2, 74 and 77** contribute no valid state-1
candidates. Source class-623 i984 (group 101's neighbor in the earlier
staged encounter) is itself state 15, so its earlier proximity-only
staged selection was not evidence of true retail *initial* admission.

These are **startup** state/flag semantics. The native state-15
arm starts at **0x0030282c** (jump-table entry +0x3c at
0x002186ec). It reads the linked Moby index at PVar **+0x2b8**
and checks that Moby's runtime **+0xbc** byte; a nonzero value
branches to the state-1 reactivation at **0x0030287c–0x00302888**,
which restores flags **0x1000**. An alternate gate reads
PVar **+0x2bc** and invokes native **0x00289c68**; its
full predicate still needs recovery. The instruction words for
this first reactivation route are covered by the retail test.

Godot has no source-backed runtime ownership of the linked Moby's
+0xbc byte or the alternate predicate yet, so this native reactivation
is **documented but not hosted**. The class-623 damage receiver and
full subsequent state/flag lifecycle also remain unimplemented.

On the retail Rilgar disc there are **56 class-623** placed Mobys.
Exactly **52 Amoeboid** instances reference candidate groups
**2, 74, 77 and 101**, all containing matching class-623 placements.
There are none of these referenced candidates on Novalis or Pokitaru.
The engine-neutral `Rac1AmoeboidTargetSelection` validates this
retail census and applies nearest-valid-target admission with
group/range/liveness and stable list-order tie resolution.

Godot now requires both **rendered, active** class-623 presence
and its **source-backed initial state-1 / 0x1000 flag** before
admitting an Amoeboid target. It keeps the originally state-15
population in the candidate registry but excludes them until genuine
later activation is recovered. It does not pretend those objects
remain in state 15 forever in retail. An engine-neutral source-owned
lunge-envelope factory retains the **class-623 victim ID** separately
from the Amoeboid attacker/owner, with the recovered 0x10001/1-point
contact descriptor. A conservative **Godot-only per-lunge target latch** rejects
contact if the selected victim switches mid-animation. This prevents an
attack initially aimed at a class-623 Moby from damaging Ratchet, and
the inverse switch cannot create a hit on Ratchet either. The latch
is a safety policy, **not** a recovered PS2 target-pointer rule. The
host still does not dispatch the class-623 envelope: **the native
class-623 damage receiver and its state/flag eligibility remain
unrecovered**. This remains an explicit partial integration.

## Verified population on an authorised retail ISO

| Level | 572 large | 865 medium | 866 small | Total | Initially active | Volume gated | Dormant reserves |
|---|---:|---:|---:|---:|---:|---:|---:|
| 1 Novalis | 5 | 10 | 20 | **35** | **5** | 0 | **30** |
| 5 Rilgar | 13 | 29 | 66 | **108** | **20** | **4** | **84** |
| 11 Pokitaru | 12 | 24 | 48 | **84** | **12** | 0 | **72** |
| **All three** | **30** | **63** | **134** | **227** | **37** | **4** | **186** |

The original-source regression test checks these counts and each
actor's group, state and identity at registration. The host excludes
dormant sources from collision broadphase and skinning updates; an
inactive source cannot admit damage. All 227 source nodes exist only as
the actual imported authored identity, with no cloned stand-in.

## Staged Godot encounter evidence

A disposable, ignored test harness injected valid player-wrench
damage events through **the normal Godot enemy-damage dispatcher**.
In an actual Novalis headless game update, source **large i574**
split into **medium i916 and i917**, then **medium i916** split into
**small i926 and i927**; killing **small i926** cleaned it up.
The two activated medium children's Godot body positions, mesh
positions and collision layers were asserted against the recorded
parent hit position after terminalizing the parent. No new runtime
Moby was constructed or class rewritten. The harness restored the
original source in a `finally` block; it is *not* committed.
This proves a **staged real-engine encounter** using original Mobys,
not a fully manual unassisted player fight.

Headless ordinary world loads also verified all three level counts:
Novalis `35/5/0/30`; Rilgar `108/20/4/84`; Pokitaru
`84/12/0/72`, in total/active/gated/reserve order. No
startup mass-spawn regressions or load errors were observed.
The session's deterministic tests exercise two-child exhaustion,
group isolation, multistage split, zero-child death, reloading,
damage owner/flag admission, frame-15.5 lunges and source volume gates.

## Deliberate host fallbacks and remaining work

- Native `follow_path` waypoint visibility and connectivity are now
  decoded and used with the authored `+0x240/+0x244` obstacle region
  and path. The **original PS2 collision callback**, `+0x260`
  linked-target gate, `+0x248` candidate-list admission and blocked
  state 16 still lack exact host reproduction. Godot supplies LOS rays,
  direct steering after a waypoint is selected and a 45-unit active
  simulation radius. Thus **complete native movement remains
  provisional**, even though routing decisions use the original paths.
- Group membership comes from the original source record. The native
  packed-halfword group-list **format and traversal** have been recovered;
  the loader's **actual runtime ordering** still needs proof. The
  ascending-index fallback has deterministic tests for
  interleaved/nonsequential and exhausted groups. A retail witness
  must establish the imported runtime ordering before replacing it.
- The `trooper_start_knockback(...,4,8,0)` initializer at source
  `FUN_L00_0025c558` takes the source horizontal velocity from
  **PVar +0x78**, vertical velocity from **+0x7c**, and
  `FUN_L00_0025c698` advances the trajectory with gravity
  **+0x70** and horizontal drag **+0x74**. Retail-backed testing of
  **all 227 instances** establishes the original values
  **0.2, 0.125, 0.008, 0.006944444** per native tick, respectively.
  The Godot host now reads each *activated child's actual runtime PVar*
  rather than injecting a class-wide launch guess; a deterministic
  unit test also proves that distinct PVars yield distinct velocities.
  **PS2 ground probing, bounce, material effects and reattachment**
  remain Godot collision-adapter approximations rather than claimed
  exact native physics.
- Rilgar's class-623 candidate selection uses the original class, group,
  120-unit search limit and current-target proximity comparisons.
  The **initial state/flag admission** has now been recovered from the
  retail executable and all 56 PVars. Native state-15 reactivation,
  subsequent flag changes and the actual class-623 damage receiver
  remain unported, so these targets do not yet take damage.
- The host tests a valid player contact at source lunge frame >=15.5.
  Exact `FUN_00214cc8` subframe admission, target filtering,
  audio voice lifetime, and particle/debris tuning remain bounded
  approximations. This implementation must not be described as
  bit-perfect retail AI.
- Dormant visual instances are registered once at level load but
  do not upload animation frames or run game AI until activated.
  This avoids spending frame time updating all **186** inactive
  reserves, though there is still a one-time initial mesh registration cost.

## Verification commands

Run `./tools/test.ps1 -Configuration Release`,
`dotnet format OneBigPackage.sln --verify-no-changes --no-restore`,
`./tools/test-retail.ps1` with the authorised local ISO and Godot
headless level smokes for `rac1:LEVEL1`, `rac1:LEVEL5`, `rac1:LEVEL11`.
The PR description records which gates actually passed.
