# UYA Veldin gameplay recovery

**Target:** `rac3:TABLE1` / Veldin  
**Authority:** NTSC-U retail UYA, `SCUS-97353`

This note tracks the gameplay vertical slice separately from the already-admitted
world reconstruction. Retail/static evidence is authoritative. Numeric class
similarity to R&C1 or Going Commando is not sufficient to assign UYA behavior.

## Baseline audit

The production UYA provider already reconstructs TABLE1 through the normal
provider path. It supplies tfrags, authored TIE/shrub placements, collision,
textures, sky, level settings, Moby class geometry, all authored Moby placement
records, and referenced PVars.

Pinned TABLE1 counts are:

- 735 authored Moby instances;
- 670 authored instances with referenced PVars;
- 1,960 TIE and 1,894 shrub placements;
- 264,313 collision triangles;
- 427 Mobies with admitted presentation across static and animated paths.

Every Moby crosses the neutral runtime boundary with native class, authored
instance index, compatibility UID, transform, raw `0x88` placement record and,
when present, its PVar bytes. That is sufficient identity for gameplay runtime
work without hand-placing substitutes.
What was missing at the start of this goal was gameplay interpretation. UYA had
no live Moby store, no source-owned lifetime seam, no damage/event transport,
and no crate/enemy class controller. The Godot host therefore rendered authored
objects but did not give them UYA gameplay behavior.

The host already uses the recovered R&C1 player movement controller for ordinary
movement in all supported trilogy worlds. UYA does **not** enable R&C1 gameplay
semantics, so this temporary locomotion layer does not silently make UYA Mobies
R&C1 crates or enemies.

## Ordinary TABLE1 entry and nearby authored population

The currently admitted TABLE1 settings expose a ship transform at approximately
`(370.286, 78.898, 95.857)` in OBP Y-up coordinates, yaw `0.884552`. The
generic host currently uses that as UYA's preferred player start.

A retail census around that point finds these authored objects among the closest
placements:

| distance | instance | class | PVar bytes | approximate OBP position |
|---:|---:|---:|---:|---|
| 9.12 | 0 | 0 | 0 | (365.32, 78.68, 103.50) |
| 9.12 | 9 | 3171 | 16 | (377.80, 79.87, 100.95) |
| 11.36 | 5 | 3171 | 16 | (365.74, 79.87, 106.22) |
| 21.38 | 173 | 7032 | 144 | (367.72, 77.99, 117.06) |
| 21.69 | 497 | 6429 | 0 | (369.89, 77.99, 117.52) |
| 25.94 | 459 | 6306 | 1200 | (373.75, 77.99, 121.55) |
| 26.63 | 430 | 5821 | 1520 | (377.17, 78.54, 121.58) |
| 30.29 | 311 | 500 | 320 | (346.94, 77.97, 115.14) |

Additional class-500, 501 and 511 placements recur through the first roughly
160 world units of the spatial census. Classes 5821 and 6306 are also heavily
represented near the initial corridor.
These are population facts, **not gameplay labels**. GC archaeology separately
identifies numeric class 500 as a crate family, but UYA has not yet earned that
semantic promotion. Likewise, no nearby class is called an enemy here merely
because its model or placement appears suggestive.

The authored class-0 placement is about 9.1 units from the current ship entry.
R&C1 uses an authored class-0 player start, but that fact is not automatically
portable to UYA. TABLE1 remains on the admitted ship transform until UYA loader
or live-runtime evidence establishes the class-0 role and entry ordering.

## TABLE1 native 500-series controller evidence

Direct TABLE1 overlay archaeology now tightens the structural family. The retail
overlay loads seven sections, with its Moby dispatch table in `lvl.vtbl` and code
in `.text`. TABLE1 dispatch records route authored classes **500**, **501** and
**511** to the same update routine, `0x0033A5C0`.

That routine independently confirms UYA-native controller structure:

