# One Big Package — Current State

_Last refreshed: 2026-09-12._

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

Merged `main` treats retail-source ownership and world loading as trilogy-level application concepts rather than GC-specific state. `ObpSourceLibrary` can attach and restore all three primary authorities, `ObpDestination` keeps native destination identity separate from display labels, and `IObpWorldProvider` / `ObpWorldProviderRegistry` route a selected source-game destination to a neutral `RuntimeWorld`. **All three games have registered native C# providers** (`Rac1WorldProvider`, `GcWorldProvider`, `Rac3WorldProvider`) and load through the same Godot presentation spine — `WorldHost`, `PresentationEnvironment`, `DebugOverlay`, `CaptureHarness` — verified with per-game deterministic shot sets (`tools/shots/rac{1,2,3}.json`). GC is the deepest target (lighting, animated mobies, dynamic objects); RAC1/RAC3 populate core geometry/collision/environment, RAC3 also preserves dynamic objects, and Veldin now exposes one bounded evidence-safe multi-joint Moby preview through `AnimatedMeshes`. RAC3 `Lighting` remains null and the host continues to degrade gracefully where optional presentation data is absent. UYA's TypeScript/reference importer remains useful as equivalence evidence for behaviour not yet promoted natively, but it is no longer the production world-loading path.

### Neutral gameplay entity lifecycle

`RuntimeDynamicObject` remains immutable authored identity/render/source data. `RuntimeEntityState` is now the narrow live boundary: stable authored identity plus current transform, neutral `Active`/`Inactive` presence and neutral object-animation role. Native state numbers, PVar fields, health and AI semantics remain in source-game libraries. R&C1 class 1781 proves a live animation-role change without lifetime change; GC class 500 proves native state 1 -> 3 followed by PVar-owned deactivate/state-6 routing, with only the deactivate route projected to neutral `Inactive`. The Godot `DynamicObjectNode` consumes this snapshot for visibility/transform and validates identity; it does not parse PVars. UYA currently corroborates authored identity/PVar preservation only, so no unsupported mutable UYA semantics are exposed. See [`../research/TRILOGY_ENTITY_LIFECYCLE.md`](../research/TRILOGY_ENTITY_LIFECYCLE.md).

### Godot presentation layer (Milestones 1–2)

The Godot side of a loaded world is a thin applier over engine-neutral, unit-tested logic in `OBP.Runtime.Presentation`. World geometry renders **`Unshaded`**: the baked PS2 vertex colour *is* the level lighting, and the presentation pipeline only adds tone-map / exposure / colour-grade / fog / sky. `OBP.Tests` covers the presentation maths without launching the engine (it references `OBP.Runtime`, never `OBP.Godot`). Full detail lives in [`GODOT_PRESENTATION.md`](GODOT_PRESENTATION.md).

- **World host and environment.** `WorldHost` owns one loaded `RuntimeWorld`'s sub-tree, its `WorldEnvironment`, a region-steered "hero" `DirectionalLight3D`, the camera-followed sky, and the single per-frame `Tick`. `PresentationEnvironment` resolves an AgX tone-map with a bounded ambient-exposure clamp and a gentle contrast/saturation grade; `EnvResolver` picks per-region hero colour and fog from the level's env-sample points and doorway transition volumes (GC only — a no-op elsewhere).
- **Debug overlays.** `DebugOverlay` provides reversible inspection layers toggled F1–F7 plus J: per-asset-kind isolate/tint, collision wireframe, world-bounds box, env-light gizmos, sky hide, and a dormant skeleton layer. All go through `MaterialOverlay` / visibility, never the source material.
- **Interactive world inspector.** Press **I** (or `--inspect`) to ray-pick a runtime object and read a structured `WorldObjectDescriptor`: asset kind, texture id / size / alpha profile, resolved material flags, and for a preserved `RuntimeDynamicObject` its native class id, instance index, UID, model ref, interaction id, decomposed transform and local bounds. Opaque PVar / payload data is surfaced as **presence + `Format` tag + byte length only** — never decoded in Godot. Builder and transform decomposition are pure and tested.
- **Material presentation.** `WorldMaterialFactory` is the single place a Godot material is built from a neutral mesh; the decisions (alpha handling, back-face cull, vertex-colour-as-albedo, the tfrag bake-lift curve, sky ordering, emissive) live in the pure `MaterialModel`. Current behaviour is parity with the pre-consolidation inline code. Histogram-driven alpha-mode selection and subtle emissive presentation are implemented and tested but **not yet switched on** pending per-world visual review.
- **Deterministic animation.** `AnimationClock.FrameAt` (pure; `Loop` / `PingPong` / `HoldLast`) drives `AnimatedMesh` from accumulated world-clock seconds rather than per-`_Process` ticks, so an animated capture reproduces the same pose regardless of frame rate. **K** pauses. CPU-baked frame positions now cover GC single-joint clips, RAC1 rigid hierarchies and one retail-bounded UYA multi-joint preview. `RuntimeAnimatedMesh` also carries an optional, defaulted-null `RuntimeSkeleton`; that debug/introspection hook remains dormant until its cross-game coordinate contract is pinned independently.
- **Deterministic capture.** `CaptureHarness` captures a settled frame with a JSON metadata sidecar built *after* settling (so it reflects the loaded world, not launch state); `ShotList` / `tools/shots.ps1 -Game rac1|rac2|rac3` run named shot sets per game with committed JSON goldens.
- **Local visual-regression tooling.** `tools/vizcompare/` (Python, numpy + Pillow only) compares captures with MAE / windowed-SSIM / changed-pixel metrics against per-shot tolerances and writes diff heat-images. Baselines live only on the local machine under the gitignored `captures/baseline/` — retail reference captures are never committed or uploaded.

