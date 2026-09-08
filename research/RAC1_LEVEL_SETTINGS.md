# Ratchet & Clank 1 level settings

Authority: `rac1-ntscu-original` (`SCUS-97199`), all 19 native levels.

R&C1's decompressed NTSC gameplay stream uses pointer `0x00` for a fixed first-part level-settings record. Direct retail census establishes that this record is **0x50 bytes**, not the 0x5c-byte GC/UYA/DL generation.

## Retail layout

```text
RacLevelSettingsFirstPart (0x50)
  0x00 s32 background_rgb[3]
  0x0c s32 fog_rgb[3]
  0x18 f32 fog_near_distance
  0x1c f32 fog_far_distance
  0x20 f32 fog_near_intensity
  0x24 f32 fog_far_intensity
  0x28 f32 death_height
  0x2c f32 ship_position[3]
  0x38 f32 ship_rotation_z
  0x3c s32 ship_path
  0x40 s32 ship_camera_cuboid_start
  0x44 s32 ship_camera_cuboid_end
  0x48 u32 pad[2]
```

The colour convention matches the later games at the decoded layer: signed 32-bit 0..255 channels, with native R=-1 meaning unset. Native coordinates are Z-up, so `death_height` maps directly to OBP Y.

## Complete authority census

All 19 levels satisfy:

- pointer `0x00` addresses a complete 0x50-byte record;
- the next positive native gameplay block pointer begins **exactly 0x50 bytes later**;
- all fog distances/intensities, death heights, ship positions and ship rotations are finite;
- both trailing words at `0x48/0x4c` are zero;
- all non-sentinel colour channels are in 0..255.

Observed death heights range from 0 to 230 native units. Fog far distances range from 2,048 to 307,200 native units. The values vary substantially between levels and are therefore useful direct environment data rather than constants.

## Evolution versus Going Commando

R&C1 has no spherical-world fields in this first part. Going Commando inserts:

- `s32 is_spherical_world` at 0x2c;
- `Vec3f sphere_centre` at 0x30;

and moves the ship fields down, growing the first part to 0x5c. The colour/fog/death prefix through 0x28 is shared semantically, but the full structure is **not binary-compatible**.

OBP therefore keeps an explicit R&C1 settings decoder instead of feeding these bytes to `gc-level-settings`.

Public Wrench `RacLevelSettingsFirstPart` independently corroborates the same generation split; the structure above was promoted only after the complete retail census.
