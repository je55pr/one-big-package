# Avatar mesh upload profiling (#483)

## Reproduction

This is **in-process gameplay profiling**, not a synthetic frame loop. With the
authorised original trilogy installed, build the Godot game in **Debug** with the
pinned toolchain, import the project once in the Godot editor, and run:

```powershell
$env:OBP_AVATAR_MESH_PROFILE = '1'
$godot = '<pinned-Godot-.NET-console-executable>'
& $godot --headless --fixed-fps 60 --path game --rendering-method gl_compatibility -- `
    --rac1-iso '<RAC1.iso>' --rac2-iso '<RAC2.iso>' --rac3-iso '<RAC3.iso>' `
    --movement-smoke --destination rac1:LEVEL0
```

The smoke exercises ordinary movement, walking/running, crouch/slide, jump,
turning, equipment preview, and traversal. Check the process exit code and
`[movement-smoke] PASS`; capture the `[avatar-mesh-profile]` lines every
250 **changed** avatar poses. The final line at 5,750 updates is the comparison
snapshot used below. Diagnostics measure `PlayerAvatarView.ApplyFrame` after
clip/pose selection, including coordinate conversion and mesh submission.
`Stopwatch.GetTimestamp` measures calling-thread elapsed time and
`GC.GetAllocatedBytesForCurrentThread` measures its managed allocations.
Counts of surface rebuilds/uploads are explicitly recorded. The counters for
GC collections are **process cumulative**, not attributable to this method.

## Local baseline and experiments (2026-10-30)

- PC: Windows, Intel Core i5-9600K (6 logical processors), 15.9 GiB RAM,
  NVIDIA RTX 3070.
- Godot: 4.7.2-stable Mono; GL Compatibility selected, **headless** output.
- Build: Debug, .NET 8 target. The machine temporarily used SDK 10.0.402
  instead of the repository-pinned 10.0.403 because the latter was unavailable.
  That SDK workaround must not be committed.
- Workload: `rac1:LEVEL0`, original source data, fixed engine FPS 60,
  5,750 avatar updates / four avatar surfaces per update.
- Each row is a separate end-to-end smoke run. Other local processes were
  present, so CPU timing should be rerun in a controlled environment before
  claiming a small absolute speed improvement.

| Rendering strategy | Average update (µs) | Managed bytes/update | Surface rebuilds | Vertex-region uploads | Smoke |
| --- | ---: | ---: | ---: | ---: | --- |
| Original: new pose array, clear/add every surface | 530.561 | 68,054.2 | 23,000 | 0 | PASS |
| Persistent surfaces, new pose array | 730.487 | 65,404.2 | 0 | 22,996 | PASS |
| Persistent surfaces, reusable pose array | 639.790 | 1.9 | 0 | 22,996 | PASS |
| Original clear/add, reusable pose array | 481.517 | 2,650.8 | 23,000 | 0 | PASS |

**Finding:** Reusing the sampled-pose conversion buffer is independently
valuable. The rebuilt-surface path with that buffer used approximately 96%
less managed memory per update than the original while taking around 9% less
calling-thread time in this specific run. Persistent surface updates nearly
eliminated allocations in the measured method, but consumed **more** CPU time
in this headless environment. Do not equate fewer resource rebuilds with an
established end-to-end performance win.

## Shared updater assessment

The geometry contracts enforce stable vertex count between clips and stable
indices, UVs, textures and material ownership per surface. A Godot-only
`DynamicVertexSurfaceSet` prototype shares one packed XYZ stream and
per-pose AABB calculation between the avatar and the R&C1 wrench/bomb glove.
The world dynamic-object renderer already has a similar vertex-region method,
but its separate per-object surface layout and authored colours differ from
the avatar's shared vertex stream. Forcing all of these into a new universal
rendering abstraction would be premature.

The experimental dynamic-surface implementation passed the source-backed
movement smoke and focused portable tests, but its CPU cost was higher. It was
**not retained in the proposed production change**. The reusable pose conversion
buffer is shared by the avatar, wrench and bomb glove without changing the
existing mesh or material upload behaviour. This reduces a large source of
managed allocations while keeping the proven rendering path.

Before reconsidering dynamic surface updates, run rendered Godot captures and
profiler/renderer timing at the same resolution, renderer, scene and pose
sample, including transitions, equipment switching, world changes and
resource lifetime. The headless measurements cannot establish GPU upload or
render-thread savings.

Do not commit original retail files, local file paths, video captures or
profile logs. Only retain source-free methodology and aggregate measurements.