### R&C1 native world provider

R&C1 NTSC-U (`SCUS-97199`) is now a second native provider rather than a reference-only world slice. All 19 authority levels load through `Rac1WorldProvider` and the same `ObpWorldProviderRegistry` used by GC. The merged C# path covers retail terrain/textures/collision, level settings, shared RC sky, native TIE/shrub placement, and authored Moby identity/model/texture linkage as neutral dynamic objects. Across the authority set all 16,232 Moby placements are preserved: 15,340 carry linked dynamic render geometry, 19 hand off only to Ratchet's separately proven player animation path, and 873 remain explicitly meshless because no recovered class geometry is available. Veldin class 1781 is the first ordinary Moby with both a decoded clip and native state-selection witness: all 33 red-plant instances advertise a neutral 20-frame `Reaction` clip at 30 FPS, while sequence ids, selector flags and the retail distance/motion predicate remain inside `OBP.RAC1`. Decoded classes 766/1134 are deliberately static at runtime until their selectors are recovered. R&C1 class 500 now also has the first recovered resource loop: authored UID/reward-centre and 0x100-byte PVar preservation, positive-damage break projection through `RuntimeEntityState`, centre-10 native payout range 7..13, physical 1/5/20/50-bolt classes, exact collection credit and UID-backed destruction persistence; the persistence clearing event and dynamic pickup-threshold evolution remain intentionally unresolved. Retail all-level gates exercise every destination, and `rac1:LEVEL0` is covered by the generic deterministic Godot capture path with the debug player grounded on reconstructed collision.

### Up Your Arsenal native world provider

UYA NTSC-U (`SCUS-97353`, `rac3-ntscu-original`) exposes all 51 retail-observed sparse main-table rows through `Rac3WorldProvider`. The native C# path reconstructs tfrag terrain, decoded textures, TIE/shrub authored placement, strict UYA sky shells/textures, octree collision, first-part level atmosphere, compatible ship starts where retail settings do not carry the default sentinel transform, and authored Moby identity/PVars as neutral dynamic objects.

The UYA sky path preserves shell rotation/angular-velocity/bloom evidence and renders the initial pose through the ordinary camera-pinned `SkyRoot`. Materialless/gouraud backdrop geometry remains evidence-safe untextured runtime geometry, uses the retained sky/background tint policy, and is ordered behind the textured cloud shells. Retail table 1 remains 336 static meshes / 890,426 render triangles / 264,313 collision triangles, including all 4,348 sky triangles.

A shared GC/UYA Moby codec lives in `OBP.PS2`; evidence-safe UYA class models are linked to individual `RuntimeDynamicObject` instances while zero-local-core classes and unresolved skinning remain deliberately meshless. The all-row retail gate reproduces the committed production census. Veldin row 1 preserves 735 authored Mobies, links 427 renderable instances across 36 referenced models and contributes 221,349 dynamic triangles. The deterministic Godot player-start capture renders those 427 dynamic objects through the neutral host while preserving the recovered UYA start position.

The shared codec now also preserves a separately retail-validated animation binding stream: persistent VU0 matrix-slot state, current-record skin-control bits, raw skin-local positions, parent-relative `common_trans` translations and sequence bounding spheres. `GcUyaMobyPose` evaluates multi-joint frames in `OBP.PS2`. As a deliberately OBP-created showcase admission, four all-frame sphere-safe Veldin clips are exposed through `RuntimeAnimatedMesh`: oClass 6800/sequence 2 (4 authored instances), 6577/2 (3), 6317/4 (3) and 6886/15 (28). That yields **38 moving authored Mobies**, 45 animated texture surfaces and **34,301 animated triangles**. Every admitted class is validated against its authored retail sequence sphere before promotion. The original 221,349-triangle Veldin Moby census remains exact as 187,048 dynamic + 34,301 animated triangles. This does **not** claim recovered native animation-state selection. See [`../research/UYA_MOBY_ANIMATION.md`](../research/UYA_MOBY_ANIMATION.md).

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

`OBP.Composition` is an engine-neutral placement/alignment layer consumed by the Godot Fusion Lab. Multiple independently reconstructed `RuntimeWorld` values can coexist under separate transforms, be soloed/tinted/inspected, and be aligned from equivalent landmark anchors without modifying either source reconstruction. A deterministic R&C1-level-0 + GC-Oozla composition capture succeeds, and a three-cycle cross-game build/teardown stress run reports zero orphan nodes, an empty composition root after teardown and no post-warmup object/node growth. `CompositionLab` now runs on the Milestone 1–2 spine: one `WorldHost` per placement under an independent transform root, a shared `CaptureHarness`, and one neutral shared environment (`WorldHost.Options.ManageEnvironment = false`). With all three providers registered the cross-game Veldin alignment (RAC1 `LEVEL0` vs RAC3 `TABLE1`) is unblocked. See [`WORLD_COMPOSITION.md`](WORLD_COMPOSITION.md).

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
- broader Moby animation/state selection and wider class/variant behaviour;
- weapons, damage, AI and combat;
- mission/story state, cutscenes and progression machinery;
- vendors, economy, inventory and save semantics;
- runtime audio;
- exact GS material/blend fidelity and remaining sky/effect work — including switching on the staged histogram alpha-mode and emissive presentation in `MaterialModel` once per-world visual review with `tools/vizcompare` clears them;
- populating the dormant `RuntimeAnimatedMesh.Skeleton` debug/introspection hook for proven animation paths once its cross-game coordinate contract is pinned;
- per-world debug overlays / inspector inside `CompositionLab` (F-keys and I currently act on the active world only);
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
