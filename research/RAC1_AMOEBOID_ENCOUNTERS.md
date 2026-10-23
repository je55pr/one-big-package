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
PVar **+0x2bc** and invokes native **0x00289c68**, a
3D clip-volume containment test. The source instructions for both
reactivation routes are covered by retail tests.

All **44** state-15 class-623 targets point at source **class-830**
controller Mobys. There are **25** class-830 placements on Rilgar.
The class-830 updater at **0x0030c5b0** has now been traced
through its state-0 and state-1 completion branches. In state 0,
the native world-cell byte at **0x001cb6b0 + 0x454** (read at
0x0030c620) or native bit-table mask from **0x0015c190** (read at
0x0030c64c–0x0030c658) immediately sets native state **2** and
Moby **+0xbc = 2** at **0x0030c690–0x0030c6a8**. When neither
gate fires, the PVar **+0x08** branch selector decides whether
to check a third native table at **0x0015c050** for sentinel
**0xff** (another immediate completion) or enter state **1**
at **0x0030c72c–0x0030c734**. All **25 authored controllers**
have **0x10-byte PVars**, with 23 zero selectors and **i1094,
i1095** using selector 1.

Native state 1 waits for the live owner pointer at
**0x0014f350 + 0x2fc** to equal this exact Moby and its
halfword control slot **+0x30e** to be zero, as checked at
**0x0030c7d4–0x0030c7e4**. It then enters native state **2**
at **0x0030c868–0x0030c880**, writing **+0xbc = 1**.
Class-623 state 15 accepts **either nonzero byte**, not a
specific value. A new engine-neutral
`Rac1Class830ControllerSession` reproduces these **source
state/byte transitions** from *explicitly supplied native
table/owner observations*, remaining unchanged when inputs are
unknown; source instruction words and authored PVars are tested.
Its snapshots are wired to the class-623 activation consumer,
but Godot does **not** currently supply the native world-cell,
bit-table or owner-slot state. The 25 controllers therefore
remain uncompleted in ordinary host play until those source
facts are imported; this is deliberate and must not be
replaced with a visibility heuristic.
This proves that the link is a controller-completion signal, **not**
mere visibility or generic Moby activity. Nine waiting class-623s
also specify alternative authored **volume indices 51, 52 or 58**.
The native alternate predicate `0x00289c68` indexes the **0x80-byte
3D clip-volume table** and checks a transformed point against cube
bounds. The caller passes source pointer **0x0013f3d0**;
the Godot host supplies Ratchet's position as an explicit adapter
rather than claiming a verified byte-for-byte pointer equivalence.

`Rac1AmoeboidClass623Admission` decodes both PVar links and
reproduces the state-15 transition to state **1** and flag **0x1000**
only when the **real controller completion byte** is nonzero or the
separate authorised volume predicate succeeds. The per-world
`Rac1Class623ActivationSession` now owns all class-623 state snapshots,
resolves source controller IDs, and accepts an externally supplied
native completion byte only from **class 830 at that exact linked
instance index**. Unknown bytes, unrelated class-830 controllers,
incorrect classes and zero bytes never activate waiting targets.
Reactivations are emitted once, and world reload restores the authored
state-1/state-15 census. These conditions and a 56-target/25-controller
retail replay are tested. This is an **admission consumer**, not
an implementation of the class-830 signal *producer* or class-623
damage receiver; it must not be fed a Godot visibility heuristic.

### Native class-623 receiver boundary

A further Rilgar trace identifies the native hit/reaction helper at
**0x00303390**. Its first call, **0x002846d8**, is specifically
a **native interaction-marker prefilter**, not a damage-result query.
It examines the source Moby's **+0xa4** marker ID, with **0xff**
meaning empty, indexes a **0x40-byte-stride** registry at **0x00178500**,
compares the registry's owner pointer **+0x34** against that exact
Moby, and reads registry flags **+0x24**. The caller's
`lui a1,0x33` passes the full **0x00330000 mask**, **not**
the integer 0x33; the third call argument is zero. Only a
nonempty marker with the matching owner and
`(registryFlags & 0x00330000) == 0` is cleared to **0xff**.
Unknown registry identity or flags are not grounds for removal.
The engine-neutral `Rac1Class623ImpactMarkerFilter` now
models this exact marker-invalidating predicate, tested against
native call-site and helper instruction words. This does not
run the native registry in Godot or admit any hit.

