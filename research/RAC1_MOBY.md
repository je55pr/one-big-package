# Ratchet & Clank 1 Moby archaeology

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`).

This note records only structures checked directly against the retail input. Public tooling is used as corroboration and terminology, not as authority. No retail payload bytes are stored in the repository.

## Native class table

The R&C1 core-index ArrayRange at `0x18` is the dynamic/Moby class table. Its entries are `0x20` bytes and retain the same positional class-library shape already preserved by `rac1-level-classes`:

- `+0x00 s32` asset offset in decompressed core data
- `+0x04 s32` native class id (`oClass`)
- `+0x08/+0x0c` unknown words (preserved)
- `+0x10..+0x1f` sixteen class-local -> level Moby texture ids

Across the 19 authority levels there are 3,641 table entries, of which 2,968 have an asset payload. The remaining 673 zero-offset entries are retained in the table census rather than fabricated as meshes.

## Class header and RAC1 packet generation

A payload begins with the shared-family `0x48` Moby class header. The high-LOD packet table still uses `0x10` packet entries, and the packet's VIF grammar is the same family used by later games:

- ST: `V2_16`
- index buffer: `V4_8`
- optional texture/GIF data: `V4_32`

The important R&C1 generation boundary is the **vertex-table header**. R&C1 uses eight little-endian `u32` words (`0x20` bytes):

| offset | observed role |
|---:|---|
| `0x00` | matrix-transfer count |
| `0x04` | two-way blend vertex count |
| `0x08` | three-way blend vertex count |
| `0x0c` | main vertex count |
| `0x10` | duplicate vertex count |
| `0x14` | total transfer vertex count |
| `0x18` | vertex table offset |
| `0x1c` | vertex-tail boundary |

Going Commando/UYA compress the corresponding fields into a `0x10` header of `u16` values. This is why the existing GC Moby parser must not be applied directly to R&C1.

All 22,227 authority high-LOD packets satisfy all of the following with zero exceptions:

- packet VIF and vertex ranges are in-class;
- UNPACK shape is exactly `(V2_16,V4_8)` or `(V2_16,V4_8,V4_32)`;
- index-header padding byte is zero;
- header transfer count equals the packet entry transfer count;
- transfer count equals `twoWay + threeWay + main + duplicate`;
- ST count equals transfer count;
- packet derived bytes match `(0xf + transfer*6)/0x10` and `(3 + transfer)/4`;
- matrix-transfer + duplicate prelude ends before the vertex table;
- vertex-tail boundary is aligned and produces fewer than seven pipeline epilogue vertices.

The class byte at header `0x0b` is **not** used as an R&C1 format discriminator. `0xff` dominates retail data (2,928 payload classes), but other values are present. Game/build identity plus the directly observed `0x20` vertex header is the evidence-backed discriminator.

## Seven-entry vertex pipeline and cross-packet cache

Moby vertex ids are software-pipelined seven entries ahead. R&C1 also carries the pending tail ids through padding/epilogue vertex words defined by the `0x1c` boundary.

Duplicate vertices are not necessarily references to the current packet. The native/Wrench recovery model keeps a 512-entry vertex cache across high-LOD packets of the class. Retail R&C1 directly requires this: treating the cache as packet-local produces unresolved duplicate ids, while preserving it across packets resolves the complete 2,968-payload bind-pose census without exceptions.

This cache is a geometry dependency only. It does not justify merging packet-local unknown state or inventing animation semantics.

## Bind/rest-pose high-LOD milestone

The packet geometry itself does **not** require animation skinning to recover the base surface. Direct retail decoding of all payload classes uses the same scaled native positions (`s16 xyz * classScale / 1024`), seven-entry vertex-id pipeline, cross-packet duplicate cache and strip/material walk. `jointCount` changes the animation metadata/state machine, not the base-position decode.

Complete authority census across all 19 levels:

- payload class occurrences: **2,968 / 2,968 decoded**
- high-LOD packets: **22,227 / 22,227 decoded**
- recovered vertices (including duplicate-cache emissions): **1,920,636**
- recovered triangles: **1,933,983**
- packets containing two-way or three-way blend vertices: **6,407**
- decode/index/material errors: **0**
- geometry-free payload occurrences: **38** — exactly native class ids `1` and `2` on each of the 19 levels; both have `jointCount=20` but zero high-LOD packets

The earlier rigid-only milestone remains useful as an independent scale check: all 1,523 rigid payload classes decode with a maximum axis-extent / native radius of `2.0000004768`. Animated class spheres are **not** treated as a universal hard geometry bound: most classes remain near the same diameter relationship, but a small set of valid animated base meshes exceed it. Topology, packet structure, duplicate-cache closure and material ranges remain zero-error for those classes.

Independent public corroboration is consistent with this separation. noclip.website's R&C renderer uploads `classScale / 1024 * rawVertex.xyz` directly for Moby geometry and applies only the per-instance transform in its renderer; it does not pre-skin the base vertex positions. This is corroboration only—the promotion here is based on the complete retail packet/topology census.

`rac1-moby` therefore exposes a general **bind/rest-pose** decoder for both rigid and animated classes. The older rigid-only entry points remain as compatibility/safety wrappers for consumers that deliberately do not want animated class ids.

Generated per-level counts are in `research/generated/rac1-moby-bind-pose-census.json`.

## Packed placement -> runtime transform

The gameplay Moby placement record is **0x78 bytes**. It is not the live Moby structure used by the renderer. The retail placement parser directly recovers:

- `+0x18 s32` native `oClass`
- `+0x1c f32` uniform instance scale
- `+0x30..+0x38` native XYZ position
- `+0x3c..+0x44` native XYZ Euler radians

Executable archaeology independently identifies the live Moby as a **0x100-byte runtime record**. On the render/update path the supported retail executable uses, among other fields:

- runtime `+0x10`: position
- runtime `+0x2c`: uniform scale
- runtime `+0xc0/+0xd0/+0xe0`: three basis/scale vectors
- runtime `+0xf0`: rotation state in radians

The routine around `0x0020def0..0x0020dffc` reads the runtime rotation state and writes the three consecutive basis vectors through COP2/VU stores, then uses the position while updating the runtime bounding state. This pins the important semantic relationship directly from retail: the Moby Euler state drives the runtime basis construction. It also corrected an earlier lightweight-disassembler ambiguity: the relevant `0xfb...` stores are COP2 `SQC2`, not ordinary GPR `sq` instructions.

The exact Euler composition is corroborated by pinned Wrench commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`: column-vector `T * S * Rz * Ry * Rx`, so points encounter `Rx`, then `Ry`, then `Rz`. OBP keeps that provenance distinction explicit rather than presenting the public implementation as retail authority.

