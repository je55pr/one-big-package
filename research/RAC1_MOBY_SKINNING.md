# Ratchet & Clank 1 Moby skinning state-machine census

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`), all 19 native levels. This note extends `RAC1_MOBY.md`. Public Wrench code was used only to suggest competing interpretations after the retail packet structure had already been recovered; the promoted rules below are selected by complete retail validation.

No retail payload bytes are committed.

## Why this checkpoint exists

The rigid Moby milestone established the R&C1 `0x20` vertex-table header, VIF/index packet family, seven-entry native vertex-id pipeline, and cross-packet 512-entry duplicate cache. Animated classes add a second persistent machine: a 64-slot VU0 matrix/blend cache filled by pre-loop transfers and updated while vertices are consumed.

One ambiguity mattered before a bind-pose decoder could be trusted: the `MobyVertex.lowHalfword` contains both the low 9-bit native vertex id and seven upper bits. The vertex id is known to be staged seven records ahead. It was not safe to assume the upper bits are staged the same way.

## Retail-selected VU0 interpretation

The complete animated high-LOD census selects **current-record upper bits**, not seven-ahead upper bits.

With the current record:

- all pre-loop transfer destinations are aligned and inside the 64-slot VU0 cache;
- all two-way / regular transferred joint ids are `< jointCount`;
- all matrix loads read initialized slots;
- no two-way blend loads an already-blended source;
- no three-way blend loads an already-blended source;
- no load/store-same-address exporter invariant is violated;
- every three-way third-source address is aligned/in-range and initialized.

Running the same retail packets while taking the upper bits seven records ahead introduces **20 hard state-machine violations** (`13` invalid/unaligned third-source addresses and `7` three-way reads from an already-blended source).

Therefore only the **low 9-bit vertex id** participates in the seven-entry pipeline. The current record's upper bits have operation-dependent meaning:

- two-way vertex: transferred SPR joint id;
- regular vertex: transferred SPR joint id;
- three-way vertex: half the third VU0 matrix-load address (`address = upperBits * 2`), **not a joint id**.

The last distinction also resolves 32 apparent `jointCount` overflows from an earlier naive census: all were three-way address fields and were never joint references.

## Complete animated geometry-bearing census

Across all 19 authority levels:

- geometry-bearing animated class occurrences: **1,407**;
- high-LOD packets: **16,963**;
- in-file vertices: **1,314,409**;
- pre-loop matrix transfers: **4,310**;
- two-way blend vertices: **47,245**;
- three-way blend vertices: **16,245**;
- regular vertices: **1,250,919**;
- two-way + three-way blend vertices: **63,490**;
- every blend-weight sum is exactly **256**;
- strict VU0 state-machine errors under the selected interpretation: **0**.

The 64-slot blend cache persists across packets, matching the already-proven cross-packet nature of Moby recovery state.

`research/generated/rac1-moby-skinning-census.json` contains per-level counts and the selected-versus-rejected interpretation summary.

## Geometry-free special classes

The wider `jointCount > 0` population contains another **38** payload occurrences: native class ids `1` and `2` on each of the 19 levels.

Both have:

- `jointCount = 20`;
- `packetTableOffset = 0`;
- `highLodPackets = 0`;
- no in-class skeleton/common-transform ranges.

They therefore explain every apparently missing animated skeleton/common-transform range in the all-level census and do **not** block mesh skinning. They are preserved as special geometry-free class payloads rather than treated as malformed animated meshes.

## Skeleton / bind-pose evidence

For geometry-bearing animated classes, R&C1 stores `jointCount` native `0x40` skeleton records and matching `0x10` common-transform records.

Direct observations already rule out reusing OBP's current GC translation-only bind shortcut:

- **5,384** joint records have a top-left diagonal differing from identity;
- **4,751** have non-zero off-diagonal rotation terms;
- `common_trans.vector` matches the translation carried with the skeleton record;
- `common_trans.parent_offset` is always 0x40-aligned, points to an earlier joint when nonzero, and is in-range;
- for ordinary rotated joints, the skeleton record's final XYZ vector matches the inverse-affine translation relationship `-R^T * T_global` to floating-point tolerance.

This strongly suggests the native skeleton carries real rotational inverse-bind information. Full bind-pose reconstruction remains a separate promotion step: classes with unusual zero final vectors and hierarchy details still need to be included in the complete geometry-bound validation before production code emits animated meshes.

## Production C# promotion

The retail-selected skin-state interpretation is now promoted into `OBP.RAC1.Geometry.Rac1Moby` without changing the already-validated base-surface positions.

Production C# now:

- preserves the 64-slot VU0 skin/blend cache across high-LOD packets;
- decodes current-record upper bits with the operation-dependent meanings established above;
- emits three joint indices plus normalized weights for every emitted vertex of geometry-bearing animated classes;
- carries those bindings through the same persistent native vertex cache used by cross-packet duplicate emissions;
- exposes the matching native `0x40 * jointCount` skeleton records and `0x10 * jointCount` common-transform records, preserving each 4x4 matrix verbatim plus the aligned parent byte offset / record index;
- deliberately does **not** apply those skeleton matrices to the bind/rest surface yet.

The permanent retail-gated C# test now reproduces the complete animated census exactly: 1,407 geometry-bearing animated class occurrences, 16,963 packets, 1,314,409 in-file vertices, 4,310 pre-loop transfers, 47,245 two-way vertices, 16,245 three-way vertices, and 38 geometry-free special occurrences. It also checks that every emitted animated vertex has a normalized binding whose nonzero joint references are inside the class joint table. The full local retail-authority suite passes with all three supported authority ISOs.

This checkpoint is intentionally animation-ready rather than animation-complete. The remaining blocker is the semantic interpretation of the non-identity R&C1 skeleton matrices and animation-frame data; those transforms must be retail-validated before production code deforms the recovered base surface.

## R&C1 -> GC evolution implication

At the packet skin-state-machine layer, the current evidence supports a shared lineage: pre-loop transfers, 64 VU0 slots, two-way/three-way blending and exact 8-bit weights all match the later machinery conceptually. R&C1's already-established `8*u32` vertex-table header remains a separate generation.

Do not infer that the **skeleton bind representation** is interchangeable with the current GC importer. R&C1 retail contains widespread non-identity skeleton rotations, so bind-pose reconstruction must be validated independently.
