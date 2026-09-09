# World Composition / Fusion Lab

A neutral developer tool for loading two or more independently reconstructed OBP
worlds into one Godot process, placing them under independent transforms,
defining equivalent landmarks, solving a reproducible alignment, and capturing
deterministic comparison images — **without modifying either source
reconstruction**.

First real target: R&C1 Veldin vs UYA Veldin, to find out quantitatively whether
the two versions share a coordinate basis and can be combined into one larger OBP
Veldin. The first **cross-game** composition proof is part of the production
baseline: R&C1 native level 0 and GC Oozla load simultaneously through their
independent `IObpWorldProvider`s. All three games now have native C# providers,
so a UYA world can join any composition through the same
`CompositionWorldLoader` path (`rac3:TABLE{n}`).

Each world is built by its own **`OBP.Godot.WorldHost`** (geometry / collision /
the per-frame animation tick) with `ManageEnvironment = false` — one viewport
holds one environment, so the lab keeps a single neutral `LabEnvironment` for
side-by-side comparison. Teardown is `WorldHost.Unload` per world; capture is the
shared `CaptureHarness`.

This is an engineering / debugging tool, not campaign gameplay.

---

## Architecture

```
CompositionLab                     (game/scripts/CompositionLab.cs — Godot host)
├── CompositionRoot
│   ├── <worldId-A>                 WorldTransformRoot: Transform3D = placement transform
│   │   └── WorldHost_A             RuntimeWorldScene build (no env) + animation tick
│   ├── <worldId-B>
│   │   └── WorldHost_B
│   └── …
├── AnchorMarkers
└── CompositionDebugUi              (HUD)
```

The composition itself is **engine-neutral data** in `src/OBP.Composition`
(`net8.0`, references only `OBP.Core`). Godot consumes it; it is never the
authoritative format.

| Layer | Project | Role |
| --- | --- | --- |
| `WorldComposition` / `WorldPlacement` / `AnchorPair` | `OBP.Composition` | the document: world references + transforms + anchors + comparison state |
| `CompositionTransform` | `OBP.Composition` | scale → Y-rotation → translation above a world |
| `PlanarAlignmentSolver` | `OBP.Composition` | closed-form least-squares rigid / uniform-scale planar fit + residual report |
| `CompositionJson` | `OBP.Composition` | tiny deterministic JSON persistence |
| `CompositionView` | `OBP.Godot` | transform → Godot `Transform3D`; non-destructive visibility / opacity / tint |
| `CompositionLab` | `game/` | the interactive host + capture path |
| `CompositionWorldLoader` | `game/` | thin adapter over `ObpWorldProviderRegistry` (`TrilogyWorldProviders`) |

### World loading

`CompositionWorldLoader` holds no game knowledge. A `WorldPlacement` names a
canonical destination id (`rac1:LEVEL0`, `rac2:LEVEL1`, `rac3:TABLE1`), or the
`sourceGame` + `levelId` shorthand the loader synthesises one from. It calls
through the same `ObpWorldProviderRegistry` the main `OBPGame` destination path
uses. R&C1, Going Commando, and UYA all enter the lab through this exact neutral
provider path; composition adds placement/presentation only, never decode rules.

Retail sources are registered per game from `--gc-iso` / `--rac1-iso` /
`--uya-iso`.

### Runtime design principles

1. Game-specific archaeology stays upstream — the lab only ever sees `RuntimeWorld`.
2. `RuntimeWorld` is the neutral world boundary; the lab never touches decoded data.
3. Composition is non-destructive: transforms and display state live **above** the generated scene.
4. Each imported world stays individually inspectable (solo, focus, per-kind toggles).
5. Alignment is metadata, never baked geometry.
6. Provenance is preserved (`sourceGame` / `buildId` / `destinationId` / `levelId` on every placement).
7. Mismatches are reported, never hidden with an arbitrary correction.
8. Deterministic developer workflows (headless capture, byte-stable persistence).
9. Works for RC1 / GC / UYA equally — no per-game Godot conditionals.
10. No assumption that there are only ever two worlds.

---

## Transform conventions

`CompositionTransform` maps a **world-local** point (OBP space, Y-up) into shared
composition space:

```
p_composition = translation + R_y(rotationYDegrees) · (scale · p_local)
```

- Applied in the order **scale → Y-rotation → translation**.
- `R_y` is the single shared convention (`CompositionTransform.RotateY`):
  `x' = x·cosθ + z·sinθ ; z' = −x·sinθ + z·cosθ`.
- **Scale defaults to 1 and is archaeological evidence, not a fitting knob.** A
  non-unit scale means the two source worlds genuinely differ in proportion.
  Rigid (scale-locked) alignment is the default comparison.
- Godot side: the generic world builder X-negates geometry to undo the PS2 → OBP
  reflection, so `CompositionView.ToGodotTransform` conjugates the placement
  transform: translation X negates, the Y rotation reverses sign, uniform scale
  is unchanged.

---

## Anchor-solving mathematics

An `AnchorPair` is two world-local points asserted to be the same real place
(`LocalA` in world A, `LocalB` in world B). The solver
(`PlanarAlignmentSolver.Solve`) finds the `CompositionTransform` carrying B's
points onto A's that minimises `Σ |T(b_i) − a_i|²`:

- **Planar (XZ)** fit is the 2-D Procrustes / Umeyama closed form:
  - centroids `ā`, `b̄`; centred points `a'_i`, `b'_i`
  - `sDot = Σ(b'x·a'x + b'z·a'z)`, `sCross = Σ(b'x·a'z − b'z·a'x)`
  - optimal angle `φ = atan2(sCross, sDot)`; because `R_y(θ) = R_std(−θ)`, the
    solved `rotationYDegrees = −φ`
  - uniform scale (opt-in): `s = √(sDot² + sCross²) / Σ|b'_i|²`
  - translation: `t_xz = ā_xz − s·R_y(θ)·b̄_xz`
- **Y** is a pure mean offset `t_y = mean(a_iy − s·b_iy)` — this is a *planar*
  alignment (is it the same map, moved and turned?), not a height-field relation.
- **Degrees of freedom:**
  - 1 pair → translation only (`TranslationOnly`)
  - 2 pairs → exactly-determined translation + Y rotation
  - 3+ pairs → best-fit, with per-anchor residuals
- **Degenerate arrangements are flagged, never silently deformed:**
  - coincident anchor points on XZ → translation-only fallback, `Degenerate`
  - 3+ near-collinear points → solved but `Degenerate` with a note that Y
    rotation (and any scale estimate) is weakly constrained

### Reported output

`PlanarAlignmentResult` carries: the solved `CompositionTransform`, `AnchorCount`,
`MeanError`, `MaxError`, `RmsError`, `PerAnchorResiduals`, `ScaleWasFitted`,
`FittedScale`, `Quality` (`Empty` / `TranslationOnly` / `Degenerate` / `Solved`)
and a human `Note`.

`WorldComposition.SolveAlignment(worldAId, worldBId, allowScale)` returns a copy
with world B's transform replaced (world A untouched) plus the result.

---

## Composition schema (`*.json`)

Tiny, deterministic, camelCase, LF newlines, trailing newline. Canonicalised on
serialize (worlds sorted by id, numbers rounded to 6 dp) so the same logical
composition is byte-identical regardless of edit order. **No reconstructed
geometry is ever written** — only references + placement metadata.

```json
{
  "version": 1,
  "name": "R&C1 Veldin vs UYA Veldin",
  "notes": "…",
  "comparison": { "activeWorldId": "uya-veldin", "soloWorldId": null, "fitScale": false, "overlayMode": "both" },
  "worlds": [
    {
      "id": "rc1-veldin",
      "destinationId": "rac1:…",           // canonical neutral id when the provider registry exists
      "sourceGame": "R&C1",
      "buildId": "rac1-ntscu",
      "levelId": 3,                          // current GcWorldImport-style load path
      "label": "R&C1 Veldin",
      "transform": { "translation": [0, 0, 0], "rotationYDegrees": 0, "scale": 1 },
      "visible": true,
      "opacity": 1,
      "debugTint": [1, 0.2, 0.2],
      "categoryVisibility": { "collision": false }
    },
    {
      "id": "uya-veldin",
      "sourceGame": "Up Your Arsenal", "levelId": 42,
      "transform": { "translation": [123.4, 0, -77.2], "rotationYDegrees": 42.1, "scale": 1 }
    }
  ],
  "anchors": [
    { "worldA": "rc1-veldin", "worldB": "uya-veldin", "label": "Ratchet garage doorway",
      "localA": [10, 1, 20], "localB": [-5, 2, 8], "enabled": true }
  ]
}
```

