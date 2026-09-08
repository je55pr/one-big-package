# Going Commando level lighting

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

Mobies carry **no baked vertex colour** — they store a per-vertex normal
(`research/GC_MOBY.md`) and are lit at runtime from the level's lights. Two
pieces feed that: a small table of **directional lights** in the gameplay lump,
and per-**moby-instance** fields that pick one directional light and add a baked
ambient.

Facts below from the real gameplay lump (LEVEL1 / LEVEL8) cross-checked against
Wrench's `instance_schema.wtf`, `gameplay.cpp` (`GC_UYA_GAMEPLAY_BLOCKS`) and
`gameplay_impl_env.inl` / `gameplay_impl_classes.inl` (structure names / field
meanings only — see `EXTERNAL_SOURCE_POLICY.md`).

## Directional lights — gameplay pointer `0x04`

`InstanceBlock`: `TableHeader { s32 count; s32 pad[3] }` at offset 0, then
`count` × `DirectionalLightPacked` (`0x40`) starting at `0x10`.

```text
DirectionalLightPacked (0x40)
  0x00 Vec4f colour_a       // RGB in [0,1]; 4th float unused (occasionally a small −ve bias)
  0x10 Vec4f direction_a    // unit, world space, native Z-up
  0x20 Vec4f colour_b
  0x30 Vec4f direction_b
```

Each entry is a **pair** of directional components (a = key, b = fill). Retail
counts: LEVEL1 (Oozla) 3, LEVEL8 (Tabora) 4. Example — Oozla light 0:
`colour_a (0.373, 0.467, 0.451)`, `direction_a (−0.000, 0.839, −0.545)`,
`colour_b (0.373, 0.412, 0.090)`, `direction_b (0, 0, 1)`. Tabora light 0 is a
warm key `colour_a (0.925, 0.914, 0.671)` with a dim grey `colour_b`.

The direction is the way the light **travels**, so a surface with normal `n` is
lit by `max(0, dot(n, −direction))`.

## Per-moby-instance lighting — `GcUyaMobyInstance` (`0x88`)

```text
0x74 Rgb96 light_colour   // s32 r, s32 g, s32 b — 0..~255, / 255 => baked static/ambient RGB
0x80 s32   light          // index into the directional-light table; out of range => ambient only
```

`light_colour` is the ambient the level's light probes baked at that instance's
position (Oozla mobies 1..63, Tabora 1..126 per channel). `light` picks the one
directional light that also applies. Retail `light` histogram, LEVEL1:
`{0: 506, 1: 168, 2: 61, 3855: 13}` — `3855` is an out-of-range sentinel
(ambient only). LEVEL8: `{0: 686, 1: 131, 3: 151, 3855: 1}`.

## Point lights — gameplay pointer `0x80`

`GcUyaPointLightsBlock`: `TableHeader` at 0, a 0x800-byte cell grid, then
`GcUyaPointLightPacked` (`0x10`) starting at `0x10 + 0x800`. Only affects mobies.

```text
GcUyaPointLightPacked (0x10) — all u16
  0x00 pos_x   0x02 pos_y   0x04 pos_z    // / 64
  0x06 radius                              // / 64
  0x08 colour_r 0x0a colour_g 0x0c colour_b // / 65535
```

Rare and dim in GC: Oozla 2 (radius ~20 / ~39, colour ~0.05), Tabora 5, most
planets 0. `radius == 0` entries are unused slots.

## Env sample points — gameplay pointer `0x8c`

`InstanceBlock` of `GcUyaDlEnvSamplePointPacked` (`0x20`). Spatial probes; "the
nearest is used" for a point's ambient + directional light + fog + reverb.

```text
GcUyaDlEnvSamplePointPacked (0x20)
  0x00 s32 hero_light            // index into dir_lights — the light for this region
  0x04 s16 pos_x  0x06 s16 pos_y  0x08 s16 pos_z   // / 4, native Z-up
  0x0a s16 reverb_depth   0x0c s16 music_track
  0x0e u8  fog_near_intensity    0x0f u8 fog_far_intensity   // 0..255 visibility
  0x10 Rgb24 hero_col            // ambient colour for this region, / 255
  0x13 u8 reverb_type  0x14 u8 reverb_delay  0x15 u8 reverb_feedback  0x16 u8 enable_reverb
  0x17 Rgb24 fog_col            // / 255
  0x1a s16 fog_near_dist   0x1c s16 fog_far_dist   // WORLD UNITS (not fixed-point)
```

`fog_far_dist <= fog_near_dist` ⇒ no fog override for that sample. Retail
samples per level: Oozla 10 (no fog; `hero_col` ≈ 0.11,0.18,0.20), Tabora 11
(some carry `fog_col` 0.63,0.65,0.47 near 50 / far 250 — a khaki desert haze,
different from the level-settings fog), Endako 13, Grelbin 8.

## Env transition volumes — gameplay pointer `0x84`

`EnvTransitionBlock`: `TableHeader` at 0, `count` × `Vec4f` bounding spheres
(`[cx, cy, cz, radius]`, world Z-up) at `0x10`, then `count` ×
`EnvTransitionPacked` (`0x80`). A box you cross where the hero light and/or fog
blend from state A to state B — a doorway.

```text
EnvTransitionPacked (0x80)
  0x00 Mat4 inverse_matrix     // world -> box local, column-major (16 f32)
  0x40 Rgb32 hero_colour_1     0x44 Rgb32 hero_colour_2   // Rgb32 = u8 r,g,b,pad / 255
  0x48 s32 hero_light_1        0x4c s32 hero_light_2      // dir-light indices
  0x50 u32 flags               // bit0 = blend hero, bit1 = blend fog
  0x54 Rgb32 fog_colour_1      0x58 Rgb32 fog_colour_2
  0x5c f32 fog_near_dist_1  0x60 f32 fog_near_int_1  0x64 f32 fog_far_dist_1  0x68 f32 fog_far_int_1
  0x6c..0x78  ..._2
```

Retail counts: most levels have some (Endako 12, Tabora 11, Joba 10, Notak 6,
Todano 5, Barlow / Grelbin / Boldan 2; Oozla / Damosel / Siberius / Smolg /
Yeedil 0). Nearly all are `flags 1` (hero only) — they warm / darken the light
on Ratchet as he moves between areas. A few carry fog (Grelbin: teal, far
150 → 235; Boldan: cyan, near 20 → 50).

## OBP use

`GcInstances.Gameplay` exposes `DirLights`, `PointLights`, `EnvSamples`,
`EnvTransitions`. `GcWorldImport` packs them into the neutral
`RuntimeLighting` (OBP Y-up) on `RuntimeWorld`.

- **Mobies** (`GcWorldImport.PlaceMobyInstances`): per-vertex colour =
  `light_colour + colA·max(0,−(n·dirA)) + colB·max(0,−(n·dirB)) + Σ pointLight`
  where each point light adds `colour · atten² · max(0, n·toLight)`, `atten =
  1 − dist/radius`. `n` is the class-local normal rotated into OBP Y-up world
  space; clamped `[0.03, 1]`; multiplied into the moby texture / tint in Godot.
- **Atmosphere** (`RuntimeEnvironment`): the nearest env sample to the ship park
  point supplies the scene `AmbientColour` (`hero_col`); the nearest sample that
  *carries* a fog override supplies fog colour + world-unit distances +
  intensities (the spawn region often defines none). Falls back to the global
  level-settings fog (÷1024) otherwise. `RuntimeEnvironment` fog distances are
  now always world units.

Reverb / music / hero_light-for-the-player and env-transition volumes are not
consumed.
