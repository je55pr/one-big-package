# Going Commando level textures

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

Reproduce:

```
node tools/gc-world.mjs "<GC iso>" --level 1 --no-collision --out captures/level1.world.json
# textures are embedded in world.materials as data:image/png URIs
```

## Where the textures live

The `LevelCoreHeader` ([`GC_LEVEL_CORE.md`](GC_LEVEL_CORE.md)) has an `ArrayRange`
per texture kind into the **uncompressed core index**:

| field | offset | table |
|---|---|---|
| `tfragTextures` | `0x30` | terrain / static geometry |
| `mobyTextures` | `0x38` | enemy / object classes |
| `tieTextures` | `0x40` | instanced environment classes |
| `shrubTextures` | `0x48` | foliage classes |
| `partTextures` / `fxTextures` | `0x50` / `0x58` | particles / effects |

Each entry is a 16-byte `TextureEntry`:

```text
0x0 s32 dataOffset   byte offset into the decompressed asset blob, from LevelCoreHeader.texturesBaseOffset
0x4 s16 width        0x6 s16 height        0x8 s16 type
0xa s16 palette      slot index; the CLUT is at gsRam[palette * 0x100]
0xc s16 mipmap       0xe s16 pad
```

- **Pixels:** `width * height` **linear** 8-bit palette indices at
  `assets[texturesBaseOffset + dataOffset]`. (RC2/RC3 pixels are not swizzled;
  only Deadlocked swizzles the pixel data.)
- **Palette:** 256 little-endian RGBA `u32` at `gsRam[palette * 0x100]`, where
  `gsRam` is the uncompressed GS RAM block named by `GcUyaLevelDataHeader.gsRam`
  (now exposed as `GcLevelCore.gsRam`).

## PS2 fixups — `packages/ps2-texture`

1. **CLUT reorder** (`mapPaletteIndex`): the GS stores a 256-colour CLUT with
   index bits 3 and 4 swapped whenever they differ —
   `(((i & 16) >> 1) != (i & 8)) ? i ^ 0x18 : i`.
2. **Alpha scale** (`multiplyAlphas`): PS2 alpha is 0..128 —
   `a < 0x80 ? a * 2 : 255`.

Then a straight palette lookup per pixel → RGBA.

`packages/gc-level-textures` (`readGcLevelTextures(core, table)`) reads the table,
slices the pixels + palette, and returns decoded RGBA. `tools/lib-png.mjs`
encodes each to a PNG `data:` URI for `OBPMaterial.image`; the viewer uploads
them as GL textures and samples with the tfrag UVs.

## Verification

`node tools/gc-world.mjs --level 1` decodes **77 / 77** Oozla tfrag textures.
A contact sheet shows coherent, recognisable art: Megacorp metal panels, glowing
cyan tech surfaces, wood planks, stone, moss/foliage and swamp-water textures —
matching Oozla in-game. In the viewer the tfrag mesh renders textured
(Megacorp Outlet platform with its cyan edge lights, mossy lily-pad platforms).

The `tex0.data_lo` id on each tfrag texture primitive
([`GC_TFRAG.md`](GC_TFRAG.md)) indexes this `tfragTextures` table directly.

## Confidence

| Claim | Status |
|---|---|
| `TextureEntry` layout, `tfragTextures` `ArrayRange` | confirmed (77/77 decode, art is coherent) |
| pixels linear at `texturesBaseOffset + dataOffset` | confirmed |
| palette at `gsRam[palette * 0x100]`, 256 × RGBA u32 | confirmed (colours are sensible; adjacent slots sometimes share) |
| CLUT reorder + alpha scale | confirmed (Wrench + coherent output) |
| `type` field, `mipmap` chain, non-8-bit formats | **not decoded** (only the base 8-bit level is read) |
| moby / tie / shrub textures | table format is the same; not yet wired to their models |
