# Godot presentation layer

How a neutral `RuntimeWorld` becomes something you can look at and inspect in
Godot. This is the **presentation / host** side only — it never parses retail
formats (see [`ARCHITECTURE.md`](ARCHITECTURE.md)); it consumes `RuntimeWorld`,
`RuntimeLighting` and friends from `OBP.Runtime`.

```text
RuntimeWorld  (neutral: welded meshes, RGBA textures, tri-soup collision,
   │           atmosphere, spawn, lighting — no Godot or Ratchet types)
   │
   ▼  OBP.Runtime.Presentation        engine-independent maths, unit-tested
   │    WorldPresentation   → PresentationState (background / ambient / fog / tonemap)
   │    EnvResolver         → EnvResolved      (per-region hero light + fog at a point)
   │
   ▼  OBP.Godot
   │    RuntimeWorldScene.Build(world, options) → Result   (ArrayMesh / StaticBody3D / materials)
   │    RuntimeWorldScene.ConfigureEnvironment  → Godot Environment
   │    WorldHost                                 owns the loaded world (below)
   │
   ▼  game/  OBPGame
        spawn point, cameras, HUD, input, capture — all the player-facing parts
```

## `OBP.Godot.WorldHost`

The single owner of one loaded world's Godot side:

- the built `RuntimeWorldScene.Result` sub-tree (geometry + collision), parented
  under the caller's world root;
- the world `WorldEnvironment` and the "hero" `DirectionalLight3D`;
- the camera-followed sky root;
- the resolved **ambient animations** (`RuntimeWorld.AmbientAnimations`, or a
  synthesised gentle sky drift when a world declares none);
- **the one per-frame presentation tick** — `Tick(delta, cameraGlobalPos)` —
  which advances animated mobies, keeps the sky centred on the camera, applies
  the ambient animations, and resolves + applies the region hero light /
  ambient / fog via `EnvResolver`.

API: `Load(hostNode, sceneParent, world, name, options)` /
`Unload()` (frees everything + `GC.Collect`, leak-verified by the stress
harness) / `Tick(...)`.

`game/OBPGame` creates one `WorldHost` for the session and drives it from
`EnterWorld` (legacy GC path) and `AdoptRuntimeWorld` (neutral destination
path) — both of which used to hand-roll the same environment / light / build /
sky / teardown code twice.

`CompositionLab` now uses **one `WorldHost` per placement** (with
`ManageEnvironment = false` — the lab keeps a single shared `LabEnvironment`,
one environment per viewport) under independent transform roots, plus the shared
`CaptureHarness`. `OBP.Composition` (transforms, `PlanarAlignment`,
`CompositionJson`) stays the engine-neutral authority.

## `OBP.Runtime.Presentation`

Pure, `OBP.Godot`-free, unit-tested (`tests/OBP.Tests/WorldPresentationTests`,
`EnvResolverTests`):

| Type | Role |
|---|---|
| `WorldPresentation.Resolve` | `RuntimeEnvironment` → `PresentationState`: background (explicit → fog colour → default), ambient lift, load-time fog. |
| `WorldPresentation.ResolveFog` / `FogFromResolved` | the one fog resolver — begin/end/density/curve, far-visibility drive, end-plane stretched past `bounds.Diagonal * 1.4`. |
| `EnvResolver.Evaluate` | nearest env sample + fog fallback + env-transition doorway blend at a world point (`research/GC_LIGHTING.md`). |
| `AmbientAnimator.Sample` | `RuntimeAmbientAnimation` (`UvScroll` / `Spin`) → `AmbientAnimationSample` at time _t_. Deterministic. |
| `WorldPresentation.ResolveToneMap` | AgX + an exposure nudge from the baked ambient (dark planets open, bright pull back). |
| `WorldPresentation.ResolveGrade` | a small fixed post-tone-map contrast/saturation lift. |
| `PresentationState` / `EnvResolved` / `FogState` / `ToneMap` / `ColourGrade` / `Rgb` | engine-independent result records. |

`OBP.Godot.PresentationEnvironment` translates that onto a Godot `Environment`
(clear colour, ambient, tone-map + exposure, adjustment grade, depth fog) at load
and applies the per-region ambient lift + fog each frame.

