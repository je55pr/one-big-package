# R&C1 NPC and interactable census

Authority: pinned NTSC-U retail `rac1-ntscu-original` / `SCUS-97199`.
This note retains the static-only w349 findings. No PCSX2/PINE session was used.
The payload-free machine-readable witness is
`research/generated/rac1-npc-interactable-census.json`.

Names from Lawrence/public tooling are corroboration only. They are not promoted
from model resemblance, text proximity, or PVar size. Where public labels conflict,
the conflict is retained rather than resolved by guesswork.

## Class 750 progression Moby

Class `750` has a 208-byte PVar, a 19-packet high-LOD model, 43 joints, and five
ordinary sequence slots. It is authored in level tables
`0,1,3,4,5,6,7,8,10,12,13,14,15,17`, but only eight instances are placed:
`LEVEL1 x1`, `LEVEL3 x1`, `LEVEL6 x2`, `LEVEL7 x1`, `LEVEL10 x2`, `LEVEL17 x1`.

The placed `PVar+0x04` values are, in native placement order:
`LEVEL1:0`, `LEVEL3:4`, `LEVEL6:5,5`, `LEVEL7:8`, `LEVEL10:11,12`, `LEVEL17:18`.
This offset is not merely patterned data: the registered updater witness at
`0x002d5de8` dispatches 12 states through `0x001e9f60`; state 8 calls campaign
admission primitive `0x002607d0` at `0x002d6a20` using that PVar destination.

The retained semantic boundary is therefore a visible mission/progression Moby,
strongly corroborated by public tooling as the infobot family. The native behavior,
not the public name, is the identity anchor.
## One-off mission/contact actor candidates

| oClass | Native placement | PVar | Model / joints | Registered update | Corroborative label | Evidence grade |
|---:|---|---:|---:|---|---|---|
| 890 | `LEVEL3 x1` | 560 B | 28 / 92 | `0x002da870` | Helga | strongly corroborated |
| 774 | `LEVEL1 x1` | 416 B | 30 / 90 | `0x002ff118` | Plumber | strongly corroborated |
| 1283 | `LEVEL8 x1` | 384 B | 30 / 90 | `0x003065d8` | Plumber | strongly corroborated |
| 919 | `LEVEL5 x1` | 384 B | 30 / 92 | `0x00317470` | Bouncer | corroborated |
| 1144 | `LEVEL8 x1` | 416 B | 37 / 96 | `0x00305270` | Deserter | corroborated |
| 1130 | `LEVEL8 x1` | 400 B | 33 / 92 | `0x00302ce8` | Commando | corroborated |
| 1190 | `LEVEL4 x1` | 336 B | 36 / 83 | `0x002e3078` | Fred / Lieutenant | unresolved, contradictory labels |

The two Plumber-labelled classes are especially useful structural corroboration:
both are unique placed actors with exactly 30 high-LOD packets and 90 joints,
while retaining distinct PVar sizes and update routines. That supports a
level-specific-variant relationship without making the public label native truth.

Class 1190 deliberately remains unresolved. Public tooling supplies both `Fred`
and `Lieutenant`; neither label is selected as the retail identity here.

## Acquisition and world-interaction objects

These are retained separately from NPCs. Public progression tooling correlates
their removal/acquisition behavior, but this work does not claim a dialogue actor
or complete input state machine for them.
| oClass | Native placement | PVar | Model / joints | Corroborative label |
|---:|---|---:|---:|---|
| 1005 | `LEVEL2 x1` | 80 B | 14 / 10 | Trespasser |
| 1016 | `LEVEL6 x1` | 80 B | 13 / 11 | Hydrodisplacer |
| 1120 | `LEVEL4 x1` | 80 B | 17 / 20 | Suck Cannon handoff/object |
| 1290 | `LEVEL9 x1` | 80 B | 10 / 2 | Pilot's Helmet |
| 18 | `LEVEL10 x1` | 80 B | 14 / 0 | Magneboots |
| 1354 | `LEVEL14 x1` | 96 B | 20 / 23 | Morph-o-Ray |
| 1428 | `LEVEL17 x1` | 64 B | 14 / 13 | Codebot |

Class 1290 is a useful authored-versus-placed boundary: it appears in all 19
native Moby class tables but has exactly one authored placement, on `LEVEL9`.

## Dedicated retail vendor speech bank

The disc index contains a dedicated vendor speech bank at TOC offset `0x1a0`
with exactly 37 entries. Its metadata carries vendor-specific `ven...` source
names and the final metadata label `Fanfare_Vendor`. This is a real retail
subsystem boundary, separate from Qwark/general-help material.

No visible vendor Moby `oClass` is established by this evidence. Apparent text-ID
hits that decode as address halves are not promoted, and this note intentionally
does not invent a vendor class from the speech bank.

## Payload-free regression boundary

The generated witness stores only class/level IDs, PVar byte sizes and one proven
PVar scalar selector, model packet/joint counts, executable addresses, evidence
grades, public corroborative labels, and speech-bank metadata/counts. It contains
no retail model, texture, animation, PVar, executable, VAG, or ISO payload bytes.

`Rac1NpcInteractableCensusTests` pins these semantic boundaries and cross-checks
the structural rows against `rac1-authored-moby-census.json`, so later archaeology
cannot silently drift the placements, PVar sizes, packet/joint counts, or the
class-1290 authored-versus-instantiated distinction.
