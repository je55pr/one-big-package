# One Big Package — Current State

_Last refreshed: 2026-09-08._

This document distinguishes the **merged production baseline** from fast-moving work on specialist branches. Detailed format evidence belongs in [`../research/`](../research/README.md); creative possibilities belong in [`PROJECT_VISION.md`](PROJECT_VISION.md) and [`brainstorming/`](brainstorming/README.md).

## Headline

OBP's production direction is **Godot 4 + C#**, with the older TypeScript implementation retained under [`../reference-ts/`](../reference-ts/) as an executable archaeology/equivalence oracle.

The trilogy source/destination/provider architecture now has **two native production providers**: R&C1 and Going Commando. The native app can attach all three supported retail authorities, browse neutral destinations, reconstruct all 19 R&C1 worlds and arbitrary GC worlds through the same `RuntimeWorld`/Godot boundary, and repeatedly enter/leave them without a game-specific host rewrite. The merged cross-game Fusion Lab can also hold multiple provider worlds at once; its first retail proof loads R&C1 level 0 and GC Oozla simultaneously in one Godot scene.

```text
GC retail ISO
  -> bounded disc/build identification
  -> GC native importer
  -> neutral RuntimeWorld
  -> game-neutral Godot world builder
  -> planet selector / repeated load-unload
  -> walk / jump / fly through reconstructed retail worlds
```

## Merged production baseline (`main`)

### Native solution and boundaries

The repository contains:

- `OBP.Core` — shared provenance/math primitives;
- `OBP.IO` — bounded random access, seekable files, subranges, numbered split readers, hashing and authority verification;
- `OBP.PS2` — ISO-9660, `SYSTEM.CNF`, ELF32 and shared PS2 codecs such as WAD-LZ, VIF, textures and collision;
- `OBP.RAC1`, `OBP.RAC2`, `OBP.RAC3` — game-specific native libraries;
- `OBP.Runtime` — engine-independent runtime world data;
- `OBP.Godot` — the game-neutral Godot adapter;
- `game/` — application shell, input, UI/cameras and debug player;
- `OBP.Cli` and `OBP.Tests` — deterministic command-line validation and xUnit coverage.

The architectural rule is unchanged: **Godot hosts OBP; Godot does not define Ratchet's native formats.** Native parsers stay testable without launching the engine.

### Trilogy source / destination / provider layer

Merged `main` treats retail-source ownership and world loading as trilogy-level application concepts rather than GC-specific state. `ObpSourceLibrary` can attach and restore all three primary authorities, `ObpDestination` keeps native destination identity separate from display labels, and `IObpWorldProvider` / `ObpWorldProviderRegistry` route a selected source-game destination to a neutral `RuntimeWorld`. R&C1 and GC are registered native providers; UYA can attach as a source and already has a complete retail TypeScript/reference importer, but its native C# provider is still pending.

### R&C1 native world provider

R&C1 NTSC-U (`SCUS-97199`) is now a second native provider rather than a reference-only world slice. All 19 authority levels load through `Rac1WorldProvider` and the same `ObpWorldProviderRegistry` used by GC. The merged C# path covers retail terrain/textures/collision, level settings, shared RC sky, and the current native TIE/shrub static-instance layer. Retail all-level gates exercise every destination, and `rac1:LEVEL0` has been captured through the generic Godot path with the debug player grounded on reconstructed collision.
### Going Commando native reconstruction and planet hopping

Going Commando NTSC-U v1.01 (`SCUS-97268`) is the deepest merged authority target.

The merged native C# pipeline covers:

