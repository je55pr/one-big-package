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
- **the one per-frame presentation tick** — `Tick(delta, cameraGlobalPos)` —
  which advances animated mobies, keeps the sky centred on the camera, and
  resolves + applies the region hero light / ambient / fog via `EnvResolver`.

API: `Load(hostNode, sceneParent, world, name, options)` /
`Unload()` (frees everything + `GC.Collect`, leak-verified by the stress
harness) / `Tick(...)`.

`game/OBPGame` creates one `WorldHost` for the session and drives it from
`EnterWorld` (legacy GC path) and `AdoptRuntimeWorld` (neutral destination
path) — both of which used to hand-roll the same environment / light / build /
sky / teardown code twice.

`CompositionLab` keeps its own multi-world path for now (it holds several worlds
under independent transforms with sky disabled); it still shares
`WorldPresentation` / `EnvResolver`.

## `OBP.Runtime.Presentation`

Pure, `OBP.Godot`-free, unit-tested (`tests/OBP.Tests/WorldPresentationTests`,
`EnvResolverTests`):

| Type | Role |
|---|---|
| `WorldPresentation.Resolve` | `RuntimeEnvironment` → `PresentationState`: background (explicit → fog colour → default), ambient lift, load-time fog. |
| `WorldPresentation.ResolveFog` / `FogFromResolved` | the one fog resolver — begin/end/density/curve, far-visibility drive, end-plane stretched past `bounds.Diagonal * 1.4`. |
| `EnvResolver.Evaluate` | nearest env sample + fog fallback + env-transition doorway blend at a world point (`research/GC_LIGHTING.md`). |
| `WorldPresentation.ResolveToneMap` | AgX + an exposure nudge from the baked ambient (dark planets open, bright pull back). |
| `WorldPresentation.ResolveGrade` | a small fixed post-tone-map contrast/saturation lift. |
| `PresentationState` / `EnvResolved` / `FogState` / `ToneMap` / `ColourGrade` / `Rgb` | engine-independent result records. |

`OBP.Godot.PresentationEnvironment` translates that onto a Godot `Environment`
(clear colour, ambient, tone-map + exposure, adjustment grade, depth fog) at load
and applies the per-region ambient lift + fog each frame.

## Coordinate handedness

`RuntimeWorldScene.ToScene` negates X (the OBP→Godot reflection). Any host-side
world-space maths — sky follow, env-sample queries, gizmos, camera framing —
mirrors X on the query point and mirrors resolved directions back, exactly as
`WorldHost.UpdateRegionLighting` does.

## Deterministic capture

`OBPGame` runs a screenshot + JSON-sidecar pass under `--capture-frame` /
`--capture-out`. `tools/capture.ps1`, `tools/capture-planets.ps1` wrap it. See
those scripts for the argument surface.

## Milestone 1 status

- [x] PR 1 `presentation-core` — extract the pure maths.
- [x] PR 2 `world-host` — `WorldHost`, one presentation tick.
- [x] PR 3 `presentation-lighting` — `PresentationEnvironment`, AgX tone-map, ambient exposure, gentle grade.
- [ ] PR 4 `env-animation` — neutral ambient-animation contract + applier + sky motion.
- [ ] PR 5 `debug-overlays-and-shots` — runtime overlay toggles + shot-list harness + material factory.
