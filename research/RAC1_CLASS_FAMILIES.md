# Ratchet & Clank 1 native class families

Authority: `rac1-ntscu-original` (`SCUS-97199`), all 19 retail level cores.

This note records direct retail validation of the three class-table families in the common 0xbc-byte level-core index. Public Wrench structures are used only as correlation aids; the classifications below were promoted only after the retail ranges and payloads survived deterministic validation across the complete R&C1 level catalogue.

## Core-index tables

The native core-index ArrayRange fields at `0x18`, `0x20`, and `0x28` are structurally valid on all 19 levels. Their entries use the same 0x20/0x20/0x30-byte table records later documented for Moby/Tie/Shrub class tables. Every table range lies inside the core index, every nonzero class asset offset lies inside decompressed core data, and every 16-byte texture map refers only to ids inside the corresponding native texture table.

Retail totals:

| core field | entries | direct payload result | evolution classification |
|---|---:|---|---|
| `0x18` | 3,641 | common 0x48 Moby class header, but R&C1 packet generation | **redesigned after R&C1** |
| `0x20` | 1,804 | R&C1-specific class header; shared tie packet grammar | **header redesigned, packet grammar shared** |
| `0x28` | 551 | later shrub class/header + VIF grammar validates directly | **binary-compatible at current decoded layer** |

No game-specific semantic names are inferred from the raw class ids.

## `0x20`: static instanced / Tie family

A naive Going Commando header interpretation fails because the packet-count bytes moved between generations. R&C1 stores the three packet counts at class-header `0x20..0x22`; GC/UYA/DL store them at `0x0c..0x0e`. R&C1's class header is 0x70 bytes (`RacTieClassHeader` in Wrench) rather than the later 0x80-byte form.

Using only the R&C1 header locations while keeping the existing packet walk:

- 1,804 / 1,804 retail classes validate.
- 25,385 packet headers/ranges validate.
- 127,762 strips validate through the shared packet/event grammar.
- recovered LOD0 geometry totals 1,062,775 triangles.
- no recovered triangle uses an out-of-range class-local texture slot.

This is strong evidence that the class metadata header was redesigned for Going Commando while the underlying tie packet representation survived.

## `0x28`: decorative / Shrub family

R&C1 retail payloads directly satisfy the later shrub class grammar. The class header's `oClass` word agrees with the containing class-table id for all 551 entries tested.

Complete retail census:

- 551 / 551 classes validate.
- 5,374 VIF packets validate.
- 228,315 decoded vertices.
- 190,749 decoded triangles.
- zero out-of-range texture/material references.

At the currently decoded geometry layer, R&C1 and GC can share one shrub codec without an R&C1 special case.

## `0x18`: dynamic / Moby family

The high-level class header is recognisably the same 0x48-byte family used later, but byte `0x0b` frequently carries nonzero R&C1 values (including `0xff`) where the GC-specific decoder expects its newer packet generation. Public Wrench independently models this explicitly as `MobyFormat::RAC1`, and notes that some GC museum classes can retain that older format.

Therefore OBP must not claim the existing GC Moby packet decoder is R&C1-compatible. R&C1 Moby geometry will get an explicit RAC1 packet-path implementation and retail census before it is promoted into `OBPWorld`.

## OBP policy

- Preserve native class ids and table offsets as provenance.
- Share byte codecs only where retail validation proves the representation is shared.
- Keep R&C1 Tie header parsing separate from GC even though the packet walk is shared.
- Treat Shrub as a shared RC codec at the current decoded layer.
- Treat R&C1 Moby as a distinct packet generation until independently recovered.
- Do not commit extracted retail class bytes, textures, or geometry.
