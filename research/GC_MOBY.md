# Going Commando moby models

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

Mobies are the dynamic objects — enemies, crates, the vendor, gadget pickups,
breakables, triggers. Oozla (`LEVEL1`): 748 moby instances, ~227 classes.
`packages/gc-moby` recovers the **high-LOD mesh at bind pose** — the rigid
props directly, and the skinned classes by walking the skeleton and simulating
the PS2 VU0 bone-blend loop (see *Skinning*). Animation frames are not applied.

Reproduce: `node tools/gc-world.mjs "<GC iso>" --level 1 --no-collision --out captures/level1.world.json`

## Instances

Gameplay pointer `0x4c`: `MobyBlockHeader` (0x10) then `GcUyaMobyInstance` (0x88)
— `oClass` @ 0x28, `f32 scale` @ 0x2c, `Vec3f position` @ 0x40, `Vec3f rotation`
(XYZ euler radians) @ 0x4c. `size` @ 0x00 is always `0x88`. See
[`GC_SHRUBS_MOBIES.md`](GC_SHRUBS_MOBIES.md).

## Class geometry

`LevelCoreHeader.mobyClasses` → `MobyClassEntry` (0x20): `s32 offset; s32 oClass;
s32; s32; u8 textures[16]`. Geometry at `assets[offset]`, to the next section
boundary.

```text
MobyClassHeader (0x48)
  0x00 s32 packetTableOffset
  0x04 u8 highLodCount; u8 lowLodCount; u8 metalCount; u8 metalBegin
  0x24 f32 scale
MobyPacketEntry (0x10), per packet
  0x00 u32 vifListOffset      0x04 u16 vifListSize (*0x10)
  0x08 u32 vertexOffset       0x0f u8 transferVertexCount
GcUyaDlVertexTableHeader (0x10)
  0x00 u16 matrixTransferCount   0x02 u16 twoWayBlend   0x04 u16 threeWayBlend
  0x06 u16 mainVertexCount       0x08 u16 duplicateVertexCount
  0x0c u16 vertexTableOffset
MobyVertex (0x10): u16 lowHalfword (bits 0..8 = vertex index); s16 x @ 0xa, y @ 0xc, z @ 0xe
```

Per **high-LOD packet**:

- **VIF list** (`class[vifListOffset .. +vifListSize*0x10]`): UNPACK `[0]` = STs
  (`s16 s, t` pairs), `[1]` = a 4-byte `MobyIndexHeader` then `s8` strip indices,
  `[2]` (optional) = `MobyTexturePrimitive[]` (0x40 each; `tex0.data_lo` @ +0x20,
  `super_secret_index` @ +0x0c/+0x1c/…).
- **Vertex table** (`class[vertexOffset ..]`): `mainCount + twoWay + threeWay`
  `MobyVertex`. Position = `(x, y, z) * scale / 1024`. The **low 9-bit strip
  vertex id** is software-pipelined `VERTEX_PIPELINE = 7` entries ahead
  (`rawIdx[i-7] = rawIdx[i]`). Full-retail GC/UYA matrix-state censuses reject
  applying that offset to the upper skin-control bits: skinning consumes those
  from the **current record**. `duplicateVertexCount` `u16 >> 7` entries follow
  the matrix transfers (aligned to 8) and copy an earlier vertex (by index) with
  a fresh ST.
- **Index walk** (Insomniac triangle strips): each `s8` index — `<= 0` twice in a
  row starts a new strip (seeded with the two flagged verts); a lone `<= 0` is a
  fan-pivot restart (the flagged vert is the pivot; the strip fans around it);
  an index of `0` reads the next "secret index" (texture switch, or `0` =
  end-of-packet: drop the last 3 in-flight indices). Strip index into the vertex
  list = `(index & 0x7f) - 1`. Confirmed sound on retail — props and the vendor
  decode cleanly; the residual spikes on skinned characters are bone binding.

Class-local material slots map to level `moby` texture ids through
`MobyClassEntry.textures[]` (`readGcLevelTextures(core, "moby")`).

**Texture state is GS-global and persists across every packet of a class.** A
packet emits an AD-GIF (a `MobyTexturePrimitive`, VIF unpack `[2]`) only when the
texture *changes*, so a packet — or a strip before the first AD-GIF — draws with
whatever `TEX0` the previous packet left set. The recovered `texture_index` is
therefore **one running value across the whole packet loop, initialised to `0`**,
updated only at AD-GIF events (`textures[ad_gif_index].d3_tex0_1.data_lo`).
`data_lo`: `>= 0` = class texture slot, `-1` = `MOBY_TEX_NONE`, `-2` = chrome,
`-3` = glass (facts from Wrench `moby_packet.h` / `recover_packets`). Resetting it
per packet (the OBP bug fixed 2026-09-07) drops the texture on strips with no
AD-GIF of their own — **~85 % of Tabora's moby triangles** (its dune floor ships
as one ~683k-tri moby), Oozla ~225k, Grelbin ~1.7M. After the fix Tabora is
99.4 % textured.

