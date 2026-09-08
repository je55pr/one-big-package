# UYA Moby animation and shared GC/UYA skin-state evidence

**Authority:** UYA NTSC-U `SCUS-97353`, build `rac3-ntscu-original`.

This note records the retail evidence used to admit the first native multi-joint
UYA Moby animation into OBP. Going Commando NTSC-U v1.01 is used as an
independent cross-check where stated. Public Wrench source was useful as a
hypothesis/corroboration source, but retail data remains the selector.

The production milestone is intentionally narrow. It does **not** claim that OBP
has recovered UYA's native gameplay animation-state selection. OBP chooses one
retail sequence for one authored Veldin instance as an explicit showcase preview.

## VU0 matrix-slot state

The shared GC/UYA vertex format uses a 64-slot VU0 matrix cache. Four competing
interpretations were tested over all 51 observed UYA main-table rows:

| hypothesis | retail result |
|---|---|
| current-record skin bits + persistent cache | **0 hard state errors** |
| current-record skin bits + reset per packet | ~2.49M uninitialised reads |
| 7-ahead skin bits + persistent cache | 130 invalid addresses, 109 invalid joints, 88 uninitialised reads |
| 7-ahead skin bits + reset per packet | millions of uninitialised reads plus the invalid address/joint cases |

The winning UYA census covers **3,304 classes, 39,045 packets and 3,042,169
in-file vertices**. Matrix cache state persists across packet boundaries.

The old decoder applied the seven-record software-pipeline offset to the whole
`lowHalfword`. Retail disproves that for skinning: only the **low 9-bit strip
vertex id** is seven records ahead. The upper skin-control bits are consumed from
the **current** vertex record.

Every decoded two-way and three-way blend in the UYA corpus has an exact fixed
weight sum of **256**:

- 102,679 two-way blends;
- 26,201 three-way blends;
- zero bad sums.

A two-way or three-way blend never consumes an already blended cache entry as a
source. A normal vertex may consume a cached 2/3-joint blend. Therefore the
existing three-joint/three-weight runtime representation is sufficient; no
recursive or wider influence representation is needed.

Going Commando independently selects the same state model. Its only observed
current-record joint anomalies are three transfers in `LEVEL21` oClass 2131, a
known special/variant class (`0x0a = 0xff`, `0x0b = 0xff`), so OBP keeps such
unresolved classes outside multi-joint animation admission rather than inventing
exceptions.

## Skeleton and bind evidence

A full-retail census disproves the earlier assumption that the GC/UYA skeleton
3x3 basis is always identity: GC contains **9,758** non-identity joint bases and
UYA contains **19,657**.

As an independent static-geometry check, candidate transforms were applied to
stored skin-local vertices and scored against each class's authored bounding
sphere. The strongest convention uses the stored 3x3 basis transposed into
OBP's column-vector convention plus the native row-3 translation. It reduces
vertices outside the class sphere from **46.68% to 8.97% in GC** and from
**47.48% to 4.25% in UYA**; the median class maximum-radius ratio is ~0.98 in
both games.

That is strong evidence for a future static bind-pose correction, but it is
**not promoted by this milestone**. Changing the established static decoder also
changes spike-pruning/fallback decisions and therefore existing world censuses.
The production animation path instead preserves raw skin-local positions and the
retail-selected bindings alongside the old static render reconstruction.

## Multi-frame pose convention

Each `MobySequence` stores an authored bounding sphere. Candidate hierarchy /
quaternion conventions were evaluated by posing real sequence frames and asking
whether vertices remain inside that retail sphere.

Both games select the same transform family: positive `common_trans.vector`,
parent × local hierarchy composition, the transposed/native quaternion matrix
convention, then the **inverse global joint transform** applied to stored
skin-local vertices.

The all-retail comparison sampled **12,475 GC frames / 20,647,766 vertex
evaluations** and **24,160 UYA frames / 34,277,978 vertex evaluations**. This
convention beats all forward-transform and local×parent alternatives in both
games.

The residual all-corpus sphere escape rate is still non-zero (18.35% GC,
19.71% UYA), so OBP does not treat every decoded sequence as animation-safe.
Frame bodies carry additional `thing1` / `thing2` channels after the joint
quaternions; those remain unresolved and may matter to some classes/sequences.

## Veldin safe-clip census

All frames of every referenced multi-joint Veldin sequence were scored against
its own sequence sphere. Several clips are already clean using only the proved
joint-quaternion path:

| oClass / sequence | authored instances | joints | frames | max sphere ratio | sphere escapes | max motion |
|---|---:|---:|---:|---:|---:|---:|
| 6800 / 2 | 4 | 33 | 9 | 0.9513 | **0** | 2.7785 |
| 6577 / 2 | 3 | 32 | 11 | 0.6526 | **0** | 2.8167 |
| 6317 / 4 | 3 | 111 | 25 | 0.9934 | **0** | 1.8860 |
| 6886 / 15 | 28 | 22 | 8 | 0.9884 | **0** | 0.2100 |

The tempting 62-instance oClass 5821 is **not admitted**: even its best currently
decoded sequence retains measurable sphere escapes. That class remains an
archaeology target rather than being made visually plausible by relaxed limits.

## Production admission

OBP currently promotes only **Veldin table 1, oClass 6800, instance 665,
sequence 2**. This is an OBP showcase choice, not a recovered native state-machine
decision.

`GcUyaMoby` now preserves a separately validated animation binding stream,
skin-local positions, native parent-relative translations and sequence spheres.
`GcUyaMobyPose` evaluates frames in `OBP.PS2`. `Rac3WorldImport` validates every
frame against the authored sphere before exposing the preview through the neutral
`RuntimeAnimatedMesh` contract.

The established static UYA Moby renderer is unchanged. Instance 665 keeps its
`RuntimeDynamicObject` identity and native/PVar payloads but its two static render
surfaces are suppressed to avoid double drawing. The animation bridge carries
**2,390 rendered triangles in two texture surfaces** at **9 frames / 7.5 fps**.
Accounting remains exact: Veldin has 218,959 remaining dynamic triangles plus
2,390 animated triangles = the committed **221,349 Moby-triangle** census.

## Godot proof

The game-neutral Godot host was run with `rac3:TABLE1` and `--anim-solo` using
the pinned Godot 4.7.2 Mono toolchain. Two captures use the same isolated camera
and the same 2,390-triangle scene:

- capture frame argument 120: world animation clock 2.007 s, preview frame 6/9;
- capture frame argument 136: world animation clock 2.273 s, preview frame 8/9.

The PNGs have different SHA-256 values and 348,185 / 921,600 pixels differ
(~37.8%), while the camera and scene accounting remain unchanged. This proves
that the promoted UYA Moby is actually deforming through the existing neutral
animation host rather than merely carrying animation metadata.

## Remaining work

- Recover the extra frame channels well enough to expand admission beyond clips
  that are already sequence-sphere safe from quaternion data alone.
- Revisit the full static bind transform separately, with new retail-backed
  geometry goldens rather than silently changing the existing world census.
- Recover native UYA animation-state selection/update behaviour before claiming
  which sequences play during normal gameplay.
- Populate optional `RuntimeSkeleton` debug metadata only after its coordinate
  contract is pinned independently; CPU-baked frame positions remain authoritative.
