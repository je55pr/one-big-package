# Going Commando disc and level loading

Authority: NTSC-U v1.01 `SCUS-97268`, build `rac2-ntscu-v1.01`.
Retail ISO SHA-256: `9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5`.

The ISO-9660 filesystem has the root and `/G/`. Numbered LEVEL, AUDIO and
SCENE WADs each span 0..26; shared MISC/HUD/GADGET/ARMOR/BONUS/SPACE/SCENE/AUDIO
and three MPEG WADs are separate. SCENE21/24/25 are 4,988-byte stubs. The
complete inventory and ELF metadata remain in
[the authority report](generated/rac2-ntscu-v1.01.stage0.json).

## File number and native identity

File number is not always level ID: LEVEL21 reports native ID 30. Use
[GC_LEVEL_CATALOGUE.md](GC_LEVEL_CATALOGUE.md) and `GcPlanetCatalogue`.
[GC_LEVEL_WAD.md](GC_LEVEL_WAD.md) records the retail `0x60` outer header;
[GC_LEVEL_CORE.md](GC_LEVEL_CORE.md) records the primary decoded core.

## RC2.HDR packing

`RC2.HDR` is 408,444 bytes at LBA 1001. For file n,
`block(n) = 0x5800 + n * 0x3800`:

| Relative offset | Header | Actual size | Sector-padded span |
|---:|---|---:|---:|
| `+0x0000` | LEVEL | `0x60` | `0x800` |
| `+0x0800` | AUDIO | `0x1018` | `0x1800` |
| `+0x2000` | SCENE | `0x137c` | `0x1800` |

The bounded authority probe reproduced these starts on all 27 rows. Row 0
has `0x60` at `0x5800`, LEVEL0's absolute LBA `0x13a098` at `0x5804`,
`0x1018` at `0x6000`, `0x137c` at `0x7800`, and next LEVEL at `0x9000`.
The seven sectors are three padded headers (1+3+3), not seven semantic slots.
The header begins at `0x5800`; starting at `0x5804` drops its size word.

This establishes row packing, not every AUDIO/SCENE field name, global-region
meaning or executable loader path. `SCUS_972.68` is ELF32 LE MIPS ET_EXEC,
entry `0x131ae8`, with two PT_LOAD segments. The directory observations support
LBA-oriented asset lookup, but a deeper loader trace remains separate work.

## Audio and current validation

[GC_AUDIO.md](GC_AUDIO.md) contains the bounded container/codec/remap contracts.
The initial audio choice used this authority's file-addressable containers,
known bank ranges and retained remap fields; it did not establish trilogy-wide
audio equivalence. Runtime and parser ownership follow the native architecture.
Use the current C# tests and `tools/test-retail.ps1`, not retired browser tools.