## Materials — `OBP.Godot.WorldMaterialFactory`

Every Godot material for a built world comes from one factory: it owns the
decoded `ImageTexture`s and the `StandardMaterial3D` cache, and is the single
place a material is made from a neutral `RuntimeMesh` / `RuntimeObjectMesh` /
`RuntimeAnimatedMesh` (three inline sites in `RuntimeWorldScene.Build` before).

The decisions are engine-independent and unit-tested in
`OBP.Runtime.Presentation.MaterialModel`: back-face cull by kind
(`tfrag`/`tie`), vertex-colour-as-albedo by kind, the tfrag `BakeCurve` lift,
and a texture `AlphaProfile`. World geometry stays `Unshaded` — baked PS2 vertex
colour is the lighting.

`MaterialModel` also carries a histogram-driven `ResolveAlpha` (opaque / scissor
/ blend from what the texture actually contains) and an emission model — wired
and tested but **not yet switched on**: on the showcase set they move
metal-city planets (Endako) more than a fidelity pass should without
per-planet review. A focused follow-up turns them on with `vizcompare` evidence.

## Visual regression — `tools/vizcompare/`

`compare.py` (numpy + Pillow) diffs two capture sets: MAE, windowed-SSIM,
changed-pixel %, and a `_diff.png` heat image for any pair over its tolerance
(`tolerances.json`). `tools/vizcompare.ps1 -Update` refreshes a local baseline
under `captures/baseline/<set>/` — **gitignored; baselines are retail-derived
and never committed or uploaded**.

## Coordinate handedness

`RuntimeWorldScene.ToScene` negates X (the OBP→Godot reflection). Any host-side
world-space maths — sky follow, env-sample queries, gizmos, camera framing —
mirrors X on the query point and mirrors resolved directions back, exactly as
`WorldHost.UpdateRegionLighting` does.

## Interactive world inspector

**I** toggles a panel showing the neutral `WorldObjectDescriptor` for whatever is
under the crosshair (or the mouse, cursor free). `OBP.Godot.WorldPicker` ray-casts
against the visible mesh AABBs and resolves the hit to `(kind, textureId)`, the
owning `dyn_*` `RuntimeDynamicObject`, or a `RuntimeAnimatedMesh`;
`OBP.Runtime.Presentation.WorldObjectDescriptorBuilder` (pure, tested) turns that
into the readout: asset kind, texture WxH + `AlphaProfile`, material flags,
decomposed transform, local bounds, PVar **presence + format + byte length**
(never decoded), animation state, and provenance. `--inspect` opens it on load.

## Animation presentation

`AnimatedMesh` (its own file) is driven by a **world clock in seconds**
(`WorldHost` `_animClock`, pauseable with **K**) via
`RuntimeWorldScene.AdvanceAnimated(result, clockSeconds)` — frame selection is the
pure `AnimationClock.FrameAt` (`Loop` / `PingPong` / `HoldLast`). A capture at a
fixed settle time reproduces the same pose regardless of frame rate; the sidecar
records `animClockSeconds` + per-mesh `currentFrame`.

**Dormant skeleton hook:** `RuntimeAnimatedMesh.Skeleton` (`RuntimeSkeleton` —
joints + parents + per-frame local rotations) is optional and **defaulted null**.
`DebugOverlay` layer **J** draws bone lines when it's populated; until a decoder
chain wires `MobyAnimation` / `Rac1MobyPose` output into it, J prints
"no RuntimeSkeleton data".

## Debug overlays — `OBP.Godot.DebugOverlay`

Runtime inspection layers over a built `RuntimeWorldScene.Result`, all idempotent
and reversible (kind tint / visibility go through `MeshInstance3D.MaterialOverlay`
and `Visible`, never the source material; generated meshes live under one
`DebugOverlay` node freed with the world):

