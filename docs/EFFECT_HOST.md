# Shared native effect host (#285)

Enter Fusion Lab with the three admitted original NTSC-U sources attached. Walk
to Test Chamber Alpha, then use **PAUSE > DEVELOPER > START NATIVE EFFECTS**.
**RESET NATIVE EFFECTS** retires only effects; player configuration, equipment,
health and native actor state keep their existing owners. The same controls are
available in original worlds, with the demonstration row placed above nearby
terrain. Start again to replay the authored experiment from its original seeds.

`game/data/effects_experiment.json` supplies six requests to the existing room:

| Source bank | Registered recipe | Authentic inputs |
|---|---|---|
| R&C1 LEVEL1 | `rac1/class27/type6/v1` | Novalis class-27 owners 285 and 286 |
| GC LEVEL21 | `rac2/museum/type85/v1` | Museum class-4887 presets 262 and 263 |
| UYA TABLE1 | `rac3/shock-cannon/charging/v1` | Pre-ready charge caller `0x385f18` |
| UYA TABLE1 | `rac3/shock-cannon/beam-glow/v1` | Charged-shot callers `0x38667c` and `0x386724` |

All ten retail Novalis owners share one authored preset. The two R&C1 instances
exercise separate handles, seeds, placement and live owners; they do not claim
two authored parameter sets. The GC inputs have different original texture,
colour, motion, size and spin parameters. UYA retains its two-particle and
six-particle caller paths. Original native parameters remain in the source
modules and are decoded from the authorized sources.

## Ownership and clocks

- `OBP.Runtime/Effects` carries qualified recipe/resource identities, immutable
  sprite snapshots, existing neutral triangle meshes, selected light facts,
  placement/view observations and source factory/instance ports. It contains no
  native particle recurrence or emitter orchestration.
- The three `Rac*ParticleEffectFactory` adapters own source simulation. R&C1
  emits before its pool update and reads the previous native view; GC and UYA
  update their pools before emission. All three admitted paths declare 60 Hz.
  A render sample never advances them or consumes random values.
- `OBP.Fusion/EffectWorldSession` owns request admission, a bounded instance
  budget, per-provider tick accumulators, emission windows, reset generations
  and retirement. Its input contains a world lease identity, with no destination
  game field. It delegates owner death to the native instance: R&C1 type 6
  retains owner parameters and retires them; GC/UYA copied particles drain.
- `game/scripts/WorldEffectHost` owns CPU preparation and scene resources for a
  world lease. `EffectNativeClockDriver` runs after gameplay/replay input and
  before replay checkpoints. Loading and pause suspend advancement. World
  teardown cancels preparation, disposes the native instances and releases GPU
  images, materials, batches and nodes.
- `OBP.Godot/EffectSceneView` renders source-normalized colours, native blend
  selections, depth intent, UVs, rotation and projection. `Rac1ParticleView`
  delegates its existing weapon sprites to this renderer. `FusionMeshView`
  delegates existing weapon and Cannon beam triangles to `EffectMeshSceneView`.
  The retained Tesla ribbon caller uses the same mesh upload path, preserving
  its source widths, extension, UV phase and endpoint colours.
  This changes presentation plumbing, without a second weapon update or damage
  path. The neutral mesh/light ports admit recovered facts; they do not claim
  recovery of additional native effect families or native light assignment.

Each texture binding retains **game + build + level bank + Part/Fx kind + native
texture ID**. An equal integer from another bank cannot choose the image.
Source preparation verifies the original disc hash independently of the current
world. Missing sources, wrong builds, absent images and unsupported recipes or
contexts produce explicit failure/status, without replacement particles.
Detaching/changing a source retires its active instances; re-enter after fixing
the attachment. An unavailable experiment cannot partially start.

Handles contain world identity, lease generation, reset generation and request
sequence. Sequences increase within one reset; repeated accepted requests cannot
allocate twice, even after expiry. Reset permits fresh sequences while fencing
all old handles. An optional existing `ActorInstanceKey` binds an emitter to an
actor in that same lease. The caller supplies pose updates; an old or dead actor
cannot acquire a new attachment by recycling a numeric ID.

OBP chooses the demonstration's seeds, emission windows, translated placement,
64-instance host budget and at most eight supplied presentation lights. Those
are authored admission/presentation choices, not recovered game policies. The
host accepts at most one second per advance; the Godot adapter caps a longer
stall to that catch-up budget. Native pool capacity/RNG allocation behavior
stays in the source implementations.

