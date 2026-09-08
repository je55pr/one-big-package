# R&C1 native C# promotion

Status: active on `chatgpt/rc1`.

The mature R&C1 reconstruction remains in `reference-ts/` while evidence-backed pieces are promoted into the production C# architecture. Promotion must preserve the native-evidence boundary: no GC outer-container assumptions, no invented gameplay semantics, and no production provider until the native importer is useful enough to justify exposing it.

## Shared codecs

Retail archaeology has independently established the following inner formats as shared below the game-specific level-container boundary:

- WAD-LZ — `OBP.PS2.Compression.WadLz`;
- octree collision — `OBP.PS2.Collision.RcCollision`;
- PS2 indexed texture decode — `OBP.PS2.Textures.Ps2Texture`;
- tfrag inner packet grammar — `OBP.PS2.Geometry.RcTfrag`;
- 16-byte level texture-entry + GS-RAM palette grammar — `OBP.PS2.Textures.RcLevelTextureTable`.

The last two were promoted from GC-specific native locations only after the R&C1 retail census had independently validated compatibility. `GcTfrag` and `GcLevelTextures` remain compatibility projections so existing GC callers and equivalence tests continue to guard behaviour.

Sharing an inner codec does **not** mean R&C1 and GC share outer level containers. R&C1 native disc-index, level header, level-data directory, core-index offsets and gameplay layout remain owned by `OBP.RAC1`.

## First production slice

The first native R&C1 target is intentionally narrow but real:

```text
retail R&C1 ISO
  -> bounded raw-disc index / native level header
  -> R&C1 level-data directory
  -> core index + WAD-LZ core data + GS RAM
  -> shared RcTfrag + RcCollision + RcLevelTextureTable
  -> neutral RuntimeWorld terrain / textures / collision
```

This slice does not imply that TIEs, shrubs, Mobies, sky, gameplay scripts or mission state are absent from the game. They remain unrecovered in the C# production path until promoted from the already-proven reference implementation.

## Acceptance rule

The native slice must reproduce deterministic retail observations already established by `reference-ts`, while the same branch reruns GC retail equivalence for any newly shared codec. A new shared implementation is not accepted merely because R&C1 parses; it must also preserve the mature GC results.
