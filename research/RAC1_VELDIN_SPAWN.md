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

Existing independent RAC1 archaeology identifies class `0` as Ratchet on all 19 authority levels: the invariant 111-joint body has exactly one authored placement per level, always instance `0`, and uses the dedicated Ratchet animation table. A read-only PCSX2/PINE witness further observes the active class-0 Moby running Ratchet's standing/idle sequence selectors during untouched Veldin gameplay.

Generation-3 archaeology closed the missing authored-placement -> live-Moby bridge from a read-only PCSX2 savestate of untouched retail Veldin. The relevant population routine is in the **loaded Veldin EE image**, not the corresponding boot-ELF bytes: Veldin has overlaid that address range by the time gameplay is live. The loaded routine proves the following dataflow directly:

- `0x00242828` reads the gameplay Moby-block relative pointer from gameplay `+0x44`;
- `0x00242878..0x00242890` reads each authored record's declared size and computes the next source record as `current + declaredSize`; retail authored records declare `0x78`;
- `0x00242A54..0x00242A70` forms the live destination as `livePoolBase + (emittedIndex << 8)` and reads authored `oClass` from the current record `+0x18`;
- `0x00242A7C` calls loaded live-Moby initializer `0x0024E930`; that initializer supplies size `0x100` (`0x0024E934`) and writes the class to live `+0xA6` (`0x0024E9B8`);
- after the class read, the same source cursor reaches authored scale `+0x1C`, copied to live `+0x2C`;
- authored position `+0x30..+0x38` is copied directly to live `+0x10..+0x18` at `0x00242AD4..0x00242AEC`;
- authored Euler `+0x3C..+0x44` is copied directly to live `+0x40..+0x48` at `0x00242AF0..0x00242B08`;
- `0x00242C48` advances the authored cursor to the previously computed record end before the population loop continues.

The captured live pool starts at `0x01845E80`; emitted slot `0` there is class `0`. Independent read-only Ratchet animation archaeology had already pinned that same `0x01845E80` live Moby as the active player object on Veldin. Thus retail now directly proves that authored gameplay Moby instance `0` / class `0` seeds live Ratchet's initial scale, position and rotation. The earlier boot-ELF `0x100` signatures remain useful corroboration of the live-Moby storage family, but they are not the Veldin population-path authority.

## Promotion boundary

Do not use `RuntimeWorld.Ship` as Ratchet's start on RAC1. The current shared runtime contract documents that field as the native ship park point, and retail Veldin proves the ship tuple is not the player start.

`OBP.RAC1.Player.Rac1PlayerStartProvider` now owns the strict RAC1-local rule supported by the retail dataflow: select the single authored class-0 placement, require authored instance index `0`, and convert its native Z-up placement transform into an OBP Y-up `RuntimeObjectTransform`. It does not mutate or reinterpret `RuntimeWorld.Ship`.

Passing this distinct start through the loaded world/game path still requires an explicit player-start contract or equivalent game-layer wiring outside this task's write lease. That is the manager follow-up; there is deliberately no ship fallback.

## Reproduction

`tools/rac1-veldin-spawn-probe.mjs` hashes the authority ISO, extracts `SCUS_971.99` in memory, verifies the boot-image live-Moby signatures, and records the exact loaded-Veldin overlay signatures/mapping above. Supplying `--ee-memory <32-MiB-capture>` (or `--ee-memory -` on stdin) re-verifies every loaded-overlay word against an external read-only EE-memory capture. The generated JSON contains only hashes, addresses, instruction words, meanings and field mappings; no retail payload is committed.

`tools/rac1-veldin-spawn-probe.ps1` accepts optional `-EeMemoryPath` for that loaded-image verification and then runs the focused retail tests. `Rac1VeldinSpawnTests` is gated by `OBP_RAC1_ISO`; it verifies the Veldin footprint anomaly, the all-level class-0/ship separation census, and that `Rac1PlayerStartProvider` returns the authored Ratchet transform with an equivalent OBP Y-up matrix rather than the ship tuple.