Novalis PVars include authored world-space bounds. Its adapter therefore keeps
the original simulation frame and applies a fixed presentation translation,
with inverse-translated camera observations. It does not rewrite those bounds
to make relocation work. Pose changes affect future emission; existing particles
keep their native positions relative to that fixed placement.

## Adding a recovered recipe

1. Recover and validate the caller, parameters, bank and update in its RAC
   project first. Keep original instruction/record witnesses beside the native
   tests. Follow the [external-source policy](../research/EXTERNAL_SOURCE_POLICY.md).
2. Implement `IRuntimeEffectFactory` in that RAC project, bound to the admitted
   native assets/parameters. Expose a complete `RuntimeEffectRecipeKey`, a native
   tick rate, qualified textures and an explicit emission bound where required.
   Fail construction for unsupported context or missing images. Each instance
   owns simulation state/RNG and emits neutral observations from `Capture`.
3. Register the factory in the CPU-only `EffectSourceData` source loader. Keep
   decoding off the scene thread, check cancellation/source stamps and avoid
   consulting the loaded world's origin.
4. Add an entry to `effects_experiment.json` referencing that exact registered
   caller. Supply only its instance ID, source/build/bank/asset/recipe identity,
   seed, OBP Y-up position/endpoint and emission ticks. Do not put raw PVars,
   native addresses-as-pointers, replacement curves or arbitrary type dispatch
   into the neutral data. R&C1 type 6 has one authored preset; UYA charging
   currently ends before age 79's unsupported ready-charge composite.
5. Test admission, snapshots, native clocks, drain/retirement, reset and source
   loss. Add a retail-backed instance to the simultaneous rendered fixture.

`EffectExperimentDefinition.ResetKey` matches the Lab station's `native-effects`
key. The focused start/reset control result is available for #257's future
experiment UI; this milestone adds no second encounter reset framework or
gallery geometry for #258.

## Reproduction and limits

Run `tools/test.ps1 -Configuration Release`, the format check required by
`AGENTS.md`, and `tools/test-retail.ps1` with authorized source environment
variables. `EffectSourcePortRetailTests` repeats all six real recipes through
ten resets in each of five world identities, comparing exact float/byte draw
facts and native RNG counts. The original #282–#284 instruction oracles remain
the authority checks for native behavior.

For real rendered acceptance and separate-process record/replay:

```powershell
./tools/test-effects.ps1 -OutputDirectory captures/effects-285/run-a
```

Use the checksum-pinned Godot .NET editor (`-Godot` can select an existing
installation). The script requires all three authorized source paths, uses the
ordinary source gate and original avatar, and writes private reports/captures,
an isolated source configuration and a versioned input recording under the
ignored output directory. Use a fresh directory for a new recording; use
`-Mode replay -SkipBuild -OutputDirectory ...` to replay an existing run without
changing its assembly identities.

The rendered scenario checks all six live generators together, ten isolated
resets, stop/drain, source removal, retired world resources/handles and captures
in R&C1 Veldin, GC Oozla, UYA Veldin and Lab re-entry. Its owned camera and
above-terrain placement are diagnostic framing. It requires visible pixel
changes for each source, in addition to CPU snapshot and resource assertions.
Record/replay captures ordinary `effects start/reset` operations and native
ticks, counts, attempts, emission and RNG checkpoints; the experiment data
fingerprint participates in replay provenance.

The focused input tape covers two native windows of at least 20 ticks. A longer
idle Lab tape currently exposes small class-749 actor wall-contact differences
across fresh processes, tracked in [#404](https://github.com/je55pr/one-big-package/issues/404).
Effect clocks/counts/RNG agree in those longer runs, but whole-scene replay is
not claimed past that contact. Actor checkpoint strictness is unchanged; full
effect expiry/reset/unload is exercised by the separate rendered scenario.

Godot presentation remains bounded: full native depth-bucket sorting,
conditional GS depth writes/alpha tests, VU1 rasterization, GS colour response
and whole-retail global RNG ordering are not reproduced. GC moving-context and
UYA transformed-world particles, the age-79 charge composite and additional
sprite/mesh/light algorithms remain explicit native recovery work. This host
does not turn them into generic Godot particle curves.
