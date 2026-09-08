# UYA retail texture compatibility census

Status: retail-confirmed compatibility evidence for the existing GC 16-byte paletted texture reader across four sampled UYA worlds, with an explicit negative boundary for the public-derived `partTextures` and `fxTextures` ranges.

## Authority and run

Retail authority image:

- Ratchet & Clank: Up Your Arsenal NTSC-U original
- serial `SCUS-97353`
- size `4,379,377,664` bytes
- full-image SHA-256 `d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444`
- bounded ToC authority window: LBA 1001, 2 MiB, SHA-256 `a9e3e338df29045222ab0c0c0ba68fe1cff2048add582124f51915bb4ba55e5a`

Local census:

- pipeline `2827249114` / iid `343`
- job `16351957076`
- self-hosted runner `obp-local`
- rows `1, 8, 20, 50`
- full repository check: `195/195` tests passed before the retail probes

The probe independently censused the prerequisites used by `readGcLevelTextures()` and then applied that unchanged GC decoder. A compatibility result therefore requires the table to lie inside the decoded core index, dimensions to be valid, indexed pixels to lie inside the decoded asset stream, the palette to lie inside the observed GS-RAM range, and the unchanged GC reader to emit every independently eligible entry.

## Compatible tables

For every sampled row, the public-derived `tfragTextures`, `mobyTextures`, `tieTextures`, and `shrubTextures` ranges behaved as ordinary GC `TextureEntry[16]` arrays. Every declared entry was eligible and every eligible entry was emitted by the unchanged GC decoder. No sampled entry failed dimension, pixel-range, or palette-range validation.

### Tfrag textures

| Row | Count | Dimensions | Types | RGBA bytes | Aggregate decoded RGBA SHA-256 |
|---:|---:|---|---|---:|---|
| 1 | 109 | 32–128 × 32–128 | `3` | 5,742,592 | `c280d8bec3edf7028982b138bc86b096fda4fac236cf9115133f786695d4c8ba` |
| 8 | 54 | 32–256 × 32–256 | `3` | 3,219,456 | `ecfa45b264fb9614a916a8d2406144dacab7a544369732dd48778f125c1abb7b` |
| 20 | 76 | 64–128 × 64–128 | `3` | 4,882,432 | `aecb800212ff1b285a492a2dd8453238d57e4bc2bb3066d7e48e206e981cc075` |
| 50 | 46 | 64–256 × 64–256 | `3` | 3,162,112 | `a9cae9dc062f18a3fd8e1eb4f0aabc90dcb3c895590efee4a17a2c75169acdda` |

### Moby textures

| Row | Count | Dimensions | Types | RGBA bytes | Aggregate decoded RGBA SHA-256 |
|---:|---:|---|---|---:|---|
| 1 | 143 | 16–128 × 16–128 | `0,1,3` | 6,198,272 | `0411d595b4d711a9d897db52dfde353188b3c869b89efd76d84ef6dc03a31c50` |
| 8 | 154 | 16–256 × 16–256 | `0,1,3` | 5,813,248 | `ff39bdd1c19ef4c18a981977b6618d11d70c7c15617f2b3551b05f1bd769ad4f` |
| 20 | 159 | 16–256 × 16–256 | `0,1,3` | 7,062,528 | `d7d2330aa3c3ea6db80a5c99075a9206c40e24de247892dcef40d190418e4397` |
| 50 | 167 | 32–128 × 32–128 | `0,1,3` | 8,462,336 | `a070d496236aac7d2705f46b7400d35147782c51274c0fbf529cdccd18e0507e` |

### TIE textures

| Row | Count | Dimensions | Types | RGBA bytes | Aggregate decoded RGBA SHA-256 |
|---:|---:|---|---|---:|---|
| 1 | 166 | 32–128 × 32–128 | `3` | 7,622,656 | `3e7dd96fae493a4be28039a195dcdb7afdc0ec2d17f8f9a0b57921e0ba423a4b` |
| 8 | 57 | 32–256 × 32–256 | `3` | 3,182,592 | `7b01201360d7293634534e36a05f71663e1dccdf4ff393a03af2cee5ffcfb712` |
| 20 | 89 | 32–128 × 32–128 | `3` | 4,788,224 | `478c2bfd70ec8422d44759cb86478f61657d3599b66d4faa33e7a79eedcd7efe` |
| 50 | 100 | 32–256 × 32–256 | `3` | 6,184,960 | `b5497beda0f3820b6c52c147041276554d3bddff4e654e43e16d2181ad92dc9c` |

