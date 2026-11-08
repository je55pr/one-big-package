# Novalis water rendering

Scope: how the original level draws Novalis's water, what OBP reproduces, and what is still an
approximation. Authority is the NTSC-U level overlay read from the player's own disc; no retail
bytes are committed. Cross-checked (reference only, nothing copied) against the matching
decompilations `lombyte-project/Lombyte` (`src/overlays/l01/scroll_layers.c`,
`mobys/{bounded,rippling,pulsing}_scroll_layers.c`, `mobys/pool_tiles_and_effects.c`,
`overlays/shared/rendering/render_water_grids.c`, `overlays/shared/props/pool_set_scale.c`) and
`RAC-Native/rac1-native-game` (`novalis_water.cpp`, `tools/codegen/asm2fn_specs/novalis_water.json`,
`src/game/ovl/ovl_26_2a.cpp` `region_height_splash`). Every VU0/MIPS routine listed below was also
read from the overlay itself.

Novalis has two independent water systems. Both draw through VU1 program 7 inside the draw-list
callbacks of their owning moby class, after the world, with depth test but no depth write.

## 1. Moving water: scrolling strips (classes 676, 678, 761, 1225)

Each class owns a table of 0x60-byte layer records in the overlay (`Rac1NovalisScrollingWater`):

| Class | Table | Layers | What it is |
|---:|---|---:|---|
| 676 | 0x1e2fc0 | 8 | the river (z about 39, x 22-178, y 137-297) |
| 678 | 0x1e3380 | 2 | the two flat shallow quads at the plumber entrance (z 82.25) |
| 761 | 0x1fbc80 | 4 | the sloped shallow stream climbing from x 234 to 323 (z 67-94; texture 44, alpha 40, pulsing height) |
| 1225 | 0x202d80 | 3 | the three pulsing pool glows (texture 43) |

Record: +0 vertex pointer (x, y, z0, z1 floats), +4 uv pointer, +0xc count, +0x10/+0x14 scroll rates,
+0x18 wobble amplitude, +0x1c frame step, +0x20..0x2c scroll directions, +0x30 vertex colour, +0x34/+0x38
the two Fx textures, +0x3c/+0x3d the two ALPHA FIX bytes, +0x5c height blend. Class 761's first update
(0x2feb58) lowers every vertex 0.45 and offsets alternating pairs by +-0.05 (reproduced at read time).

Per frame (0x2b96e0 drawer, 0x262618 vertex builder):

* scroll accumulators `u0,v0 += du0,dv0 * rate0` and `u1,v1 += du1,dv1 * rate1`, wrapped to [-1, 1];
* each vertex gets a wobble angle `((seed * 64 + phase) * 8 & 0xfff) / 4096 * 2pi - pi`, where `seed` is
  bits 16..23 of `bits(x) + bits(y)` and `phase` is the frame counter times the frame step;
  `wobble = amplitude * (sin, cos)`;
* first list uv = `uv + scroll0 + wobble`, second list uv = `uv + scroll1 - wobble`;
* height = `z0 * mix + z1 * (1 - mix)`. Classes 761 and 1225 set `mix = 0.5 + 0.5 sin(((frame & 63) - 32) pi / 32)`
  every frame (761 in its update, 1225 after drawing); 676 and 678 leave it at 0.

GS state (packet 0x1cac80 and the template at 0x1c44a0): triangle strip with TME, ABE and fog, Gouraud
colour, repeat wrap, linear filter. ALPHA_1 / ALPHA_2 = `(Cs - Cd) * FIX / 128 + Cd`, FIX from the record;
texture alpha is ignored. TEST: alpha >= 0x60 with AFAIL = frame-buffer-only, so alpha only gates the
depth write. The strip is drawn twice (context 1 then 2, same vertices and colour, textures 0 / 1).
Vertex colour modulates with 0x80 = 1.0 and saturates per pass.

OBP folds the two sequential constant-alpha blends into one equivalent blend in the shader
(`D(1-a0)(1-a1) + a0(1-a1)C0 + a1 C1`).