A complete authority validation applies that candidate transform to every geometry-bearing rigid placement on all 19 retail levels:

- native Moby placements: **16,232**
- geometry-bearing rigid placements: **9,122**
- animated placements: **6,237**
- placements whose class entry has no payload available to this geometry path: **873**
- distinct referenced rigid classes: **400**
- transformed rigid vertices: **3,215,117**
- non-finite transformed vertices: **0**
- negative instance scales: **0**
- zero instance scales: **0**
- rigid placement centres inside reconstructed terrain/collision bounds: **9,042 / 9,122**
- rigid geometry AABBs intersecting those static bounds: **9,053 / 9,122**
- maximum absolute instance scale: **14.654263496398926**

Native class-sphere containment is retained as a diagnostic rather than used to choose Euler order: a uniform scale and a rotation preserve distance from the sphere centre, so that test cannot discriminate among rotation orders. The largest observed excess is only `0.0031010601` world units (`0.0004217452` relative) on level 5, class 810, and is preserved in the generated diagnostic instead of hidden behind an arbitrary tolerance.

`rac1-instances` therefore exposes `transformRac1MobyPoint` for the promoted native point transform. Native RC is Z-up; neutral OBP is Y-up with coordinate basis change `C:(x,y,z)->(x,z,y)`. The equivalent neutral rotation is therefore `C * Rnative * C`. `rac1MobyRotationToObpEuler` performs this basis conversion and decomposes back into OBP's explicitly documented column-vector XYZ convention (`Rz * Ry * Rx`). A deterministic unit test proves that `native transform -> Y/Z swap` and `Y/Z-swapped local point -> converted OBP Euler` produce the same point.

`rac1-world` now emits each parsed Moby placement as an `OBPInstance` with native `oClass` preserved as `sourceClass`, true Y-up position, equivalent Y-up Euler and uniform scale. **This does not yet bind a decoded class mesh to the instance.** OBP has no neutral model/prototype asset relation today, and putting class definitions in `world.meshes` would incorrectly render those definitions at world origin. Class geometry linkage therefore remains deliberately deferred rather than encoded as an importer-private `meshId` convention.

The reproducible retail validator is `reference-ts/tools/rac1-moby-transform-validation.mjs`; its local output is written outside the repository and contains no committed retail payload.

## Animated skin-state archaeology

Animation remains a separate layer from base mesh recovery. Retail validation across all geometry-bearing animated classes has already pinned the VU0 blend-state interpretation:

- 1,407 geometry-bearing animated class occurrences;
- 16,963 animated high-LOD packets;
- 1,314,409 in-file animated vertices;
- 47,245 two-way and 16,245 three-way blend vertices;
- every blend weight sum is exactly 256;
- only the low 9-bit native vertex id is seven-entry pipelined; the upper seven bits belong to the current record;
- on three-way vertices those upper bits encode half a VU0 matrix address, not a joint id;
- zero uninitialised loads, invalid VU0 addresses, blend-of-blend inputs or real joint-id range failures under that interpretation.

The `0x40 * jointCount` skeleton and `common_trans` hierarchy are still being classified for animation. In particular, the skeleton 3x3 may include scale as well as rotation, so OBP does not yet synthesize animation transforms from it. See `research/RAC1_MOBY_SKINNING.md`.

## Evolution classification

| concept | R&C1 -> GC classification |
|---|---|
| core Moby class table entry | closely shared |
| `0x48` class header family | shared/extended family |
| `0x10` packet entry | shared |
| ST/index/texture VIF grammar | shared |
| seven-entry vertex-id pipeline | shared |
| cross-packet duplicate cache | shared |
| vertex-table header | **redesigned** (`8*u32` -> compact `8*u16`) |
| packed-placement/runtime representation | distinct (`0x78` disc placement -> `0x100` live Moby) |
| animated/skinning semantics | related, not yet claimed binary-identical |

Generated per-level counts are in `research/generated/rac1-moby-census.json`.

## Public corroboration snapshot

Compared against Wrench commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb` after the retail structures above were identified. Its `MobyFormat::RAC1`, `RacVertexTableHeader`, packet reader, cross-packet vertex-cache logic and placement transform convention agree with the direct retail observations where they overlap. Wrench is corroboration; parser assertions and promotion decisions remain grounded in the R&C1 authority census and executable archaeology described above.
