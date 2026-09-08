# Going Commando level catalogue

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

Machine-generated: [`generated/rac2-level-catalogue.json`](generated/rac2-level-catalogue.json).
Regenerate:

```
node tools/gc-level-wad.mjs "<GC iso>" --out research/generated/rac2-level-catalogue.json --md
```

Every row below is derived from the WAD's own bytes: `levelId` is the u32 at
`LEVEL<n>.WAD` offset `0x08` (see [`GC_LEVEL_WAD.md`](GC_LEVEL_WAD.md)). Human
planet names are **not** included — the executable has `"%s, Planet %s"` format
strings but the planet-name table has not been located yet, and inventing names
is out of policy.

## Native level id → container file

| Native `levelId` | Container file | File size (B) | Lumps | `unknown0x0c` | Evidence |
|---|---|---|---|---|---|
| 0 | `/G/LEVEL0.WAD` | 15,467,136 | 4 | 3 | header `levelId` |
| 1 | `/G/LEVEL1.WAD` | 21,890,388 | 8 | 0 | header `levelId` |
| 2 | `/G/LEVEL2.WAD` | 20,712,440 | 8 | 0 | header `levelId` |
| 3 | `/G/LEVEL3.WAD` | 17,643,616 | 4 | 0 | header `levelId` |
| 4 | `/G/LEVEL4.WAD` | 24,713,692 | 8 | 4 | header `levelId` |
| 5 | `/G/LEVEL5.WAD` | 16,685,776 | 4 | 0 | header `levelId` |
| 6 | `/G/LEVEL6.WAD` | 19,411,936 | 4 | 3 | header `levelId` |
| 7 | `/G/LEVEL7.WAD` | 22,281,520 | 6 | 0 | header `levelId` |
| 8 | `/G/LEVEL8.WAD` | 20,602,928 | 6 | 0 | header `levelId` |
| 9 | `/G/LEVEL9.WAD` | 18,821,856 | 4 | 0 | header `levelId` |
| 10 | `/G/LEVEL10.WAD` | 14,395,584 | 3 | 0 | header `levelId` |
| 11 | `/G/LEVEL11.WAD` | 22,379,168 | 8 | 0 | header `levelId` |
| 12 | `/G/LEVEL12.WAD` | 17,556,112 | 4 | 3 | header `levelId` |
| 13 | `/G/LEVEL13.WAD` | 17,982,128 | 4 | 0 | header `levelId` |
| 14 | `/G/LEVEL14.WAD` | 17,356,704 | 4 | 3 | header `levelId` |
| 15 | `/G/LEVEL15.WAD` | 14,095,808 | 4 | 0 | header `levelId` |
| 16 | `/G/LEVEL16.WAD` | 18,357,216 | 4 | 0 | header `levelId` |
| 17 | `/G/LEVEL17.WAD` | 16,748,576 | 4 | 4 | header `levelId` |
| 18 | `/G/LEVEL18.WAD` | 17,691,328 | 4 | 0 | header `levelId` |
| 19 | `/G/LEVEL19.WAD` | 25,119,448 | 10 | 0 | header `levelId` |
| 20 | `/G/LEVEL20.WAD` | 21,377,628 | 10 | 0 | header `levelId` |
| 22 | `/G/LEVEL22.WAD` | 13,616,432 | 4 | 0 | header `levelId` |
| 23 | `/G/LEVEL23.WAD` | 12,603,856 | 4 | 0 | header `levelId` |
| 24 | `/G/LEVEL24.WAD` | 8,350,423 | 3 | 0 | header `levelId` |
| 25 | `/G/LEVEL25.WAD` | 8,642,189 | 3 | 0 | header `levelId` |
| 26 | `/G/LEVEL26.WAD` | 10,990,224 | 4 | 0 | header `levelId` |
| **30** | `/G/LEVEL21.WAD` | 13,359,328 | 4 | 0 | header `levelId` (file index 21 ≠ id) |

Notes:

- **`levelId` 21 does not exist**; file `LEVEL21.WAD` carries `levelId = 30`. So
  the on-disc file numbering is not the engine's level id space — a lookup table
  (likely inside `RC2.HDR` or the executable) resolves id → file. That table is
  not yet decoded ([`GC_LEVEL_LOADING.md`](GC_LEVEL_LOADING.md)).
- `SCENE<n>.WAD` / `AUDIO<n>.WAD` are indexed by the same **file** number `n`
  (not confirmed to be indexed by `levelId`). `SCENE21.WAD`, `SCENE24.WAD`,
  `SCENE25.WAD` are 4,988-byte stubs.
- Lump count / `unknown0x0c` columns are carried so a later pass can correlate
  them with level type (hub vs planet vs arena vs space) once slot semantics are
  established.

## Confidence

| Claim | Status |
|---|---|
| `LEVEL<n>.WAD` is the per-level container | confirmed (outer header decoded, 27/27) |
| `levelId` value at header `+0x08` | confirmed (directly read) |
| `levelId` 30 ↔ file `LEVEL21.WAD` | confirmed (directly read) |
| id → file resolution table location | **unknown** |
| planet / world names | **unknown** (not recoverable yet) |
| `SCENE<n>`/`AUDIO<n>` pairing with `LEVEL<n>` | inferred from parallel numbering only |
