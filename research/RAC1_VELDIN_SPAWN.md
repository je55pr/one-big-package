# R&C1 Veldin player-spawn archaeology

Authority: retail NTSC-U original, `SCUS-97199`; ISO SHA-256 `ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d`; executable SHA-256 `e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9`.

## What is proved

The level-settings ship tuple is **not** a valid player-start proxy on Veldin level 0. Retail level 0 decodes:

- settings ship: native `(20, 20, 20)`, yaw `0`;
- the unique class-0 authored Moby, gameplay instance index 0: native `(132.09, 115.48, 31.43)`, Z rotation `0.6627015`, scale `1`.

`Rac1VeldinSpawnTests` reconstructs retail tfrags and collision and classifies both points against their horizontal footprints. `(20,20,20)` lies outside both static terrain and collision; the class-0 point lies inside both. This is stronger than the older level-settings anomaly observation because the comparison is made against decoded playable static data in the same retail run.

The two points are `147.6863` native world units apart. Veldin is therefore not a small ship-versus-Ratchet offset of the sort seen on ordinary arrival planets.

Every native level 0..18 contains exactly one authored class-0 Moby and it is always gameplay instance index 0. No level has a ship tuple exactly coincident with that class-0 placement. Levels 1..18 keep the two points nearby, at `9.1900..25.1423` units separation; level 0 is the unique extreme at `147.6863`.

These facts support Jess's remembered “remote Veldin ship” story only at the data-layout level: Veldin really does carry an anomalous remote ship tuple. They do **not** establish why the retail game authored it that way, so the remembered developer explanation remains a lead, not authority.

## Runtime evidence

Existing independent RAC1 archaeology already identifies class `0` as Ratchet on all 19 authority levels: the invariant 111-joint body has exactly one authored placement per level, always instance `0`, and uses the dedicated Ratchet animation table. A read-only PCSX2/PINE witness further observes the active class-0 Moby running Ratchet's standing/idle sequence selectors during untouched Veldin gameplay.
The executable probe in `tools/rac1-veldin-spawn-probe.mjs` independently pins the live-Moby storage family. `0x0020C538` and `0x0020C550` advance the live pool by `0x100`; `0x0020C5F4` initializes a `0x100`-byte record. This agrees with the previously recovered live-Moby layout where runtime `+0x10` is position and `+0xF0` is rotation state.

This establishes that the authored class-0 placement and the active Ratchet runtime object belong to the expected two representations, but it does **not yet prove the initial-transform dataflow between them**. The remaining archaeology target is the retail level-population path that consumes the authored `0x78` record and seeds the live class-0 Moby, ideally pinning authored `+0x18/+0x30..+0x44` to live class/position/rotation fields directly.

## Promotion boundary

Do not use `RuntimeWorld.Ship` as Ratchet's start on RAC1. The current shared runtime contract names that field as the player entry point even though retail Veldin disproves that equivalence.

No RAC1 player-start provider is promoted in this checkpoint because the authored-record to live-player transform link is still missing. Once that link is proved, the natural RAC1-local provider is the unique class-0 authored placement, converted from native Z-up to OBP Y-up with the existing Moby rotation conversion. Passing that distinct start through `RuntimeWorld` would require a new `OBP.Runtime` contract or equivalent game-layer wiring, which is outside this task's write lease and should be a manager follow-up rather than an importer fallback rule.

## Reproduction

`tools/rac1-veldin-spawn-probe.mjs` hashes the authority ISO, extracts `SCUS_971.99` in memory, verifies exact executable instruction words, and emits only hashes/addresses/meanings to `research/generated/rac1-veldin-spawn/executable-evidence.json`. No retail bytes are committed.

`Rac1VeldinSpawnTests` is retail-gated by `OBP_RAC1_ISO` and verifies the Veldin footprint anomaly plus the all-level class-0/ship separation census.