The separate **0x00284730** contact/impact service supplies the
subsequent query status and scalar outputs. The class-623 caller passes
the actor's **PVar+0x20** pointer as the service's third argument,
an output-status address on its stack **+0x20**, an output-scalar
address at stack **+0x24**, and native query mode **4**
(**0x003034d4–0x003034ec**). Status value **1** skips the
health/reaction logic at **0x003034f4–0x003034fc**, as does an actor
already in native state **14** (**0x00303504–0x0030350c**).
Otherwise it loads source float **PVar+0x20** and the service's
observed float scalar, executes `sub.s health,health,scalar`
(**0x00303510–0x0030351c**), writes the updated float back at
**0x0030352c**, and compares the result against zero using the
native ordered `c.le.s` at **0x00303520**. The nonpositive
branch selects response index **1** at **0x00303530**, whose
native jump table points to the terminal response helper. The
other response indices still depend on the generic service's
returned classification and have not been fully reconstructed.

The source general impact service at **0x00284730** has a
distinct, earlier **provisional mode classifier** at
**0x002849cc–0x00284afc**. Its eight-entry jump table at
**0x00217650** branches on a native impact-context byte **+0x28**,
with submode byte **+0x29** and (for mode zero) source kind
halfword **+0x2a**. The source table has modes **0–7**;
modes outside that range take status **2**, response **1**.
The recovered table proposals are:

| Mode | Source submode/kind | Proposed status | Proposed response |
| --- | --- | --- | --- |
| 0 | kind 1 or 2 / other | 2 | 0 / 1 |
| 1 | submode 0–1 / 2 / 3+ | 2 | 11 / 5 / 9 |
| 2 | submode 0–1 / 2+ | 2 | 8 / 13 |
| 3 | submode 0–1 / 2 / 3+ | 2 | 10 / 15 / 16 |
| 4 | any | 2 | 14 |
| 5 | submode 0–1 / 2+ | 3 / 4 | 4 / 7 |
| 6 | any | 2 | 1 |
| 7 | submode 0–1 / 2+ | 2 | 12 / 17 |

The mode-3 high-submode branch also clears the source collision
slot's halfwords **+0x14** and **+0x1c**, as shown at
**0x00284a94** and **0x00284a9c**. Crucially, these are
**provisional** responses: the native routine subsequently invokes
**0x0022dc50** at **0x00284b30**, dispatches a separate
reaction/timer table at **0x00217670**, and evaluates control flags
through **0x00284c74** before returning its final response index.
The new bounded engine-neutral
`Rac1Class623ImpactModeClassifier` reproduces just this table
from explicit native context inputs and does **not** feed its
proposal into `ApplyObservedNativeImpact` as an authentic final
result. Original-ISO instruction tests assert the table, mode and
submode access, tracking reset writes, and later callback boundary.

The later general-impact callback is also now bounded by the
original Rilgar code. Before invoking it, **0x00284b24–0x00284b2c**
compares the source collision slot's previous halfword **+0x14**
with the new linked identifier and clears countdown halfword
**+0x1c** when they differ. The callback at **0x0022dc50**
reads that signed halfword, returns **1** if it was already zero,
otherwise clamps it to at least **1** using the native `PMAXW`
instruction **0x702208c8**, decrements it and stores the result.
It returns **0** if the countdown remains positive or **2**
if it has just expired. Source instructions and a bounded
`Rac1Class623ImpactTimerPolicy.Tick` reproduce this transition;
it does not mean the collision itself has been admitted.

The subsequent branch at **0x00284b38** selects an
original twelve-entry response-index duration table
**0x00217670** for a *nonzero countdown callback result*.
The timer-duration arguments provided to native allocator
**0x0022dbd8** are **37** for response index 0, **15** for
index 1, **60** for indices 4/11, **30** for other indices
or indices outside the twelve-entry table, and **45 or 60**
for index 7 depending on live global byte **0x0013e533**
(nonzero / zero respectively). Native allocator output is
stored into the collision slot **+0x1c**. The native allocator
at **0x0022dbd8–0x0022dbfc** is now decoded further:
`MTC1` passes the selected integer duration to float register f1;
`CVT.S.W` converts it to single precision; source float f2 is loaded
from **`gp - 0x7e98`** (a live original global scale, **not**
an immediate or a generic Godot frame delta); `ADDA.S` places
**0.25f + 0.25f = 0.5f** in the native FPU accumulator;
`MADD.S` combines **duration × source scale + accumulator**;
`CVT.W.S` converts the result to a 32-bit integer **toward zero**
and `MFC1` returns it in v0, to be stored at collision-slot
**+0x1c**. Instruction bytes and the allocator call/store are
asserted against the authorised source overlay. The chosen global
scale's *runtime value* is not yet imported, and the PS2 EE FPU's
non-IEEE intermediate rounding/overflow behavior is not presumed.