- live Moby `+0x20` is the state byte;
- live Moby `+0x68` is dereferenced as the PVar pointer;
- state zero takes a distinct initialization path;
- states 1 through 6 dispatch through a six-entry jump table;
- the routine contains explicit 500-series class comparisons, including 501,
  502, 504, 505 and 506 branches;
- class-specific break/effect/resource helpers are called from the shared
  controller.

This is direct UYA retail evidence of the same broad 500-series gameplay family
shape previously recovered in GC. It does **not** import GC state meanings,
damage constants, reward selectors or terminal-state policy into UYA. The exact
UYA hit admission and state meanings remain under recovery. Public lineage may
corroborate the familiar crate naming, but native behavior will be promoted only
as individual UYA paths are traced.

## Runtime seam added for this goal

`OBP.RAC3.Gameplay.UyaMobyRuntimeSession` now registers the complete authored
UYA population using native class plus authored instance index. It preserves
immutable source identity and copies PVars into live source-owned storage.
Unknown classes remain live authored entities with no guessed controller. Exact-class update and damage-consumer dispatch fail closed until a UYA controller is explicitly registered, so later crate/enemy recovery can attach reusable behavior without privileging one witness instance.

Terminalisation is currently only the neutral lifetime operation:
active presentation becomes inactive. No R&C1/GC terminal state byte is copied
into UYA. A class controller may add a native state transition only after UYA
evidence proves it.

`UyaDamageTransportSession` provides synchronous sequenced event delivery with
stable player/Moby/projectile references. Native damage, flag and marker fields
are optional. This lets later recovered UYA weapon/enemy evidence populate exact
values without inventing defaults now, and transport itself applies no damage.

## Validation in this slice

Focused unit tests cover authored identity/PVar copying, source isolation,
terminal lifetime projection, stable damage references and sequenced delivery.

An authorized-retail TABLE1 test builds the normal production world, registers
all 735 authored Mobies into the UYA runtime, and pins representative opening
placements including class 0 / instance 0 and class 500 / instance 311. It does
not teleport, inject a witness object or substitute a hand-authored placement.

## TABLE1 class-500 destructible recovery

The loaded TABLE1 Moby vtable maps authored classes 500, 501 and 511 to the
same update routine at `0x0033A5C0`. This is UYA overlay evidence, not an
assumption imported from Going Commando. The routine uses live Moby `+0x20`
as a six-state selector and live `+0x68` as the PVar pointer.

Before state dispatch it calls the owned collision-damage resolver with mask
`0x0B030001`. A returned record with exact flags `0x02000000` is discarded.
On the ordinary state-1 path, class 500 requires positive floating damage at
record `+0x2C` before calling the shared transition/reward helpers
`0x0033BE30` and `0x0033B940`.

The class-500 state-3 path reads PVar `+0xF8`. Zero enters
`0x0033CA98`; that helper takes its direct native deactivation path when the
PVar dword at `+0xB0` is also zero. Exact-authority TABLE1 census closes this
route for the entire authored population:

- 131 class-500 instances;
- all 131 carry exactly `0x140` PVar bytes;
- all 131 have PVar `+0xB0 dword == 0`, `+0xC8 == 0`,
  `+0xF8 == 0` and `+0xFB == 0`;
- authored static `+0x14` values are `11` (14 objects), `44` (110),
  and `61` (7).

The TABLE1 static-population path independently copies authored static
`+0x10 -> live Moby +0xB2` and static `+0x14 -> live Moby +0xB4`.
The post-break helper later reads and conditionally clears live `+0xB4`, but
only **after** its separate indexed-resource spawn loop. The new trace therefore
proves lifecycle consumption of that authored value, not that it selects or
quantifies the resource drop. Its exact semantic name remains unknown.

`UyaClass500DestructibleSession` now implements this recovered path for every
authored TABLE1 class-500 object. Damage dispatch requires the UYA-native mask,
exact-flag exclusion and positive damage scalar. A matching hit records native
state 1 -> break state 3 and projects the proven ordinary deactivation route to
neutral inactive presence. Unrecovered PVar variants fail closed.

