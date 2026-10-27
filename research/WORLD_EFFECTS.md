# Retail effects and world presentation

Native NTSC-U inputs own formats and update rules. Godot presents the recovered
records; it does not supply replacement particle emission or motion curves.
The coverage below is bounded, not a claim that the trilogy's effect engines
have all been implemented.

The [effects reference audit](EFFECT_SYSTEMS_AUDIT.md) and its
[bounded fixture plan](EFFECT_SYSTEMS_FIXTURES.md) map the reusable native
generators, upstream licences and remaining recovery work for #280/#281.

## R&C1 particles and weapon explosions

`Rac1ParticleSession` runs float32 records at 60 Hz in native Z-up coordinates.
Constructors, lifetime phases, colour interpolation, texture choices and motion
are recovered from the LEVEL0 overlay. `Rac1EffectAssets` decodes the separate
Part and Fx banks from the authorized disc. No texture or executable payload is
distributed. The Veldin overlay identity is
`eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804`.

| Type (decimal) | Constructor / update | Implemented behavior |
| --- | --- | --- |
| 2 | `0x2690b8` / `0x269290` | Three-phase flame motes; packed directions, fade-in, colour/size transition, fade-out |
| 4 | `0x269958` / `0x269a70` | Growing, slowing fragment trails; 45 frames |
| 6 | Novalis `0x27e900` / `0x27eb08` | Class-27 PVar-driven sprites; source implementation, host registration deferred to #285 |
| 8 | `0x26a4c8` / `0x26a5b8` | Growing explosion puffs; ten authored texture frames |
| 11 | `0x26a9f0` / `0x26ac78` | Splitting explosion particles, two child generations, then smoke |
| 12 | `0x26b230` / `0x26b500` | Pyrocitor's two flame looks, rise, braking, growth and fading |
| 15 | `0x26bb70` / `0x26bca8` | Ballistic sparks shedding nonrecursive children |
| 16 | `0x26bed0` / `0x26c088` | Four ground-mine spray modes, distinct gravity, camera fade and smoke after landing |

Pyrocitor emits two long and two short particles per ordinary update, reduced to
one long particle when the unobstructed reach is below four. The short look
randomly selects among eight Part textures; the long look uses lookup24. The
source keeps emitter index2 every fourth update in fifteen travelling damage
slots. A tracked flame at ten remaining frames emits two type2 motes. Their
native diameters transition from0.2 to0.1. World obstruction and damageable-body
queries remain the host adapter; full native collision/filtering, the nozzle
Moby and type25 embers are not represented by this slice.

An ordinary Bomb Glove impact emits splitting type11 particles and fifteen
class122 fragments; only five large fragments leave type4 trails. The old
always-on 150-particle type34 burst was incorrect: that is dust from a separate
water/rolling branch. The ordinary fragment spray now uses the resolved impact
plane, reflected push and a camera-directed fragment. Class1192 shell layers use
their source sizes, colours and timers; their stored velocity does not move them
in `shard_update` (`0x2acad8`).

The generic weapon blast `0x25e450` is distinct from the Bomb recipe. Ordinary
weapon call sites supply these counts; each type11 count emits a warm particle
and a shorter white flash, with close-camera reductions:

| Weapon | Type15 sprays | Type11 pairs | Type8 puffs | Class122 debris | Main / white shell size |
| --- | ---: | ---: | ---: | ---: | --- |
| Mine, air burst | 3 | 3 | 5 | 1 | 2 / 1 |
| Doom bot | 5 | 2 | 4 | 5 | 2 / 1 |
| Visibomb | 10 | 3 | 16 | 1 | 4 / 2 |
| Devastator / RYNO | 10 | 3 | 9 | 1 | 4 / 2 |
| Drone Device | 0 | 1 | 1 | 1 | 0.4 / 0.2 |

The camera-directed debris piece is emitted only within14 units. A grounded
mine instead uses type16 sprays, with the source planar camera-distance count
and four dust/fire/smoke modes. Underwater and gold-weapon branches remain
separate recovery work.

The [type-6 milestone](RAC1_TYPE6_EMITTER.md) records the single genuinely authored
Novalis preset, its parameter map, live-owner boundary, unsupported collision and
original EE/VU instruction comparisons. The other seven family algorithms and
weapon recipes remain unchanged. No second authored preset is invented.

`Rac1ParticleView` batches copied sprite snapshots by native texture/blend/depth range. GS ALPHA
selector`0x44` maps to alpha blending and`0x48` to additive blending; primitive
colour components use the PS2 unity value128. Native angle bytes and near/far
depth fading are retained. Fx rows differ from Part rows: palette, pixels, width,
height are read from the independent bank at core`+0x68` (`0x23f768`).

The configurable 2048-record default remains an OBP allocation budget. The #281
Novalis retail witness independently confirms a native 2048 x 0x40-byte pool;
OBP's managed allocator does not reproduce its stale second-half record bytes.
Native hardware-load shedding is not reproduced. Stable record identities
prevent a recycled particle slot from becoming an old flame's damage volume.
Source random distributions are used with a separate host seed; global retail
random ordering is not replayed. Godot texture batches do not reproduce the GS's
complete primitive ordering, VU arithmetic or colour-space response.

