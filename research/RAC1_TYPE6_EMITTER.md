# R&C1 class-27 / type-6 emitter (#282)

This milestone implements the original NTSC-U class-27 generator in `OBP.RAC1`.
It uses the existing particle pool and Part-bank sprite renderer. It does not
register world emitters in the shared game host; that composition belongs to #285.

## Authored fixture and corrected acceptance

The complete census of all 19 retail level gameplay files finds **ten** class-27
instances, all in Novalis (`LEVEL1`): instance indices 285–294, PVar indices
339–348. Every 0xe0-byte PVar block has SHA-256
`8d5d507bb3ef654ef549279a97fc2c94d08cb6a87e3c6db13ff2076f74f82c71`.
There is one initial authored preset, not two different parameter sets. The issue
owner explicitly waived #282's second-preset requirement after this census; no
second authored effect is invented. Synthetic branch probes are labelled as such.

Authority is `rac1-ntscu-original`, NTSC-U SCUS-97199, original retail. The Novalis
overlay SHA-256 is
`81673b8dcf5930c7313a7c83d0f76e5e1948691dc09af63dd8c8f6a04f9a72ae`.
The boot executable SHA-256 is
`e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9`.
`EffectsReferenceAuditRetailTests` retains the bounded constructor/update code
hashes and native call witnesses from #281.

## Native behavior and admitted boundaries

`Rac1ParticleEmitterParameters` reads class-27's PVars without changing their
native units. `Rac1ParticleEmitter.FromAuthored` supplies build/level/instance
provenance and the original parameter hash. `ApplyNativeFrame` accepts subsequent
source-owner position, rotation and parameter writes; existing particles read
the owner's current parameters on each update. This is the attachment boundary,
not a new implementation of the class-660 flyer/joint controller.

Call `Emit(owner, previousView)` during the source Moby pass, then `Step` once at
60 Hz. The source paths are class-27 `0x2bd100`, constructor `0x27e900`, update
`0x27eb08`. Novalis alone checks the previous rendered view (far 240, radius 20).
A missing load-pass view culls without decrementing the countdown or drawing RNG.
The supplied orthonormal view uses a bounded host projection; exact VU frustum
boundary rounding is not claimed.

Each attempted particle consumes eight native high-15-bit RNG samples, including
failed allocations: three offsets, three velocity/Euler components, size, angle.
The native Y offset range is `[-P.d0, +P.d4]`. Euler degrees multiply by pi before
division by 180. Flag 0x4000's Euler branch passes the newly computed X angle to
the Y and Z additions; that retail quirk is retained. A host seed is a controlled
fixture, not a replay of the game's other global RNG consumers.

The updater preserves position-before-acceleration, speed limiting rather than
drag, split high/low colour bytes, flagged ramp reversals, packing the old alpha
and angle before stepping them, size clamps, the native timer and XYZ kill box.
Scalar add/subtract/multiply chop toward zero; FPU division rounds nearest.
Euler sine/cosine and normalization remain bounded host projections of VU helpers.
The initial Novalis velocity is approximately unit length; its first move uses
that velocity before the 0.015 speed cap. Size starts in [200000,310000), grows
500 native units per tick, and lifetime is 150 updates. Texture comes from
definition **4**'s first frame (Part texture 0), not definition 6; blend is 0x48
and depth byte 0x74. Initial packed RGBA is 0xff262626.

Collision flag 1 explicitly throws `NotSupportedException`. The native contact,
reflection and re-placement path is witnessed but has no recovered contact
adapter in this slice. Unknown high flag bits and unsupported timer ranges also
fail explicitly. There is no implicit substitute collision/gravity simulation.
`StopEmission` drains existing particles; `Retire` removes them at the next Step.
Those two safe lifetime controls are OBP policy, not recovered pointer behavior.

## Existing-family consolidation audit

| Generator | Simulation owner / reusable presentation | Decision |
| --- | --- | --- |
| Types 2, 4, 8, 11, 12, 15, 16 | `Rac1ParticleSession` and its existing partials; shared sprite pool | Preserve every existing family algorithm, RNG order and recurrence |
| Type 6 | Same pool, native owner/PVars and sprite facts | Add only the class-27 constructor/update dispatch |
| Bomb / generic weapon / mine recipes | `Rac1BombExplosion`, `Rac1WeaponExplosion`, existing session methods | Keep source-owned counts, debris, smoke phases and camera-conditioned recipes |
| Tesla | `Rac1TeslaPresentation` specialised native core/curl ribbon generator | Keep ribbons separate; do not relabel them type 6 |
| Debris and explosion shells | Existing source actor recipes and mesh presentation | Keep separate from pool sprites |

`CaptureSprites` copies deterministic texture, blend, depth, transform, colour,
angle, lifetime and slot identity facts in slot order; type-6 snapshots also carry
the qualified source and owner. `Rac1ParticleView` reuses a caller-owned snapshot
buffer with the same batching/shader behavior and contains no emission or motion implementation.
No new lines/flat-quads/ribbons framework is needed for this fixture. `SupportsType`
explicitly lists only 2,4,6,8,11,12,15,16; the other native pool types are unsupported
by this managed session. This is not coverage of the entire native dispatcher.

The default pool remains 2048 records. Allocation uses the lowest free slot;
identity changes on reuse and survives Clear monotonically. Step fixes its entry
high-water mark: children in an existing later hole can update immediately, while
growth past that end waits. The managed pool does not expose stale native bytes
in unused fields, or reproduce GS ordering/hardware load shedding.

## Reproducible verification

Portable tests cover burst/countdown/cull RNG consumption, full-pool attempts,
2048-record capacity, stale identities, immutable snapshots, owner movement and
live parameter writes, ramps, packing order, kill bounds and retirement. Existing
Bomb/Pyrocitor/explosion/mine regressions remain required. Retail tests census the
actual authored PVars, decode their Part texture and test their lifetime.

The optional original-instruction comparison uses the external MIT interpreter
at `daino-selvatico/rac1-native-game` revision
`3fd14c12e4dff2fd4b6661a6e54187faea3f76c8`. Build it locally with:

```powershell
./tools/build-rac1-type6-oracle.ps1 -ReferenceRoot $reference -Cxx $compiler -OutputPath captures/type6-oracle.exe
$env:OBP_RAC1_TYPE6_ORACLE = (Resolve-Path captures/type6-oracle.exe).Path
./tools/test-retail.ps1
```

`$reference` is that local pinned checkout; `$compiler` is a C++20 compiler
(Zig's `c++` driver is supported); either a path or an executable name on `PATH`
is accepted. `OBP_RAC1_ISO`, `OBP_GC_ISO`, `OBP_UYA_ISO`
select authorized local inputs. The test creates boot/Novalis RAM temporarily and
deletes it in `finally`. No retail payload, executable, capture, personal path or
upstream generated translation is committed.

The oracle executes the original class-27 emitter, RNG, type-6 constructor and
updates, including original boot VU microcode. Only allocation/free and visibility
are conditioned hooks (one free record and a visible owner). It compares the
genuine authored preset and explicitly synthetic Cartesian/Euler owner-rotation,
acceleration and ramp probes, each at seeds 0,1234,32768 over 150 updates.
Texture/blend/depth, packed colour, angle byte, timer and live/dead state match
exactly. Position/velocity tolerance is 0.0001 native world units; raw-size
tolerance is 0.03125 (the float32 round trip through renderer size units).
This is a conditioned instruction comparison, not an end-to-end PS2 gameplay or
render-equivalence claim. See `THIRD_PARTY_NOTICES.md` for ISC/MIT attribution.
