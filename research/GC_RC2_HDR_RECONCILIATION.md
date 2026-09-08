# `RC2.HDR` alignment / sector-padding reconciliation

**Status:** public-source-derived verification target. Do not change the retail-backed parser/model until the tiny authority probes below are run.

Current `main` documents a real retail observation in `GC_LEVEL_LOADING.md`: `/RC2.HDR` contains a regular per-level region with `0x3800`-byte stride, and bytes observed from `+0x5804` line up with the absolute LBA and fields of `LEVEL0.WAD`.

The *interpretation* of that observation is likely off by four bytes. Public GC header sizes explain the whole `0x3800` stride exactly without requiring seven independent `0x800` asset-slot types.

## The alignment hypothesis

For GC, public Wrench/noclip structures give these top-level header sizes:

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

So the observed seven sectors per level can be explained much more simply as **three sector-padded headers occupying 1 + 3 + 3 sectors**.

Candidate aligned block 0:

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

## Four tiny authority probes

A local retail-disc check can settle the entire question without dumping any large data:

```text
u32le(RC2.HDR + 0x5800)  expected 0x00000060
u32le(RC2.HDR + 0x5804)  expected LEVEL0 absolute LBA (known: 0x0013a098)
u32le(RC2.HDR + 0x6000)  expected 0x00001018
u32le(RC2.HDR + 0x7800)  expected 0x0000137c
```

If all four match, update `GC_LEVEL_LOADING.md` / `tools/rc2-hdr.mjs` to describe three padded header records rather than seven logical slots, and check the same boundaries for all 27 level blocks.

If the first/third/fourth probes do **not** match, keep the current retail model and record the mismatch; public header sizes are evidence, not authority.

## Consequence if confirmed

The current apparent unknown per-level slots `2/3` and `5/6` would disappear as semantic objects: they are merely continuation sectors occupied by the larger AUDIO and SCENE headers. The `0x3800` stride remains correct; only its interpretation and the block's starting alignment change.

This is exactly the kind of reconciliation public archaeology is useful for: it proposes a smaller explanation for already-observed retail bytes, but retail bytes still get the final vote.
