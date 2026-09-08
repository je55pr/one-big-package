# UYA retail gameplay block census

**Authority:** `rac3-ntscu-original` / `SCUS-97353`
**Retail ISO SHA-256:** `d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444`

This is a semantic-free structural census of the first 32 little-endian words (`0x00..0x7c`) in each decompressed UYA main-level gameplay lump. It exists to separate byte-level regularities from later naming work.

## Strong all-level result

All **51** observed retail main-level gameplay lumps have:

- all **32 / 32** header words non-zero;
- all **32 / 32** values in-range as pointers into the same decompressed gameplay lump;
- all **32 / 32** pointers unique within the row;
- no row requiring parser leniency or pointer repair.

Therefore the earlier Veldin observation of 32 non-zero top-level pointers is a stable all-level RAC3 structure, not a one-level accident.

The catalogue still depends on the current candidate hidden-table discovery path. The bounded LBA-1001 bytes are retail-verified; executable provenance of that address remains provisional.
## Census method

`reference-ts/packages/uya-gameplay-census/src/index.ts` treats each non-zero header word only as a candidate top-level pointer. For each unique pointer it records:

- header slot and raw pointer value;
- the apparent extent to the next greater top-level pointer;
- SHA-256 of that apparent extent;
- the first eight raw `u32` words where available;
- the first raw `s32` word;
- arithmetic stride hypotheses only when a small positive first word divides the apparent extent under a small set of possible header sizes.

An apparent extent is a structural partition between top-level pointers, **not** an asserted native block size. A stride hypothesis is a divisibility observation, **not** a structure declaration.

All-level driver:

`reference-ts/tools/uya-gameplay-block-census.mjs`

Machine-readable output:

`research/generated/rac3-ntscu-original.uya-gameplay-block-census.json`

Generated file SHA-256: `bb060bc058ac987cf3992dbfa8fa8d6791a0014271281aad35da88f4097b572e`.
## Apparent extent ranges by raw header slot

Every slot is present on all 51 rows. The ranges below are bytes from that pointer to the next greater top-level pointer in the same row.

| slot | apparent extent min..max |
|---:|---:|
| `+0x00` | 132..164 |
| `+0x04` | 96..1,440 |
| `+0x08` | 208..624 |
| `+0x0c` | 16..6,208 |
| `+0x10` | 221,628..231,836 |
| `+0x14` | 221,632..231,840 |
| `+0x18` | 242,448..253,168 |
| `+0x1c` | 243,648..254,896 |
| `+0x20` | 238,176..248,800 |
| `+0x24` | 236,064..247,184 |
| `+0x28` | 233,072..245,264 |
| `+0x2c` | 203,456..212,784 |
| `+0x30` | 16..416 |
| `+0x34` | 32..858,736 |
| `+0x38` | 32..1,664 |
| `+0x3c` | 16..176 |
| slot | apparent extent min..max |
|---:|---:|
| `+0x40` | 16..557,552 |
| `+0x44` | 32..1,616 |
| `+0x48` | 384..1,264 |
| `+0x4c` | 16..183,760 |
| `+0x50` | 784..2,560 |
| `+0x54` | 16..416 |
| `+0x58` | 16..2,032 |
| `+0x5c` | 64..9,264 |
| `+0x60` | 704..614,384 |
| `+0x64` | 16..24,192 |
| `+0x68` | 16..39,824 |
| `+0x6c` | 16..272 |
| `+0x70` | 16..1,552 |
| `+0x74` | 18,480..18,608 |
| `+0x78` | 16..132,080 |
| `+0x7c` | 52..45,232 |

These size ranges are intentionally not used to name the unknown blocks.

## Existing compatibility cross-references

Separate retail/shared work already gives structural compatibility evidence for a subset of these raw slots. This section is a cross-reference only; it does not rename the remaining slots.

- `+0x34`: TIE placement block under the shared GC/UYA instance reader.
- `+0x40`: shrub placement block under the shared GC/UYA instance reader.
- `+0x48..+0x64`: the Moby/PVar slice documented in `UYA_RETAIL_PVAR_COMPATIBILITY.md`.
The first 32-word census deliberately stops at `+0x7c`. Current shared gameplay code has GC-derived leads beyond that boundary (for example lighting/environment structures), but those offsets should receive their own UYA prerequisite census before being promoted as UYA evidence.

## Why this matters

The complete, stable pointer surface gives executable archaeology a bounded set of targets. Instead of searching arbitrary gameplay bytes for plausible structures, future work can trace loader reads of specific header offsets and then correlate the loaded block with all 51 retail shapes/hashes.

The highest-value unresolved destinations remain the neutral `OBPWorld` fields that are currently empty for UYA:

- `splines`
- `volumes`
- `spawnPoints`
- authored/runtime instance state beyond flattened render geometry

Do not assign any of those names to an unknown slot solely from stride/size resemblance.

## Next proof steps

1. Extend the semantic-free frontier beyond `+0x7c` and determine where the top-level pointer table actually ends.
2. Use UYA executable/loaded-overlay dataflow to identify consumers of individual header slots.
3. Once a consumer establishes a structure, add a strict parser and all-row prerequisite census before mapping it into `OBPWorld`.
4. Keep public Wrench layouts as search/naming leads only until retail/native evidence agrees.