`Rac1Class623ImpactTimerPolicy.DurationForResponseIndex`
exposes only the proven duration argument, returning unknown
for index 7 until the real global flag is supplied. The new
`TicksForDuration` reproduces the finite ordinary-range conversion
from an **explicitly supplied native global scale** with a documented
host floating-point approximation. Missing/invalid scale fails
closed; neither routine manufactures a timer in ordinary Godot play
or implies an admitted collision.

The *other* service branch, when the countdown callback returns
**0** (a still-active countdown), now has its original source
dispatch recovered. A second twelve-entry native table at
**0x002176a0** (used at **0x00284ba8–0x00284bc0**) selects
the following outcomes according to the provisional response
index and original runtime collision-flag mask held in `s8`:

| Initial response | Primary bit | Secondary bit | Outcome |
| --- | --- | --- | --- |
| 0 or 1 | 0x10 | 0x20 | Missing primary changes response to 11; missing secondary changes status to 1 |
| 4 | 0x01 | 0x02 | Same independent response/status gates |
| 7 | 0x04 | 0x08 | Same independent response/status gates |
| 11 | n/a | n/a | Response changes to 12; scalar is not forcibly cleared |
| 2,3,5,6,8,9,10 or outside 0–11 | n/a | n/a | Response changes to 13; native scalar is cleared |

In the paired-bit cases the scalar is cleared regardless of
which bits are present; secondary-bit suppression writes native
status **1** at **0x00284c04–0x00284c08**. The source uses
a branch-likely instruction at **0x00284bfc** to conditionally
skip that status write while still clearing the scalar.
At **0x00284c24–0x00284c28**, *every* active-countdown
branch resets the collision slot's halfwords **+0x16/+0x14**.
The engine-neutral
`Rac1Class623ImpactTimerPolicy.ResolveActiveCountdown`
models only these intermediate outputs from an explicitly
provided native flag mask; it does **not** manufacture the mask,
collision owner, scalar or final contact event. An original-ISO
test anchors all twelve table entries and the conditional
MIPS flag/status/slot instructions.

The original class-623 reaction dispatch table begins at
**0x002186f0**, as indexed by instructions **0x00303590-0x003035a4**.
Response **index 1** (selected when source health is nonpositive)
and **index 2** both resolve to the native terminal handler at
**0x00303660**. That path clears **0x1000** targetability and enters
state **14** at **0x00303714-0x0030371c**. Source index **0** instead
returns through **0x0030376c**, while indices 3-8 route to the
alternative reaction arm **0x003035b8**. The generic impact service's
entire classification policy and other reaction consequences remain
unrecovered. Retail instruction tests now pin the dispatch pointer
and index-1/2 table entries.

The per-level engine-neutral `Rac1Class623ActivationSession` now
retains each target's original PVar **+0x20 health** and provides
`ApplyObservedNativeImpact` as an input-only bridge from the native
service's trusted status/scalar output. For an eligible state-1
target, it accumulates non-terminal health changes; nonpositive
health follows source response index 1 to state **14**, with
targetability withdrawn. Independently, when the trusted native
service's actual **return response index** is supplied and equals
**1 or 2**, the same terminal handler is selected even if the
health subtraction remains positive. The service's return-index
output is **separate** from its status and scalar outputs; a
missing classification is not guessed. Indices 3-8 have other
reaction side effects which are still unhosted, so their source
health accounting does not fabricate a state/animation transition.
Repeated terminal hits and state-15 hits
cannot apply. A retail-backed test replays **56 source placements**
with explicitly injected native-shaped results, then verifies
the restored 12/44 eligible/waiting split and 1.0 health on reload.
Restricting dispatch to state-1, rejecting unknown/nonfinite service
inputs and refusing any synthetic contact are *host safety policies*,
not claims of a fully reconstructed native receiver.

