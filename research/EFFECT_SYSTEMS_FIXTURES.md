# Effects audit fixtures and reproduction (#281)

Read [the catalogue and licence ledger](EFFECT_SYSTEMS_AUDIT.md) first. This
file is the handoff to #282–#285, not a report that those generators or a new
Museum editor already run.

Completed source fixtures are documented separately for [R&C1 #282](RAC1_TYPE6_EMITTER.md)
and [GC Museum #283](RAC2_MUSEUM_PARTICLES.md). The latter has three distinct
authored type-85 presets; type 97 is no longer needed to demonstrate reuse in #283.

## Checks supplied by the investigation

[EffectsReferenceAuditRetailTests](../tests/OBP.Tests/EffectsReferenceAuditRetailTests.cs)
contains three source witnesses:

| Test | What its assertions establish |
|---|---|
| `NovalisClass27AndType6HaveAConcreteRetailFixture` | Correct overlay; ten authored owner records and identical PVar hashes; type-6 constructor/update windows and real caller; allocator capacity/stride/half-clear; visibility and collision call edges. |
| `MuseumEditorAndDemonstratorUseDifferentSourceParticleFamilies` | Museum identity and 17 controls; pool clearing/callback dispatch; correct type-85 constructor/update; separate type-91/type-78 qcopy placements; a shared recipe's type-97 child. |
| `UyaChargeMuzzleAndReferenceSpawnerHaveSeparatePoolPaths` | Veldin overlay; allocator/update walk; charge type 97; muzzle types 23/44; separate type-107 lead and registration stores. |

Expected SHA-256 values, exact window bounds and call addresses are in those
tests, next to the assertions. They are reproducible numeric witnesses, not a
second hand-maintained dump or a licence to distribute any input. Window hashes
cover the declared ranges, including padding where present; they are not a
claim to have matched every upstream compiled function.

On an authorised machine, set `OBP_RAC1_ISO`, `OBP_GC_ISO` and
`OBP_UYA_ISO` to the matching local sources. Keep those values out of logs,
Git and reports. From the repository root:

```powershell
dotnet test tests/OBP.Tests/OBP.Tests.csproj -c Release --nologo --filter FullyQualifiedName~EffectsReferenceAuditRetailTests
./tools/test.ps1 -Configuration Release
dotnet format OneBigPackage.sln --verify-no-changes --no-restore
./tools/test-retail.ps1
```

The focused tests are independently skippable per source. A skipped test is not
retail verification. The complete retail wrapper requires all three sources.
Portable coverage still includes the existing particle, texture, explosion,
Tesla and Shock Cannon suites. No new Godot script or UID is needed.

The audit ran the focused tests against all three sources: **3 passed, 0
skipped**. No PCSX2 particle trajectory, external native PC application or
new rendered effect was run. Completing the source-witness gate does not
complete the planned generator fixtures below.

## Common native trace procedure for later stages

This is a manual breakpoint/probe plan; no new trace CLI is implied.

1. Start the exact source/build/overlay in an authorised native execution
   environment, with its normal loader/registration completed. Verify the
   overlay hash before trusting an address. Initial file bytes alone do not
   contain the live relocated pointer tables or owner mutations.
2. Stop before the named caller. Record the native tick, caller and all inputs:
   a0–a3, t0–t3, f12–f14, relevant stack arguments, owner/source identity, camera
   state, timer and RNG state. Preserve ABI evidence; reconstructed C prototypes
   can omit mixed-register arguments or mislabel bytes as pointers.
3. Follow the real allocator and constructor. Observe allocated slot/index,
   generation tracked by the test harness, count/high-water/hint and selected
   definition/texture. Sample the record **after caller patches**, not only
   after the constructor return.
4. Step one source update at a time. Sample position/velocity, packed colour,
   size, rotation, remaining life, owner/context inputs and pool membership
   at spawn, updates 1/2/5/10, a midlife point, last-live and first-dead. Log
   random-call counts and allocation failures; a chosen seed alone is not
   enough to reproduce the game's global stream.
5. For draw verification, sample native view, sorted slot order, selected bank
   entry, GS TEX0/TEX1/CLAMP/ALPHA/TEST/ZBUF and emitted primitive/vertex counts.
   Use overlapping normal/additive particles and an opaque occluder. This
   resolves conditional depth and ordering questions that isolated sprites hide.
6. Repeat the same captured inputs in isolated source-owned C# tests. Use
   float bits/packed bytes when the native operation order is recovered;
   name and bound tolerances for host trigonometry or VU projections. Stop
   emission, drain naturally, then retire/reuse a slot and retire its owner.
7. Keep raw dumps, executable slices, textures, savestates and captures private.
   Commit only small numeric assertions, hashes, schema/command descriptions
   and authority identities. Record execution versus static-byte evidence
   separately. Do not commit personal paths.

A useful per-sample summary is source/build/overlay hash, fixture ID, source tick,
phase, recipe/caller, source-qualified owner, slot/generation, live count,
definition/bank identity, field hashes and draw-count/state observations.
Unsupported collision/context or a changed source must fail explicitly.

## R1-A: Novalis class-27 / type-6 emitter

**Prerequisites:** R&C1 original NTSC-U LEVEL1, its Part bank/definitions and one
authored class-27 owner (i285 is the first). The existing test pins i285–i294,
PVars 339–348, 224 bytes each, all SHA-256
`8d5d507bb3ef654ef549279a97fc2c94d08cb6a87e3c6db13ff2076f74f82c71`.
The collision bit is clear in flags 0x170E, so the first bounded fixture can
recover useful native behaviour without pretending collision is implemented.

**Native route:** owner `0x2BD100`, visibility `0x2221F0`, spawn
`0x27E900`, allocate `0x27C498`, update `0x27EB08`, free `0x27C5E8`,
draw `0x27C878`. Observe owner PVars through record +2C.

| Sample / experiment | Expected observation and present confidence |
|---|---|
| Authoring facts | **Verified:** one particle per burst, interval byte 0, life 150, texture definition selector 4, flags 0x170E. These values are not an OBP-designed emitter preset. |
| Spawn | **Inferred from current ReRAC source:** eight random draws; additive 0x48, depth byte 0x74, initial RGBA 0xFF262626; the referenced Novalis definition selects Part texture 0. Confirm the runtime definition pointer and texture against the native loader. |
| Update 1 | **Inferred:** initial velocity moves the particle before the speed clamp to 0.015; size rises by 500; alpha falls by approximately 0.0067; packed colour/angle use the pre-step values. Compare actual bits, including the split-channel precision quirk. |
| Update 149 / 150 | **Inferred:** last live then retired on update 150 from alpha/timer. Disable further bursts before checking drain. Observe whether allocation-tick dispatch makes a rendered sample's apparent age differ. |
| Culled/load-time owner | Zero view before Novalis's first render should suppress emission and RNG; an off-screen sphere must suppress both. Verify this in native execution, not by treating distance as a radial cutoff. |
| Live owner | Freeze the owner's full input sequence first, then supply a translated/rotated owner sequence. Native class-660 flyers mutate attached emitters; a stationary-owner test is an explicitly isolated fixture, not ordinary Novalis ambient behaviour. |
| Saturation/reuse | Fill to 2048, fail another allocation, free lowest/last slots, reuse. Check RNG order, fixed-end child dispatch and half-cleared native bytes independently of managed object identity. |
| Collision extension | A later flag-1 fixture must capture `0x211870/0x271170` results, bounce factor and hit+normal offset. First-slice admission must report unsupported collision rather than skip it silently. |

The current ReRAC [type-6 tests](https://github.com/re-rac/rerac/blob/7d0d031a4f293c1214d01de90918fe3ec234b673/crates/rc-game/src/particles/type06.rs)
provide useful isolated spawn/150-update/owner/cull comparisons. A developer
with Rust installed can check out the ledger's revision and run
`cargo test -p rc-game particles::type06` in that external checkout.
Its full native trace workflow is documented in
[tools/trace](https://github.com/re-rac/rerac/blob/7d0d031a4f293c1214d01de90918fe3ec234b673/tools/trace/README.md).
These upstream tools were inspected, not run here; they are not new OBP
runtime dependencies.

**First #282 stop point:** one admitted no-collision owner-driven generator,
with native spawn/update observations, existing seven-family regressions and
drain/reuse passing. Then choose collision or one additional native family,
rather than expanding to all 81 types.

## GC-A: Museum editor's type 85

**Prerequisites:** GC NTSC-U v1.01, LEVEL21.WAD/engine 30, its relocated
definition table/Part bank, native registration and Museum PVars. Use
`GcMuseumEffectControls.ReadParticleEditor` to enumerate the 17 typed
controls; preserve the literal native labels, including its timer-label typo.

The parameter map is velocity +00/+04/+08, life +10, RGB +14/+18/+1C, rotation
+20, initial/end opacity +24/+28, initial/end scale +2C/+30, gravity +34,
texture selectors +40/+44 and short interval bounds +4C/+4E.
The emission countdown is a separate float at +0C, not the lifetime.

**Route:** `0x418B04 → 0x33E8F8(type 85) → 0x330670`;
registration maps 85 to `0x3309C0`, which reads the record's
`+3C = 0x33EAA0`. The editor then sets record +09 to FF and selects the
texture via `0x1FF940[Texture1][Texture2]`.

Use two recorded runs: the authored native setup, then an explicitly injected
test setup with zero rotation/gravity/velocity, life 12, equal opacity 96,
equal scale 1 and timer bounds both zero. The second is a diagnostic input,
not a recovered authored preset. Select a **validated available definition/frame
pair**, not an arbitrary pair just because both numbers are in menu range.

Expected constructor samples for that injected setup are life 12, size
210000, additive blend, zero size/opacity deltas; the post-caller depth byte
is FF. These are **inferred** until captured in the native function. Check
updates 1/2/11/12 and the timer helper's exact expiry; do not impose R&C1's
timer convention. Then vary end scale/opacity to expose integer quotient and
remainder handling in the constructor rather than replacing it with a tween.

**Blocker resolved next:** decode and witness the real definition/frame bank
and draw state for this family, including the post-constructor overrides.
No complete PC Museum renderer was verified by the survey.

## GC-B: shared demonstrator recipe's type 97

Use the same Museum source. The locally checked caller at `0x41911C`
inside the Museum-placed shared driver `0x418E30` invokes recipe
`0x419350`. Its child call `0x419730 → 0x3401B0` allocates type
97, with update `0x340328`. The first child selects frame 1; its colour
arguments supply RGB 0xFF7F40 with opacity 127. The later call at 0x419784
uses another frame/colour/rotation combination. Keep native float inputs
and integer arguments; do not infer them from a screenshot.

Capture those two child constructions and updates 1/2/5/last-live/dead,
including selected texture, alpha, scale, gravity and rotation. This
demonstrates a second native family with a reusable renderer. It does not
require claiming that the complete parent composite's shell, debris,
distortion, light or camera shake has already been recovered.

**Next bound:** one captured child recipe and its source-owned update/drain.
The qcopy references are further leads: type 91 at Museum 0x33F538 and type
78 at 0x33D2E8 are not the editor constructor. If an additional family is
needed, recover one of those with its real caller, not an address-based alias.

## UYA-A: Shock Cannon charge particles, type 97

**Prerequisites:** original NTSC-U TABLE1, charge-capable held Cannon class
0x1882/item 43, its actual muzzle/owner inputs and the Part bank/definitions.
The beam's Fx10/Fx23 textures cannot substitute for these Part textures.

**Route:** held-controller call `0x385C40 → 0x46DCB8 → 0x45E438(97)`;
registration at `0x45F4D0` selects update `0x46DE20`.
Capture one ordinary charge emission's mixed-register arguments and all
caller patches, stop the held emitter, then sample until its last particle
retires. Repeat with native allocation saturation.

Expected mechanism is additive blend, byte-scaled rotation and opacity
delta, float scale step, timer/free and per-tick velocity/gravity. Exact
spawn values and lifetime are capture outputs, not assumed from GC or from
the weapon's total charge duration. The UYA update additionally invokes
`0x4C6EC8` and, under native context, `0x4C41E8`; isolate both branches
before admitting a general context-independent generator.

**Next bound:** capture/replay this one family independently of the beam and
compare it with GC97. Matching type number/payload fields is insufficient:
compare native calls, operation order, timer scaling, RNG and context effects.

## UYA-B: Shock Cannon muzzle particles, types 23 and 44

**Route:** held call `0x386948 → 0x3877D8`; its two allocation paths
are `0x387A18 → 0x4633E8 → allocator(23)` and
`0x387B10 → 0x466F98 → allocator(44)`. Registered updates are
`0x463578` and `0x4670E8`. One muzzle emission is the bounded
input; disable subsequent shots and follow its two families through drain.

Expected samples must distinguish the two types, texture definitions,
native RNG draw order, owner/muzzle transforms, alpha/scale and expiry.
Use one firing direction and then a rotated muzzle to test attachment.
Render this with the charge fixture to prove that a recipe can reuse
presentation without using the existing ellipsoid beam as its success image.

Spawner `0x46EE10` / type 107 / update `0x46F008` is a separately
pinned allocator example. It is not a third proven Shock Cannon path.
Find a real caller before selecting it for a later generator fixture.
Likewise, `0x3887F8` creates Moby 0x1883; its trails/impact lifecycle
cannot be validated by checking particle count alone.

## Integration acceptance held for #285

Once #264 and the three source milestones are ready, the #258 gallery owner
can place R1-A, GC-A/GC-B and UYA-A/UYA-B together in one #257 Lab scenario.
#271 supplies the selected equipment/attachment inputs. The effect source,
not destination origin, resolves the adapter and bank.

At minimum verify equivalent fixed inputs across destinations, retained
player configuration, source-qualified texture collisions, stop versus
drain, owner retirement, stale generations, source removal, pool exhaustion,
pause/native clock handling and scene/resource cleanup on repeated re-entry.
Native-equivalence comparisons and host repeatability are separate gates.
Preserve all existing source tests and use the renderer's real materials.

Do not call #280 complete merely because these plans or source witnesses pass.
The live per-game generators, complete draw-state recovery and three-game
room are future milestones.
