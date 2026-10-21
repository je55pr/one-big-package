# Up Your Arsenal world import reference

Authority: NTSC-U original `SCUS-97353`, build `rac3-ntscu-original`.
Disc SHA-256: `d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444`.

`OBP.RAC3` imports retail data into `RuntimeWorld`; the Godot host consumes that
neutral result. The current implementation and executable checks are
`Rac3WorldImport`, `UyaLevelCore`, `UyaGameplay`, `UyaSky`, `UyaLevelSettings`
and `Rac3WorldTests`. No browser or pre-generated world JSON is required.

## Discovery and provenance

[UYA_RETAIL_TOC.md](UYA_RETAIL_TOC.md) describes the sparse observed table.
There are 51 main-level rows. Row 38 contains an associated container but no
main level and is excluded. The reader preserves the table index and main
header's raw `+0x08` word separately and requires equality for this authority.

The LBA-1001 window is hash-verified retail evidence. Its role in the native
loader, and the names of fields initially identified through public layouts,
remain distinct from structural parser compatibility. Public comparison uses
Wrench revision `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`; it is not authority
over contradictory retail bytes. Display names in `Rac3DestinationCatalogue`
are public cross-references; numeric world identities remain independent.

## Container and geometry path

The bounded read path is `0x60` outer header -> `0x58` primary-data header ->
`0xbc` core header -> WAD-LZ assets. The outer primary/gameplay ranges, index,
GS-RAM and assets stay independently bounded. Declared compressed and decoded
sizes must agree with the WAD-LZ stream; shared readers receive no UYA leniency.

| Family | Retail compatibility and admission |
|---|---|
| Tfrags | The shared GC/VIF decoder accepted all declared fragments on sample rows 1/8/20/50, with finite geometry, complete VIF streams and in-range texture IDs. |
| Collision | The shared RC collision reader accepted four distinct sampled blocks without relaxed validation. Core extent field names retain compatibility provenance. |
| TIEs and shrubs | Shared class/placement readers require complete tables, finite matrices, resolved class identities and valid texture references. Gameplay pointers are `+0x34` and `+0x40`. |
| Ordinary textures | Tfrag/Moby/TIE/shrub tables use the shared 16-byte paletted entries; dimensions, asset pixels and GS-RAM palettes are independently bounded. |
| Part/FX textures | They are not admitted as ordinary GC texture tables. All sampled part entries had invalid dimensions; FX yielded one accidental eligible record per row with otherwise implausible types/dimensions. |
| Mobies | A declared zero-local-core class, an empty decoded model and unresolved skinning are separate cases. Preserve every authored instance; attach render geometry only when independently supported. |

The four texture-table counts on rows 1/8/20/50 were respectively:
tfrag `109/54/76/46`, Moby `143/154/159/167`, TIE `166/57/89/100`, and shrub
`55/10/19/9`. Moby texture byte `0xff` is the explicit no-texture sentinel;
other out-of-range references remain errors. Native placements use `Rz * Ry * Rx`
before the Z-up to Y-up conversion; `UyaMobyPlacementTests` guards that order.

## Gameplay and PVars

The historical all-row census found the first 32 gameplay words populated,
unique and in-range on every main level. The extent to the next greater pointer
is an observation, not a native block size or sufficient evidence for a name.

| Gameplay header | Compatible structure |
|---:|---|
| `+0x48` | Moby class list |
| `+0x4c` | Static Moby instances, `0x88` records |
| `+0x58` | Moby-link fixups, terminated 8-byte pairs |
| `+0x5c` | PVar table, 8-byte offset/size entries |
| `+0x60` | PVar data spans |
| `+0x64` | Relative-pointer fixups, terminated 8-byte pairs |

