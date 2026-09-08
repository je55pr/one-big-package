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

For geometry-bearing animated classes, R&C1 stores `jointCount` native `0x40` skeleton records and matching `0x10` common-transform records. The `0x40` record is not a conventional homogeneous 4x4 matrix: its first 15 words are finite floats, while the raw word at `+0x3c` equals `(common_trans.raw_0x0e << 16) | common_trans.parent_offset` for **28,193 / 28,193** decoded joints. Production therefore models it as 15 affine floats plus packed metadata rather than assigning matrix-W semantics to the last word.

Direct observations rule out reusing OBP's current GC translation-only bind shortcut:

- **5,384** joint records have a top-left diagonal differing from identity;
- **4,751** have non-zero off-diagonal rotation terms;
- `common_trans.parent_offset` is always 0x40-aligned, points to an earlier joint when nonzero, and is in-range;
- `common_trans.vector` behaves as a hierarchy-local offset on ordinary chains, while the skeleton tail carries accumulated/inverse-bind information;
- simple two-joint specimens satisfy the inverse-affine relationship exactly, but one global hierarchy formula does not yet explain every rotated/scaled branch;
- the remaining `common_trans +0x0e` field uses only `0x0000` and `0x7000` in the authority census and is retained raw because both values occur on rigid and non-rigid joints.

A systematic convention sweep found a strongest `common_trans` hierarchy recurrence covering **25,509 / 26,786** non-root joints, but the exceptions include scaled/sheared and special skeleton branches. That recurrence is retained as negative archaeology only: production does not use `common_trans` as pose authority. The later bind-pivot derivation below solves the proven rigid subset without relying on it.

## Sequence/frame structure and first safe pose subset

The authority build uses the RAC1/GC/UYA sequence-container lineage, but this checkpoint validates the R&C1 bytes independently rather than borrowing later-game field semantics. Across all 19 levels production decoding sees:

- **13,697** sequence slots;
- **10,745** present sequences and **2,952** null slots;
- **109,156** ordinary frames;
- **3,110,018** joint quaternion records, all unit-length to retail quantisation tolerance;
- **zero** frame-table entries with the high-nibble special/compressed flag used by generic cross-game tooling;
- every ordinary frame has `joint_data_size == jointCount * 8`;
- every frame body size exactly equals the 16-byte-padded size of joint data plus the two counted 8-byte payload arrays;
- **1,526** present sequence slots belong to `jointCount == 0` classes, proving sequence/state data is broader than skeletal animation.

Animation timing is now pinned independently from retail data and executable behaviour. Frame `+0x04` is a nondecreasing native timestamp and frame `+0x00` is the transition rate to the next timestamp. Across **98,411 / 98,411** adjacent frame pairs, the stored IEEE bits equal `8.0f / (next_timestamp - current_timestamp)` exactly. The five formerly suspicious non-finite values are all `+infinity` and correspond exactly to the five zero-delta timestamp pairs. No timestamp moves backwards.

Sequence `+0x18` is the constant-rate fast path. All **586** sequences whose frame rates vary have sequence `+0x18 == 0`. Retail executable routine `0x20d580` selects the sequence `+0x18` rate when nonzero and otherwise loads frame `+0x00`; the ordinary live-Moby update path then advances the interpolation accumulator at Moby state `+0x54` by that selected rate and stores the selected rate at `+0x5c`. The global Moby update loop calls this animation updater for eligible live objects. For the NTSC-U 60 Hz authority path, class `1134` sequence 1 therefore advances at `0.5 * 60 = 30` source frames per second.

A conservative first pose path is nevertheless proven for a large one-joint subset. Of 389 one-joint geometry-bearing class occurrences, 377 first frames cancel the stored rigid inverse-bind orientation to retail quaternion tolerance. Nine of the twelve exceptions have non-rigid scale in the skeleton, and the remaining three are occurrences of special class `66`. Production `Rac1MobyPose` therefore accepts only one-joint, rigid, zero-tail classes and provides an explicit rest-anchor check. Level 1 class `1134` is the permanent specimen: sequence 0 reproduces the stored rest surface within `2e-5` native world units, while sequence 1 has 170 frames and its first frame is visibly distinct from rest.

The native timing clock is promoted for the proven constant-rate runtime specimens. General variable-rate runtime scheduling remains a separate milestone. General non-rigid scale/shear also remains gated; the dedicated Ratchet positive-leaf static-stretch subset is independently proved below.

Rigid multi-joint deformation is now independently pinned. Across all 19 authority levels, **286 multi-joint class occurrences** have a one-frame sequence 0 where every frame quaternion cancels that joint's stored rigid inverse-bind 3x3. All 286, covering **206,213 vertices**, reproduce the decoded retail rest surface within `0.002` world units (observed worst error `0.00109782`) when evaluated by the production hierarchy path. This supports treating the frame quaternions as **global joint orientations for this proven rigid subset**, rather than importing GC's local-rotation hierarchy semantics.

The rigid hierarchy derives each bind pivot directly from the native inverse-bind linear block and tail, derives child-local offsets from those bind pivots, then evaluates each influence as `posed = frameRotation * (inverseBind * rest + tail) + animatedPivot`. The existing exact 1/2/3-way skin weights are then combined by ordinary linear blend skinning. `common_trans` is not used as pose authority because candidate recurrence interpretations do not hold universally across retail.

