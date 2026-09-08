# Going Commando level core

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

The `data` lump — slot 0 of the level WAD ([`GC_LEVEL_WAD.md`](GC_LEVEL_WAD.md)) —
holds the bulk of a level: an overlay ELF, HUD banks, GS/VRAM tables, and the
**level core** (static geometry, collision, textures, moby/tie/shrub class
models). `packages/gc-level-core` parses the container down to per-section byte
ranges; individual section codecs (tfrags, textures, classes) are still to come.

Reproduce: `node tools/gc-collision.mjs "<GC iso>" --all` (uses this path for the
19 non-streamed levels).

## `GcUyaLevelDataHeader` @ offset 0 of the data lump

All fields `ByteRange { s32 offset; s32 size }`.

| off | field | notes |
|---|---|---|
| `0x00` | `overlay` | per-level ELF overlay (raw) |
| `0x08` | `coreIndex` | **uncompressed**; `LevelCoreHeader` + class/texture tables |
| `0x10` | `gsRam` | GS upload table (`GsRamEntry[]`) |
| `0x18` | `hudHeader` | |
| `0x20` | `hudBanks[5]` | |
| `0x48` | `coreData` | **WAD-LZ compressed** asset blob |
| `0x50` | `transitionTextures` | |

## `LevelCoreHeader` @ offset 0 of `coreIndex` — `0xbc` bytes, all `s32` LE

Struct names from Wrench; offsets confirmed against the retail bytes.

| off | field | meaning |
|---|---|---|
| `0x00` | `gsRam` `ArrayRange {count, offset}` | offset is into `coreIndex` |
| `0x08` | `tfrags` | byte offset into the **decompressed** asset blob |
| `0x0c` | `occlusion` | " |
| `0x10` | `sky` | " |
| `0x14` | `collision` | " — the RC octree collision ([`GC_COLLISION.md`](GC_COLLISION.md)) |
| `0x18` | `mobyClasses` `ArrayRange` | table in `coreIndex`; entry `0x20` B, first `s32` = `offset_in_asset_wad` |
| `0x20` | `tieClasses` `ArrayRange` | entry `0x20` B |
| `0x28` | `shrubClasses` `ArrayRange` | entry `0x30` B |
| `0x30`–`0x5c` | tfrag / moby / tie / shrub / part / fx texture `ArrayRange`s | |
| `0x60` | `texturesBaseOffset` | start of texture pixel data in the asset blob |
| `0x88` | `assetsCompressedSize` | == `coreData.size` |
| `0x8c` | `assetsDecompressedSize` | **exactly matches the WAD-LZ output for all 27 levels** |
| `0xb4` | `mobySoundRemapOffset` | |

Other `0xbc`-header fields are preserved as `coreHeader.raw[]` (47 u32 words).

## Section extents

`LevelCoreHeader` gives each section's **start** offset; a section runs to the
**next larger** section start. `packages/gc-level-core` builds that boundary set
from: `tfrags`, `occlusion`, `sky`, `collision`, `texturesBaseOffset`,
`assetsDecompressedSize`, every moby/tie/shrub class `offset_in_asset_wad`,
`mobySoundRemapOffset`, and the 256-entry `ratchet_seqs` table (`raw[0x78/4]`).
`gcLevelCoreSectionRange(core, offset)` returns `{offset, size}`;
`gcLevelCoreCollision(core)` returns the raw collision bytes.

Unlike chunk collision, level-core sections are **not** individually WAD-LZ
compressed — they are raw slices of the single decompressed asset blob.

## Verification (all 27 GC levels)

| | result |
|---|---|
| `coreData` WAD-LZ output length == `assetsDecompressedSize` | 27 / 27 |
| collision section parses through `rc-collision` (sane octree, vertex indices, bounds) | 27 / 27 |
| streamed-level core collision == that level's chunk-0 collision | matches (e.g. LEVEL1: 308,108 triangles both ways) |

Decompressed asset blobs are 6–21 MiB; `openGcLevelCore` holds one in memory
(`maxDecompressedBytes` cap, default 128 MiB). A future streaming section reader
can avoid the full-blob buffer once section codecs exist.

## Confidence

| Claim | Status |
|---|---|
| `GcUyaLevelDataHeader` layout | confirmed (ranges valid, `coreData` decompresses) |
| `LevelCoreHeader` section-offset fields (`tfrags`/`occlusion`/`sky`/`collision`/`texturesBaseOffset`) | confirmed (collision parses; sizes consistent) |
| class-table `ArrayRange`s and entry sizes | inferred (Wrench); used only for boundary math so far |
| texture `ArrayRange`s, `gsRam`, other `raw` fields | **not yet used / decoded** |
