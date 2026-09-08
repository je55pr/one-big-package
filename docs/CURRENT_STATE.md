# One Big Package — Current State

_Last refreshed: 2026-09-08._

This document summarizes the current merged production baseline. Detailed format evidence belongs in [`../research/`](../research/README.md); creative possibilities belong in [`PROJECT_VISION.md`](PROJECT_VISION.md) and [`brainstorming/`](brainstorming/README.md).

## Headline

OBP's production direction is **Godot 4 + C#**. `reference-ts/` is temporary archaeology/equivalence code retained only where native parity is incomplete, most notably UYA/RAC3.

The trilogy source/destination/provider architecture now has **three native production providers**: R&C1, Going Commando and Up Your Arsenal. The native app can attach all three supported retail authorities, browse neutral destinations, reconstruct all 19 R&C1 worlds, arbitrary GC worlds and all 51 observed UYA main rows through the same `RuntimeWorld`/Godot boundary, and repeatedly enter/leave them without a game-specific host rewrite. The merged cross-game Fusion Lab can also hold multiple provider worlds at once; its first retail proof loads R&C1 level 0 and GC Oozla simultaneously in one Godot scene.

```text
trilogy retail ISO
  -> bounded disc/build identification
  -> game-specific native importer
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

Merged `main` treats retail-source ownership and world loading as trilogy-level application concepts rather than GC-specific state. `ObpSourceLibrary` can attach and restore all three primary authorities, `ObpDestination` keeps native destination identity separate from display labels, and `IObpWorldProvider` / `ObpWorldProviderRegistry` route a selected source-game destination to a neutral `RuntimeWorld`. **All three games have registered native C# providers** (`Rac1WorldProvider`, `GcWorldProvider`, `Rac3WorldProvider`) and load through the same Godot presentation spine — `WorldHost`, `PresentationEnvironment`, `DebugOverlay`, `CaptureHarness` — verified with per-game deterministic shot sets (`tools/shots/rac{1,2,3}.json`). GC is the deepest target (lighting, animated mobies, dynamic objects); RAC1/RAC3 populate core geometry/collision/environment and RAC3 also dynamic objects, with `Lighting` / `AnimatedMeshes` still null (the host degrades gracefully). UYA's TypeScript/reference importer remains useful as equivalence evidence for behaviour not yet promoted natively, but it is no longer the production world-loading path.

### R&C1 native world provider

R&C1 NTSC-U (`SCUS-97199`) is now a second native provider rather than a reference-only world slice. All 19 authority levels load through `Rac1WorldProvider` and the same `ObpWorldProviderRegistry` used by GC. The merged C# path covers retail terrain/textures/collision, level settings, shared RC sky, and the current native TIE/shrub static-instance layer. Retail all-level gates exercise every destination, and `rac1:LEVEL0` has been captured through the generic Godot path with the debug player grounded on reconstructed collision.

### Up Your Arsenal native world provider

UYA NTSC-U (`SCUS-97353`, `rac3-ntscu-original`) exposes all 51 retail-observed sparse main-table rows through `Rac3WorldProvider`. The native C# path reconstructs tfrag terrain, decoded textures, TIE/shrub authored placement, strict UYA sky shells/textures, octree collision, first-part level atmosphere, compatible ship starts where retail settings do not carry the default sentinel transform, and authored Moby identity/PVars as neutral dynamic objects.

The UYA sky path preserves shell rotation/angular-velocity/bloom evidence and renders the initial pose through the ordinary camera-pinned `SkyRoot`. Materialless/gouraud backdrop geometry remains evidence-safe untextured runtime geometry, uses the retained sky/background tint policy, and is ordered behind the textured cloud shells. Retail table 1 remains 336 static meshes / 890,426 render triangles / 264,313 collision triangles, including all 4,348 sky triangles.

A shared GC/UYA Moby codec lives in `OBP.PS2`; evidence-safe UYA class models are linked to individual `RuntimeDynamicObject` instances while zero-local-core classes and unresolved skinning remain deliberately meshless. The all-row retail gate reproduces the committed production census. Veldin row 1 preserves 735 authored Mobies, links 427 renderable instances across 36 referenced models and contributes 221,349 dynamic triangles. The deterministic Godot player-start capture renders those 427 dynamic objects through the neutral host while preserving the recovered UYA start position.

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

### TypeScript archaeology role

`reference-ts/` remains only to preserve evidence and working decoders that have not yet been promoted into native C#. Do not add new product/runtime architecture there. UYA/RAC3 still has important parity gaps in animation, semantics and effects, but its production world provider is now native C#; obsolete TypeScript components should be deleted only after their useful behaviour/evidence is preserved natively.

### Retail-authority development

Portable CI runs without retail images. Retail-backed archaeology is executed explicitly on an authorized local development machine against user-owned sources, and only bounded payload-free evidence belongs in Git.

## Known gaps / deliberately unfinished areas

Across the project, major work still includes:

- native Ratchet player movement/animation rather than the debug capsule;
- Moby animation and wider class/variant behaviour;
- weapons, damage, AI and combat;
- mission/story state, cutscenes and progression machinery;
- vendors, economy, inventory and save semantics;
- runtime audio;
- exact GS material/blend fidelity and remaining sky/effect work;
- broader R&C1/UYA promotion of remaining animation, gameplay semantics and effects from research/reference code into the C# production stack;
- removing remaining GC-specific assumptions from legacy HUD/capture/regression paths after the neutral path is settled;
- explicit cross-game fusion rules once enough native behaviour is understood.

`RC2.HDR` also retains a documented reconciliation question; see [`../research/GC_RC2_HDR_RECONCILIATION.md`](../research/GC_RC2_HDR_RECONCILIATION.md) before treating older packing descriptions as final.

## Near-term engineering direction

These are capability goals, not a frozen campaign roadmap:

1. Continue productionising GC world/runtime fidelity and dynamic behaviour.
2. Promote stable R&C1 and UYA discoveries into engine-independent C# libraries with equivalence tests rather than re-reverse-engineering them.
3. Use the now-working three-provider runtime baseline to extend composition into UYA and move into cross-game Veldin alignment without weakening each game's native provenance.
4. Gradually route old GC-only debug/HUD/capture code through the same neutral destination/runtime path rather than maintaining two architectures indefinitely.
5. Keep using targeted local retail probes for questions where retail bytes/executable behaviour can settle ambiguity cheaply.
6. Preserve the native-evidence / OBP-design boundary while gameplay archaeology expands.
7. Keep deterministic build/test/capture loops as the runtime becomes more game-like.

For what OBP is ultimately trying to become, see [`PROJECT_VISION.md`](PROJECT_VISION.md).
