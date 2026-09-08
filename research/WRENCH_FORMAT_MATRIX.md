# Wrench cross-game format matrix

Evidence snapshot: `chaoticgd/wrench` commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb` (inspected 2026-09-06).

This file records only claims supported by Wrench source. It is a lead for OBP archaeology, not a substitute for direct comparison against our game builds.

| Area | R&C1 | Going Commando | UYA | Deadlocked | Stage-0 finding |
|---|---|---|---|---|---|
| Level WAD top-level | `RacLevelWadHeader` | `GcUyaLevelWadHeader` (+ legacy `0x68` branch) | `GcUyaLevelWadHeader` | `DlLevelWadHeader` | GC/UYA share a top-level unpack/pack path; R&C1 and DL are distinct families. |
| Collision asset | shared unpacker | shared unpacker | shared unpacker | shared unpacker | Very strong evidence for a common collision abstraction. |
| Tfrag asset | shared asset path | shared asset path | shared asset path | shared asset path | Top-level flow is shared; low-level read/write receives `Game`, so game-specific details may remain. |
| Moby class | shared `unpack_moby_class` | shared | shared | shared | Strong shared class/model lineage. Low-level `MOBY::read_class(..., config.game())` remains game-aware. |
| TIE class | shared `unpack_tie_class` | shared | shared | shared | Strong evidence that instanced environment classes can normalize through one OBP concept with per-game decoding details. |
| Shrub class | shared `unpack_shrub_class` | shared | shared | shared | Same top-level class path spans all four games. |
| Texture asset | shared `unpack_texture_asset` | shared | shared | shared | One top-level texture asset path spans all four games. |
| Gameplay instances | shared `InstancesAsset` unpacker | shared `InstancesAsset` unpacker | shared `InstancesAsset` unpacker | same asset unpacker + core/mission/art-instance structure | One top-level instance asset path spans all four; DL layers a substantially different mission/container structure around it. |
| Sky | `read_sky` (`RacGcSkyShellHeader`) | same (`RacGcSkyShellHeader`) | `read_sky` (`UyaDlSkyShellHeader` — adds shell rotation) | same as UYA | Shared `SkyHeader` / cluster layout; only the per-shell header differs (RAC/GC vs UYA/DL). OBP `gc-sky` decodes the RAC/GC path, verified on GC. |
| Level settings | `RacLevelSettingsFirstPart` (0x50) | `GcUyaDlLevelSettingsFirstPart` (0x5c) | same 0x5c struct | same 0x5c struct + third/reward/fifth parts | First part is near-identical; GC+ add `is_spherical_world` / `sphere_centre`. OBP `gc-level-settings` decodes the first part, verified on GC. |
| Flat WAD asset | shared unpacker | shared | shared | shared | Generic flat-WAD handling is common across all four. |
| Sound bank at level WAD | no equivalent field in shown R&C1 WAD path | present | present | present | Container-level difference; do not force one native layout into OBP. |
| Reverb field | not used in shown R&C1 WAD path | present | present | present | Preserve as optional normalized metadata until direct evidence clarifies semantics. |
| IRX WAD | R&C1-specific path | GC-specific path | UYA/DL family | UYA/DL family | System/module container is more versioned than world asset codecs; keep outside the common world model. |

## Evidence notes

### Level containers
`src/wrenchbuild/level/level_wad.cpp` registers:

- R&C1 -> `RacLevelWadHeader` / `unpack_rac_level_wad`
- R&C2 -> `GcUyaLevelWadHeader` / `unpack_gc_uya_level_wad`
- R&C3 -> the same GC/UYA header + function
- Deadlocked -> `DlLevelWadHeader` / `unpack_dl_level_wad`

The GC/UYA unpacker also detects a `header_size == 0x68` Going Commando variant containing separate NTSC/PAL gameplay ranges.

Source: https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/wrenchbuild/level/level_wad.cpp

### Collision
`src/wrenchbuild/level/collision_asset.cpp` assigns the exact same `unpack_collision_asset` and `pack_collision_asset` functions to R&C1, R&C2, R&C3 and Deadlocked.

Source: https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/wrenchbuild/level/collision_asset.cpp

### Tfrags
`src/wrenchbuild/level/tfrags_asset.cpp` similarly uses the same top-level pack/unpack functions for all four games. Those functions call `read_tfrags(..., config.game())` / `write_tfrags(..., config.game())`, so game identity still reaches the low-level codec.

`src/engine/tfrag_low.cpp` shows the common low-level structure and at least one Deadlocked-specific write condition (`light_end_ofs_rac_gc_uya`). This supports "shared format lineage with versioned details", not "byte-identical across games".

Sources:
- https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/wrenchbuild/level/tfrags_asset.cpp
- https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/engine/tfrag_low.cpp

### Moby / TIE / shrub classes
Wrench registers a single top-level class unpacker per asset kind for all four games:

- `MobyClassAsset` -> `unpack_moby_class`
- `TieClassAsset` -> `unpack_tie_class`
- `ShrubClassAsset` -> `unpack_shrub_class`

Moby unpacking then calls `MOBY::read_class(buffer, config.game())` or `read_mesh_only_class(..., config.game())`, preserving game identity in the low-level codec. This is the same architectural pattern seen in tfrags: one semantic asset family, versioned binary details.

Sources:
- https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/wrenchbuild/classes/moby_class.cpp
- https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/wrenchbuild/classes/tie_class.cpp
- https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/wrenchbuild/classes/shrub_class.cpp

### Textures and generic WADs
`TextureAsset` registers the same `unpack_texture_asset` for R&C1, R&C2, R&C3 and Deadlocked. `FlatWadAsset` similarly uses one common unpacker. By contrast, the IRX/system WAD explicitly branches into R&C1, GC, and UYA/DL codec families.

Sources:
- https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/wrenchbuild/common/texture_asset.cpp
- https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/wrenchbuild/common/flat_wad.cpp
- https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/wrenchbuild/globals/irx_wad.cpp

### Instances
`src/wrenchbuild/level/instances_asset.cpp` registers the same `unpack_instances_asset` entry point for R&C1, R&C2, R&C3 and Deadlocked. Game-specific gameplay block descriptions are selected downstream, which again points to a shared abstraction with per-game layouts.

Source: https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/wrenchbuild/level/instances_asset.cpp

## Stage-0 architectural conclusion

The public tooling evidence repeatedly shows the same pattern across **world-facing assets**: a common semantic asset/class entry point spans the PS2 series, while `Game`/`BuildConfig` remains available to low-level codecs for binary differences. Containers and system WADs diverge more strongly.

That is almost exactly the architecture OBP wants: keep R&C1/R&C2/R&C3/Deadlocked container parsing at importer edges, normalize common world concepts in the middle, and never assume their native bytes are identical just because their semantic asset families are shared.

## Next evidence to collect when inputs arrive

1. Exact hashes + disc TOCs for the three NTSC-U authority builds.
2. Wrench full unpack of one small level per game.
3. Binary + semantic diff of collision headers/flags across those levels.
4. Tfrag header/VIF-field comparison across all three.
5. Moby/TIE/shrub class header comparison across all three.
6. Instance class/pvar layout comparison before introducing semantic `Enemy`, `Vendor`, `GadgetSurface`, etc. types.
