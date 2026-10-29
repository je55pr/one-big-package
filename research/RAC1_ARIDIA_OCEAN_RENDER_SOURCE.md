# Recovering native liquid surfaces: Aridia ocean data

Scope: source-backed first step for #477, following #232 and PR #476.
This change **does not make the mud visible yet**. It recovers the missing
draw-call data independently from collision material classes.

## Provenance and representation

The R&C1 disc level table (LBA 1500) points to Aridia LEVEL2. Its outer
level-data range 0 contains an 11-slot directory. The directory's slot 0
contains an **executable overlay** with repeated 16-byte section headers
(destination virtual address, size, type, entry point). Source-initialised
sections of type 1 can be read by native EE address. Type 8 is BSS and cannot
supply authored geometry. This is **not** the gameplay WAD in outer range 1.

`Rac1LevelOverlay` preserves that distinction and rejects truncated,
overlapping or unsupported sections. The same section reader works for
other R&C1 native levels.

Aridia's class-854 ocean actor registers a draw-list callback at
`L2:0x2ea048`, uses VU1 program 7 for the geometry and uploads an animated
texture sequence from native Fx indices **40..103** (64 frames).
Its callback selects seven surface records in the overlay:

| Native record | X | Y | Z (height) | Grid columns x rows | Step X/Y |
|---|---:|---:|---:|---|---:|
| 0x001f3c00 | 132.60 | 133.46 | 24.77 | 84 x 70 | 2.62 / 2.62 |
| 0x001f3c60 | 162.60 | 60.54 | 24.77 | 42 x 28 | 2.62 / 2.62 |
| 0x001f3cd0 | 44.20 | 204.90 | 26.04 | 42 x 42 | 1.49 / 1.49 |
| 0x001f3d30 | 53.90 | 267.30 | 25.01 | 42 x 35 | 1.429 / 1.49 |
| 0x001f3d90 | 140.20 | 215.30 | 24.91 | 21 x 42 | 1.80 / 1.80 |
| 0x001f3dd0 | 178.20 | 258.30 | 4.78 | 14 x 7 | 1.80 / 1.80 |
| 0x001f3e10 | 112.20 | 284.30 | 24.74 | 7 x 7 | 1.80 / 1.80 |

Coordinates above are **native Z-up**, not OBP Y-up. The values are read
from the authorised local NTSC-U image during tests; no asset bytes or
texture frames are committed.

The surface record's +0x1c points to frame-packet state at 0x1f3e80,
and +0x34 points to native per-cell VIF packets. A native 7-by-7-cell
unit supplies an 8-by-8 vertex lattice. Each 0x200-byte cell packet has a
0x10-byte VIF prefix ending in the **V4-8 unpack command 0x6e7cc002**,
followed by 0x7c packed four-byte vertex values (496 bytes). All 231
referenced cell packets validate, including reused packet ranges across
records. These packed values are **not** plain float-UV coordinates.
The source index strip is stored at **native 0x001cb400**, as 124
little-endian 16-bit byte offsets into an 8-byte-stride 8x8 lattice.
Its 124 entries form 98 nondegenerate triangles and include row-joining
degenerate vertices. The 124 VIF V4-8 values per cell supply the original
**RGB samples in exactly that strip order**; the fourth byte is zero in
all 231 validated packets and must not be mistaken for an authored
transparent material. `ReadStripLatticeIndices` and `DecodeCellStrip`
preserve this topology and colour assignment without inventing UVs,
alpha, wave geometry or a Godot material.

A separate 248-float source table at **0x001cb510** contains 124
paired values from -3 to +3. The class-854 actor's initialisation calls
`ocean_wave_scale_l2(1/6)`, producing values from -0.5 to +0.5.
The generated native `ari_ocean_cells_draw_l2` DMA builder confirms
these are **texture ST inputs**, not geometry positions: it submits
124 independently computed world XYZ values, then 124 packed colour
values from each cell, then this scaled pair table to VU1 program 7.
`ReadStripTextureCoordinates` recovers the original ST values; all
124 pairs match the indexed 8x8 lattice to within 0.001. UV phase,
Q, wrapping and the GS texture function still need host parity checks.