Pinned Wrench cross-game data names numeric class 500 as a Bolt Crate, but its
UYA-specific underlay does not name class 500. Direct UYA evidence now points
elsewhere: TABLE1 class 500 has an indexed-resource emission path, not the GC
class-500 physical-Bolt denomination path. OBP therefore keeps the UYA object
named by class/destructible behavior.

## TABLE1 indexed-resource drop and class 3291

Direct TABLE1 disassembly closes the ordinary `PVar+0xFB == 0` drop path used
by all 131 authored class-500 instances. The shared post-break helper calls
`0x00440E98`, whose native table has **156 resource slots**. It filters slots
by an availability byte, a non-zero item-definition `+0x8A` limit and an exact
resource-index exclusion set (`16, 57, 6F, 97, A7, C0..C4` hex). If any
eligible resource is below its limit, selection is from that under-limit pool;
otherwise it falls back to all eligible resources. Both selections consume an
explicit bounded native RNG result.

The helper then performs a separate `rand(5)`: zero yields two spawned objects,
all other results yield one. Each spawn calls `0x0030EF98`, which explicitly
allocates native **class 3291 (`0xCDB`)**. The constructor stores the selected
resource index at PVar `+0x68`. With the class-500 caller's amount argument
`-1`, it resolves the selected item's table row and copies item-definition
halfword `+0x88` into PVar `+0x60`.

Class 3291's TABLE1 update later reads those same fields on collection:
`+0x68` supplies the indexed-resource slot and `+0x60` the increment. It
calls `0x0050D980`, which adds the amount to the slot's 32-bit current count
and clamps to item-definition `+0x8A` when that limit is non-zero. The pickup
then transitions out even if the count did not increase. This proves an indexed
resource/count pickup contract without proving human names for individual slots.

A retained authorized UYA savestate
(`SHA-256 1e16f9a6262c77507ed38f7887d6b3bf849703c574d8b870611873dbbb7f6b5c`)
was loaded with its native PCSX2 v2.8.2 compatibility requirement in a
g007-owned portable profile. Visual capture confirmed live Veldin gameplay.
PINE sampling of the exact selector arrays in that state found zero eligible
resource slots under the recovered filter, so that particular retail state
would make the helper return zero and spawn no class-3291 resource pickup.
That state fact is not generalized into an opening inventory policy.

`UyaClass500ResourceDropPlanner` and `UyaIndexedResourceSession` now retain
this engine-independent contract. Callers must supply all 156 resolved resource
facts and explicit native-range RNG results; OBP does not invent UYA inventory,
slot names or random outcomes.

## TABLE1 class 5821 actor and damage evidence

Direct TABLE1 vtable scanning establishes a 12-byte dispatch record
`{oClass, update, aux}`. Class **5821 (`0x16BD`)** resolves to update
`0x0034FC70` with aux `0x002F74D4`. TABLE1 carries **62 authored
class-5821 instances**, all with `0x5F0` PVars and the same recovered damage
profile: relative lifetime pointer `+0x00 -> +0x30`, damage-config pointer
`+0x10 -> +0x160`, initial runtime lifetime `+0x30 = 1.0`, authored
capacity `+0x34 = 1`, config vertical threshold `+0x40 = 0.0`, config
byte `+0x49 = 0`, same-class multiplier `+0xA8 = 0.0`, and optional
multiplier `+0xAC = 1.0`.

The live-Moby pool bridge is direct UYA evidence. Global `0x001E009C`
points at `0x100`-byte Mobies; state is `+0x20`, PVar pointer `+0x68`,
owned damage-record slot `+0xA8`, class `+0xAA`, and position
`+0x10/+0x14/+0x18`. In the retained v2.8.2 Veldin state the pool base was
`0x01D36D00`, equal to the live-player pointer at
`0x001A4BE0+0x25C0`. PVar `+0x230` can therefore be identified exactly as
a target pointer when it equals Ratchet's live Moby.