## R&C1 Tesla ribbons

`Rac1TeslaPresentation` retains the clocks/parameters of `0x2b94d0` and the
draw calls at `0x2ba970` / `0x2baae0`: white Fx14 cores and blue Fx16 glows on
separate core/curl paths. The white width is reloaded as0.05 even for the curl;
blue half-widths are0.4/0.3. Parameters0.9/0.1 extend blue segment ends on the
nearer camera sphere, rather than setting texture repetition. UVs run along each
segment from the shared phase to phase+1; the phase subtracts0.3 and wraps at-8.

The target line currently supplies a bounded projection with up to twenty
samples. Target discovery, the stateful native moving-point solver, the four
auxiliary five-point arcs and their attachment remain separate. Short host arcs
retain at least five samples so their end fading does not erase the whole arc.
Morph-o-Ray's existing beam is still a presentation approximation.

## R&C2 Museum reference

`LEVEL21.WAD` is the **Insomniac Museum**, engine level30. Its ELF fallback to
name index0 does not make it another Aranos return. Museum-only callbacks and
help/menu identities prove the distinction; the destination catalogue now uses
the correct name. Overlay SHA-256:
`b46e44c26195efd160b1bc62910cf22e1b1fe3eb46944f5926a9f2b7879e45b6`.

`Rac2MuseumEffectControls` reads the actual particle-editor registrations at
`0x4187d4..0x4189f8` (helper`0x413528`) and their labels from retail memory.
The seventeen controls identify velocity XYZ, life, RGB, rotation, initial/end
opacity and scale, gravity, two texture selectors and min/max emission interval.
Values include opacity0..127, Texture1 indices0..103 and Texture2 indices0..20.
The emission path at `0x418a4c..0x418b54` invokes particle constructor`0x33e8f8`;
this is evidence from the native editor, not an invented Godot control panel.

`Rac2MuseumParticleEmitter` and `Rac2ParticleSession` now implement that source
path and type-85 callback, with an independent Part-bank decoder and native
sprite descriptor math. Three distinct authored Museum emitters use the same
implementation. Retail tests compare original EE/VU menu writes, records through
expiry, texture bindings and sprite descriptors. The [#283 recovery notes](RAC2_MUSEUM_PARTICLES.md)
document the integer-opacity lifetime adjustment, copied-owner inputs, explicit
context restriction and conditioned draw boundaries. Host composition remains #285.

Museum shot and explosion demonstrators additionally expose beam thickness,
textures, colour/alpha, debris/cloud/line counts, shell, distortion and camera
shake. They guide further GC recovery; they are not evidence that the GC and
R&C1 recurrences are interchangeable. The Museum editor and GC/UYA particle
engines are not yet hosted.

Shared R&C1 effect descriptions also resolve to class639 dynamic lights
(`0x2d3838` / `0x2d3a10`): independent RGB/radius rise, hold and fall, delays and
lifetimes, with eight native light slots. Those timed lights, native light
assignment/falloff, camera shake and general screen effects are still outside the
current baked-light presentation.

Behavior tests cover lifecycle phases, bounds, splits, texture frames, mine
families, capacity/reuse, sustained emission and eventual cleanup. Native-window
hashes and parameter assertions run with the authorized retail inputs; rendered
weapon/Bomb smoke tests exercise the actual Godot materials and captures.

## Native material effects

Recovered GC/UYA GS material state already reaches `RuntimeMaterialPresentation`.
When a native primitive enables blending, decoded additive source-alpha and fixed-alpha
equations map to Godot additive blending. Unknown equations remain unknown.

Fields named `glow_rgba` or `mode_bits` are not treated as emissive evidence. Their
render semantics are still unresolved, and brightness-derived emission remains a
presentation heuristic rather than native behaviour.

## UYA looping sky shells

UYA retail sky data preserves a signed three-component angular-velocity vector per
shell. Retail sampling proves non-zero values on multiple levels, including Veldin.
OBP now keeps each UYA shell in an independent presentation group and emits a neutral
`RuntimeAmbientAnimation` spin only when that shell's native velocity is non-zero.

The raw velocity is native evidence. The conversion
`value * (60 * 2π / 32768)` radians/second follows the documented public Wrench
interpretation for the NTSC-U authority and is kept source-specific in
`UyaSkyPresentation`. The native Z-up to OBP Y-up Y/Z swap is an odd basis
change, so angular velocity uses the corresponding axial-vector transform rather
than the ordinary position transform. Godot applies the same rule for its X mirror.

`RotationRaw` is still retained by the decoder but is not applied: the exact native
rotation composition/order has not been recovered. The UYA sky bloom flag is likewise
preserved but not promoted into additive or post-process bloom. Sky sprites/fx remain
outside this bounded slice.

GC and R&C1 use the older shell prefix and declare no equivalent shell velocity, so
normal loads for those games gain no invented motion. The existing `AnimateSky`
option remains an explicit synthetic presentation fallback and stays disabled by
default.