`Rac1AridiaOceanGeometry.Read` combines these native inputs into the
exact 136 enabled cell strips: **16,864 per-strip vertices and 13,328
nondegenerate triangles** across seven source records. It preserves
native XYZ (converted to OBP Y-up), per-vertex ST and original RGB bytes,
but deliberately does **not** choose alpha, blending, fog or texture
frame. It is a source-backed geometry decoder, not an enabled renderer.

The source record also has a **+0x38 pointer to one byte per native 7x7 cell**.
All 231 bytes are 0 or 1, with 136 enabled across the seven records
(57, 8, 31, 20, 17, 2, 1). The importer retains these masks independently
of the draw packets; they are not collision-derived. The original class-854
callback additionally selects *which records to draw* using the camera's
level clip-volume membership. The retail class-854 actor i713 supplies
**clip-volume indices 46, 48, 49** in its first three PVar words.
These select records **[0]**, **[2, 3, 4, 5, 6]**, and **[1]** respectively.
Existing `Rac1ClipVolumes` geometry provides the exact oriented-box
containment test, including overlapping camera zones. Drawing all seven regions simultaneously
is not original behaviour and produced overlapping geometry in the local
experimental visual capture. No such preview is enabled in the game.

The callback sets the native base RGBA bytes **15, 15, 25, 30** and uses
VU1 program 7 to draw the strips. These bytes are not yet a proven Godot
blend/alpha equation. The exact material stays unresolved.

## What remains to render correctly

1. Reproduce the recovered 8x8 source vertex lattice and VU1 strip
   presentation with the actual ST inputs and source RGB. The translated
   native `ari_ocean_cells_draw_l2` computes the 8x8 XYZ grid by adding
   `StepX`/`StepY` across columns/rows and 7x those steps per cell, at
   the record's base height. Its 124 positions follow the 0x1cb400
   strip-index table, not 124 invented grid triangles. Precise screen-space
   clipping, Q interpolation, GS alpha/depth/fog and blend state remain.
2. Reproduce the source's two-frame FX interpolation and UV sampling in
   the host renderer. The generated `ari_ocean_draw_tr_l2` call specifies
   **30 game ticks per frame**, **64 frames starting at Fx 40**.
   `ari_ocean_vu7_setup_l2` divides the native frame counter by 30,
   takes the modulo-64 texture pair, and interpolates by the fractional
   frame position. `Rac1AridiaOceanAnimation` now extracts all 64
   original frame textures and reproduces the frame-selection clock;
   native GS pixel blending has not yet been reproduced. The frame-packet
   pointer is initially zero in the overlay and is populated at runtime.
3. Preserve the source's clipping, depth writing, blending and fog rules
   where recovered, with bounded host approximations identified separately.
4. Emit game-neutral render surfaces, not collision-class-derived mud meshes.
   Validate before/after in an Aridia Godot capture; the same neutral
   rendering path can then be reused by R&C1 Novalis/Gaspar/Pokitaru and
   investigated separately for GC Oozla and UYA Florana.

Sources cross-checked for the draw pipeline (not copied):
- `RAC-Native/rac1-native-game`: `tools/codegen/asm2fn_specs/aridia_ocean.json`,
  `src/game/ovl/aridia_ocean.cpp`, `docs/rendering.md` (MIT).
- `lombyte-project/Lombyte`: `src/overlays/l02/gameplay/entities/skids_agent.c`
  for the callback and draw-record table (GPL; researched only).
- Local retail NTSC-U source is the authority for record bytes.

## Verification

`Rac1LiquidSourceRecordsTests` covers synthetic section bounds and
type-8 BSS rejection without retail payload, and a retail-gated seven-record
census, fixed 7-cell grid dimensions, cell packet bounds and 64 Fx frames.
