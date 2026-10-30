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
The number of surface rebuilds is explicitly recorded. The counters for
GC collections are **process cumulative**, not attributable to this method.

For a rendered comparison, use the same source flags and smoke with
`--windowed --position -20000,-20000 --resolution 960x540` in place of
`--headless`. In profiling mode, `PlayerAvatarView` enables Godot's
per-viewport CPU/GPU timers once the view enters the scene tree. The
`viewportCpuMs`, `viewportGpuMs`, `renderSetupMs`,
`renderSamples` and `gpuSamples` fields are **whole-viewport**
statistics (not avatar-only or render-thread queue timings). A value of
`-1` means a positive sample was unavailable, as may happen in headless
mode. The profiling flag is opt-in and does not change animation decisions.
A rendered fixed-seed Bomb Glove smoke verified that both viewport timing
fields return nonzero measurements with the real GL Compatibility renderer.

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
eliminated allocations in the measured method, but showed **longer elapsed update time**
in this headless environment. Do not equate fewer resource rebuilds with an
established end-to-end performance win.

## Rendered cross-check (same workload)

A separate isolated-worktree experiment used a **windowed** Godot GL
Compatibility renderer at 960 x 540, moved offscreen (not headless), with the
same fixed 60-FPS R&C1 LEVEL0 movement smoke, 5,750 avatar updates and four
avatar surfaces. After 120 warm-up frames, a temporary host-only probe sampled
`RenderingServer.ViewportGetMeasuredRenderTimeCpu/Gpu` and
`GetFrameSetupTimeCpu` each process frame (5,630 samples). These are
**whole-viewport rendering times**, not avatar-only GPU costs. The probe was not
added to production code.

| Strategy/run | Avatar ApplyFrame elapsed (µs) | Managed B/update | Viewport CPU render (ms) | Viewport GPU render (ms) | Frame setup CPU (ms) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Rebuild + reused conversion, run A1 | 663.212 | 2,647.5 | 1.568 | 1.592 | 0.125 |
| Persistent vertex regions + reused conversion, run B1 | 669.155 | 0.0 | 1.617 | 0.917 | 0.125 |
| Rebuild + reused conversion, repeat A2 | 715.922 | 2,646.8 | 1.657 | 0.835 | 0.131 |

All three smokes passed. The apparent large GPU advantage of B1 **did not
reproduce**: rebuilding surfaces in A2 was faster on the measured GPU while
still performing 23,000 surface reconstructions. The elapsed avatar-update
measurements likewise vary. This may reflect graphics clock changes or other
system activity; the absolute GPU differences cannot be assigned to one avatar.
The dynamic prototype therefore remains **experimental**, not a demonstrated
net performance improvement.

The experimental persistent updater keeps a fixed resource/UV/index layout
and updates per-frame bounds. It is feasible for the avatar, wrench and bomb
glove, but adding it now would expand rendering infrastructure without clear
performance evidence. Keep the proven reusable pose conversion buffer and
revisit surface updates only after controlled GPU-clock/power conditions,
repeatable rendered timings and visual/resource-lifetime comparisons.

## Rendered pose comparison

Using the repository's fixed-seed, 960 x 540 rendered Bomb Glove smoke,
captures from the retained rebuild implementation were compared with an
isolated **fully shared** dynamic-surface prototype for avatar, wrench and
glove. The held-glove and throw PNG frames were **pixel-identical**
(zero changed RGB pixels across 518,400 pixels per shot). Both smoke runs
passed. Later floor-impact and expanding-blast frames did not match between
separate runs, including a comparison against the original revision; effect
timing and transient particles make those shots unsuitable as exact mesh-pose
oracles without tighter capture synchronization.

This establishes compatibility for the matched static/throw shots, not
equivalence for every skeletal transition, renderer or GPU resource lifetime.

## World-change lifetime check

The retained buffer-only implementation also passed a source-backed
`--stress-switch rac1:LEVEL0,rac2:LEVEL1,rac3:TABLE1,rac1:LEVEL0` host
regression. It reported zero orphan nodes after each transition. On returning
to R&C1 LEVEL0, measured process memory was 96.8 MB versus 94.1 MB at
initial entry (+2.7 MB). These are overall process snapshots, not dedicated
avatar resource-leak measurements, but no unbounded growth was observed in
this single round trip.

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
movement smoke and focused portable tests, but its measured update elapsed time was higher. It was
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