`categoryVisibility` keys are neutral `RuntimeMesh.AssetKind` values (`tfrag`,
`tie`, `shrub`, `moby`, `sky`, `moby-marker`) plus `collision`.

Example: [`compositions/example-gc-two-world.json`](../compositions/example-gc-two-world.json).

---

## CLI usage

The lab is a mode of the existing Godot host (`OBPGame`), entered with
`--compose`. Run windowed (headless can't render a capture) after a one-off
`--headless --path game --import`.

```
# interactive
<godot> --path game -- --compose --gc-iso <GC.iso> \
    --composition compositions/example-gc-two-world.json

# seed from a spec instead of a file  (id=game:level, comma-separated)
<godot> --path game -- --compose --gc-iso <GC.iso> --compose-worlds "oozla=gc:1,endako=gc:3"

# deterministic capture
<godot> --path game --rendering-method gl_compatibility --resolution 1280x720 -- \
    --compose --gc-iso <GC.iso> --composition <path> \
    --composition-view overview --capture-frame 30 --capture-out captures/x.png

# solve the anchors on load, then capture the aligned result
<godot> … -- --compose --gc-iso <GC.iso> --composition <path> --compose-solve --composition-view overlay …
```

| Flag | Meaning |
| --- | --- |
| `--compose` | boot the lab |
| `--gc-iso` / `--rac1-iso` / `--uya-iso` `<path>` | register a retail source per game |
| `--composition <path>` | load a saved composition JSON |
| `--compose-worlds <spec>` | seed worlds from `id=game:level,…` |
| `--compose-solve` / `--compose-solve-scale` | run the rigid / uniform-scale solve on load and apply to world B |
| `--composition-view <v>` | capture view: `overview` (default) · `top` · `a-only` · `b-only` · `overlay` · `a-start` · `b-start` · `start-overlay` · `start-side-by-side` |
| `--capture-frame N` / `--capture-out <path>` | deterministic capture (writes `.png` + `.json` sidecar) |

The `*-start` views are comparison helpers, not archaeology claims. `a-start` and
`b-start` frame each authority around its own neutral `RuntimeWorld.Ship` point.
`start-overlay` translates B so the two ship/start points coincide while
preserving the preset rotation and scale. `start-side-by-side` uses the same
translation-only anchor, then adds a fixed presentation-only +520 X offset to B.
No semantic landmark, fitted rotation, fitted scale, or whole-map transform is
introduced by these views.

`captures/composition-*.json` sidecars record every world's effective capture
transform, the anchor count, the last solve result, and a `placementNote` that
states the capture-only placement contract.

---

## Controls (interactive)

| Key | Action |
| --- | --- |
| RMB (hold) + mouse | free-fly look |
| WASD / Q E | move · **Shift** = faster |
| Tab | cycle the active world |
| `[` / `]` | rotate active world −/+ 1° about Y |
| arrows / PgUp / PgDn | translate active world on X / Z / Y (**Shift** = ×10 step) |
| V | toggle active world visibility |
| O | solo the active world / show all |
| C | cycle + toggle an asset category on the active world |
| F | focus camera on the active world |
| 1 / 2 | capture an anchor point (A / B side) under the crosshair |
| 0 | clear pending anchors |
| Enter | solve alignment (world B → world A) and print residuals |
| Ctrl+S | save the composition |
| Esc | quit |

Minimal anchor workflow: aim the crosshair at a landmark in world A, press `1`;
aim at the same landmark in world B, press `2`; repeat for more pairs; press
Enter. World B aligns and the residuals print to the HUD + log.

---

## Known limitations

- Start-point comparison views intentionally use only neutral `RuntimeWorld.Ship`
  positions. They do not infer heading, semantic landmark identity, or a global
  alignment from visual resemblance.
- Anchor capture needs collision geometry under the crosshair; toggle collision
  back on for a world before picking anchors on it.
- The Y alignment is a mean offset only — no height-field fitting.
- Collinearity is judged from the source (world B) anchor spread only.
- Sky shells are disabled in the lab (they follow a single camera).
- Nested composition roots (`CompositionTransform.Then`) exist for completeness
  but the lab keeps every world one level under `CompositionRoot`.

---

## Performance observations

Two full GC worlds (Oozla ~1.88 M render tris, Endako ~1.53 M; Siberius ~2.5 M)
load and coexist in the OpenGL-compat renderer. Import is ~1.5–2 s per world.

`--compose-reload 6` (build every world → settle → tear down → GC), headless,
Oozla + Endako:

```
#  0 built 2 world(s) | mem  68.5 MB (+ 0.0) | objects 2926 nodes 11 orphans 0 | compRoot desc 0
#  5 built 2 world(s) | mem  68.5 MB (+ 0.0) | objects 2926 nodes 11 orphans 0 | compRoot desc 0
```

Flat across cycles — no orphan nodes, no object growth, `CompositionRoot` empty
after teardown. (Fixing this surfaced a pre-existing leak in `RuntimeWorldScene`:
a never-parented `Sky` root was left dangling whenever `IncludeSky` was false —
now `Free()`d, which also helps the single-world planet-hopping path.)

### Cross-game validation (2026-09-08)

`compositions/example-rac1-gc-two-world.json` is the first two-source proof. On
Jess-Laptop it loaded the exact R&C1 authority (`rac1:LEVEL0`) beside the exact GC
authority (`rac2:LEVEL1` / Oozla) in one Godot scene. After the merged R&C1
static-instance promotion, level 0 imports 286 runtime meshes / 751,435 triangles
in ~0.6 s; current GC main imports Oozla as 302 meshes / 1,860,879 triangles in
~1.4-1.8 s. A deterministic overview capture succeeded with both worlds visible simultaneously.

A three-cycle `--compose-reload 3` stress run rebuilt and tore down both games
each cycle with **0 orphans**, 11 nodes after teardown, an empty
`CompositionRoot`, managed memory flat at 55.2 MB across all three teardown
samples, and no object/node growth. This is the first lifecycle proof involving
two different source games rather than two GC worlds.

---

## Veldin comparison showcase

`compositions/veldin-rac1-uya-comparison.json` pins the two retail authorities used
by the current archaeology note: R&C1 `rac1:LEVEL0` and UYA `rac3:TABLE1`. The
saved preset contains identity transforms, no anchors, and no fitted alignment.
That is deliberate: current retail evidence supports strong local resemblance
around the start area, but not one rigid full-map transform.

For the deterministic local comparison pack, run:

```powershell
./tools/capture-veldin-comparison.ps1 -Rac1Iso <RAC1.iso> -UyaIso <UYA.iso>
```

The workflow builds once, captures `rac1-start`, `uya-start`, `start-overlay`, and
`start-side-by-side`, then runs a three-cycle simultaneous-load teardown stress
check by default. Overlay/side-by-side placement is computed only from each
neutral `RuntimeWorld.Ship` position; the UYA world keeps the preset rotation and
scale, and the side-by-side offset exists only to separate the two start-area
views visually. Capture sidecars retain that explanation in `placementNote`.

Retail validation on 2026-09-09 loaded both authorities simultaneously through
the neutral providers: R&C1 LEVEL0 produced 315 meshes / 894,053 render triangles
and UYA TABLE1 produced 336 meshes / 890,426 render triangles. All four capture
views saved successfully at 1280x720. A three-cycle `--compose-reload 3` run was
flat at 61.3 MB with 2,914 objects, 12 nodes, 0 orphans, and an empty
`CompositionRoot` after every teardown.

Archaeological interpretation remains in
[`research/VELDIN_CROSS_GAME_ALIGNMENT.md`](../research/VELDIN_CROSS_GAME_ALIGNMENT.md):
do not promote exploratory 5°, -8°, -18°, or scaled fits into this preset unless
independent semantic landmarks later justify them.