**Per-vertex normal — decoded 2026-09-07.** Mobies carry **no baked vertex
colour**; instead each `MobyVertex` stores a unit normal in spherical form:
`u8 azimuth @0x08`, `u8 elevation @0x09`, `angle = byte · π/128`,
`n = (sin az · cos el, cos az · cos el, sin el)` (native Z-up, not pipelined —
read directly like the position). Facts from Wrench `moby_vertex.h` /
`unpack_vertices`. `GcMoby.Mesh.Normals` (flat XYZ, one per position). Every
decoded normal is unit-length. `GcWorldImport` rotates it by the instance matrix
and turns it into a soft directional+fill vertex shade so an otherwise flat or
flat-UV moby surface still reads as 3-D (`RuntimeWorldScene` multiplies it in via
`VertexColorUseAsAlbedo`). The normals' exact bits are libm-dependent
(`sin`/`cos` differ by a ULP between C# and JS) so the moby equivalence test
checks them structurally (unit length) rather than by hash. Runtime dynamic
lighting of mobies from these normals is a later step.

## Skinning (bind pose)

A skinned vertex is stored in **its bone's local space**, so plotting it raw
collapses every limb toward the origin ("folded"). The bind pose is
`pos_model = Σ_k w_k · globalBind[joint_k] · pos_local`.

- **Skeleton — corrected 2026-09-08.** `MobyClassHeader` `s32 skeletonOffset
  @ 0x14`, one 0x40 matrix-like record per joint, plus `commonTransOffset @
  0x18`. The earlier claim that every GC 3x3 basis is identity is false at
  full-retail scale: GC contains 9,758 non-identity joint bases and UYA 19,657.
  A class-sphere census strongly selects the stored 3x3 basis transposed into
  OBP's column-vector convention plus native row-3 translation for a future
  static bind correction. Production static geometry deliberately retains the
  older translation-only path for census compatibility; animation preserves
  `common_trans.vector` separately and does not treat that shortcut as native
  truth. See `UYA_MOBY_ANIMATION.md`.
- **Per-vertex bone binding — pinned for the normal GC/UYA family.** The PS2
  VU0 matrix-slot machine has 64 slots whose contents **persist across packet
  boundaries**. Each packet's matrix transfers update that persistent state,
  then vertices execute in file order (`twoWay`, `threeWay`, `main`). Bytes 2–7
  are VU0 load/store addresses and fixed `/256` blend weights. Every observed
  normal-family two-way and three-way blend sums exactly to 256, and blend
  operations consume rigid matrix sources; ordinary vertices may reuse a
  cached 2/3-joint blend.
- **Skin-control upper bits are current-record, not 7-ahead.** Applying the
  seven-record pipeline to them was an old visual heuristic. Across all 51 UYA
  rows, current-record bits + persistent state gives zero invalid addresses,
  invalid joints or uninitialised reads; the 7-ahead interpretation produces
  hard contradictions. GC independently selects the same rule, apart from three
  transfers in known special `LEVEL21` oClass 2131 variant territory. The low
  9-bit strip vertex id remains independently seven-ahead.

**Static-render status:** the established GC bind-pose renderer remains a
compatibility path. It still uses spike pruning/folded fallback and its committed
world counts are intentionally unchanged by the 2026-09-08 animation work. The
newly pinned VU0 binding stream is stored separately for animation so a future
full-bind static migration can be reviewed with new retail-backed geometry
goldens instead of silently changing the existing world census.

**Animation-binding status:** the normal GC/UYA VU0 state machine is now pinned
strongly enough to fail closed on unresolved classes and to drive bounded
multi-joint UYA previews. Special format variants remain outside that admission.

## Verification (retail Oozla)

| | result |
|---|---|
| moby classes parsed | 180 / ~227 (47 have no asset offset or fail the strict parse) |
| classes with geometry | 172 / 180 |
| total class triangles | 85,061; **0 with an edge > 50 units** — no scrambled meshes |
| triangle-strip decode | confirmed sound — crates / vendor / props decode cleanly; the shared strip walk (two `s8 <= 0` in a row = new strip, a lone one = fan-pivot restart, `0` = secret-index / end) is correct |
| instances placed | 687 / 748 (61 fall on classless / trigger mobies) |
| instantiated triangles | 325,838 (was 326,422 — spike triangles pruned) |
| skinning | 37 classes have blend vertices; **36 un-fold and pass**, 1 (`oClass 2590`) stays folded |
| visual | crates, structures and props sit in the right places; the un-folded characters read as recognisable bipeds with correct boots / torso / arms / head |

## Animation (`MobySequence`)

`MobyClassHeader`: `u8 sequenceCount @ 0x0c`. Sequence offset list `s32[sequenceCount] @ class + 0x48` (right after the 0x48 header), each **relative to the class**.

`MobySequenceHeader` (at `class + offset`):

```text
0x00 Vec4f  boundingSphere
0x10 u8     frameCount
0x11 u8     soundCount        (0xff seen as a "none" sentinel)
0x12 u8     triggerCount
0x13 u8     unk
0x14 u32    triggersOffset
0x18 u32    animationInfoOffset
0x1c u32[frameCount] frameTable   — entry & 0x0fffffff = frame offset (relative to class);
                                     entry & 0xf0000000 = flags
```

`MobyFrameHeader` (0x10, at `class + frameOffset`):

```text
0x00 f32  speed              (frames advanced per 60 Hz tick; 0.5 is common)
0x04 u16  unk
0x06 u16  dataSizeQwords
0x08 u16  jointDataSize      (= jointCount * 8)
0x0a u16  thing1Count
0x0c u16  unkC
0x0e u16  thing2Count
```

Frame body at `frameOffset + 0x10`: **`s16[4]` quaternion per joint** (`x, y, z, w`,
each `/ 32768`), then `u64 thing1[thing1Count]`, then `u64 thing2[thing2Count]`.
The quaternions are unit length (confirmed against retail). Component order is
`(x, y, z, w)` — pinned by the 1-frame idle sequence of `oClass 1134`, whose
rotation matches the class's bind-pose skeleton twist (~4.4° about Z) only under
that order.

### Skeleton for animation (`readGcMobyJoints`)

> **2026-09-08 scope note:** the formulas below describe the retained legacy GC
> single-joint playback path. Evidence-safe multi-joint GC/UYA evaluation now
> lives in shared `OBP.PS2.Geometry.GcUyaMobyPose` and uses raw skin-local
> vertices + the separately pinned persistent/current-record binding stream. See
> [`UYA_MOBY_ANIMATION.md`](UYA_MOBY_ANIMATION.md) for the retail selection.

`MobyTrans` (`commonTransOffset`, 0x10 stride): `Vec3f vector; u16 parentByteOffset; u16`.
Parent joint index = `parentByteOffset / 0x40`; a self-reference marks a root.
`common_trans.vector` equals the 4th column of the row-major skeleton matrix
(`skeletonOffset + j*0x40`, translation at bytes 12/28/44) — the joint's bind
translation. But the **rest-pose decoder** subtracts the skeleton's *row 3*
(bytes 48/52/56), which for some classes (e.g. `oClass 1134`) is `(0,0,0)` while
`common_trans` is large. `MobyJoint.B{x,y,z}` therefore stores `-skeletonRow3` —
the exact value the decoder used — so an identity-quaternion pose reproduces
`mesh.positions` bit-for-bit (verified: max deviation ~1e-8).

### Pose evaluation (`OBP.RAC2.Animation.MobyAnimation`, engine-independent)

`bindGlobal[j] = -skeletonRow3[j]` (raw units → ×`scale/1024` for model units).
`localTrans[j] = bindGlobal[j] - bindGlobal[parent]`.
`animGlobal[j] = (R(q[j]) · T(localTrans[j])) · animGlobal[parent]` (row-vector).
`skin[j] = animGlobal[j] · T(-bindGlobal[j])` — standard linear blend skinning;
at identity every `skin[j]` is the identity so the rest pose is returned.
`Pose(mesh, joints, frame)` = `Σ wᵢ · rest · skin[jᵢ]` per vertex, using the
decoder's per-vertex `VertexJoints` / `VertexWeights` (from the VU0 skin sim,
normalised to sum 1).

