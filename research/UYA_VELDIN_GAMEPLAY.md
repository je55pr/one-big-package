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

## Runtime seam added for this goal

`OBP.RAC3.Gameplay.UyaMobyRuntimeSession` now registers the complete authored
UYA population using native class plus authored instance index. It preserves
immutable source identity and copies PVars into live source-owned storage.
Unknown classes remain live authored entities with no guessed controller.

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
The post-break helper later consumes live `+0xB4` and clears it. This proves
that the authored value participates in the native post-break/reward flow,
without yet assigning a stronger semantic name to the value.

`UyaClass500DestructibleSession` now implements this recovered path for every
authored TABLE1 class-500 object. Damage dispatch requires the UYA-native mask,
exact-flag exclusion and positive damage scalar. A matching hit records native
state 1 -> break state 3 and projects the proven ordinary deactivation route to
neutral inactive presence. Unrecovered PVar variants fail closed.

Pinned Wrench cross-game data names numeric class 500 as a Bolt Crate, but its
UYA-specific underlay does not name class 500. OBP therefore keeps the current
UYA implementation named by class/destructible behavior rather than promoting
that familiar label as UYA authority.

## Next recovery boundary

The next evidence task is to identify the first ordinary-route hostile family
from UYA itself. That requires UYA loaded-overlay/live-runtime
evidence linking authored class identities to update routines, damage admission,
state/lifetime transitions and PVar fields. Once one family is proved, its
controller should attach to every authored instance of that class through the
runtime store, then be exercised from normal TABLE1 spawn with the existing
temporary R&C1 player locomotion.