| Key | Layer | |
|---|---|---|
| **F1** | isolate kind | cycle none → tfrag → tie → shrub → moby → moby-marker → sky |
| **F2** | `KindTint` | flat colour per asset kind (replaces the old `OBP_KIND_DEBUG` env var) |
| **F3** | `CollisionWire` | octree collision wireframe, coloured by native `TriangleMaterialIds` |
| **F4** | `WorldBounds` | wire box at `world.Bounds` |
| **F5** | `EnvGizmos` | sphere per `RuntimeEnvSample`, wire box per `RuntimeEnvTransition`, ray per `RuntimeDirLight` |
| **F6** | `HideSky` | drop the sky shells |
| **F7** | clear | all layers off |
| **J** | `Skeleton` | bone lines per animated mesh (dormant — no skeleton data decoded yet) |
| **I** | — | toggle the interactive world inspector |
| **K** | — | pause / resume moby animation (sky keeps drifting) |

`--overlay kindtint,collisionwire` / `--overlay isolate:moby` applies layers on
load for a deterministic capture.

## Deterministic capture

`OBP.Godot.CaptureHarness` is the primitive: settle N frames → one
`FramePostDraw` → grab the viewport → write a PNG + a JSON sidecar of the
caller's metadata, with a watchdog. Both capture paths use it.

- **Single frame** — `--capture-frame N --capture-out <path>`. `tools/capture.ps1`,
  `tools/capture-planets.ps1` wrap it.
- **Shot list** — `--shots <file.json> [--shots-world <token>]` runs every named
  `ShotSpec` in a `ShotList` in one process: set the framing (`showcase` /
  `topDown` / `orbit`), apply the shot's `overlay` layers, settle, write
  `<world>-<shot>.png` + sidecar. The world token is a GC planet name/id **or**
  a neutral destination id (`rac1:LEVEL0`, `rac3:TABLE1`) which routes through
  the provider path. `tools/shots.ps1 -Game rac1|rac2|rac3` picks
  `tools/shots/<game>.json` + the right ISO; `-Check` diffs each sidecar's
  deterministic keys against `tools/shots/golden/` (via `tools/shots/check.py`).

`ShotSpec` / `ShotList` live in `OBP.Runtime.Presentation` (pure, JSON, tested).

## Trilogy

`WorldHost` / `PresentationEnvironment` / `DebugOverlay` / `CaptureHarness`
consume a `RuntimeWorld` from any provider. RAC1 and RAC3 populate core geometry
/ collision / environment (RAC3 also dynamic objects); `Lighting` and
`AnimatedMeshes` are still null there, and the host degrades gracefully — the
hero light hides, F5 env-gizmos report "no decoded lighting", the HUD reads
`LEVELn` / the native label. Per-game shot sets: `tools/shots/rac{1,2,3}.json`.

## Milestone 2 status

- [x] `m2-tooling-and-materials` — `tools/vizcompare/`; `WorldMaterialFactory` +
  `MaterialModel` (parity; histogram alpha / emission staged, not on).
- [x] `m2-trilogy-bringup` — shots + host null-safe across RAC1/2/3; per-game
  shot sets; `CaptureHarness` metadata-factory fix.
- [x] `m2-world-inspector` — `WorldObjectDescriptor` + `WorldPicker` +
  `WorldInspectorPanel` (I); animation bridge (`AnimationClock`, world-clock
  `AnimatedMesh`, K pause, dormant `RuntimeSkeleton` + J).
- [x] `m2-composition-refresh` — `CompositionLab` on `WorldHost` per world +
  `CaptureHarness`.

## Milestone 1 status

- [x] PR 1 `presentation-core` — extract the pure maths.
- [x] PR 2 `world-host` — `WorldHost`, one presentation tick.
- [x] PR 3 `presentation-lighting` — `PresentationEnvironment`, AgX tone-map, ambient exposure, gentle grade.
- [x] PR 4 `env-animation` — `RuntimeAmbientAnimation` contract, `AmbientAnimator`, `WorldHost` applier, synthesised sky drift.
- [x] PR 5 `debug-overlays` — `DebugOverlay` runtime inspection layers (F1–F7) + `--overlay`.
- [x] PR 6 `capture-shots` — `CaptureHarness` + `ShotSpec`/`ShotList` + `--shots` runner + `tools/shots.ps1` + golden sidecars.

Deferred: extracting `WorldMaterialFactory` from `RuntimeWorldScene.Build` — a
pure tidy-up with pixel-parity risk and no consumer that needs it yet.