## 2. Stationary water: pool tiles (class 751)

The controller (update 0x2fd0e8, draw callback 0x2fd0c0) owns 18 populated records of a 21-entry table at
0x1e34c0 (`Rac1NovalisPoolWater`). A record is a 16 x 16 cell height field centred on (x, y) at height z
(cell 1 unit, corner at -8), with three height slots and edge halos shared with abutting tiles
(neighbour bytes at +0xc: y-16, x-16, y+16, x+16 then the diagonals).

* Zones: seven camera clip volumes (the class-751 instance's PVar: 2..8) select tile ranges
  (`pool_first/count/odds` at 0x1fa5e8 / 0x1fa608 / 0x1fa628); inside a zone each tile draws the 4 x 4
  sub-tiles set in its mask (table 0x1fa590). Tiles outside every zone neither draw nor simulate.
* Simulation (`pool_flush` 0x2b7fe0, step 0x262038): every eighth frame
  `h' = 0.9375 * (0.25 * sum8(neighbour h) - h_old)`; between steps the two newest slots are blended by
  the accumulator (0.1 per frame).
* Surface (0x2b8c08, 0x261df8, 0x2b80d0): 17 x 17 vertices at `z + h`. The normal is the sum of two
  cross products of neighbour differences. Light grey is `64 + 128 * clamp((n.L - 0.573) * 3.25 + 0.573)`
  with `L = (cos a, sin a, -1) * 0.57735`; flat water therefore sits at about 139 / 128. Context 1 samples
  the scrolling pattern (tile +0x18 texture, ALPHA_1 = byte +0x1d) with a checkerboard of two scroll phases
  (scroll `-0.0016` per frame plus two swell circles of radius 0.03); context 2 samples the reflection
  texture (+0x14, ALPHA_2 = byte +0x1c) at `0.5 + 0.45 * reflect(view).xy`. Flat-edge flags (tile 15 bit 3)
  pin an edge at tile height.
* Disturbances: two radius-2 bumps of 0.2 per tile at start (`pool_ripple`), random radius-1 dips of 0.05
  per tile with chance 1 / odds per frame while a zone is active, and the hero's water-state splashes
  (entering from the air -0.4, surfacing +0.3, diving -0.3, climbing out +0.35).

## What OBP does and what it approximates

Reproduced from the overlay: all record tables, vertex layout, scroll / wobble / height-mix maths, GS blend
and texture selection, tile placement, zone masks, the wave step, splash kernel and surface lighting.

Approximations / host choices (not retail facts):

* Frame clock: the native frame counter; OBP uses 60 Hz derived from the gameplay tick delta and starts
  at 0 when the world loads. The pool scroll uses the same closed form per tile rather than only counting
  frames in which the tile was drawn.
* Sun heading: the controller seeds it from the live key-light bank at 0x180340 when it first runs; that
  bank is not recovered in OBP, so the heading is fixed. It only tilts highlights on rippled water.
* Host random numbers for splashes (visual only, separate from gameplay RNG).
* Hero splashes come from OBP's recovered swim-state transitions instead of the native state-entry timers.
* Fog: the engine's depth fog applies as for other world materials (the GS fog flag is set).
* Edge halos use the neighbour's slot at the time of the step rather than the native copy-on-write halos.

Not yet reproduced (open):

* The underwater look: while the camera is under a water surface the engine swaps the fog to the water
  tint (`0x161204..0x161214`) and fades a 37-50% full-screen overlay (`0x161200..0x161203`). Both colours
  are only written when the camera has been inside a pool zone, and the underwater flag itself is a
  collision-line test (`update_camera_underwater_flag`, 0x1ed7f0).
* Pool drips (class 0x313 spawned at `0x1fa650`), the tank bubbles, water lilies (class 695) bobbing
  with the hero's weight, and water currents (`water_current`).
* Other levels: the tile drawer, step and surface routines are the shared engine used by levels 1, 5, 7,
  11, 12 and 13, but each level has its own tile table, zone logic and controller (for example Rilgar's
  rising water), so every level needs its own record reader on top of the shared simulation / surface code.
