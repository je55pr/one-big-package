# TypeScript reference baseline (pre-migration checkpoint)

Recorded at the start of `migration/godot-native-runtime`, from `main` @ `0caa281`.
The TypeScript implementation now moves to [`reference-ts/`](../reference-ts/) and
becomes the **comparison oracle** for the Godot + C# port. These are the numbers
the native implementation must reproduce.

## Build / tests

- `cd reference-ts && npm ci && npm run check`
- **130 tests pass**, 0 fail, 0 skipped (`node --test tests/*.test.mjs`).
- `npm run validate` (fixture manifests) and `npm run build:capture` (Vite bundle) pass.
- Toolchain: Node 22, TypeScript 5.8.3.

## Retail authority

- Going Commando **NTSC-U v1.01**, serial `SCUS-97268`,
  ISO SHA-256 `9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5`
  (3,828,350,976 bytes). Local ISO only — never committed.
- `research/manifests/*.json` hold the canonical authority manifests.

## GC world reconstruction — `reference-ts/tools/gc-world.mjs`

`node tools/gc-world.mjs "<GC iso>" --level <n> --out <file>`

### LEVEL1 — Oozla (the milestone target)

| quantity | value |
|---|---|
| tfrag textures decoded | 77 |
| tfrag blobs | 1 → 401 tfrags, 28,418 verts, 26,192 tris |
| tie instances placed | 1617 / 1617 (91 classes) → 674,252 tris |
| shrub instances placed | 2825 / 2825 (24 classes) → 852,785 tris |
| moby instances placed | 687 / 748 (180 classes) → 325,838 tris |
| moby marker cubes (no decoded mesh) | 48 |
| collision blob 0 | 22,404 octants → 308,108 tris |
| collision blob 1 | 4,210 octants → 20,815 tris |
| collision triangles (total) | **328,923** |
| render meshes / triangles | 301 meshes / **1,881,339** tris |
| materials | 398 |
| sky | 4 shells, 3 textures → 1,694 tris |
| world bounds (OBP Y-up) | min (2.40, 50.11, 204.95) — max (592.55, 163.15, 818.31) |
| `environment.deathHeight` | 0 |
| `environment.fogColor` | (0.0392, 0.1569, 0.1176) |
| `environment.fogNearDistance` / `fogFarDistance` | 25600 / 230400 (fixed-point; viewer ÷ 1024) |
| `environment.isSphericalWorld` | false |

Native ship position for Oozla (from `readGcLevelSettings`, not emitted by the
current `gc-world.mjs` on `main` but read by the package): native
`shipPosition ≈ (278, 411, 115)` → OBP `(278, 115, 411)`, `shipRotationZ ≈ 1.784 rad`.
This is what Phase 16 spawns near.

### LEVEL0 — Aranos (Floating Prison)

| quantity | value |
|---|---|
| tfrags | 833 → 43,613 verts, 33,710 tris |
| tfrag textures | 92 |
| tie instances | 2000 / 2000 (74 classes) → 771,104 tris |
| shrub instances | 523 / 523 (7 classes) → 71,334 tris |
| moby instances | 383 / 472 (187 classes) → 232,692 tris |
| collision blob 0 | 12,079 octants → 81,113 tris |
| render meshes | 266 |
| sky | 6 shells, 5 textures → 3,388 tris |
| `environment.deathHeight` | 0 |

## Deterministic captures

- `reference-ts/tools/capture-viewer.py` drives the browser viewer via injected
  Playwright (`about:blank` + IIFE bundle) → `captures/stage0-synthetic.{png,json}`.
- The GC-level screenshot workflow used a dev-server `/__capture` POST route +
  `window.__viewer` camera handle (see `reference-ts/tools/dev-server.mjs`).
- The native (Godot) capture workflow re-establishes this loop — see
  [`MIGRATION.md`](MIGRATION.md).

## What the port must NOT change

- The GC→OBP coordinate rule: native Z-up `(x, y, z)` → OBP Y-up `(x, z, y)`,
  applied only at the world-assembly boundary.
- The fixed-point / scale conventions (`s16 * scale / 1024` positions,
  `u16 / 4096` UVs, fog distances in 1024ths).
- Which native fields stay uninterpreted (`unknown0x..`, raw instance class ids).
- Retail v1.01 as the only supported build for native world import.
