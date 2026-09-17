# Going Commando level loading — where level data lives

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

> **RC2.HDR packing update (2026-09-18):** a bounded authority probe confirmed the repeated file-number block begins at `+0x5800` and consists of three sector-padded headers: LEVEL `0x0800` (`0x60` header), AUDIO `0x1800` (`0x1018` header), SCENE `0x1800` (`0x137c` header). The same boundaries matched all 27 rows. The older seven-semantic-slot interpretation is superseded for this authority; deeper header-field/global-loader semantics remain open. See [`GC_RC2_HDR_RECONCILIATION.md`](GC_RC2_HDR_RECONCILIATION.md) and [`AUDIO_FOUNDATION.md`](AUDIO_FOUNDATION.md).

## Disc layout

The Going Commando ISO-9660 filesystem is flat: the disc root plus a single `/G/` directory. Full inventory: [`generated/rac2-ntscu-v1.01.stage0.md`](generated/rac2-ntscu-v1.01.stage0.md).

| Group | Files | Meaning (evidence level) |
|---|---|---|
| `/G/LEVEL0.WAD` … `/G/LEVEL26.WAD` | 27 | **per-level container** — confirmed, see [`GC_LEVEL_WAD.md`](GC_LEVEL_WAD.md) |
| `/G/SCENE0.WAD` … `/G/SCENE26.WAD` | 27 | per-level cutscene container (parallel numbering; `SCENE21/24/25.WAD` are 4,988-byte stubs) |
| `/G/AUDIO0.WAD` … `/G/AUDIO26.WAD` | 27 | per-level audio container (parallel numbering) |
| `/G/MISC.WAD`, `HUD.WAD`, `GADGET.WAD`, `ARMOR.WAD`, `BONUS.WAD`, `SPACE.WAD`, `SCENE.WAD`, `AUDIO.WAD` | 8 | global / shared containers (unnumbered) |
| `/G/MPEG.WAD`, `MPEG1.WAD`, `MPEG2.WAD` | 3 | FMV streams |
| `/RC2.HDR` | 1 | engine-facing asset/LBA directory; packing interpretation is under reconciliation |
| `/SCUS_972.68`, `/SYSTEM.CNF`, `/IOPRP255.IMG`, `/Z6TAIL.DUP` | 4 | boot exe, config, IOP modules, padding tail |

The three `LEVEL<n>` / `SCENE<n>` / `AUDIO<n>` families with identical `0..26` numbering support a per-file-number relationship. **File index `n` is not always the engine level id** — `LEVEL21.WAD` self-reports `levelId = 30`, so the id↔file relationship is a lookup, not identity. See [`GC_LEVEL_CATALOGUE.md`](GC_LEVEL_CATALOGUE.md).

## `RC2.HDR` — 408,444 bytes at LBA 1001

`RC2.HDR` is an engine-facing asset/LBA directory. `tools/rc2-hdr.mjs` can inspect it, but the tool/document's older semantic slot labels should be considered provisional until the reconciliation probes are run.

### Directly observed retail facts

- The per-level region repeats with a `0x3800` stride.
- At `RC2.HDR + 0x5804`, the first visible word for file 0 is `0x0013a098`, exactly the ISO-9660 LBA of `LEVEL0.WAD`.
- The following words line up strikingly with fields/lump ranges from the `LEVEL0.WAD` `0x60` header.
- Blocks are ordered by on-disc file number, not native `levelId`; `LEVEL21.WAD` occupies file position 21 even though it self-reports level id 30.
- The pre-level region contains global asset directory data.

### Older interpretation: seven `0x800` slots

The original retail read was described as seven sectors per `0x3800` level block:

| apparent slot | relative offset | older label |
|---|---|---|
| 0 | `+0x0000` | LEVEL |
| 1 | `+0x0800` | AUDIO |
| 2 | `+0x1000` | unknown |
| 3 | `+0x1800` | unknown |
| 4 | `+0x2000` | SCENE |
| 5 | `+0x2800` | unknown |
| 6 | `+0x3000` | unknown |

That interpretation used `RC2.HDR + 0x5804 + n*0x3800` as the apparent level start.

### Reconciliation result

Public-format header sizes suggested a simpler explanation of the same `0x3800` stride, and the 2026-09-18 authority probe confirmed it:

```text
LEVEL header 0x0060 -> padded to 0x0800 (1 sector)
AUDIO header 0x1018 -> padded to 0x1800 (3 sectors)
SCENE header 0x137c -> padded to 0x1800 (3 sectors)
                                      --------
                                      0x3800
```

That predicted the following boundaries:

```text
RC2.HDR + 0x5800  LEVEL0 header (first word should be 0x00000060)
RC2.HDR + 0x5804  LEVEL0 absolute LBA (observed 0x0013a098)
RC2.HDR + 0x6000  AUDIO0 header (first word should be 0x00001018)
RC2.HDR + 0x7800  SCENE0 header (first word should be 0x0000137c)
RC2.HDR + 0x9000  next 0x3800 block
```

The bounded authority read returned exactly those row-0 words, and an all-row census found the same `0x60 / 0x1018 / 0x137c` starts on 27/27 rows. The apparent unknown slots 2/3 and 5/6 are therefore continuation sectors of the padded AUDIO/SCENE headers, not separate semantic records. This settles row packing only; inner AUDIO/SCENE field semantics and deeper loader behaviour remain separate work.

See [`GC_RC2_HDR_RECONCILIATION.md`](GC_RC2_HDR_RECONCILIATION.md) for the full argument.

## Boot executable

`SCUS_972.68` is ELF32 LE MIPS `ET_EXEC`, entry `0x00131ae8`, with two `PT_LOAD` segments and retained section headers. Full ELF report: [`generated/rac2-ntscu-v1.01.stage0.md`](generated/rac2-ntscu-v1.01.stage0.md).

An ASCII-string scan found loader/CD-ROM strings but no contiguous `LEVEL%d.WAD` filename template. Together with the LBA directory evidence, this supports an engine path that can address level assets through `RC2.HDR` rather than needing to construct ISO-9660 filenames at runtime. A deeper executable loader trace remains future work.

## Current status

- Confirmed: all 27 `LEVEL<n>.WAD` files exist and the authority build uses the retail-backed `0x60` GC outer header described in [`GC_LEVEL_WAD.md`](GC_LEVEL_WAD.md).
- Confirmed: file index is not always native level id; use [`GC_LEVEL_CATALOGUE.md`](GC_LEVEL_CATALOGUE.md) rather than assuming identity.
- Confirmed: `RC2.HDR` contains repeated per-file-number LBA/directory data with `0x3800` stride and points at real retail assets.
- **Confirmed by bounded authority probe (2026-09-18):** the `0x3800` block is three padded headers beginning at `+0x5800`; `0x60 / 0x1018 / 0x137c` starts reproduce on all 27 file-number rows.
- No longer a future target: native LEVEL content such as collision, tfrags, textures, TIEs, shrubs, Mobies, sky and level settings now has dedicated retail-backed research/packages. See [`README.md`](README.md) for the subsystem index.
- Still open here: inner `RC2.HDR` field semantics, the global header region, AUDIO/SCENE internals and deeper executable loader behaviour.