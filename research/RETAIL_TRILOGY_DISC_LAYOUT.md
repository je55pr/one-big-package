# Retail trilogy disc layout — authority validation

Machine-generated per-build reports live in [`research/generated/`](generated/). Their `stage0` filenames are retained as historical artifact names from the development stage in which this bounded disc path was introduced.

| Build | Report | ISO size | Payload SHA-256 |
|---|---|---|---|
| `rac1-ntscu-original` | [`rac1-ntscu-original.stage0.md`](generated/rac1-ntscu-original.stage0.md) | 4,214,095,872 | `ab849fe7…bc40d9d` |
| `rac2-ntscu-v1.01` | [`rac2-ntscu-v1.01.stage0.md`](generated/rac2-ntscu-v1.01.stage0.md) | 3,828,350,976 | `9db2e33e…a9b1ce5` |
| `rac3-ntscu-original` | [`rac3-ntscu-original.stage0.md`](generated/rac3-ntscu-original.stage0.md) | 4,379,377,664 | `d2bb15c7…8c42444` |

All three local ISOs hash **exactly** to their canonical authority manifests (`research/manifests/*.json`). They were verified 2026-09-06 by `tools/disc-archaeology.mjs --hash` and independently by `sha256sum`.

Regenerate a legacy-format report with:

```text
node tools/disc-archaeology.mjs "<path to the ISO>" --build <buildId> --out-dir research/generated --hash
```

## Bounded source-path result

The common safe source path

```text
ISO-9660 -> SYSTEM.CNF -> PS2 boot ELF -> ELF32 structural validation -> PT_LOAD mapping
```

runs cleanly and deterministically against all three retail authority ISOs. The generated reports capture volume identifier, `SYSTEM.CNF`, boot executable path, normalized serial, executable size, ELF entry point, program headers, PT_LOAD virtual/file-offset ranges and the full ISO-9660 file/directory inventory.

| | rac1 | rac2 (Going Commando) | rac3 (UYA) |
|---|---|---|---|
| ISO-9660 volume id | `RATCHETANDCLANK` | `RATCHETANDCLANK2` | `RATCHETANDCLANK3` |
| `SYSTEM.CNF` `VER` | `1.00` | `1.01` | `1.00` |
| Boot serial | `SCUS-97199` | `SCUS-97268` | `SCUS-97353` |
| Boot ELF entry | `0x0012d728` | `0x00131ae8` | `0x00800008` |
| Boot ELF size | 1,383,028 | 2,618,684 | 771,008 |
| PT_LOAD segments | 1 (`0x00100080`) | 2 (`0x00100080`, `0x01800000`) | 1 (`0x00800000`) |
| ELF section headers | 61 | 71 | 9 |
| ISO-9660 files | **3** | **97** | 44 |
| Level containers in ISO-9660 | none | `LEVEL0.WAD`…`LEVEL26.WAD` (+ `SCENE*`, `AUDIO*`) | none |

## Key finding: only Going Commando exposes its world data through ISO-9660

- **R&C1** publishes just `SYSTEM.CNF`, `SCUS_971.99` and `IOPRP243.IMG` in the ISO-9660 tree (~1.6 MiB). The volume descriptor claims 2,057,664 logical blocks (~4.2 GiB); essentially the rest of the disc is addressed outside ordinary ISO-9660 files and will require raw-LBA/executable-table archaeology.
- **UYA** publishes 44 files, but they are primarily online/DNAS infrastructure, a bundled *Sly 2* demo, the small boot loader and padding. The main game engine/world content is again outside normal ISO-9660 level files; the boot ELF is a 771 KiB bootstrap at VA `0x00800000`.
- **Going Commando** is the outlier: `/G/` contains the numbered level/scene/audio WAD families plus shared containers, and `RC2.HDR` lives at the disc root. Native level data is directly file-addressable through bounded ISO-9660 reads.

This remains the architectural reason GC became the first deep native world target: it offered named, individually range-readable level containers before equivalent R&C1/UYA raw-LBA tables had been decoded.

## Going Commando container groups

Everything below `/G/` unless noted otherwise:

| Family | Members | Current understanding |
|---|---|---|
| Per-level world/gameplay | `LEVEL0.WAD` … `LEVEL26.WAD` | 27 retail containers; outer header and substantial native contents decoded |
| Per-level cutscene | `SCENE0.WAD` … `SCENE26.WAD` | Parallel file-number family; several tiny stubs; internals not yet a main runtime path |
| Per-level audio | `AUDIO0.WAD` … `AUDIO26.WAD` | Parallel file-number family; internals not yet a main runtime path |
| Global/shared | `MISC.WAD`, `HUD.WAD`, `GADGET.WAD`, `ARMOR.WAD`, `BONUS.WAD`, `SPACE.WAD`, `SCENE.WAD`, `AUDIO.WAD` | Shared/global containers; not all decoded |
| FMV | `MPEG.WAD`, `MPEG1.WAD`, `MPEG2.WAD` | Large FMV streams |
| Disc index | `/RC2.HDR` | Engine-facing LBA/directory data; exact per-level padding interpretation is under reconciliation |
| Padding | `/Z6TAIL.DUP` | Dummy/padding tail |

Do **not** assume file number equals native level id. For example, `LEVEL21.WAD` reports native `levelId = 30`. Use [`GC_LEVEL_CATALOGUE.md`](GC_LEVEL_CATALOGUE.md).

The GC authority build uses `header_size == 0x60` across the numbered level WADs. The outer container/range layout is documented in [`GC_LEVEL_WAD.md`](GC_LEVEL_WAD.md); decompressed core/world subsystems now have dedicated retail-backed documents and packages indexed from [`README.md`](README.md).

`RC2.HDR` was initially described as a seven-slot per-level grid. That interpretation is now explicitly under reconciliation; read [`GC_LEVEL_LOADING.md`](GC_LEVEL_LOADING.md) and [`GC_RC2_HDR_RECONCILIATION.md`](GC_RC2_HDR_RECONCILIATION.md) before treating slot labels as authority.

## Tools

- `tools/local-random-access.mjs` — Node `fs`-backed `RandomAccessReader` using bounded reads.
- `tools/disc-archaeology.mjs` — regenerates the historical authority-disc reports above.
- `tools/iso-read.mjs` — bounded hex/`u32` dump of an ISO byte range, ISO-9660 file extent, absolute LBA or boot-ELF virtual address.

For current subsystem status, see [`../docs/CURRENT_STATE.md`](../docs/CURRENT_STATE.md).