**Status: playing.** Single-joint rigid classes reproduce their rest pose exactly
and play their retail sequences correctly (`oClass 1134` — a 170-frame spinner;
`oClass 2602` — a 200-frame full-revolution turret). `GcWorldImport` lifts up to
12 single-joint animated instances per level out of the merged static mesh into
`RuntimeWorld.AnimatedMeshes` (per-frame world-space positions, baked); the Godot
host CPU-swaps the vertex buffer each tick (`RuntimeWorldScene.AdvanceAnimated`).
GC production playback remains deliberately limited to its existing admitted
single-joint classes. The shared codec/evaluator now also supports bounded
multi-joint playback; UYA Veldin has the first promoted evidence-safe preview.

Equivalence: `GcLevelTests.Level1_MobySequencesMatchTypeScript` hashes joints +
frames against `reference-ts` (180 classes / 1929 joints / 6476 frames on Oozla).

## Limitations / next

1. **Static bind-pose migration** — the newer retail evidence selects a full
   non-identity skeleton basis for many classes, but production static geometry
   intentionally keeps the older census-compatible bind shortcut until new
   geometry goldens are reviewed separately.
2. **Broader multi-joint animation/state semantics** — the shared VU0 binding
   state is pinned for the normal GC/UYA family, but per-frame `thing1` / `thing2`
   channels and native sequence/state selection remain unresolved. Special
   format variants such as GC `LEVEL21` oClass 2131 remain fail-closed.
3. Low-LOD and metal packets, bangles, the corncob, per-instance pvars.
4. The ~47 classes that don't parse — likely a format variant / `force_rac1`
   (`MobyClassHeader` byte `0x0b` != 0).
5. `tools/gc-world.mjs` still drops a marker cube for moby instances whose class
   has no decoded mesh.