The retained live population also corrects an earlier ambiguity: **state
`0xFD` alone is generic Moby inactivity, not proof of death**. Several
class-5821 instances were `0xFD` while retaining lifetime `1.0`. Three
moved, Ratchet-targeting instances were instead `0xFD` with lifetime
`-1999.0`. OBP therefore treats only the witnessed combination of inactive
state plus non-positive lifetime as death-like; state `0xFE` remains
unpromoted for this class.

Incoming damage is now traced through UYA itself. Class 5821 follows
`0x0034FC70 -> 0x00351E18 -> 0x0044FC98 -> 0x0044FCD0 -> 0x00444358`.
The common resolver at `0x00444358` interprets Moby `+0xA8 == 0xFF` as
no owned record, otherwise addresses a `0x40`-byte record in the pool at
`0x001EED00`; record `+0x24` is flags, `+0x28` is an exact native kind
byte whose broader name is still unknown, `+0x2C` is the floating damage
scalar, and `+0x38` points back to the victim. The class-5821 wrapper calls
the resolver with mask **`0x00010000`**. For record kind **10** the direct
lifetime-subtraction branch is skipped. Otherwise the shared consumer processes
the record damage and subtracts it from the caller-supplied lifetime pointer.
For this 62-instance TABLE1 profile, positive sub-unit damage is clamped to
`1.0`; the special same-class and optional multipliers are inactive on this
path. `UyaClass5821DamageSession` now reproduces this recovered lifetime
mutation only. A lethal result is deliberately **not** presentation-terminalized
immediately because the subsequent native reaction/death state progression is
still class-update-owned.

Outbound damage has two distinct native paths and they must not be conflated.

