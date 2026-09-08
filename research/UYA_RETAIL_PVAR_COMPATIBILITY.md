# UYA retail PVar compatibility

**Authority:** `rac3-ntscu-original` / `SCUS-97353`
**Retail ISO SHA-256:** `d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444`

This document records the retail evidence for reusing OBP's strict Going Commando gameplay/PVar reader on Up Your Arsenal data. Retail bytes are authority. The candidate hidden-table address at LBA 1001 remains provenance-separated: its bounded retail contents are verified, but executable control-flow has not yet proven that address as the native loader ToC.

## Status

**RETAIL-COMPATIBILITY CONFIRMED across all 51 observed main-level rows.**

The unchanged parser in `reference-ts/packages/gc-pvars/src/index.ts` accepts every retail UYA main gameplay lump after an independent UYA prerequisite census validates the relevant header pointers, class/Moby spans, PVar table/data spans, both fixup arrays, their terminators, and every fixup target offset.

This proves a shared structural layout. It does **not** by itself make the GC-derived semantic labels (`UID`, `oClass`, `mode bits`, `PVar Moby link`, and `relative pointer`) native UYA symbol names. Those names retain their existing provenance until UYA executable/dataflow evidence independently supports them.

## Compatible gameplay-header slice

The following UYA gameplay-header slots are populated coherently and pass the strict shared reader on all applicable retail rows:
| gameplay header | shared/GC label | UYA evidence |
|---:|---|---|
| `+0x48` | Moby class list | retail-compatible |
| `+0x4c` | static Moby instances | retail-compatible, `0x88` records |
| `+0x58` | PVar Moby-link fixups | retail-compatible terminated 8-byte pairs |
| `+0x5c` | PVar table | retail-compatible 8-byte offset/size entries |
| `+0x60` | PVar data | retail-compatible referenced spans |
| `+0x64` | PVar relative-pointer fixups | retail-compatible terminated 8-byte pairs |

The compatibility probe is `reference-ts/packages/uya-pvars-compat/src/index.ts`. It does not relax or fork `gc-pvars`; it validates UYA prerequisites independently and then applies `parseGcGameplayMobyPvars()` unchanged.

## All-row census

The census covers the 51 observed rows carrying exactly one clean `0x60` main-level header under the current catalogue evidence. Row 38 is the known no-main-WAD special case and is not counted as a parser failure.

Across those 51 gameplay lumps:

- **133,935,536** decompressed gameplay bytes
- **10,922** declared Moby-class entries
- **23,221** static Moby records
- **19,001** static Mobies carrying a non-negative PVar index
- **19,001** distinct PVars referenced by static Mobies
- **3,855** PVar Moby-link fixups
- **36,414** PVar relative-pointer fixups
- **19,017** distinct PVars referenced by the union of Mobies and both fixup streams
The extra **16** referenced PVars are not owned by static Mobies under the GC reader's current Moby-focused output. They are reached only through fixup records and occur on table rows 3, 6, 8 and 9:

| table row | Moby-owned PVars | all referenced PVars | fixup-only delta |
|---:|---:|---:|---:|
| 3 | 269 | 273 | 4 |
| 6 | 253 | 254 | 1 |
| 8 | 481 | 484 | 3 |
| 9 | 1,128 | 1,136 | 8 |

This is useful negative/extension evidence: `gc-pvars` is correct for its documented static-Moby slice, but a complete UYA authored-PVar catalogue must retain fixup-only entries too.

### Reconfirmed handoff rows

| row | gameplay bytes | classes | Mobies | Mobies with PVar | Moby PVars | Moby links | relative pointers |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 3,147,136 | 265 | 735 | 670 | 670 | 242 | 1,324 |
| 8 | 2,538,560 | 263 | 777 | 481 | 481 | 130 | 1,048 |
| 20 | 2,387,200 | 270 | 200 | 186 | 186 | 64 | 330 |
| 50 | 2,182,336 | 142 | 198 | 191 | 191 | 158 | 227 |

Row 8 additionally contains three fixup-only referenced PVars, so its all-reference count is 484.
## Machine-readable evidence

The per-row census is committed as:

`research/generated/rac3-ntscu-original.uya-pvars.json`

Generated file SHA-256 at this checkpoint:

`0a484cea68e0a73976db06e05137a156561b9f790ea0c829f16e8489b78978a1`

Each row records the decompressed gameplay SHA-256, strict counts, maximum referenced PVar index, and raw gameplay-header pointer values. It contains derived metadata/hashes only, not retail payload bytes.

Local reproduction:

```text
cd reference-ts
npm run build
node tools/uya-pvar-census.mjs "<retail UYA ISO>" --out "<report.json>"
```

The tool first verifies the pinned retail ToC-window identity, discovers the current 51 main-level rows from the bounded catalogue evidence, decompresses each gameplay WAD using the shared strict WAD-LZ codec, and refuses the run on any prerequisite or shared-parser disagreement.

## Detailed Moby/PVar catalogue

`reference-ts/tools/uya-moby-pvar-catalogue.mjs` emits a payload-free per-instance/per-PVar catalogue. The full local report contains all 23,221 static Moby records, all 19,017 referenced PVars, PVar size/content hashes, fixup offsets, and GC-compatibility field values. Retail PVar payload bytes are never embedded.

A compact source-controlled index is committed as `research/generated/rac3-ntscu-original.uya-moby-pvar-families.json`. It retains 892 observed numeric oClass families, row coverage, instance/PVar counts, PVar sizes and mode-value sets, plus hashes identifying the reproducible full report.

One high-coverage numeric pattern is compatibility oClass `500`: 3,749 instances across 22 rows, all with a 320-byte PVar and compatibility mode value `0x20`. No UYA object identity is asserted from that numeric pattern alone.
## Next native questions

1. Use UYA executable/overlay evidence to confirm which GC-derived field semantics carry unchanged into RAC3.
2. Use the detailed catalogue to cluster authored PVar shapes/content and prioritize executable traces without embedding retail payload bytes.
3. Cluster common oClass/PVar shapes across levels and choose a simple high-coverage family for the first UYA runtime state-machine trace.
4. Census the remaining gameplay-header blocks semantically-free before assigning names such as spline, volume, zone or spawn.
