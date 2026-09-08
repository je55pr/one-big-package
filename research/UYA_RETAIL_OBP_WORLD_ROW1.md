# Retail UYA neutral `OBPWorld` reconstruction — table row 1

This note records successful construction and deterministic viewer capture of a neutral OBP world directly from canonical retail **Ratchet & Clank: Up Your Arsenal** NTSC-U bytes, including the current evidence-backed Moby render subset, level atmosphere and layered UYA sky.

The input authority is `SCUS-97353`. The source first passes the pinned 2 MiB retail identity window at LBA 1001 (`a9e3e338df29045222ab0c0c0ba68fe1cff2048add582124f51915bb4ba55e5a`). The hidden-ToC address and public-derived semantic labels remain provenance hypotheses; this result does not claim native-loader proof for them.

## Reconstruction path

`tools/uya-world.mjs` composes independently tested retail compatibility results into the shared OBP representation:

1. public-derived UYA table row → bounded main level payload;
2. exact 0x60 / 0x58 / 0xbc candidate headers retained through the UYA evidence path;
3. strict existing WAD-LZ decoder → core assets;
4. unchanged GC tfrag/VIF reader → terrain LOD0 geometry;
5. unchanged GC texture reader → tfrag, Moby, TIE and shrub textures;
6. unchanged GC TIE/shrub/Moby class readers → class geometry;
7. unchanged GC gameplay instance reader → retail placements;
8. TIE/shrub geometry flattened through exact retail matrices; evidence-backed Moby geometry flattened through retail position/rotation/scale;
9. native Z-up geometry normalized to neutral OBP Y-up;
10. unchanged shared RC collision reader → neutral collision mesh;
11. retail-censused GC/UYA `0x5c` level-settings first part → background/fog/death-height/spherical-world environment fields;
12. strict UYA-versioned sky reader → layered initial-pose sky geometry and native sky textures;
13. shared `validateWorld()` validates the finished `OBPWorld` before output.

The builder refuses core decode warnings, unexpected missing classes, non-finite geometry/transforms and non-sentinel out-of-range texture references. Valid zero-local-core Moby entries, decoded empty Moby classes and unsupported bind-pose cases are classified explicitly rather than invented or silently promoted.

UYA sky rotation/angular velocity and bloom are retained as source evidence. The neutral viewer currently renders the retail initial pose; it does not invent sky animation or bloom behaviour.

## Current retail row-1 result

Pipeline `2827490869` / iid **527**, job `16353465532`, ran entirely on the self-hosted `Jess-Laptop` runner and passed through actual Chrome/WebGL2 capture.

Native/reconstructed content:

| component | result |
|---|---:|
| tfrags | 802 |
| tfrag source vertices | 49,775 |
| tfrag triangles | 44,280 |
| tfrag textures | 109 |
| TIE classes | 78 |
| TIE textures | 166 |
| TIE instances placed | 1,960 / 1,960 |
| expanded TIE triangles | 432,246 |
| shrub classes | 22 |
| shrub textures | 55 |
| shrub instances placed | 1,894 / 1,894 |
| expanded shrub triangles | 409,552 |
| Moby textures | 143 |
| Moby declared classes | 227 |
| Moby local-core classes decoded | 151 / 151 |
| Moby gameplay instances decoded | 735 / 735 |
| Moby instances rendered | 427 / 735 |
| expanded Moby triangles | 221,349 |
| zero-local-core Moby instances intentionally skipped | 307 |
| empty-geometry Moby instances intentionally skipped | 1 |
| unresolved-skinning Moby instances skipped | 0 |
| sky textures | 10 |
| sky shells | 8 |
| sky clusters | 181 |
| sky vertices | 4,111 |
| sky triangles | 4,348 |
| sky shells with native motion | 4 |
| sky shells with bloom flag | 0 |
| collision octants | 21,510 |
| collision vertices | 306,015 |
| collision triangles | 264,313 |

Finished `OBPWorld` statistics:

- render meshes: **398**
- render triangles: **1,111,775**
- collision meshes: **1**
- collision triangles: **264,313**
- neutral-world bounds (sky intentionally excluded from scene bounds):
  - min `(-212.80, -140.58, -467.91)`
  - max `(868.77, 197.99, 867.61)`

### Row-1 retail atmosphere

