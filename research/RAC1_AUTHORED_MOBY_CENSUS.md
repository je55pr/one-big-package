# R&C1 authored Moby census

Status: retained, static-only census for the pinned NTSC-U authority
`rac1-ntscu-original` / `SCUS-97199`.

This census records every authored Moby class-table row on all 19 native levels,
including rows with no asset payload and classes that are never instantiated.
No PCSX2 session was used. The authorized retail disc was sufficient.

## Durable outputs

- `research/generated/rac1-authored-moby-census.json`: global 1,350-class matrix
  plus per-level occurrences and totals.
- `research/generated/rac1-authored-moby-census.csv`: one row for each of the
  3,641 authored class-table occurrences.
- `Rac1AuthoredMobyCensus`: deterministic projection over the production
  `Rac1DiscIndex`, `Rac1LevelCore`, `Rac1StaticClasses`,
  `Rac1LevelSettings`, `Rac1Instances`, and `Rac1MobyAnimation` codecs.

Reproduce from an authorized authority ISO with:

```text
dotnet run --project src/OBP.Cli/OBP.Cli.csproj -- rac1-authored-moby-census "<RAC1.iso>" research/generated
```

The command requires the pinned ISO byte length and `SCUS-97199` boot serial
before running the census. The repository does not contain the retail ISO.

## Per-level census

| Level | Table rows | Payload | High-LOD model | Source sequence | Joint-bearing | Instantiated IDs | Instances |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 125 | 96 | 94 | 95 | 54 | 21 | 296 |
| 1 | 212 | 169 | 167 | 168 | 83 | 84 | 983 |
| 2 | 187 | 156 | 154 | 155 | 87 | 60 | 775 |
| 3 | 208 | 174 | 172 | 173 | 107 | 61 | 1,029 |
| 4 | 191 | 160 | 158 | 159 | 99 | 52 | 536 |
| 5 | 180 | 147 | 145 | 146 | 74 | 62 | 1,431 |
| 6 | 200 | 165 | 163 | 164 | 64 | 62 | 766 |
| 7 | 217 | 181 | 179 | 180 | 71 | 84 | 649 |
| 8 | 219 | 181 | 179 | 180 | 90 | 71 | 847 |
| 9 | 189 | 159 | 157 | 158 | 71 | 55 | 908 |
| 10 | 196 | 158 | 156 | 157 | 72 | 63 | 872 |
| 11 | 182 | 150 | 148 | 149 | 70 | 57 | 720 |
| 12 | 173 | 138 | 136 | 137 | 69 | 59 | 695 |
| 13 | 220 | 183 | 181 | 182 | 85 | 84 | 970 |
| 14 | 189 | 148 | 146 | 147 | 73 | 71 | 551 |
| 15 | 191 | 152 | 150 | 151 | 70 | 58 | 910 |
| 16 | 190 | 155 | 153 | 154 | 72 | 76 | 1,569 |
| 17 | 187 | 149 | 147 | 148 | 65 | 66 | 734 |
| 18 | 185 | 147 | 145 | 146 | 69 | 58 | 991 |
| **Total** | **3,641** | **2,968** | **2,930** | **2,949** | **1,445** | **1,204** | **16,232** |

The 1,204 instantiated-ID figure is the sum of distinct placed classes per level.
Globally those placements collapse to 813 distinct instantiated classes.

## Global boundaries

The 3,641 rows collapse to 1,350 distinct native `oClass` IDs. Of those:

- 813 are instantiated on at least one level and 537 are authored but never placed.
- 1,194 have an asset payload and 1,192 have recovered high-LOD model packets.
- 1,193 have source-authored sequence data and 571 carry joints.
- 15,359 placements reference a recovered model; 873 reference a class without
  available high-LOD model geometry.
- 14,279 placements have a PVar and 1,953 do not. Exactly 60 byte sizes are
  observed. PVar contents and schemas are intentionally not retained or inferred.
- Among instantiated classes, 616 always carry a PVar, 196 never carry one, and
  class 0 alone is mixed: 3 placements have 32-byte PVars and 16 have none.

Availability is recorded per level occurrence rather than assumed globally.
Global class rows retain every level-table ID, per-level instance/PVar counts,
and the distinct observed packet, joint, and sequence counts.

## Structural exceptions retained without naming speculation

| `oClass` | Retained static result |
|---:|---|
| 0 | Present and instantiated once on every level. It has 63 high-LOD packets and 111 joints. All 134 ordinary class-local sequence slots are null. Animation availability comes from the dedicated 256-slot Ratchet sequence table, present and populated on all 19 levels. |
| 1 | Present on every level and never instantiated. Payload exists, with 20 joints and 23 populated ordinary sequence slots, but zero high-LOD packets. |
| 2 | Same structural boundary as class 1: present everywhere, never instantiated, 20 joints, 23 populated ordinary sequence slots, and zero high-LOD packets. |
| 12 | Present on every level and never instantiated. It has a 22-packet high-LOD model and 92 joints, but all 10 ordinary sequence slots are null. It is the only payload-bearing class with no populated source sequence. |

No names are inferred from PVar size, joint count, model shape, sequence count,
or level distribution. Classes not independently identified elsewhere remain
numeric `oClass` values.

## Payload-free boundary and regression

The retained JSON and CSV contain scalar counts, booleans, level/class IDs,
table indices, and PVar byte sizes only. They contain no model vertices,
indices, textures, animation frame data, PVar bytes, executable bytes, or disc
payload ranges.

`Rac1AuthoredMobyCensusTests` pins the checked-in static totals and structural
exceptions without requiring retail data. Its opt-in retail test rebuilds the
census from `OBP_RAC1_ISO` and requires exact JSON and CSV equality with the
checked-in outputs.
