# `RC2.HDR` alignment / sector-padding reconciliation

**Status:** retail-confirmed structural reconciliation for `rac2-ntscu-v1.01` as of 2026-09-18. The repeated per-file block is three sector-padded headers; deeper header-field and loader semantics remain separate archaeology.

A bounded authority probe confirmed `/RC2.HDR` contains a regular per-file region with `0x3800`-byte stride beginning at `+0x5800`. Row 0 and all 27 row boundaries agree with the three-header interpretation below. See [`AUDIO_FOUNDATION.md`](AUDIO_FOUNDATION.md) for the audio-selection context and payload-free probe hashes.

The earlier interpretation began four bytes late at `+0x5804`. The retail check now confirms the aligned `+0x5800` start and removes the need for seven independent `0x800` asset-slot types.

## Confirmed alignment

Public Wrench/noclip structures originally supplied these top-level header sizes as the search lead:

```text
LEVEL header = 0x0060
AUDIO header = 0x1018
SCENE header = 0x137c
```

Sector-padding those records to 0x800-byte boundaries gives:

```text
LEVEL: ceil(0x0060 / 0x800) = 1 sector  = 0x0800
AUDIO: ceil(0x1018 / 0x800) = 3 sectors = 0x1800
SCENE: ceil(0x137c / 0x800) = 3 sectors = 0x1800
                                            ------
                                             0x3800
```

The authority bytes now confirm that the observed seven sectors per file row are **three sector-padded headers occupying 1 + 3 + 3 sectors**.

Confirmed aligned block 0:

```text
RC2.HDR + 0x5800  LEVEL0 header, padded to 0x0800
RC2.HDR + 0x6000  AUDIO0 header, padded to 0x1800
RC2.HDR + 0x7800  SCENE0 header, padded to 0x1800
RC2.HDR + 0x9000  next level block
```

and in general:

```text
block(n) = 0x5800 + n * 0x3800
```

## Why `+0x5804` already looks exactly like this

The retail note currently shows the first words observed at `RC2.HDR + 0x5804` as:

```text
0013a098 00000000 00000003 000002df ...
```

If the real duplicated `GcUyaLevelWadHeader` starts four bytes earlier, those become naturally:

```text
+0x00  00000060   headerSize          <- currently skipped by starting at +0x5804
+0x04  0013a098   sector / absolute LBA
+0x08  00000000   levelId
+0x0c  00000003   reverb
+0x10  000002df   first range offset
...
```

That is a stronger explanation than saying the RC2 copy replaced/removes the ordinary header-size word. It also agrees with the retail-backed `0x60` standalone GC level-header layout now used elsewhere in OBP.

## Authority result

The 2026-09-18 bounded retail check returned:

```text
u32le(RC2.HDR + 0x5800) = 0x00000060
u32le(RC2.HDR + 0x5804) = 0x0013a098  // LEVEL0 absolute LBA
u32le(RC2.HDR + 0x6000) = 0x00001018
u32le(RC2.HDR + 0x7800) = 0x0000137c
u32le(RC2.HDR + 0x9000) = 0x00000060  // next row
```

The same row-boundary census was then run for all file indices `0..26`. Every row begins with `0x60`, every `+0x0800` associated header begins with `0x1018`, and every `+0x2000` associated header begins with `0x137c`: 27/27 for each boundary, with no mismatches.

This confirms the structural packing for the supported authority. It does not by itself prove every inner AUDIO/SCENE field name or the executable loader's higher-level use of those fields.

## Consequence

The former apparent unknown per-level slots `2/3` and `5/6` are not semantic objects: they are continuation sectors occupied by the larger AUDIO and SCENE headers. The `0x3800` stride remains correct; only its interpretation and the block's starting alignment changed.

This is exactly the kind of reconciliation public archaeology is useful for: it proposes a smaller explanation for already-observed retail bytes, but retail bytes still get the final vote.
