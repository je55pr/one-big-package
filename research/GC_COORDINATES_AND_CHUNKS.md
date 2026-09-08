# Going Commando coordinates and chunk-plane semantics

This note keeps the small part of the public archaeology that is still unresolved after the retail-backed GC world pipeline landed on `main`.

## Coordinate convention — established for current GC import

Retail geometry and collision are native **Z-up**. OBP normalises them to **Y-up** using:

```text
native (x, y, z) -> OBP (x, z, y)
```

That conversion is already exercised by the current GC collision/render pipeline. Keep native coordinates inside format readers where useful for provenance; perform the axis conversion at the normalisation boundary rather than rewriting packed-format semantics.

### Handedness — `(x, z, y)` is a reflection

**`(x, y, z) -> (x, z, y)` swaps two axes, so its determinant is `-1`: it is a
reflection, not a rotation.** Every level in "OBP space" is therefore a mirror
image of the real game (left/right flipped). This went unnoticed until the
migration put a controllable player in the level (`migration/godot-native-runtime`,
2026-09).

A determinant-`+1` map from native Z-up to OBP Y-up needs a swap **and** a sign,
e.g. `(x, y, z) -> (-x, z, y)` or `(x, y, z) -> (x, z, -y)`.

Current handling: the TS lineage and `GcWorldImport.World` keep the mirrored
`(x, z, y)` convention (so the equivalence tests and recorded baselines are
unchanged); the Godot adapter `OBP.Godot.GcWorldScene.ToScene` negates X to
un-mirror on the way into the engine. If the mirrored convention is ever fixed
at the source, drop that negation and re-baseline the coordinate assertions.

## Level-settings chunk planes — public layout, not yet authority-decoded

`packages/gc-level-settings` currently decodes the fixed `0x5c` GC/UYA/DL level-settings first part (background/fog, death height, spherical-world fields, ship position). Public implementations indicate that the following suffix begins with zero or more chunk-plane records.

Wrench layout:

```text
ChunkPlanePacked (0x20)
  0x00 f32 point.x
  0x04 f32 point.y
  0x08 f32 point.z
  0x0c s32 plane_count
  0x10 f32 normal.x
  0x14 f32 normal.y
  0x18 f32 normal.z
  0x1c u32 pad
```

The first record's `plane_count` determines how many `0x20` records belong to the list. Wrench writes that count into every emitted plane; noclip reads the first plane and uses its count to consume the rest.

For a point `p`, the natural signed-side test is:

```text
d = normal · (p - point)
```

## Public chunk-assignment rule

Both public implementations encode the same broad GC rule:

```text
positive side of plane 0 -> chunk 1
else positive side of plane 1 -> chunk 2
else -> chunk 0
```

But they disagree at exactly zero:

- Wrench: positive means `d > 0`
- noclip: positive means `d >= 0`

This probably affects few or no shipping instances, but OBP should not silently choose one and later treat it as native truth.

## Recommended implementation policy

When the level-settings suffix is added:

1. Parse and preserve the raw plane point/normal/count records first.
2. Validate count/range bounds independently of assignment semantics.
3. Expose a provisional chunk classifier only if needed by an importer feature.
4. Keep the equality rule (`> 0` versus `>= 0`) explicitly marked unverified until executable behaviour or a retail boundary case settles it.
5. Convert plane point/normal to OBP coordinates only at the world-normalisation boundary, just like other native geometry.

The existing `is_spherical_world` and `sphereCentre` fields are a separate level-wide gravity/world-shape mechanism; they should not be conflated with the ordinary chunk partition planes.

## Why this is still useful

The current GC renderer can load the world without these planes because the disc already exposes concrete streamed/core chunk ranges. Chunk planes become useful when OBP needs to reproduce runtime chunk selection, assign gameplay instances consistently, or emulate native streaming behaviour rather than merely render all recovered content.
