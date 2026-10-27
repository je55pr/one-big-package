# UYA reusable particle emission (#284)

This milestone implements UYA's type **97**, with two authentic Shock Cannon
recipes: charging glow and charged-shot glow particles. It does not use the
existing layered beam mesh as evidence of a working particle generator.
Simulation, recipes, resource bindings and source view math live in `OBP.RAC3`.
The existing weapon, beam and damage paths retain their behavior. Godot/Fusion
composition remains #285.

## Authority and external references

Authority is original NTSC-U SCUS-97353, `rac3-ntscu-original`, **TABLE1/Veldin**.
The packed boot block is at LBA 1463, 771008 bytes; its WAD starts at +7080.
SHA-256 values:

- Packed boot: `200068cb4186715521cdc1c20bc6c5a88054d0da54aa336ffdb2022633351c7d`.
- Decoded boot: `39ddc0784e58f7f94fd4c17341060a7d02a4afcd480bdf693c5ae5173c7d21f9`.
- TABLE1 overlay: `093fb8641cb2bf48073793f6cfac726cb7321654d285b848658f3e957df1dcec`.

The [#281 catalogue](EFFECT_SYSTEMS_AUDIT.md) supplies the pinned source crosswalk
and licence ledger. UYA decomp revision
`fc118a0b7442665a2b181055dc352ce5d117c41b` identifies
[type-97 construction](https://github.com/vetusmagnus/rac3-uya-decomp/blob/fc118a0b7442665a2b181055dc352ce5d117c41b/src/levels/common/func_0046DCB8.c),
[dispatch](https://github.com/vetusmagnus/rac3-uya-decomp/blob/fc118a0b7442665a2b181055dc352ce5d117c41b/src/levels/common/func_0045F4D0.c)
and the [separate type-107 lead](https://github.com/vetusmagnus/rac3-uya-decomp/blob/fc118a0b7442665a2b181055dc352ce5d117c41b/src/levels/common/func_0046EE10.c).
The latter's blend-byte argument is tentatively typed as a pointer upstream;
this is not an OBP pointer contract. Native TABLE1 callers of type 107 are
`0x310460` and `0x395918`, not the traced Cannon calls. Type 107 is not admitted.

OpenRAC's pinned [sequel-port notes](https://github.com/OpenRAC/OpenRAC/blob/415e1added5b7c879f87875604d97c7d7237734a/port/game/rac2-ntsc/README.md)
describe GC front-end startup and a first-level-load stop, not verified running
UYA particle emission. GC/UYA reconstructed `src/` files are explicitly unlicensed;
tooling/header GPL licences do not license those game implementations. No C or
assembly from either decomp is copied or mechanically translated here. These
source locations are leads; the implementation was independently recovered from
authorized retail instructions and checked by executing those instructions.
The research oracle uses the separately licensed MIT EE/VU interpreter already
acknowledged in `THIRD_PARTY_NOTICES.md`. Gameplay remains C#.

## Gameplay event to emitted particle

All addresses below are TABLE1 placements, except the boot RNG.

| Stage | Native route and implemented boundary |
| --- | --- |
| Weapon identity | V5 definition 43, held class 1882, source resource slot 39; existing `Rac3ShockCannonSession` owns admission, ammo and damage |
| Charging admission | Held controller `0x385948`; trigger/ammo/charge gates increment PVar +0C. Ages 1–78 select `0x385e80..0x385f50` |
| Charging recipe | Two iterations, constructor call `0x385f18 → 0x46dcb8`; successful calls patch Part texture from definition 104, random frame 0–3 |
| Charged-shot glow | The positive beam-counter block prepares native muzzle/end points. `0x3865cc..0x38675c` emits five random positions along that segment at `0x38667c`, then one muzzle glow at `0x386724`, through the same constructor |
| Allocation | `0x45e438 → 0x45e440`, 2048 records × 40 hex bytes, lowest-free bitmap, all four quadwords cleared |
| Dispatch | `0x45f4d0` registers 129 rows at `0x220ec0`; row 97 directly selects `0x46de20`. The constructor also stores that callback at +3C |
| Tick / expiry | Walk `0x45e788` captures its entry high end, skips negative flags. Callback `0x46de20` calls short timer helper `0x40bac0`; expiry frees via `0x45e580` before any movement/fade |
| Part resources | Loader `0x4224c8` relocates definition pointers at `0x221118`, copies the frame blob to `0x221370`, and binds packed Part texture descriptors at `0x220ac0` |
| Presentation | `PartProc 0x45e810`, sprite leaf `0x45ee98`, cache helper `0x45f438`; 64-byte sprite descriptors feed VU1 |

The caller supplies the already resolved native muzzle and beam end, including
their W components. Native joint/aim/player-pose preparation remains with its
source owner; no Godot ray, Euler convention or guessed endpoint is substituted.
The charged-shot recipe uses native `0x40be68`'s ordered VADDA/VMSUBA/VMADD
interpolation, retaining muzzle W. It is not a host `Vector4.Lerp`.

These particles run before the positive beam counter is decremented, including
its transition-to-zero update. The existing beam mesh event omits that last
update. `Rac3ShockCannonParticleEmitter` therefore uses the already returned
beam-query admission fact rather than the mesh event; it does not execute those
queries or apply damage again.

## Recovered family and two distinct recipes

| Constructor field | Native behavior |
| --- | --- |
| +00/+01/+02/+03 | Type 97 / flags 0 / resolved Part ID / additive GS ALPHA 48 |
| +04/+08/+09/+0A | Packed RGBA / random angle byte / depth 44 / signed-short remaining life |
| +0C / +10..1C | Native size / copied XYZW position; W is not GC type 85's context selector |
| +20..2C | Copied XYZ velocity / gravity |
| +30/+34/+35/+3C | Float size step / byte opacity step / byte spin / callback 46DE20 |

Common construction takes a frame index in definition **97**, whose first
texture is Part **12**. It computes opacity step with signed integer division
`(end−initial)/life`, but **keeps the requested life**. Size step divides by that
same life. Updates wrap alpha, add old velocity to XYZ, perform native frame
conversion, apply gravity, wrap angle and add size step. A life N has N−1 moves
and expires on update N. There is no particle collision, damage query or live
owner-PVar dependency in this family.

| Recipe | Native parameters and result |
| --- | --- |
| Charging, ages 1–78 | Two particles at the muzzle; life random 10–20, RGB `(32,128+age,255−age)`, initial opacity `age/3` (integer), end 0, size 21000→115500, zero initial velocity |
| Charged-shot glow | Five random segment positions, size 168000→315000, plus a muzzle particle with literal native size 63000.00390625→210000; life 10–18, RGB `(32,64,128)`, opacity 80→0, zero initial velocity |

Both use gravity −1/3600 per tick, spin from the actual reversed `(-1,-3)`
integer call, and successful post-selection from definition **104**, frames 0–3
→ Part **53–56**. All four are original 64×64 PSMT8 images; decoded RGBA hashes
are pinned in the retail test. They are not Fx10/Fx23 from the beam renderer.
The two recipes differ in count, position, colour, alpha, size and lifetime
through the identical type-97 implementation; no synthetic second preset is
used as an acceptance example.

UYA's boot RNG `srand 0x13d3d0`, `rand 0x13d3e0` uses 32-bit
`state*0x343fd+0x269ec3`, returning `(state>>16)&7fff`. Helpers
`0x440848/0x440878` use signed remainder. Reversed endpoints are not normalized:
`(-1,-3)` has divisor −1 and always yields **−1**, while consuming a sample.
Float helper `0x4408c0` retains its multiply/multiply/add order and 1/32768 factor.
A controlled seed is not a replay of other retail RNG consumers.

| One complete recipe call | Successful pool | Full pool |
| --- | --- | --- |
| Charging | 2 particles, 8 samples | 0 particles, 4 samples |
| Charged-shot glow | 6 particles, 29 samples | 0 particles, 17 samples |

Failure still consumes recipe position/life/spin draws, but skips constructor
angle and the successful-only texture choice. Native saturation is exercised
using the real allocator, not an allocator hook.

## Admission, lifetime and explicit missing channels

The source bridge binds a typed held-Moby key to the current weapon generation
and a qualified game/build/TABLE1 source. It accepts only the current returned
weapon frame once. Repeated calls return `AlreadyConsumed`; old frames or a
reset/unequipped weapon return `StaleOwner` before RNG/allocation. Missing textures,
foreign sources and unsupported context fail explicitly before frame consumption.
Failed resource admission can be retried with the same current frame.

Particles retain copied inputs and owner provenance, not a native Moby pointer.
Stopping admission drains existing particles. Slot reuse uses monotonically
increasing managed identities and reference guards. Clear/stop/generation fences,
diagnostic capacity and snapshot buffering are OBP lifetime policy. #285 must
retire the emitter on world/held-owner teardown; this milestone does not register
a world host or attach a persistent player session to a destination.

Native `0x4c6ec8` always runs; its null-frame branch copies the point. A nonzero
global frame pointer at GP−1884 invokes a separate transform. Global `0x1c673c`
also selects directional gravity through `0x4c41e8`. This slice explicitly
admits the static/null-frame, ordinary-gravity case. Moving-frame and directional
gravity flags throw before simulation mutation; neither is silently ignored.

At age 79 the separate ready-charge composite uses other type-97 calls, type
111 and optional type 44. It is not replaced with the pre-ready recipe:
`UnrecoveredReadyCharge` reports that missing channel, while the independently
admitted charged-shot glow can still run. Muzzle types 23/44, impacts (Moby
1883), spread trails, lights, shake, distortion, full camera context and those
extra charged-ready families remain outside this bounded family.

## GC/UYA comparison and safe reuse

| Area | Proven result |
| --- | --- |
| Pool/header | Both sequel fixtures use 2048×40 hex, full clearing, lowest-free allocation and a fixed-entry-end walk. This does not import R&C1's partial-clear behavior |
| Dispatch | Museum has 111 rows, TABLE1 has 129. GC type 85 uses a callback thunk; UYA type 97 is directly registered. Equal type numbers do not identify equal families |
| Lifetime | #283 GC type 85 replaces requested life when the integer opacity quotient is nonzero; UYA 97 does not. The original UYA constructor is separately checked with life 100, opacity 0→127, through expiry at 100; the GC fixture expires at 127 |
| RNG | GC's newlib LCG is 41C64E6D/3039; UYA uses 343FD/269EC3. Native call/sample order belongs to each recipe |
| Context | GC 85 has a copied float context selector; UYA 97 uses shared world-frame and directional-gravity globals |
| Resources | Measured definition-header/blob grammar, zero-offset fallback and 16-byte PSMT8 Part rows agree; actual tables, banks and texture IDs remain independently source-qualified |
| Sprite leaf | GC `3310E8+118` and UYA `45EE98+118` are byte-identical: SHA-256 `400bce1e6a3040e64674c888a902a0c1c682de798285afb218b4f158aef92526`. The entire 3112-byte renderer is different, including projection/context placement |

GC's separate type 97 (`3401B0/340328`) remains a distinct, unimplemented family
from the #281 catalogue. This comparison does not claim that GC97 equals UYA97.
Source-owned C# draw projections preserve the proven sprite leaf, native cosine
tables, depth fade and cache TEX0 packing independently. Neutral reuse is limited
to existing `RuntimeTexture`, provenance/typed identities, the shared PS2 palette
codec and `RcParticleSpriteDescriptor`'s proven 64-byte VU packet encoding. The
packet codec contains no projection, simulation or resource interpretation;
there is no cross-game particle simulator or RAC3→RAC2 dependency.

The draw oracle declares a view, projection W=1/420000, GS bank/cache bindings
and one admitted sprite after culling/sorting. Original material/descriptor
instructions and scratchpad DMA execute. VU1/GS rasterization, frustum boundary
rounding, complete sorting, depth writes/tests and live Godot images are not
claimed. Native additive ALPHA 48 and colour/alpha unity 128 remain source facts.

## Validation, reproduction and next extension

`Rac3ParticleRetailTests` compares every live 64-byte record and selected
64-byte sprite descriptor per tick for both real recipes, seeds 0/1234/32768,
start/step/expiry, repeated emission, translated/reoriented input segments and
near/far fade. It compares all 129 definition first frames and 65 packed texture
bindings against the actual native loader. Portable tests cover source/context
admission, opacity/life differences, spin, expiry order, pool reuse, unavailable
assets, double admission, stale generations, independent damage fences and the
last native beam-particle update.

Run `./tools/test.ps1 -Configuration Release`,
`dotnet format OneBigPackage.sln --verify-no-changes --no-restore`, and
`./tools/test-retail.ps1` with authorized local sources. The optional instruction
checks additionally need `OBP_RAC3_PARTICLE_ORACLE`, built using
`tools/build-rac3-particles-oracle.ps1 -ReferenceRoot ... -Cxx ... -OutputPath ...`.
The MIT interpreter pin is `3fd14c12e4dff2fd4b6661a6e54187faea3f76c8`; Git
checkouts qualify the actual compiled sources/headers against it. Non-Git exports
remain caller-qualified. Original boot/overlay/core RAM images are private
temporary inputs and the tests delete them in `finally`; no retail/GPL payload
is committed. Ordinary CI runs portable fixtures without retail assets.

For another UYA consumer, identify a real constructor caller, native owner/pose
context, argument values, post-construction patches and clock/RNG order. Reuse
`Spawn97` only after independently executing that caller; recover required world
context before admitting it. Another family needs its own dispatch/update and
draw-kind evidence. #285 can consume the qualified snapshots and explicit
admission status without re-emitting gameplay damage or adding per-effect physics.
