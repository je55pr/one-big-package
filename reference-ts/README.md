# OBP — TypeScript reference implementation

This is the original browser/Node implementation of One Big Package. As of the
Godot + C# migration (see [`../docs/MIGRATION.md`](../docs/MIGRATION.md)) it is
the **reference / archaeology implementation and comparison oracle** — not the
production runtime. It is not deleted: the native C# port is validated against
these outputs subsystem by subsystem, and much of the tooling here stays useful
for retail archaeology.

Nothing in here changed in the move except two test paths (`../research` →
`../../research`, since `research/` stays at the repo root).

## Run it

```bash
cd reference-ts
npm install
npm run check      # tsc build + node --test + fixture validation + capture bundle
npm run serve      # WebGL2 debug viewer on :4173
```

- `packages/` — the fine-grained decoders (`gc-level-wad`, `gc-level-core`,
  `wad-lz`, `rc-collision`, `gc-tfrag`, `gc-tie`, `gc-shrub`, `gc-moby`,
  `gc-sky`, `gc-level-settings`, `iso9660`, `ps2-disc`, `elf32`, `ps2-vif`,
  `ps2-texture`, `importer-*`, …).
- `apps/viewer/` — the raw-WebGL2 debug viewer.
- `tools/gc-world.mjs` — assemble one Going Commando level into an `OBPWorld`
  JSON straight from a retail ISO (the current evidence-backed path).
- `tests/*.test.mjs` — the deterministic suite (130 tests).

See [`../docs/TS_REFERENCE_BASELINE.md`](../docs/TS_REFERENCE_BASELINE.md) for the
numbers the native port must reproduce.
