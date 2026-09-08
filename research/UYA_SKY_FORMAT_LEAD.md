# UYA sky format lead

Pinned public source: `chaoticgd/wrench` commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`, specifically `src/engine/sky.h` and `src/engine/sky.cpp`.

This note records a **public-source binary-format lead**, not retail/native-loader proof by itself. Retail corroboration belongs in a separate compatibility census.

## Shared sky header

Wrench models R&C1, Going Commando, UYA and Deadlocked with the same `SkyHeader` (`0x40` bytes):

- `0x00` colour RGBA
- `0x04` `s16 clear_screen`
- `0x06` `s16 shell_count`
- `0x08` `s16 sprite_count`
- `0x0a` `s16 maximum_sprite_count`
- `0x0c` `s16 texture_count`
- `0x0e` `s16 fx_count`
- `0x10` `s32 texture_defs`
- `0x14` `s32 texture_data`
- `0x18` `s32 fx_list`
- `0x1c` `s32 sprites`
- `0x20` eight `s32` shell offsets

`SkyTexture` remains four `s32` words: palette offset, texture offset, width, height.

## UYA/Deadlocked shell header

The relevant version difference is a 16-byte `UyaDlSkyShellHeader`:

```text
0x00 s16 cluster_count
0x02 s16 flags
0x04 s16 rotation.x
0x06 s16 rotation.y
0x08 s16 rotation.z
0x0a s16 angular_velocity.x
0x0c s16 angular_velocity.y
0x0e s16 angular_velocity.z
```

This is particularly convenient for OBP archaeology because the first `SkyClusterHeader` still begins at **shell + `0x10`**, exactly where the established GC reader already expects the cluster table. UYA therefore changes the meaning of the 16-byte shell-prefix region without shifting the downstream cluster layout.

Wrench interprets shell flag bit 0 as untextured and bit 1 as bloom for UYA/DL.

## Shared cluster payload lead

Wrench uses the same `SkyClusterHeader` (`0x20`), `SkyVertex` (`0x08`), `SkyTexCoord` (`0x04`) and `SkyFace` (`0x04`) structures for the whole family. It also reverses face winding on read.

Position and UV conversion in the public implementation are:

- native position `s16 / 1024`
- native texture coordinate `s16 / 4096`
- alpha `0x80 -> 255`, otherwise native alpha multiplied by two
- face texture `0xff` means no material

For UYA/DL shell rotation fields Wrench converts a native `s16` value using:

`radians_per_second = value * (framerate * 2π / 32768)`

OBP should retain the raw signed values as evidence even if a viewer also computes the public-derived converted values.

## OBP implementation boundary

`packages/uya-sky` implements this versioned shell layout independently of the retail promotion path. The strict retail probe must establish that sampled canonical UYA sky sections satisfy the public lead before `tools/uya-world.mjs` emits sky geometry.
