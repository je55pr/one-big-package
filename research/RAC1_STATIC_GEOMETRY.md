# R&C1 static terrain / tfrag retail census

Authority build: `rac1-ntscu-original` / `SCUS-97199` / payload SHA-256 `ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d`.

This document records direct retail validation of the static-terrain block reached through:

```text
raw disc index @ LBA 1500
→ native 0x2434 level header
→ positional outer range 0
→ R&C1 11-range level-data directory
→ core index + WAD-LZ core data
→ core-index offset 0x08
→ static terrain
```

The purpose of this census is specifically to avoid declaring R&C1 terrain to be a Going Commando format because it looks similar. The byte structure was tested independently across every R&C1 retail level first.

## Direct findings

Every one of the 19 authority levels has a static-terrain block at core-index word `0x08`. The block begins with the same structural family already decoded by OBP's Going Commando tfrag work:

- `s32 tableOffset`, always `0x40` in this R&C1 census;
- `s32 tfragCount`;
- two additional header words preserved by the codec;
- `tfragCount` fixed `0x40`-byte fragment headers;
- per-fragment VIF command lists for LOD2/common/LOD1/LOD01/LOD0;
- static positions, vertex-info/UVs, strips/indices, texture references and baked colour data.

The all-level structural probe visited **20,016 / 20,016** native fragments with **zero structural failures**. In particular:

- LOD2 lists use the expected two-UNPACK grammar;
- common lists use the expected four-UNPACK grammar and STROW base position;
- LOD1 lists use the expected two-UNPACK grammar;
- LOD01 and LOD0 use the same observed VIF packet families, including legitimate empty/base-only variants.

A second probe performed actual geometry recovery using the same numeric rules as the existing GC decoder rather than merely checking headers. Across all 19 levels:

- recovered vertices: **1,090,494**;
- recovered triangles: **1,090,454**;
- sum of native per-fragment declared triangles: **1,090,454**;
- geometry-decoding failures: **0**;
- recovered triangle count equals the native declaration on **19 / 19 levels**.

The decoded position bounds are coherent world-scale volumes for every level. This is substantially stronger evidence than a shared public-tool entry point.

`research/generated/rac1-static-geometry-census.json` contains the exact per-level counts, block lengths, texture-id ranges and native Z-up bounds.

## Per-level summary

| Native id | Tfrags | Recovered vertices | Recovered triangles | Declared triangles | Invalid |
|---:|---:|---:|---:|---:|---:|
| 0 | 460 | 24,758 | 24,520 | 24,520 | 0 |
| 1 | 1,004 | 55,392 | 61,919 | 61,919 | 0 |
| 2 | 695 | 41,242 | 41,743 | 41,743 | 0 |
| 3 | 1,006 | 59,894 | 61,624 | 61,624 | 0 |
| 4 | 709 | 41,416 | 43,962 | 43,962 | 0 |
| 5 | 847 | 56,545 | 63,344 | 63,344 | 0 |
| 6 | 2,144 | 117,289 | 115,784 | 115,784 | 0 |
| 7 | 1,027 | 46,977 | 42,860 | 42,860 | 0 |
| 8 | 454 | 27,487 | 27,548 | 27,548 | 0 |
| 9 | 444 | 31,132 | 31,532 | 31,532 | 0 |
| 10 | 892 | 53,105 | 51,460 | 51,460 | 0 |
| 11 | 1,170 | 51,944 | 52,900 | 52,900 | 0 |
| 12 | 1,108 | 77,457 | 80,160 | 80,160 | 0 |
| 13 | 1,096 | 70,449 | 70,444 | 70,444 | 0 |
| 14 | 961 | 53,251 | 52,748 | 52,748 | 0 |
| 15 | 2,119 | 83,984 | 78,946 | 78,946 | 0 |
| 16 | 1,769 | 69,334 | 64,466 | 64,466 | 0 |
| 17 | 967 | 68,052 | 63,144 | 63,144 | 0 |
| 18 | 1,144 | 60,786 | 61,350 | 61,350 | 0 |

The recovered vertex total is not expected to equal the sum of the one-byte native `vert_count` header field: the renderer-facing vertex-info arrays include the LOD/common split and are the actual inputs to geometry recovery. The triangle declaration provides a much stronger end-to-end invariant because the reconstructed strip topology matches it exactly.

## Public corroboration

Pinned public comparison: `chaoticgd/wrench` commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`.

- `src/engine/tfrag_low.cpp` exposes one `read_tfrags(Buffer, Game)` path. In the inspected read path, the fixed table/header and VIF structures are read uniformly; `Game` remains available to the low-level API rather than proving byte identity by itself.
- `src/wrenchbuild/level/tfrags_asset.cpp` routes R&C1/GC/UYA/DL through the same semantic asset entry point.

Those are corroboration only. The classification below is based on the retail census above.

## R&C1 ↔ GC classification

For static terrain, current evidence supports:

**identical concept / identical recovered binary structure for the verified tfrag/VIF fields**.

That classification is intentionally narrower than claiming every unused flag or renderer-side behaviour is identical. The recovered table/header layout, VIF array grammar, position/UV scaling, strip topology and texture-id linkage are directly compatible. Unknown/unused fields remain native metadata until separately studied.

OBP now exposes a neutral `rc-tfrag` contract. The older `gc-tfrag` package remains the implementation home temporarily to avoid needless churn of the already-verified GC path; the neutral facade records that the codec is no longer GC-owned conceptually.

## Next questions

1. Validate R&C1 level texture tables and GS/CLUT storage directly rather than assuming GC texture semantics.
2. Correlate core-index `0x0c` and `0x10` independently (public leads: occlusion and sky).
3. Combine static terrain + collision into the first deterministic R&C1 `ObpWorld` summary.
4. Only then choose a first rendered planet target based on recovered complexity rather than game-memory preference.
