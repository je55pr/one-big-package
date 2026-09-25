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

## TABLE1 class 5821 actor evidence

Direct TABLE1 vtable scanning establishes a 12-byte dispatch record
`{oClass, update, aux}`. Class **5821 (`0x16BD`)** resolves to update
`0x0034FC70` with aux `0x002F74D4`. TABLE1 contains **62 authored
class-5821 instances**, each with exactly `0x5F0` PVar bytes and authored
signed halfword `PVar+0x34 == 1`.

The class initializer copies its resolved/scaled signed `PVar+0x34` value into
runtime float `PVar+0x30`. The retained v2.8.2 Veldin state confirms the
ordinary TABLE1 branch currently leaves that runtime value at **1.0** for
dormant state-0 and active state-1 instances. The same family update tests
`PVar+0x30 <= 0` before entering cleanup/death-style consequences.

The live-Moby pool bridge is also direct UYA evidence. Global
`0x001E009C` points at a pool of `0x100`-byte Mobies; class is at live
`+0xAA`, state at `+0x20`, PVar pointer at `+0x68`, and position at
`+0x10/+0x14/+0x18`. In the retained Veldin state the pool base is
`0x01D36D00`, exactly equal to the live-player pointer stored at player-global
`0x001A4BE0+0x25C0`, so Ratchet is live slot 0.

Three nearby live class-5821 objects were observed in native state **0xFD**.
All three retained that exact Ratchet Moby pointer at runtime PVar
`+0x230` and carried runtime lifetime **-1999.0**. Farther dormant state-0
instances carried a null `+0x230` target and lifetime 1.0. This proves that
class 5821 has a runtime target-acquisition field which can resolve Ratchet, and
that state 0xFD plus non-positive lifetime is a real terminal observation for
this class. The overlay also checks state 0xFE on linked Mobies, but no
class-5821 state-0xFE witness has been retained, so OBP does not promote 0xFE as
a class-5821 terminal state.

A controlled ordinary-input approach was rejected as a behavior witness after
Ratchet visibly left the intended ground line; no teleport or memory write was
used. A separate five-second g007-owned retail visual capture from the clean
state showed no player-hit event. Consequently **outbound hostility is not yet
promoted**, and the class-5821 implementation does not register a damage
consumer or attack law. In particular, class-500's recovered damage mask is not
reused. `UyaClass5821Actor` currently preserves only the exact authored
lifetime authority and observed native state/lifetime/target facts.

## Live host integration

The generic provider/world path now configures a UYA gameplay session whenever
an interactive `rac3` world is adopted. It registers the complete authored Moby
population, installs only the recovered exact-class 500 damage consumer, and
binds the 131 admitted TABLE1 class-500 instances back to their normal
`RuntimeWorldScene` presentation nodes.

The temporary non-R&C1 player action supplies only a short-range host
aim/contact envelope. Its event uses `flags=0x00000001` and positive damage,
which the TABLE1 class-500 routine admits; this is **not** promoted as recovered
UYA wrench reach, timing, animation, or weapon damage. Once admitted, the UYA
runtime owns the state-1 -> state-3 break decision and native deactivation route,
and the host only applies the resulting neutral inactive presentation state.

An ordinary `rac3:TABLE1` Godot movement smoke from the authored ship point
confirmed 131/131 class-500 instances admitted and presented, zero rejected,
normal collision grounding, and the existing temporary R&C1 locomotion path.
No witness object, teleport, or hand-authored crate was used.

## Next recovery boundary

The next integration task is to recover the UYA player inventory/resource
snapshot that supplies the 156 selector facts during ordinary TABLE1 play, then
wire class-3291 pickup presentation/collection only when those facts are
available. The selector, spawn identity, increment amount and add/clamp law are
now recovered; slot names, ordinary opening inventory ownership and host RNG
source remain intentionally unpromoted.

In parallel, continue class 5821 at the exact unresolved boundary: trace the
shared service which drives runtime lifetime from 1.0 to the observed -1999.0
terminal sentinel and recover the family's native incoming-damage admission.
Then retain a controlled ordinary-route witness of an outbound attack consequence
before promoting 5821 as a hostile actor. Only after both directions are proved
should an exact-class controller attach to all 62 authored TABLE1 instances.