### Shrub textures

| Row | Count | Dimensions | Types | RGBA bytes | Aggregate decoded RGBA SHA-256 |
|---:|---:|---|---|---:|---|
| 1 | 55 | 32–128 × 32–128 | `3` | 2,695,168 | `6ee2996020f6cb7be6562628ddf0995bc0f067029bec8809227f4b19fb4ec28e` |
| 8 | 10 | 32–128 × 32–128 | `3` | 335,872 | `9630a44e0acaa2defd3935cce528f0089d47c55e8386085108b9948dafd0b62f` |
| 20 | 19 | 64–128 × 64–128 | `3` | 901,120 | `c93b853dd1367e029cb7b1f40182920fdb89769aa1faf4b90e07adc081403f8e` |
| 50 | 9 | 64–128 × 64–128 | `3` | 442,368 | `d9eb08337d70f0aa332e594aa26f4f207a72d13a39555c33bd369178f3652578` |

Across those four categories the compatibility census observed `0` invalid dimensions, `0` invalid pixel ranges, and `0` invalid palette ranges on every row. The unchanged GC parser decoded exactly the declared count on every row.

## GS-RAM observations

| Row | Candidate GS-RAM bytes | SHA-256 |
|---:|---:|---|
| 1 | 497,664 | `771e3136efa4b67d594f90ce80e7c231688b327c963ed3188b58550aac951d6d` |
| 8 | 391,424 | `6d5fbbac576b8d41825b425a83ca1ed6dd6eb2e3dfe22b81bab69395951cfa84` |
| 20 | 512,256 | `a3a6871e0e1fe8a1ae1a8715818c7e430c51e2d708d1ebade286ee32f74f13eb` |
| 50 | 422,912 | `a732ab7bd80a0cb43863d771f7f11b51d07e286f9a420d3364ee851e69d6606f` |

## Negative compatibility boundary: part / FX ranges

The public-derived `partTextures` and `fxTextures` ranges do **not** behave as ordinary GC `TextureEntry[16]` arrays in these retail UYA cores.

`partTextures`:

| Row | Declared entries | Invalid dimensions | GC-eligible / decoded |
|---:|---:|---:|---:|
| 1 | 65 | 65 | 0 / 0 |
| 8 | 79 | 79 | 0 / 0 |
| 20 | 65 | 65 | 0 / 0 |
| 50 | 51 | 51 | 0 / 0 |

`fxTextures`:

| Row | Declared entries | Invalid dimensions | GC-eligible / decoded |
|---:|---:|---:|---:|
| 1 | 105 | 104 | 1 / 1 |
| 8 | 102 | 101 | 1 / 1 |
| 20 | 108 | 107 | 1 / 1 |
| 50 | 106 | 105 | 1 / 1 |

The FX rows additionally produce implausible `type` values (`16,32,64,128,256`) and widths up to `31744`. The single eligible-looking record per row should therefore be treated as an accidental structural match, not as evidence that the range is a GC texture table.

Do **not** weaken the GC decoder or reinterpret these ranges merely to make them pass. The correct next step for part/FX is independent format archaeology.

## Interpretation

The existing GC paletted texture path is now retail-confirmed compatible with sampled UYA **tfrag, Moby, TIE, and shrub texture data**, including table-entry layout, decoded-asset offsets, palette addressing into the candidate GS-RAM block, and final RGBA decoding.

This still does not independently prove the public-derived semantic names, the enclosing `0x60 / 0x58 / 0xbc` layering, or native loader provenance of the hidden ToC lead. Those provenance questions remain explicit and separate.

The result is strong enough for reconstruction work to proceed to TIE/shrub geometry and instance placement while reusing the unchanged GC texture decoder for these four categories.