The sampled UYA sky header's base colour is zero; the independently retail-censused level-settings block supplies the visible environment instead:

- background RGB raw: **`128,128,128`**;
- fog RGB raw: **`125,115,80`**;
- fog near/far: **51,200 / 204,800** source units;
- fog near/far intensity: **255 / 140.25**;
- death height: **60**;
- spherical world: **false**;
- settings first-part offset: **208**;
- settings first-part SHA-256: `ee1af5460d97f904da9e356790a37528fbe51d9d4e252023ea095530cf41f03f`.

The viewer retains the raw environment values in the neutral world. Its current `1024` fog-distance divisor remains an explicitly provisional render interpretation.

### Row-1 sky evidence

The UYA sky section begins at decoded core-asset offset **2,731,712**, is **485,248 bytes**, and hashes to:

`825afb60c18db867575bbaaa2ebe4bc911f242ad815e65c74d4d64cc29b98fb0`

Four sky shells carry non-zero native Z angular velocities: **5, 4, 3 and 2** respectively. These are preserved in the generated mesh source notes/summary but not animated by the current viewer.

Exact retained core evidence:

- coreIndex SHA-256: `8e9b46845270a7678b473b621bf66fde0d752f7b0d921ace897a85981016de2c`
- decoded core assets SHA-256: `d8f6ebde468c115a9b6bf48d3c5723f03cea552cae192447dba919c426acd7f7`
- Moby class-geometry SHA-256: `822c3fed6c23eefd1859ea28eaa28b7ba130406d79246ed8212e02d2beef5319`
- Moby placement SHA-256: `064c82f80b17c6757e3928628fe3a13b0f0f8d771f863b4a08c9d76d1ba064a2`

Generated local world file:

- path: `C:\ChatGPT\OBP-Reports\uya-table1.world.json`
- byte length: **71,074,854**
- SHA-256: `f63e9f5f6a407180b879ae0565f6c7b6d39a197c24a05d771a806c10d1024867`

The generated JSON is intentionally not committed; it is a deterministic derived artifact containing embedded native texture data.

## Viewer/capture proof

The same pipeline loaded the atmosphere/sky-inclusive world through the actual OBP viewer under the Windows service runner.

Capture facts:

- browser: machine-wide Google Chrome;
- WebGL2: **available**;
- renderer: ANGLE / Vulkan / **SwiftShader**;
- viewer canvas: **1249 × 625**;
- collision overlay disabled for the image after the full world loaded;
- viewer-reported render triangles: **1,111,775**;
- viewer-reported collision triangles: **264,313**;
- uninterpreted neutral instances: **0**;
- screenshot path: `C:\ChatGPT\OBP-Reports\uya-table1-screenshot.jpg`;
- screenshot byte length: **51,870**;
- screenshot SHA-256: `3bdfa0a4f564fe74433236996ae145824720ec6ee6a9895df2d038277d727f87`.

The previous Moby-inclusive but skyless screenshot was 29,935 bytes / `b9912f9e...`; the new atmosphere/sky capture is a distinct deterministic visual milestone and is retained as a normal GitLab CI artifact.

## Current boundary

This is a genuine **retail-generated neutral world candidate**, not yet a complete UYA planet importer. It now includes terrain, tfrag/TIE/shrub/Moby textures, TIE/shrub static placement, the conservative level-local Moby render subset, collision, level atmosphere and layered sky geometry/textures.

Still omitted or deliberately conservative:

- semantics/geometry behind zero-local-core Mobies;
- unsupported Moby bind-pose cases;
- native sky animation and bloom rendering;
- later level-settings blocks;
- gameplay semantics;
- splines/volumes/spawns;
- native-loader provenance for the hidden table.

Static placement matrices are currently baked into render geometry, matching the established GC viewer pipeline. `OBPInstance` presently stores decomposed position/rotation/scale rather than a lossless native matrix, so exact native TIE/shrub placement matrices remain separately evidenced by `UYA_RETAIL_INSTANCE_COMPATIBILITY.md` rather than being lossy-converted into `world.instances`.

Generality is tracked separately in `UYA_RETAIL_WORLD_RECONSTRUCTION_MULTIROW.md`; atmosphere and sky now follow the same evidence-first promotion rule as terrain/static/Moby/collision data.
