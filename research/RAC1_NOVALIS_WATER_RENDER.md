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

## 3. Underwater look and swim persistence

* While the camera is under a water surface the original fades a translucent colour over the 3D view (before the
  HUD) and swaps the fog for a short tinted one. Colours: overlay at 0x161200 (initialised to blue 0x18 0x30 0x90,
  fade 0x40), fog colour 0x161204, fog range 0x161208 (near 0, far 32768 / 1024 = 32 units, far intensity 48).
  The pool controller rewrites them each frame it finds the camera in one of its zones: dark teal 0x00 0x28 0x30
  (fade 0x40) for zones 1-3, blue (fade 0x30) otherwise. `Rac1NovalisUnderwaterLook` / `Rac1NovalisUnderwaterMechanism`.
* The underwater flag (0x167014, camera_ground_check 0x1ecdf0) is only re-evaluated while a collision-water surface
  lies within 0.75 of the camera (flag = camera below surface + 0.04), so it holds in flooded tunnels with no surface.
* Swimming: `try_transition_hero_water_state` (0x227778) keeps the hero in swim mode until the air runs out or he
  rises more than 0.4 above the last water surface; it never looks for a surface plane or a clear path to it again.
  OBP used to re-test every tick, which dropped Ratchet to running on the floor of flooded cave tunnels. It now holds
  the last surface while underwater (`PlayerHost.Rac1Swimming.cs`).

Not yet reproduced (open):

* Pool drips (class 0x313 spawned at `0x1fa650`, zones 0 and 6), water lilies (class 695) bobbing
  with the hero's weight, and water currents (`water_current`).
* Gaps: a few spots of collision water still have no drawn surface (the small clusters found by sampling the
  collision water against the strips and tiles); the large patch beside the curved bridge was an OBP bug, not retail
  data (see below).
* Other levels: the tile drawer, step and surface routines are the shared engine used by levels 1, 5, 7,
  11, 12 and 13, but each level has its own tile table, zone logic and controller (for example Rilgar's
  rising water), so every level needs its own record reader on top of the shared simulation / surface code.

## 4. Fixes found while testing

* Tank tile 15 (186, 170, zone 5) has the full sub-tile mask 0xffff. OBP remembered the mask its index list was built
  for starting at 0xffff, so that one tile never built any triangles and left a hole in the water beside the curved
  bridge between the river strips and the other tank tiles. It now starts from "nothing built".
* The water meshes draw at render priority 1, after the level's other blended geometry. The original draws the water
  callbacks after the world, so alpha-blended plants below the surface are tinted by it; with distance sorting alone
  they sometimes drew over the water untinted. Opaque and alpha-tested geometry still depth-tests the water as before.

## 5. Tank bubbles where the river meets the still water

While the camera is in pool zone 5 the controller (update 0x2fd0e8, `found == 5`) blows bubbles along the join between
the river strips and the cave tank (`Rac1NovalisPoolBubbles`, particle kinds in `Rac1ParticleSession.PoolBubble.cs`):

* every native frame one foam sprite (kind 0x39, spawn 0x287c80, update 0x2728e8) at x 177 +-0.6, z 39, y
  176 - wave[frame % 20] / 20 * 11 +-0.1 (wave table 0x1fa6a0 is 0..19 interleaved, so the sweep covers y 165..176),
  drifting +0.02 in x, size 1.0 (x 210000) growing 5880 per frame, spinning with a decaying rate, alpha ramping up
  over 64 frames and back down over 64;
* then 20 steps along the same edge (x 178 +-0.6, z 39 - 0..0.2), each with probability 1/32 a small bubble (kind
  0x38, spawn 0x287b00, update 0x272838): stationary, size 4..10 growing 6300 per frame, grey 0x60..0x7e losing 4 per
  frame.

Both kinds are additive and use part texture lookup entries 0x38 / 0x39, which resolve to Fx part texture 38: a dark,
soft noise blob, so the effect is a faint pale haze, not bright bubbles (many overlapping sprites build it up). The
size unit, blend mode and the draw alpha ramp are as the other kinds in the session; the host RNG replaces the
native one and the sprite depth range bytes (0x24 / 0x2c) come from the constructors.

## 6. Calibration against side-by-side PCSX2 captures (same spot, bank by the white tree)

* The original particle renderer (`part_proc`, read from the overlay at 0x27c878) has no per-kind size or alpha handling: size is
  scaled by the projection and divided by depth, spin comes from a sine / cosine table, and the near / far fade bytes are the
  depth-range byte (+9). The foam's spawn rules, zone test (clip volume 7 spans y 139..259, so the whole bank is inside) and the pool
  (2048 slots) match, so no retail difference explains why camera-facing sprites looked far stronger than the captures.
* In the captures the foam never stands up in the air: it reads as a faint, flat haze lying on the water. OBP therefore draws the two
  pool kinds as horizontal quads on the surface (`RuntimeEffectSpriteProjection.SurfaceFlat`, spin about the vertical) instead of
  camera-facing billboards, which also stops them glowing against the cliffs. With that the native alpha ramp is used unscaled. This is
  a presentation choice made from the captures, not a recovered retail fact; the native sprite projection of these kinds was not
  decoded beyond what is above.
* Colour samples of matching regions (OBP / PCSX2, 0..255): river strips (86,110,170) / (71,85,125) and (87,115,196) / (61,82,148);
  tank tile (104,136,197) / (114,152,219). OBP's river is about 25-30% brighter and its tank about 10% darker than the capture, so
  the strip to tank step is inverted in sign and the seam reads as an edge. Working the strip blend backwards from the capture puts the
  framebuffer under the river near (46,38,26), far darker than OBP's pale river bed, so the likely cause is the underwater terrain
  colour, not the strip blend. Not yet changed.
* Foam brightness and coverage: the flat sprites use the native colour and alpha unscaled (a gain of 1.5 looked too bright against the capture, and a gain of 3 saturated to white). They draw after the water (render priority 2 against 1), as the original draws particles after the water callbacks; before that the water covered them. The foam spawns along native y 165..176 at x 177 and drifts +x; in a render from the bank it covers the visible length of the river / tank join. Not checked: the far part of the join seen from above.
