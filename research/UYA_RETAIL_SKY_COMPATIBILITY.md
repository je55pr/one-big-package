# Retail UYA sky compatibility

This note records direct retail corroboration of the public UYA sky-format lead against the canonical NTSC-U authority (`SCUS-97353`). The public lead remains provenance-distinct from native-loader proof: Wrench supplied the candidate field meanings, while the counts, hashes and structural acceptance below come from retail bytes.

Retail census pipeline `2827474045` / iid 504, job `16353358220`, ran entirely on the self-hosted `Jess-Laptop` runner and passed. The repository suite was **209 / 209 green** in the same job.

## Promoted binary boundary

Across sampled retail rows 1, 8, 20 and 50:

- the shared `0x40` sky header is structurally valid;
- the UYA/DL-family `0x10` shell prefix is valid with `s16` cluster count/flags and two `Vec3s16` rotation vectors;
- the first cluster header begins at shell `+0x10`;
- the shared `0x20` cluster, `0x08` vertex, `0x04` texture-coordinate and `0x04` face layouts remain coherent;
- every sampled face index remains inside its cluster vertex table;
- every sampled non-`0xff` face texture remains inside the declared sky texture table;
- no sampled decoded position is non-finite;
- UYA flag bit 1 is exercised by retail data as a bloom-marked shell on table row 20;
- non-zero angular-velocity fields are exercised by retail data on several rows.

The sampled skies are therefore safe to promote through a UYA-versioned decoder. Static neutral-world rendering may preserve the initial shell pose while retaining native rotation/angular-velocity values as evidence; actual sky animation and bloom rendering remain separate viewer features.

## Four-row census

| table row | public-derived label | sky bytes | SHA-256 | textures | shells | clusters | vertices | triangles |
|---:|---|---:|---|---:|---:|---:|---:|---:|
| 1 | Veldin | 485,248 | `825afb60c18db867575bbaaa2ebe4bc911f242ad815e65c74d4d64cc29b98fb0` | 10 | 8 | 181 | 4,111 | 4,348 |
| 8 | Aquatos | 72,192 | `80db49a942a2e126176705417fc4b8bd4ef541b7d5295830d4ac1efb5a121b2d` | 3 | 3 | 70 | 1,094 | 1,008 |
| 20 | Final Boss | 393,728 | `12cb9fffe757644cee247770ac9bbc0e4da708ecc62ebf7a1c51ab0ab7c7551d` | 6 | 6 | 122 | 2,539 | 2,048 |
| 50 | Bakisi Isles (Split-screen) | 96,448 | `a78d2240bf9fc53905fef3e650bc1ba9e4ad437d395e6d2c0b46ad831130fbaa` | 4 | 5 | 87 | 748 | 496 |

The human-readable names in this table remain public-derived Wrench cross-references; the physical table indices and byte evidence are the retail authority.

## Dynamic-shell evidence

Row 1 is especially useful because its eight-shell sky contains multiple independently moving layers. Observed native angular-velocity vectors include:

- shell 3: `(0, 0, 5)`
- shell 4: `(0, 0, 4)`
- shell 5: `(0, 0, 3)`
- shell 6: `(0, 0, 2)`

Row 20 contains a retail shell with the UYA bloom flag set and also contains non-zero angular velocity. Row 50 contains shells with native angular velocity `(0, 0, 1)`.

These values are retained raw by `packages/uya-sky`; the public Wrench conversion to radians/second is also exposed for comparison, but animation is not required to promote the initial geometry.

## Colour/environment boundary

All four sampled `SkyHeader.colour` values are zero. Consequently, a faithful viewer should not assume that field supplies the visible horizon/clear colour for these levels. The GC/UYA level-settings first-part structure contains separate background and fog colours and should be retail-censused on UYA before the sky dome is visually promoted into normal captures.

## Implementation

- `packages/uya-sky/src/index.ts` — strict UYA-versioned decoder.
- `tests/uya-sky.test.mjs` — synthetic shell-layout, bloom, no-texture and rejection coverage.
- `tools/uya-sky-compat-probe.mjs` — canonical retail census.
- `research/UYA_SKY_FORMAT_LEAD.md` — provenance-separated public format lead.
