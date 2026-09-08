# Ratchet & Clank 1 — native sky archaeology

Authority: NTSC-U original / `rac1-ntscu-original`. Retail bytes are not stored in Git.

## Result

Core-index word `0x10` is now directly validated as the native sky block start across all 19 authority levels. Word `0x14`, independently validated as collision, is the sky block's following boundary on every level.

The recovered sky binary structure is the same structure already decoded for Going Commando:

- 0x40 sky header;
- up to 8 RAC/GC shell pointers;
- per-shell cluster table;
- 0x20 cluster headers;
- `s16 / 1024` vertices with native per-vertex alpha;
- `s16 / 4096` ST coordinates;
- 4-byte triangle records containing three local vertex indices plus texture ID;
- 0x10 sky texture definitions with 8-bit indexed pixels and 32-bit palettes.

This conclusion is retail-based, not inferred merely because Wrench uses a common decoder. The authority census validated every header, shell pointer, cluster range, vertex/ST/face span, triangle-local vertex index, texture definition, pixel span and palette span without an R&C1 exception.

Across the 19 levels:

- 19/19 sky blocks validate;
- 75 shells;
- 102 sky textures;
- 27,349 shell vertices;
- 26,496 shell triangles;
- shell counts range 2..7;
- no malformed texture/palette ranges;
- no out-of-range face indices.

The deterministic per-level census is in `research/generated/rac1_sky_census.json`.

## Public corroboration

Wrench commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb` is used only as corroboration:

- `src/wrenchbuild/level/level_core.h`: names core word `0x10` `sky` and `0x14` `collision`;
- `src/engine/sky*` / Wrench's level sky handling uses the RAC/GC shell family (public naming only).

OBP's retail census independently establishes the boundaries and binary grammar used by the supported build.

## R&C1 -> GC relationship

For the recovered sky path the relationship is **identical binary structure**. This does not imply the R&C1 outer level/container layout is GC-compatible; R&C1 reaches the shared inner sky block through its own raw-disc index, native 0x2434 header and 11-range level-data directory.

`packages/rc-sky` is therefore the neutral shared codec facade. `packages/rac1-level-core` retains the R&C1-specific access chain.
