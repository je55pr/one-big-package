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
sentinel. Within a group, the current session scans dormant members in
**ascending native instance index**. That is deterministic and preserves
groups, but remains an explicitly documented assumption about the
retail group's *list ordering* until the complete imported linked-list
table has been independently decoded. The split helper **also copies
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
- Group membership comes from the original source record, but source
  runtime group-list ordering remains inferred by native instance
  index. A retail witness should verify allocation selection under
  exhausted/nonsequential lists.
- Source `trooper_start_knockback(...,4,8,0)` is mapped into the
  existing Godot collision/knockback adapter, with provisional
  gravity and drag. Retail ground/contact reattachment and movement
  state transitions are not fully equivalent to PS2 common solvers.
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