- level WAD ranges and WAD-LZ decompression;
- decompressed level core and gameplay structures used by the importer;
- tfrag terrain, including all present terrain chunks rather than chunk 0 only;
- the required bounded VIF subset;
- 8-bit indexed textures, GS CLUT reorder and current alpha handling;
- TIE classes/instances;
- shrub classes/instances;
- Moby classes/instances, current bind-pose/skinning reconstruction, normals and texture-state carry;
- sky shell geometry/textures and current camera-centred backdrop handling;
- level settings including fog/death-height/spherical-world/ship fields;
- octree collision, including chunk collision;
- coordinate normalisation and Godot handedness correction;
- chunked and unchunked level import paths;
- directional lights, point lights, environment sample points and environment transition volumes used by the current runtime lighting/fog path.

All 27 known GC level files have been exercised through the generic importer path. The app has a crude native level/planet selector, can return from a loaded world to that selector and repeatedly change worlds without restarting. Lifecycle stress work verified that world nodes/resources do not simply accumulate on each switch.

`OBP.RAC2.GcIsoLoad` provides the Godot-free disc façade for identification, level import and optional streamed authority verification. `GcWorldImport` terminates GC-specific conversion at `RuntimeWorld`; `OBP.Godot.RuntimeWorldScene` does not depend on `OBP.RAC2`.

### World composition / Fusion Lab

`OBP.Composition` is an engine-neutral placement/alignment layer consumed by the Godot Fusion Lab. Multiple independently reconstructed `RuntimeWorld` values can coexist under separate transforms, be soloed/tinted/inspected, and be aligned from equivalent landmark anchors without modifying either source reconstruction. A deterministic R&C1-level-0 + GC-Oozla composition capture succeeds, and a three-cycle cross-game build/teardown stress run reports zero orphan nodes, an empty composition root after teardown and no post-warmup object/node growth. See [`WORLD_COMPOSITION.md`](WORLD_COMPOSITION.md).
### Debug player / capture harness

The merged runtime includes:

- a provisional `CharacterBody3D` capsule with WASD, mouse look, gravity, jump, respawn and fly/noclip;
- ship-spawn placement from retail settings, with a bounds-centre fallback where no usable native ship point exists;
- movement telemetry and an empirically calibrated debug-controller baseline (not a claim about exact native Ratchet movement);
- deterministic screenshots/capture metadata;
- direct planet launch and multi-planet lifecycle stress arguments.

### TypeScript reference role

[`../reference-ts/`](../reference-ts/) preserves the earlier browser implementation because it contains substantial archaeology and deterministic fixtures. It is useful for:

- byte-for-byte / hash / count equivalence while C# decoders are ported;
- targeted retail probes and research tools;
- public-format cross-checks;
- preserving already-understood behaviour while native code evolves.

Browser `File`/`Blob`, OPFS and the WebGL viewer are therefore **reference implementation details**, not production runtime requirements.

### Local retail-authority infrastructure

The project has a self-hosted Windows GitLab runner tagged `obp-local` with direct access to the three user-owned retail authority images. All three full images have been SHA-256 verified against the pinned manifests.

`.gitlab-ci.yml` defaults to `run_mode: none`. Hosted tests and local retail jobs are explicit opt-ins. Shared local modes cover runner smoke, bounded ISO inventory, trilogy Stage-0 archaeology and full hashes; specialist branches add narrowly scoped retail probes when useful.

This runner is now the preferred agent bridge for retail archaeology when online. See [`LOCAL_RUNNER.md`](LOCAL_RUNNER.md).

## Specialist branch context

These branches continue fast-moving archaeology and implementation around the merged baseline. Branch heads can move quickly; use Git history and the research docs for exact evidence.

### `claude/*`

Claude's `world-composition` work has been integrated into the production composition baseline: the engine-neutral composition core and Godot Fusion Lab route through the shared provider registry and have completed the first cross-game R&C1 + GC composition/lifecycle proof.

### `chatgpt/rc1`

R&C1 archaeology has moved substantially beyond authority probing. Retail-backed work includes:

