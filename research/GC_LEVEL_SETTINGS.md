# Going Commando level settings (death height, fog, background)

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

The **first block of the gameplay lump** (level WAD slot 2, WAD-LZ compressed).
The gameplay lump opens with a table of `s32` block pointers; the pointer at byte
`0x00` points at the level settings block. Only the fixed *first part* is decoded
here — it carries the level's atmosphere and its **death height** (the kill-plane
Y; under Oozla it sits well below the toxic goo the platforms float over).

`packages/gc-level-settings` — `parseGcLevelSettings(gameplayData)`,
`readGcLevelSettings(gameplayLump)`.

## Layout

```text
GcUyaDlLevelSettingsFirstPart (0x5c), little-endian
  0x00 s32 background_colour[3]   0..255 per channel, or r == -1 => unset
  0x0c s32 fog_colour[3]
  0x18 f32 fog_near_distance
  0x1c f32 fog_far_distance
  0x20 f32 fog_near_intensity     (0..255)
  0x24 f32 fog_far_intensity
  0x28 f32 death_height           kill plane, native (Z-up) world units
  0x2c s32 is_spherical_world     set on LEVEL22 / LEVEL23 / LEVEL26 (the small
                                  high-id WADs — probably the ship / space arenas)
  0x30 f32 sphere_centre[3]
  0x3c f32 ship_position[3]
  0x48 f32 ship_rotation_z
  0x4c s32 ship_path
  0x50 s32 ship_camera_cuboid_start
  0x54 s32 ship_camera_cuboid_end
  0x58 u32 pad
```

(The R&C1 struct is `0x50` — no `is_spherical_world` / `sphere_centre`. The chunk
planes, core-sound count and Deadlocked debug arrays that follow the first part
are not decoded yet.)

`death_height` is a native Z coordinate, so it maps straight onto the OBP viewer's
Y axis (`(x,y,z) -> (x,z,y)`).

## Verification (retail)

Level → planet identity: `planet_name_index == level_id` (see
[`GC_PLANET_NAMES.md`](GC_PLANET_NAMES.md)), cross-checked in the OBP viewer.

| level | looks like | death_height | background (RGB 0..255) | fog colour | spherical |
|---|---|---|---|---|---|
| `LEVEL1`  | Oozla (swamp)              | 0   | 6, 16, 12    | 10, 40, 30   | no |
| `LEVEL2`  | Maktar Nebula (space station) | 105 | 10, 10, 20  | 30, 30, 60   | no |
| `LEVEL3`  | Endako — Megapolis (vertical city on clouds) | 110 | 150, 190, 210 | 100, 120, 140 | no |
| `LEVEL4`  | Endako (city)              | 40  | 15, 30, 50   | 66, 92, 99   | no |
| `LEVEL16` | red-dust desert canyon     | 0   | 192, 94, 48  | 80, 0, 0     | no |
| `LEVEL19` | Grelbin (snow, night)      | 0   | 27, 27, 77   | 72, 118, 137 | no |
| `LEVEL22` | (small ship/space arena?)  | 0   | unset        | 30, 42, 40   | **yes** |

Colours read back as plausible per-level palettes. Oozla's floor sits at native
Z ≈ 50, so `death_height` 0 is a real kill plane ~50 units below the lowest
geometry. `is_spherical_world` is set only on the three small high-id WADs
(`LEVEL22/23/26`).

## Viewer

`tools/gc-world.mjs` writes these into `OBPWorld.environment` and draws a
translucent quad at `deathHeight` spanning the level footprint (`assetKind:
"death-plane"`, tinted from the fog colour; `--no-death-plane` to omit). The
viewer uses `background_colour` for the GL clear colour and renders distance fog
(toggle in the toolbar):

```
t   = clamp((dist_to_camera - fog_near) / (fog_far - fog_near), 0, 1)
vis = mix(fog_near_intensity, fog_far_intensity, t) / 255      // 255 near = clear
colour = mix(colour, fog_colour, 1 - vis)
```

**Distance unit is unverified.** The raw `fog_near/far_distance` are stored as
fixed-point — every retail value across the 27 levels is an exact multiple of
both 256 and 1024 (e.g. Oozla `25600 = 100·256 = 25·1024`). The viewer divides
by **1024** (`FOG_DISTANCE_SCALE` in `renderer.ts`), matching the 1024-per-unit
scale of every other R&C position decode; if direct evidence pins it to 256
instead it is a one-line change. The raw values stay in `OBPWorld.environment`.

| level | fog near→far (÷1024) | far visibility | look |
|---|---|---|---|
| `LEVEL1` (Oozla) | 25→225 | 0.70 (30% fog) | gentle green haze on the far swamp |
| `LEVEL3` | 80→225 | 0.50 | |
| `LEVEL5` | 200→512 | 0.35 (65% fog) | heavy |
