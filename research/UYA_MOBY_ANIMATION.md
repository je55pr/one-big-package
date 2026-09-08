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

OBP currently promotes four Veldin table-1 classes whose selected clips have
zero authored-sphere escapes across every decoded frame. The admitted authored
instances are deliberately pinned rather than inferred from class alone:

- oClass 6800 / sequence 2: instances 665-668 (4);
- oClass 6577 / sequence 2: instances 513-515 (3);
- oClass 6317 / sequence 4: instances 477-479 (3);
- oClass 6886 / sequence 15: instances 672-699 (28).

That is **38 authored animated instances**, still as an OBP showcase choice rather
than a recovered native animation-state decision.

`GcUyaMoby` preserves the separately validated animation binding stream,
skin-local positions, native parent-relative translations and sequence spheres.
`GcUyaMobyPose` evaluates frames in `OBP.PS2`. `Rac3WorldImport` validates each
class's complete selected clip against its authored sphere before any instance of
that class crosses the neutral `RuntimeAnimatedMesh` bridge.

The established static UYA Moby renderer remains unchanged for everything else.
The 38 promoted `RuntimeDynamicObject`s retain identity and native/PVar payloads,
but their static render surfaces are suppressed to avoid double drawing. The
animation bridge carries **34,301 rendered triangles in 45 texture surfaces**.
Accounting remains exact: Veldin has 187,048 remaining dynamic triangles plus
34,301 animated triangles = the committed **221,349 Moby-triangle** census.

## Godot proof

The game-neutral Godot host was run with `rac3:TABLE1` and `--anim-solo` using
the pinned Godot 4.7.2 Mono toolchain. The expanded isolated scene contains all
**45 animated surfaces / 34,301 triangles** while the existing camera remains
tightly framed on the first nearby 6800 cluster. Deterministic captures at frame
arguments 120 and 136 have different SHA-256 values and **395,809 / 921,600**
pixels differ (~42.9%), with multiple authored objects visibly changing pose.

A separate `--anim-focus` integration run loads the complete reconstructed Veldin
world with the same expanded animation set: **925 mesh instances / 1,111,775
rendered triangles / 264,313 collision triangles**. Its player camera is a debug
harness rather than a curated showcase shot, but it proves the expanded animated
representation coexists with the complete world without changing the established
visible-triangle census.

## Remaining work

- Recover the extra frame channels well enough to expand admission beyond clips
  that are already sequence-sphere safe from quaternion data alone.
- Revisit the full static bind transform separately, with new retail-backed
  geometry goldens rather than silently changing the existing world census.
- Recover native UYA animation-state selection/update behaviour before claiming
  which sequences play during normal gameplay.
- Populate optional `RuntimeSkeleton` debug metadata only after its coordinate
  contract is pinned independently; CPU-baked frame positions remain authoritative.