All referenced spans and fixup targets must fit before shared GC interpretation.
The all-row evidence contains 23,221 Mobies, 19,001 Moby-owned PVars and 19,017
PVars referenced through the union of Mobies and fixups. The 16 fixup-only entries
occur on rows 3/6/8/9 with deltas 4/1/3/8; a Moby-only catalogue is incomplete.
UID, mode and link labels retain GC-layout compatibility provenance until
independent UYA dataflow proves their semantics. Numeric oClass 500 alone does
not identify a crate; recovered class behaviour is documented in
[UYA_VELDIN_GAMEPLAY.md](UYA_VELDIN_GAMEPLAY.md).

## Environment and spawn admission

Sample rows 1/8/20/50 accept the shared `0x5c` settings first part, including
finite background/fog/death-height fields. Later settings blocks and the precise
fog-distance presentation law remain separate recovery questions.

The compatible native-Z-up start fields are position `+0x3c/+0x40/+0x44` and
Z rotation `+0x48`. All 51 rows are finite; 21 have non-default transforms,
while 30 contain exactly `(20,20,20), 0`. `UyaLevelSettings` retains the values
and admits `RuntimeSpawn` only for the non-default group. Rejecting the repeated
tuple is OBP policy, not a recovered retail sentinel rule. `Rac3WorldTests`
pins both the representative transforms and the 21/30 split.

## Versioned sky layout

The `0x40` sky header has colour at `+0x00`, signed counts/flags through `+0x0e`,
texture definitions/data at `+0x10/+0x14`, FX/sprites at `+0x18/+0x1c`, and eight
shell offsets at `+0x20`. Texture definitions are four `s32` words: palette,
pixels, width and height.

UYA/DL's `0x10` shell prefix differs from GC: `s16` cluster count and flags at
`+0x00/+0x02`, rotation XYZ at `+0x04/+0x06/+0x08`, angular velocity XYZ at
`+0x0a/+0x0c/+0x0e`. The first cluster still starts at shell `+0x10`. Shared
cluster/vertex/UV/face sizes are `0x20/0x08/0x04/0x04`; face texture `0xff`
means no material. Public conversion is position `/1024`, UV `/4096`, doubled
PS2 alpha with `0x80 -> 255`, and reversed face winding.

Public flag meanings are bit 0 untextured and bit 1 bloom; retail row 20
exercises bit 1. Row 1 shells 3/4/5/6 carry raw angular velocities `(0,0,5)`,
`(0,0,4)`, `(0,0,3)`, `(0,0,2)`; row 50 also exercises `(0,0,1)`. Retain raw
signed values. Wrench's comparison rate is `value * framerate * 2*pi / 32768`;
that conversion alone is not executable proof of retail playback timing.

All four sampled sky base colours were zero. Level settings supply the separate
background and fog values; the sky colour is not a universal clear-colour source.
`UyaSkyPresentation` and its tests own the current host presentation policy.

## Evidence and checks

Detailed measurements are kept as data rather than separate progress reports:

- [Production census](generated/rac3-ntscu-original.production-import-census.json): all 51 original reference imports; current native tests account for later material-surface splitting.
- [Gameplay pointer census](generated/rac3-ntscu-original.uya-gameplay-block-census.json): raw pointers, apparent extents, hashes and unpromoted stride hypotheses.
- [PVar census](generated/rac3-ntscu-original.uya-pvars.json) and [family index](generated/rac3-ntscu-original.uya-moby-pvar-families.json): all-row counts and structural families.
- [Format witnesses](generated/rac3-ntscu-original.format-witnesses.json): retained hash/size/count tables from the original compatibility measurements, with source Git blob identities.
- [Moby animation](UYA_MOBY_ANIMATION.md) and [Veldin gameplay](UYA_VELDIN_GAMEPLAY.md): separate recovered skeletal and class-runtime contracts.

Run `./tools/test-retail.ps1` with the three authorized ISO variables configured.
The relevant native suites include `Rac3WorldTests`, `UyaMobyPlacementTests`,
`UyaSkyPresentationTests`, and the UYA gameplay/controller tests. These are
current validation; historical browser captures and CI test totals are not.