The **TABLE1-population-backed path is native state 10** at `0x003510F0`.
All 62 authored class-5821 PVars have byte `+0x44 = 1` and byte
`+0x5F = 0`. State 10 converts `+0x44` to the damage scalar and the
zero `+0x5F` branch selects flags **`0x00000001`**. Wrapper
`0x00443790` preserves those arguments into the common spatial emitter
`0x00443648`, giving radius **0.5**, damage **1.0**, flags
**`0x00000001`**, record kind **0**, descriptor byte `+0x29 = 1`, and
an additional spatial scalar **0.75**. The emitter runs only while
`0x00436500`'s native action-progress result satisfies **`6 < progress <
10`** and live Moby byte `+0x42 == 12`. State 8 contains a direct route into
state 10. It first rejects player-global
state **`0x12`**, then requires the current target-position separation scalar
to be strictly below **`1.0`**. Helper `0x0040CFB0` directly computes the
shortest unsigned angular difference: `abs(a-b)`, folded to `2π-delta` when
the first difference exceeds π. State 8 requires that result to be strictly
below **`0x3EDF66F3 = 0.4363323 rad` (25 degrees)** before selecting native
state 10 and action/sequence byte **12**. `UyaClass5821Actor` exposes this
direct gate as explicit facts and fails closed when no current target is
provided. State 8 also has independent global/contact branches, so OBP does not
collapse its whole transition law to these comparisons alone.

The class-5821 **target selector request** is now recovered. All 62 authored
PVars carry relative pointer `+0x0C = 0x220`; retail relocation turns that
into an exact pointer to the runtime target block at `PVar+0x220`. The
selected Moby pointer is target-block `+0x10`, therefore `PVar+0x230`.
The authored target position/pointer bytes themselves start as zero, confirming
that identity and coordinates are runtime-generated.

Class 5821 reaches shared selector `0x004539A8` through
`0x00351E18`. The helper loads the relocated `+0x0C` target-block pointer
and, before candidate-group replacement, writes Ratchet's live Moby pointer
from `0x001A4BE0+0x25C0` into target-block `+0x10`. If the authored
selector index is not `-1`, helper `0x00453620` interprets it as an index
into a **runtime target-group table**, iterates that group's candidate Mobies,
scores them with `0x00453408`, and can replace the Ratchet seed with the
best eligible candidate. This proves that a host-side "nearest player" rule
would be wrong: Ratchet is the native seed, but dynamic group candidates may
supersede it.

The class caller passes workspace `PVar+0x310`, native scalar arguments
`f13=10.0` and `f14=1.0`, and zeroed optional flags. For the resident
subtypes actually retained on TABLE1, subtype **1** uses the primary authored
selector at `PVar+0x2D0` with radius **16.0**, while observed subtypes
**0** and **4** use the secondary selector at `+0x2D8` with radius
**32.0**. Runtime `PVar+0x3E4 == 0` selects native mode 1; nonzero selects
mode 2. Authored TABLE1 starts **55** instances in mode 1 and **7** in mode 2;
all 62 author selector auxiliary field `+0x5E4 = 0`. The primary selector
index distribution is `-1:31, 13:12, 82:7, 26:4, 66:3, 73:3, 7:2`; the
secondary distribution is `-1:21, 18:12, 82:7, 79:6, 24:4, 27:4, 66:3,
73:3, 7:2`.

`UyaClass5821TargetSelectionRequest` preserves the ordinary selector request
shape and explicitly records that Ratchet is seeded before candidate
replacement. Direct tracing through `0x004539A8 -> 0x00453620` proves the
request's former "mode" output is an **exact desired registry tag**: runtime
mode zero requests tag **1**, runtime mode nonzero requests tag **2**. The
shared picker accepts a non-Ratchet candidate only when its cached tag equals
that requested value; Ratchet is the explicit exception. The alternate
`PVar+0x5F != 0` class path requests tag **3**, but the admitted ordinary
TABLE1 profile has `+0x5F == 0`, so OBP now rejects that alternate path rather
than describing it as ordinary behavior. Shared helper `0x00455E98` remains
downstream steering: it receives an already-selected `+0x230` target, caches
it at `+0x3B0`, and resets a steering field.

The target-group **file substrate and cache-builder candidate universe are now
recovered directly from TABLE1**. Gameplay header `+0x98` points to an outer
block at `0x002F9230`; its first word is copy size `0x15A4`, and the copied
payload starts four bytes later. That payload contains **106** groups, a
`0x20`-byte header, then `106 × 0x30` group records. Header-relative condition
list bases are `0x1400`, `0x14C0`, `0`, `0x15A0`, `0`; their stored extents
land exactly on the next base / payload end. Each group record begins with
native world-space X/Y/Z plus a radius, five 16-bit condition counts, a native
cache stamp, then five relative list offsets. Native loader `0x0042772C`
copies and relocates this structure exactly before allocating one `0xE0` cache
entry per group.

`UyaGameplay` now parses this native block as `TargetGroups` with bounded
condition lists and preserves unresolved lists rather than naming them by
analogy. It also parses gameplay `+0x68`: native loader `0x00427310` reads its
count and copies `count × 0x80` records to the global used by condition-list 1.
TABLE1 contains **125** such target volumes. Across all 106 groups, list-entry
counts are `48, 56, 0, 1, 0`; list 0 ends exactly at list-1 base `0x14C0`,
list 1 exactly at `0x15A0`, and the lone list-3 entry (group 52) exactly at
payload end `0x15A4`. The largest list-1 index is 117, inside the 125-record
volume table.

For the ten groups referenced by class 5821, every group has exactly one fine
predicate. Groups **7, 13, 18, 24, 66, 73, 82** use condition-list 0 with
indices **8, 18, 17, 69, 97, 106, 113** respectively. Groups **26, 27, 79**
use condition-list 1 with volume indices **9, 117, 81**.

Both predicate families are now executable from authorized TABLE1 bytes.
Gameplay header `+0x78` points to the polygon block: **159** polygons, a
159-entry relative-offset table ending at `0x28C`, polygon data beginning at
relative `0x290`, and `0xE8B0` bytes of polygon records. Native loader
`0x004270E4..0x00427164` copies that blob and relocates the offsets into the
fixed BSS pointer table at `0x0021F740`. Each polygon starts with a vertex
count and stores 16-byte-stride XYZW vertices from `+0x10`. Native
`0x004432C0` is an exact half-open XY crossing test:
`(y0 < py <= y1) || (y1 < py <= y0)`, toggling parity only when the
interpolated edge X is strictly less than candidate X. Z/W do not participate.

For list 1, native `0x00440760` subtracts the volume record's `+0x30`
center XYZ with `VSUB.xyz` and passes that centered vector plus record
`+0x40` to `0x0040C2D0`. That helper multiplies XYZ by exactly three stored
inverse-basis columns at `+0x40/+0x50/+0x60`; the `+0x70` column is not
used. Membership is inclusive when all three normalized coordinates lie in
`[-1,+1]`. The group coarse gate `0x0040C060` is likewise exact:
`distanceSquared <= radiusSquared`. `UyaGameplay.TryContainsTargetGroup`
executes these recovered laws and remains fail-closed only for the unrelated
single list-3 predicate in group 52.

The lazy cache builder `0x00452F80` considers Ratchet, native class 203,
native class 7107, and a per-frame tagged Moby registry. TABLE1 authors **zero
class-203 and zero class-7107** placements, reducing Veldin candidates to
Ratchet plus that registry. A reverse dispatch call graph proves only eight
authored TABLE1 update families can reach the registry helper:
**5821 (62), 5860 (23), 6306 (18), 6317 (3), 6476 (3), 6577 (3), 6836 (1),
7032 (27)**. This is a caller census, not a claim that the caller Moby itself is
always registered.

The distinction matters for class 7032. Its state-0 initializer writes
`PVar+0x50 = -1`. On state 1, 17 of the 27 authored controllers carry
`PVar+0x40 = 6886` and `PVar+0x44` pointing to authored class-7031
instances; the other ten carry `-1` in both fields. The controller calls its
class-owned resolver, converts the returned live Moby pointer to a pool-slot
index, and stores that slot at `PVar+0x50`. The later tag-1 registry call
registers **that resolved child Moby**, not class 7032, and only when the
controller's partner PVar byte `+0x92` is nonzero. In the retained clean
retail Veldin snapshot, only three class-7032 controllers have resolved child
slots; all three children are native class **6886**, and all three partner
`+0x92` bytes are zero, so **no tag-1 child is currently registered**.
The remaining 24 controllers have no resolved child.

The seven shared hostile-family callers retain their recovered tag-5/tag-3
branches; no authored TABLE1 path currently proves an ordinary tag-2 registrant.
Therefore ordinary class-5821 tag-2 requests have only the Ratchet seed in the
recovered population, while tag-1 replacement depends on runtime-spawned class-
6886 children satisfying the class-7032 partner gate. The exact group geometry
still matters for those future children, but an authored-position census of
class-7032 controllers is **not** a native candidate census and must not be used
as one.

In the g007-owned, visually verified clean Veldin state, 58 class-5821 Mobies
are resident and only four have nonzero `+0x230`; all four point to Ratchet
and all four are already state `0xFD`. The 38 state-0 and eight state-1
residents have null targets. This is valid target-identity evidence, not an
acquisition-timing witness.

`UyaClass5821Actor` exposes the exact state-10 TABLE1 attack descriptor and
engine-neutral Ratchet damage event, but transport still applies no player-life
consequence.

A second **class-family** emitter exists in state 25. State 24 initializes
PVar `+0x300` to float bits `0x3D888889`, advances it each native update
by live TABLE1 `gp+0xC60 = 0xBB888889` (about `-1/240`), and normalizes
against `0x3E088889`. Direct single-precision replay matters: after 32
updates progress is `0x3F7FFFFF`, just below one, so state 25 is entered on
the **33rd native update**, not the ideal-fraction 32nd. The state-entry helper
`0x00452EC8` is a one-shot Moby `+0xBE` bit-0 setter. State 25 emits on
each update with radius **0.5**, damage **1.0**, flags **`0x02000001`**,
kind **0**, and byte `+0x29 = 1`; it exits to state 26 when live native Z is
at or below the query-produced scalar stored at PVar `+0x328`.
`0x00443140` produces that scalar from a spatial query with vertical offset
0.5, returning zero on no hit and otherwise the shared query result at
`0x001EAAA8`.

However, the state-24 entry path is gated by live Moby `+0x95 == 9`.
The retained clean TABLE1 state has 58 resident class-5821 Mobies with
`+0x95` values only **0 (54), 4 (3), and 1 (1)**; none is 9. The four
nonresident authored instances 432-435 do not supply a live subtype witness.
Therefore state 25 remains valid class-family code but is **not** promoted as
ordinary TABLE1 population behavior.

The state-25 emitter's nonzero-result branch still directly compares the shared
selected-Moby pointer with Ratchet's live pointer and writes `60` to
player-global `+0x1DE` on equality. The meaning and downstream player-life
effect of that field remain unrecovered. A g007-owned clean-state visual capture
confirmed live Veldin before a bounded ordinary forward-input trace. That trace
showed no class-5821 state transitions, and its post-run game frame was black,
so the approach run is explicitly rejected as behavior evidence. No teleport or
memory write was used; the clean state was reloaded afterward.

## Live host integration

The generic provider/world path now configures a UYA gameplay session whenever
an interactive `rac3` world is adopted. It registers the complete authored Moby
population, installs the recovered exact-class 500 and 5821 incoming-damage
consumers, and audits the TABLE1 class-5821 authored profile without starting a
synthetic AI loop. The 131 admitted class-500 instances remain bound to their
normal `RuntimeWorldScene` presentation nodes; all 62 class-5821 instances are
runtime-admitted only when their incoming-damage, population-backed state-10
attack, and target-selector request profiles all match the retail census.

The temporary non-R&C1 player action supplies only a short-range host
aim/contact envelope. Its event uses `flags=0x00000001` and positive damage,
which the TABLE1 class-500 routine admits; this is **not** promoted as recovered
UYA wrench reach, timing, animation, or weapon damage. Once admitted, the UYA
runtime owns the state-1 -> state-3 break decision and native deactivation route,
and the host only applies the resulting neutral inactive presentation state.

An ordinary `rac3:TABLE1` Godot movement smoke from the authored ship point
now confirms **131/131 class-500** instances admitted and presented plus
**62/62 class-5821** runtime profiles admitted with zero class-5821 rejects.
The same run passes normal collision grounding and the existing temporary R&C1
locomotion path. It does not synthesize 5821 AI or player damage. No witness
object, teleport, or hand-authored crate was used.

## Next recovery boundary

The next integration task is to recover the UYA player inventory/resource
snapshot that supplies the 156 selector facts during ordinary TABLE1 play, then
wire class-3291 pickup presentation/collection only when those facts are
available. The selector, spawn identity, increment amount and add/clamp law are
now recovered; slot names, ordinary opening inventory ownership and host RNG
source remain intentionally unpromoted.

In parallel, continue class 5821 from the executable target-group geometry.
Recover the class-7032 state-1 child resolver far enough to instantiate its
runtime class-6886 child from authored controller/partner facts, preserve the
class-7031 partner PVar `+0x92` admission gate, and feed only actually admitted
tag-1 children into the recovered group cache. Separately pin the tag-3/tag-5
admission branches for the seven shared hostile-family callers. Then execute
the native Ratchet-seed versus exact-tag candidate score/replace loop and feed
its selected target into the recovered state-8 -> state-10 transition. Also
recover the UYA player-life consequence for emitted damage records and the
post-lethal reaction progression after lifetime reaches non-positive. State 25
must remain a subtype-family branch until TABLE1 eligibility is witnessed.
R&C1 Nanotech, controller-position stand-ins, nearest-player guesses, guessed
cooldowns, or synthetic targets must not substitute.