- the native raw-disc level index / `0x2434` level header path;
- all 19 level cores using the shared WAD-LZ codec;
- shared inner tfrag compatibility validated across the retail set;
- shared octree collision compatibility across all 19 levels;
- shared indexed texture decoding across the retail set;
- native C# `RuntimeWorld` promotion for terrain, textures, collision and level settings;
- the shared RC sky path and native TIE/shrub static-instance layer, now merged into the production R&C1 world;
- ongoing executable-led Moby instance/transform/skinning archaeology.

The specialist branch owns the native evidence and parser promotion. `chatgpt/obp` consumes only sufficiently mature pieces at the neutral provider/integration boundary.

### `chatgpt/gc`

A dedicated GC native-system archaeology branch complements Claude's production implementation. Its remit is gameplay truth rather than another renderer: Moby/PVar semantics, simple interactive objects, paths/grind rails, water/Thermanator, native movement, traversal gadgets, economy/weapons/save state and other systems as retail evidence permits.

### `chatgpt/uya`

UYA work is likewise beyond the old “probe-only” description. Current `main` now contains the complete retail-backed TypeScript/reference RAC3 world importer: all 51 observed main-level rows pass `validateWorld`, with tfrags, textures, collision, TIE/shrub placements, authored static Mobies/PVars, sky and level environment represented in the neutral reference world contract. The outer UYA level/container path remains game-specific and provenance questions remain explicit. The remaining OBP integration gap is promotion of that mature path into the native C# `OBP.RAC3` / `IObpWorldProvider` stack.

### `chatgpt/obp`

This remains the cross-game integration/design branch. Its source/destination/provider work, native R&C1 integration and first Fusion Lab milestone are now part of the production baseline described above. The next large integration target is the native C# UYA `IObpWorldProvider`, using the merged retail reference importer as evidence. Once that provider and an authority-backed Veldin mapping are available, the Fusion Lab can move directly into the planned R&C1-Veldin × UYA-Veldin alignment experiment. See [`TRILOGY_SOURCES.md`](TRILOGY_SOURCES.md) and [`WORLD_COMPOSITION.md`](WORLD_COMPOSITION.md).
## Known gaps / deliberately unfinished areas

Across the project, major work still includes:

- native Ratchet player movement/animation rather than the debug capsule;
- Moby animation and wider class/variant behaviour;
- weapons, damage, AI and combat;
- mission/story state, cutscenes and progression machinery;
- vendors, economy, inventory and save semantics;
- runtime audio;
- exact GS material/blend fidelity and remaining sky/effect work;
- broader R&C1/UYA promotion from research/reference code into the C# production stack;
- promoting the remaining R&C1 static-instance/Moby layers and landing the UYA destination/world provider once its importer is ready;
- removing remaining GC-specific assumptions from legacy HUD/capture/regression paths after the neutral path is settled;
- explicit cross-game fusion rules once enough native behaviour is understood.

`RC2.HDR` also retains a documented reconciliation question; see [`../research/GC_RC2_HDR_RECONCILIATION.md`](../research/GC_RC2_HDR_RECONCILIATION.md) before treating older packing descriptions as final.

## Near-term engineering direction

These are capability goals, not a frozen campaign roadmap:

1. Continue productionising GC world/runtime fidelity and dynamic behaviour.
2. Promote stable R&C1 and UYA discoveries into engine-independent C# libraries with equivalence tests rather than re-reverse-engineering them.
3. Use the now-working R&C1 + GC composition proof as the integration baseline, then promote UYA behind the same provider contract and move into cross-game Veldin alignment.
4. Gradually route old GC-only debug/HUD/capture code through the same neutral destination/runtime path rather than maintaining two architectures indefinitely.
5. Keep using targeted local-runner probes for questions where retail bytes/executable behaviour can settle ambiguity cheaply.
6. Preserve the native-evidence / OBP-design boundary while gameplay archaeology expands.
7. Keep deterministic build/test/capture loops as the runtime becomes more game-like.

For what OBP is ultimately trying to become, see [`PROJECT_VISION.md`](PROJECT_VISION.md).
