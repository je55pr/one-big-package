# UYA production RAC3 importer

**Authority:** `rac3-ntscu-original` / `SCUS-97353`

Retail ISO SHA-256:
`d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444`

This checkpoint promotes the evidence-backed UYA reconstruction path from archaeology-only tools into the normal OBP importer contract.

Production entry points:

- `reference-ts/packages/importer-rac3/src/index.ts`
- `reference-ts/packages/rac3-world/src/index.ts`
- `reference-ts/packages/rac3-world/src/geometry.ts`

`rac3Importer.importWorld()` now consumes the verified random-access retail disc directly. A pre-generated multi-megabyte `OBPWorld` JSON is not required.

## Provenance boundary

The importer does **not** upgrade every old public-format lead into native truth merely because the complete path succeeds.

The resident table at candidate LBA 1001, outer level-range semantic labels and related public cross-reference names retain their documented provenance status. Retail UYA compatibility is separately established for every decoder admitted into world output.

## Production path

For one requested retail table/native ID, the importer performs:

```text
verified RAC3 disc source
→ bounded UYA candidate ToC / main-level selection
→ shared GC/UYA primary-data core parser
→ shared WAD-LZ / tfrag / texture / TIE / shrub / Moby / collision codecs
→ retail-censused UYA level settings
→ strict UYA PVar prerequisite gate + unchanged GC PVar parser
→ strict UYA sky decoder
→ neutral OBPWorld
```

The current 51 observed 0x60 main headers have raw `+0x08 == physical table index`. The importer checks that equality rather than silently collapsing the two identities.

Normal production imports embed decoded retail texture pixels as PNG data URIs. `importRac3World(..., { embedTextureImages: false })` exists only as a deterministic census/performance option; all native texture tables are still strictly decoded and validated.

## Authored Mobies

UYA Mobies are no longer emitted only as duplicated world-space debug geometry. The production representation uses reusable class-local `OBPModel` assets plus one `OBPInstance` for every authored static Moby.

Each instance preserves:

- numeric native `oClass` as `sourceClass`
- exact authored placement position / scale / rotation, converted from native Z-up into OBP Y-up
- native lighting fields already decoded by the shared gameplay reader
- compatibility-layout UID, raw `+0x14`, mode bits and PVar index
- PVar byte size/data offset when present
- an optional `modelId` only when local class geometry is evidence-safe to render

The UID/mode/PVar names remain GC-layout compatibility labels until RAC3 executable evidence independently proves UYA-native semantics.

The model library is filtered to classes actually referenced by the selected level's authored placements. For Veldin this reduces 145 decoded local-core class models to 36 used models while preserving all 427 renderable Moby instances.

A permanent transform test proves the neutral instance transform is equivalent to applying the native `Rz * Ry * Rx` matrix and then performing the native-Z-up → OBP-Y-up basis conversion. An earlier transposed-matrix implementation failed this guard and was corrected before promotion.

## Four-row equivalence

Expanded instance triangles include the reusable Moby model triangles once per linked instance.

| row | authored Mobies | linked models | with PVar | total visible triangles | sky shells |
|---:|---:|---:|---:|---:|---:|
| 1 | 735 | 427 | 670 | 1,111,775 | 8 |
| 8 | 777 | 668 | 481 | 625,753 | 3 |
| 20 | 200 | 117 | 186 | 586,443 | 6 |
| 50 | 198 | 182 | 191 | 152,571 | 5 |

Row 1 exactly reproduces the previous atmosphere+sky debug-tool visible triangle total. Rows 8/20/50 exceed their older documented totals only by the newly included strict sky geometry: +1,008 / +2,048 / +496 triangles respectively.

## All-main-level production census

`reference-ts/tools/rac3-import-census.mjs` exercises the same production world builder with texture image serialization disabled. It discovers rows from the observed candidate ToC rather than hard-coding the campaign subset.

All **51** retail main-level rows imported and passed `validateWorld`, spanning campaign, Vid-Comics, multiplayer, split-screen and the multiplayer-menu row with zero authored Mobies.

Aggregate retail output across those 51 imports:

- 34,433,048 visible render triangles after expanding linked Moby models
- 8,237,389 collision triangles
- 23,221 authored static Mobies
- 19,329 Mobies linked to evidence-safe local class models
- 19,001 Mobies carrying authored PVars
- 59,067 TIE placements
- 57,061 shrub placements

Row 39 independently proves the zero-Moby path: zero instances/models, a valid two-triangle render world and two collision triangles. Row 38 is deliberately excluded because retail exposes no part matching the 0x60 main-level header lead; asking the production importer for row 38 fails explicitly rather than inventing a level.

Committed census artefact:

`research/generated/rac3-ntscu-original.production-import-census.json`

- bytes: 26,758
- SHA-256: `95cca47405b7ffe7b5cc6b7c2ce5263a8bcb34880cf042faa7b8395300993489`

Reproduce locally with:

```text
npm run build
node tools/rac3-import-census.mjs <authority.iso> --out <report.json>
```

The tool first checks the pinned retail authority ToC-window hash.

## Validation

At this checkpoint the full `reference-ts` suite passes **257/257** with zero failures. The production `rac3Importer.importWorld()` contract was also invoked directly against retail Veldin, yielding a valid world with 735 authored Moby instances, 670 PVars, and the expected 1,960 TIE / 1,894 shrub placements.

This production importer is now a suitable foundation for RAC3 authored-gameplay/runtime work: an object can be selected by numeric class/instance identity while retaining its authored PVar and optional render model in the same neutral world representation.