**Godot still does not provide actual 0x00284730 contact results**,
so no live class-623 health, terminal state or damage is fabricated.
The existing `ObserveNativeTerminal` external-notice method is
also preserved for independently witnessed source outcomes.

All **56** original Rilgar class-623 PVars have 0x2e0 bytes
and **initial float health 1.0** at +0x20
(IEEE-754 `0x3f800000`), independently verified on the authorised
retail source. New engine-neutral `Rac1Class623ImpactAccounting`
models **only** the caller's status-1 / terminal-state skips and
floating-point subtraction using externally supplied **authentic**
service status and scalar. Unknown or nonfinite service facts
fail closed as a clearly labeled host-safety policy. A retail
instruction test asserts the source service call, stack-output
contract, skip gates and floating-point arithmetic; a 56-placement
test checks the actual authored health values. Godot still cannot
supply these native service outputs, so no hit is generated or
damage applied from host presentation or inferred proximity.

The non-terminal reaction path writes
native state **11** at **0x0030364c–0x00303654**. The terminal path
clears the **0x1000 targetability flag** from native Moby **+0x34**
at **0x0030369c–0x003036a0** and writes native state **14**
at **0x00303714–0x0030371c**. These instruction words are now
asserted against the original Rilgar overlay.

The target activation session accepts **only an externally observed
native state-14/cleared-flag pair** as a terminal notice. It withdraws
the target once, disallows subsequent class-830 or volume reactivation,
and resets on world reload. This is a bounded receiver *outcome*
adapter, not an implementation of the **0x2846d8/0x284730 hit filters,
health/impact calculation, damage ownership or reaction physics**.
No Godot code manufactures a terminal notice or dispatches class-623
damage while those gates remain unrecovered.

The Godot adapter now hosts the **nine volume-gated** candidates
through `Rac1Class623ActivationSession` using source
`Rac1ClipVolumes.Contains` on the player's native position (Godot
clip containment is a bounded geometric stand-in for the PS2
floating-point test). It leaves the new controller-snapshot callback
**unknown** until class-830's actual live Moby+0xbc byte is hosted. The native class-830 update/byte
owner is not hosted, so **controller-triggered** activation, the
class-623 damage receiver and full subsequent state/flag lifecycle
remain unimplemented. Existing initially eligible group-101 targets
remain unaffected.

On the retail Rilgar disc there are **56 class-623** placed Mobys.
Exactly **52 Amoeboid** instances reference candidate groups
**2, 74, 77 and 101**, all containing matching class-623 placements.
There are none of these referenced candidates on Novalis or Pokitaru.
The engine-neutral `Rac1AmoeboidTargetSelection` validates this
retail census and applies nearest-valid-target admission with
group/range/liveness and stable list-order tie resolution.

Godot requires **rendered, active** class-623 presence and a native
state **1** with flag **0x1000** before admitting an Amoeboid target.
It keeps the state-15 population registered but ineligible until a
source-backed activation predicate succeeds; volume-gated candidates
can now activate, while unresolved class-830 runtime links cannot.
The engine-neutral lunge-envelope factory retains the **class-623
victim ID** separately from the Amoeboid attacker/owner, with the
recovered 0x10001/1-point contact descriptor. A conservative **Godot-only per-lunge target latch** rejects
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
  The **initial state/flag admission**, nine authored volume-trigger
  activation cases and class-830 completion-byte link are recovered
  from the retail executable and all 56 PVars. The host activates
  state-15 candidates only through the volume route; native class-830
  controller runtime signals, subsequent flag changes and the
  class-623 damage receiver remain unported, so these targets do not
  yet take damage.
- The host tests a valid player contact at source lunge frame >=15.5.
  An unobstructed 3D line-of-sight ray is **not** a contact probe: the
  original Godot-only planar-range admission could damage Ratchet when
  he stood on an exposed ledge far above an Amoeboid. The host now
  additionally requires the *actual foot-aligned enemy and Ratchet
  capsule height spans* to overlap in native Z, using the existing
  Godot contact capsules, before it reports `targetInContact` to
  the family session. This is a conservative **host safety fallback**,
  not a recovered retail sweep or an extra native combat predicate.
  Engine-neutral tests cover all three sizes (572/865/866), a clear
  sightline separated by 25 height units, airborne/upper/lower ledges,
  and preserved ordinary one-hit-per-lunge behavior.
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