The moving population also survives a full stress census: **35 class occurrences / 20 distinct classes / 130 multi-frame sequences / 3,630 frames**, spanning **2 to 38 joints**, all pose to finite geometry; the worst posed extent is only **1.25558x** the corresponding rest extent. Level 2 class `766` is the first runtime specimen: 4 joints, 16 frames, constant rate `0.25` = **15 FPS** on the NTSC-U 60 Hz update path, and 54 placements. Those placements contribute 5,184 animated triangles while preserving the previous level total exactly. Deterministic Godot captures of the first instance at capture frames 10 and 40 use the same camera and differ across **81,172 pixels (8.81%)**, providing a visible multi-bone playback proof.

## Dedicated Ratchet player animation

Retail class `0` is now identified as the Ratchet player body by converging structure across every authority level. It is present on all 19 levels with an identical **111-joint / 5,583-vertex / 6,856-triangle** mesh, exactly one placement per level, and that placement is always Moby instance `0`. The class advertises 134 ordinary Moby sequence slots but every class-local sequence pointer is null.

The missing player animations live in a separate 256-slot table referenced by level-core header `+0x78`. On Veldin exactly slots `0..133` are populated, matching class 0's 134 null slots. Across all 19 levels the dedicated decoder sees **1,804 present sequences / 33,977 frames / 3,771,447 joint quaternions**. Every frame is the ordinary 111-joint format (`888 = 111 * 8` joint bytes), with zero special-frame flags or joint-size mismatches. Timing uses the same native equation as ordinary Mobies: **32,173 / 32,173** adjacent pairs match exactly, and all **219** variable-rate sequences use a zero constant-rate field.

Ratchet uses a distinct hierarchy convention from the ordinary rigid Moby subset. Retail rest-anchor evidence selects `globalOrientation[j] = localQuaternion[j] * globalOrientation[parent]`. Class 0 has 109 rigid joints plus two positive-determinant non-rigid leaf joints (22 and 24). Their scale/shear is carried as static bind stretch while animation supplies rotation. That interpretation is independently supported by **35** ordinary retail class occurrences / **210** known anchor frames whose only non-rigid joints are positive-determinant leaves; the static-stretch factorization reconstructs those anchors with observed worst matrix error `2.22e-16`.

Ratchet dedicated sequence `122`, frame 0 is the all-level bind anchor. Production weighted skinning reconstructs all 5,583 Veldin vertices with observed max error **0.000183594** world units; frame 1 moves the mesh by more than **0.0474** world units. A read-only PCSX2/PINE trace of normal untouched gameplay then pins the neutral selector: the active class-0 Moby cycles **sequence 0 -> 2 -> 0 -> 1** while standing still. Sequence 0 is the 10-frame standing loop, constant rate `0.125` = **7.5 FPS** on NTSC-U; sequences 1 and 2 are 77-frame variable-rate delayed idle/fidget variants. The live runtime's patched Ratchet sequence pointers map directly to the dedicated core slots, so these ids are not an OBP naming convention.

The first runtime promotion deliberately plays only sequence 0. Ratchet is emitted as four animated texture surfaces (5,356 + 966 + 404 + 130 = **6,856 triangles**) at 7.5 FPS and removed from the welded static Moby path, preserving each level's combined triangle total. Delayed idle selection between sequences 1/2 remains future gameplay-state work rather than being invented in the importer. Deterministic Godot captures at capture frames 10 and 70 use the same camera and advance the Ratchet surfaces from source frame 2 to source frame 8; **82,343 pixels (8.93%)** differ between the two 1280x720 captures.

## Production C# promotion

The retail-selected skin-state interpretation is now promoted into `OBP.RAC1.Geometry.Rac1Moby` without changing the already-validated base-surface positions.

Production C# now:

- preserves the 64-slot VU0 skin/blend cache across high-LOD packets;
- decodes current-record upper bits with the operation-dependent meanings established above;
- emits three joint indices plus normalized weights for every emitted vertex of geometry-bearing animated classes;
- carries those bindings through the same persistent native vertex cache used by cross-packet duplicate emissions;
- exposes the matching native `0x40 * jointCount` skeleton records and `0x10 * jointCount` common-transform records as 15 affine floats plus the proven packed metadata word, aligned parent byte offset / record index, local vector and raw `+0x0e` field;
- applies the retail-pinned rigid multi-joint hierarchy when every inverse-bind 3x3 is rigid and the frame provides the complete joint quaternion set;
- decodes the dedicated 256-slot Ratchet table and evaluates class 0 with parent-composed local quaternions plus the separately proven static-stretch rule for positive-determinant non-rigid leaf joints.

The permanent retail-gated C# test now reproduces the complete animated census exactly: 1,407 geometry-bearing animated class occurrences, 16,963 packets, 1,314,409 in-file vertices, 4,310 pre-loop transfers, 47,245 two-way vertices, 16,245 three-way vertices, and 38 geometry-free special occurrences. It also checks that every emitted animated vertex has a normalized binding whose nonzero joint references are inside the class joint table. The full local retail-authority suite passes with all three supported authority ISOs.

The rigid hierarchy and dedicated Ratchet player hierarchy are now animation-capable and retail-validated for their bounded populations. The remaining transform blocker is the wider non-rigid subset: R&C1 contains meaningful scale/shear beyond Ratchet's proven leaf-only case, and those semantics must still be independently recovered before production generalizes that capability.

## R&C1 -> GC evolution implication

At the packet skin-state-machine layer, the current evidence supports a shared lineage: pre-loop transfers, 64 VU0 slots, two-way/three-way blending and exact 8-bit weights all match the later machinery conceptually. R&C1's already-established `8*u32` vertex-table header remains a separate generation.

Do not infer that the **skeleton bind representation** is interchangeable with the current GC importer. R&C1 retail contains widespread non-identity skeleton rotations, so bind-pose reconstruction must be validated independently.
