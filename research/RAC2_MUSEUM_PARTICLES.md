# Going Commando Museum particle generator (#283)

This source milestone implements class 4887's editor emission path and reusable
particle type **85** in `OBP.RAC2`. Three genuinely different authored Museum
emitters use that one constructor/callback. No second recipe or preset is
invented to satisfy the two-effect acceptance item. Godot/Fusion composition is
owned by #285; the source fixture and native descriptor comparisons run without it.

## Authority and licence boundary

Authority is original NTSC-U SCUS-97268 v1.01, build `rac2-ntscu-v1.01`.
`/G/LEVEL21.WAD` is file level 21 / engine level 30 (Museum). Overlay SHA-256:
`b46e44c26195efd160b1bc62910cf22e1b1fe3eb46944f5926a9f2b7879e45b6`.
Boot `/SCUS_972.68` SHA-256:
`36d5814d8d95328d5839612ccdf7a2e7ecac0b6f868d3ad4bb2e98411f734b4a`.

The [#281 audit](EFFECT_SYSTEMS_AUDIT.md#going-commando) retains the public
decomp crosswalk, licence ledger and bounded instruction hashes. In particular,
OpenRAC's L00 qcopy leads are **types 91 and 78**, not the editor's type 85:
Museum placements `0x33f538` / `0x33d2e8`. The common editor constructor maps
from boot `func_002C65A0` to Museum `0x33e8f8`. Equal-looking addresses and
record sizes do not justify importing R&C1/UYA behavior.

GC reconstructed C files have no verified licence grant; tooling/documentation
licences do not grant permission to translate those files. This implementation
was derived independently from original retail instructions and execution.
No GC C file, translated instruction payload, disc asset or capture is included.
The optional C++ research adapter uses only the separately licensed MIT EE/VU
interpreter identified in `THIRD_PARTY_NOTICES.md`. Gameplay remains C#.

## Complete bounded route

All code addresses below are Museum-qualified unless explicitly marked boot.

| Stage | Recovered native route |
| --- | --- |
| Editor declarations | Class-4887 update `0x4186b0`; 17 registration calls in `0x4187d4..0x4189f8` to `0x413528` |
| Registration | Stores destination pointers at `0x260e30`, types at `0x1b3d30`, min/max pairs at `0x260f30`; menu selection belongs to class 4490 |
| Actual menu write | Handler `0x4130e8`, with numeric branches `0x413270` (int), `0x4132dc` (float), `0x4133f4` (short). Steps are ±1 / ±5; floats multiply the step by 0.01; short values wrap before clamp |
| Emission | Initial state draws an interval even for equal endpoints. Steady tail `0x418a4c` stores countdown−1 but tests the **old** value; emits when old=0. Bounds low to high and nonnegative, writes the clamped shorts back, draws a new inclusive interval |
| Constructor / caller patches | `0x418b04 → 0x33e8f8`; passes copied position, velocity, colour, alpha, scale×210000, life, gravity, spin, context. Caller replaces depth with FF and texture with the selected definition/frame |
| Pool | `0x330670 → 0x330678`, 2048×64-byte records, lowest-free allocation; clears all four quadwords. Full allocation returns before constructor angle RNG |
| Dispatch | Registration `0x331720`, 111 rows at `0x1ff740`; row 85 is thunk `0x3309c0`, which calls record +3C. Walk `0x3309d0` snapshots its high end, skips negative flags |
| Update / expiry | Callback `0x33eaa0` decrements signed-short life via `0x2f4b08`; timer≤0 frees via `0x3307c0` **before** colour/motion/spin/scale |
| Asset bindings | Loader `0x3042a8` relocates definition pointers at `0x1ff940`, copies shared frame bytes to `0x1ffb40`, creates packed Part descriptors at `0x1ff340` |
| Draw | `PartProc 0x330a60` (3112 bytes) owns visibility/depth buckets and render-kind dispatch. Type 85 has flags 0 and enters sprite path `0x3310e8`. Helper `0x331688` builds/caches material TEX0; the resulting 64-byte sprite descriptor feeds VU1 |

Menu UI installation and the editor's linked-player interaction are outside the
numeric/simulation fixture. The oracle conditions only those two boundaries;
registration, menu write branches, emission, allocation, random functions,
constructor, callback, walk and free execute original instructions.

## PVars, constructor and record

`Rac2MuseumParticleParameters` owns a copied 0x50-byte block, accepts the recovered
editor ranges, and exposes native Z-up/per-tick units. `Edit` applies a decoded
registration to that block; it is the real numeric write path, not a control-panel
mock. Runtime countdown belongs to the emitter separately from editable inputs.

| PVar offset | Meaning / native control range |
| --- | --- |
| 00/04/08 | XYZ velocity, floats −1000..1000 |
| 0C | Float emission countdown (state, not a menu control) |
| 10 | Requested life, int 1..1000 |
| 14/18/1C | RGB ints 0..255 |
| 20 | Spin int −1000..1000, copied to a wrapping byte |
| 24/28 | Initial/end opacity ints 0..127 |
| 2C/30 | Initial/end editor scale floats 0..1000; constructor units are ×210000 |
| 34 | Gravity float −1000..1000, subtracted from velocity Z each update |
| 38/3C | Moving-context selector / menu Moby pointer; neither is an ordinary particle owner pointer |
| 40/44 | Definition/frame ints 0..103 / 0..20 |
| 48 | Linked UID used by native interaction, outside this emission fixture |
| 4C/4E | Signed-short low/high spawn intervals, 0..1000 |

| Record offset | Type-85 meaning |
| --- | --- |
| 00/01/02/03 | Type 85 / flags / resolved Part byte / GS ALPHA selector 48 |
| 04/08/09/0A | Packed RGBA / angle byte / depth byte / signed-short remaining life |
| 0C / 10..1C | Native scale / XYZ position and float context |
| 20..2C | XYZ velocity / gravity |
| 30 / 34 / 35 | Float scale step / byte opacity step / byte spin |
| 36..3B / 3C | Cleared unused bytes / callback identity `0x33eaa0` |

The common constructor defaults to definition 85's first texture (Part **39**)
and depth **44**. Museum overrides are applied separately. It computes signed
integer `q=(endOpacity−initialOpacity)/requestedLife`, truncating toward zero.
If q≠0 it replaces life with `(end−initial)/q`; q is stored as a byte.
Example: requested life 100 and opacity 0→127 produce **127** ticks. If q=0,
opacity stays constant; substituting an interpolator would be incorrect.
Scale step divides `(endRaw−startRaw)` by the **adjusted** life.

Each surviving update wraps alpha, moves using the old velocity, subtracts
gravity from velocity Z, then wraps angle and accumulates scale. Life 40 yields
39 moves and expiry on update 40. There is no type-85 drag, collision, owner-PVar
reread or R&C1 type-6 kill box. Nonzero context needs the independently owned
`0x3800d0` moving-frame adapter and explicitly throws in this slice. Native
context 2 calls that helper; another nonzero value transitions to 2 first.

Boot rand/srand are `0x1163b0` / `0x1163a0`; the 32-bit state uses
`state*0x41c64e6d+0x3039`, with high 15 bits consumed by Museum helpers.
`0x321110` uses inclusive interval modulo; `0x3210d0(255)` gives angle 0..254.
An initial zero-interval success consumes **3** samples (startup, interval,
angle); an initial pool-full attempt consumes **2** (startup, interval).
Later successes consume **2**; later pool-full attempts consume **1**.
No callback samples RNG. A chosen seed controls this fixture, not other global
retail consumers. `Step` is one NTSC simulation tick (60 Hz); callers retain
phase ownership. Whole-world Moby/hero/camera scheduling remains outside #283.

## Actual authored results and visual trace

All three class-4887 Mobies are read unmodified from Museum gameplay. They use
zero interval and context 0. At seed 1234, all start at angle 32.
The authored factory requires the caller's level-qualified source and retains
its game/build/level; it cannot label another level's record as Museum implicitly.

| Moby / PVar | Life | Velocity Z | RGB | Opacity requested / actual | Scale | Spin | Definition/frame → Part |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 262 / 118 | 40 | .03 | 18,18,128 | 5→0 / constant 5 | .4→1.25 | 0 | 62/0 → **44** |
| 263 / 119 | 40 | .1 | 47,42,28 | 20→55 / constant 20 | .35→1.6 | 5 | 12/0 → **23** |
| 264 / 120 | 140 | .05 | 12,12,13 | 15→10 / constant 15 | .55→3 | 5 | 22/10 → **47** |

The first two already prove different source-authentic effects: different
texture images, RGB, opacity, ascent, growth and spin, through identical code.
The third additionally tests frame indexing across a definition boundary.
Their original PVar SHA-256 values, in the same order, are:

- `b022ff55f600457d27f63332559947b238e28a81316ffd81929b14855baccdfd`
- `eff43e11a85a38c306bed48464c824de0361c469305bcb970eda6c5a136e43dc`
- `05f2b6c00018295fc37d8929f11fc6ed738cc7d5133db3863ee82b2f09968833`

Native record trace, seed 1234 (derived numbers, no copied payload):

| Moby | Tick 0 scale / alpha / angle | Tick 20 scale / alpha / angle | Expiry tick |
| --- | --- | --- | --- |
| 262 | 84000 / 5 / 32 | 173250 / 5 / 32 | 40 |
| 263 | 73499.9921875 / 20 / 32 | 204749.984375 / 20 / 132 | 40 |
| 264 | 115500 / 15 / 32 | 189000 / 15 / 132 | 140 |

`Rac2ParticleRetailTests` compares **every byte** of every live 64-byte record,
every tick through expiry, for all three unmodified presets and seeds 0, 1234,
32768. It also compares every emitted 64-byte **draw descriptor**, including
colour/depth fade, projected rotating axes, blend and cached TEX0. Extra probes
cover repeated interval emission, hole reuse, negative gravity/spin, integer
opacity adjustment and near/far fade. Every registered menu control's actual
PVar writes are compared for ±1 / ±5 edits.
Mixed-sign callback probes also cover operand exponent differences 24 through
40 in VU position addition and EE gravity subtraction; all match without a
different arithmetic implementation.

## Textures and presentation boundary

Museum has 109 square PSMT8 Part rows at core index +2960, a separate bank at
assets +7D90C0, and 111 definitions at index +5070. Definition header's data
offset/size are 1D0/D0. A zero definition offset means the **first shared blob
byte**. Frames index that entire 208-byte blob; inventing a length from the next
definition would reject real Moby 264's 22/10 selection. Bounds are validated
against the blob and the 109 admitted texture IDs.

`Rac2ParticleAssets` decodes the independent Part bank with the established PS2
CLUT/alpha codec; ordinary Moby/tfrag and Fx texture grammars are separate.
All 109 native packed palette/pixel bindings are compared to the real loader.
The three selected images are 32×32 with different decoded RGBA hashes (pinned
in the retail tests). Unsupported Part encoding flags fail explicitly.

`Rac2ParticleDraw` uses the original 256-entry cosine table and material
template, loaded locally from the overlay. Its view inputs are explicit:
camera eye, native depth row, projection W factor and far cap. Native draw
depth is `trunc(viewZ*4096)`, near `(depthByte&15)<<10`, far
`min((depthByte&F0)<<13,cap)`. Within that range, alpha multiplies
`min(4096,z−near,(far−z)>>4)/4096`. Projected size is
`nativeScale*projectionW/(viewZ+0.5)`, rotated by the table's angle / angle+192
entries. Scale is not a guessed universal world-space float. Projection setup
`0x2ebbe0` also writes X/Y factors ±1/420000; W depends on the view configuration.
The native oracle declares identity rotation and W=1/420000 as a controlled
camera fixture, rather than claiming those are all retail cameras.

Blend 48 decodes to additive `Cs*As/128 + Cd` in GS ALPHA terms; colour/alpha
unity is 128, not 255. `NativeTex0` records a caller-supplied transient cache page;
it is never an asset key. Source/build/level-qualified Part identity remains
attached to snapshots. Flags' low bits select sprite/flat-quad/line/other paths;
only type 85's sprite kind is admitted. Draw flag 20 is transient bookkeeping.

The draw oracle conditions the one-sprite visible list after native culling and
sorting, then executes original descriptor/material instructions and scratchpad
DMA. VU1, GS rasterization, frustum boundary rounding, full bucket order, depth
test/write equivalence and a live Godot image are **not** validated here. Host
billboards, axis conversion, texture uploads and batching belong to #285 and
must preserve these facts without per-effect Godot physics. No gameplay outcome
is derived from visibility or pool slots. Shake, distortion, lights, sound,
debris and the demonstrator's composite/type-97 recipe remain separate work.

## Reproduction and extension

Run `./tools/test.ps1 -Configuration Release` and `./tools/test-retail.ps1` with
authorized local inputs. Retail simulation/asset tests need `OBP_GC_ISO`;
instruction comparisons additionally need `OBP_RAC2_MUSEUM_ORACLE`.
Build that optional executable with `tools/build-rac2-museum-oracle.ps1`, giving
`-ReferenceRoot` for the MIT interpreter at revision
`3fd14c12e4dff2fd4b6661a6e54187faea3f76c8`, a C++20 compiler via `-Cxx`, and an
external/ignored `-OutputPath`.
Git reference checkouts qualify the compiled source/header contents against the
pin, including dirty worktrees; a non-Git export remains caller-qualified.
The tests build private boot/overlay/core/PVar RAM
images, execute them, then delete the temporary files in `finally`. No upstream
GC decomp build is required. Ordinary CI runs portable synthetic branch fixtures;
retail bytes and the optional interpreter stay local.

To reuse type 85 for another genuine GC gadget/weapon, first identify its real
source/build/overlay constructor call and caller patches. Load that level's Part
bank, capture the actual recipe arguments and RNG/clock context, then call
`Spawn85` with those raw source-native inputs. Retain source-owned damage,
attachment and other channels separately. Add an instruction comparison for the
new caller before claiming parity; recover a context adapter if it is nonzero.
For another particle type, recover its own constructor, table dispatch, callback,
render kind and expiry rather than adding a generic tween or borrowing UYA.

`StopEmission`, `Clear`, configurable diagnostic capacity, stable allocation
identities and caller-owned snapshot buffers are OBP lifetime/tooling policy.
Native copied inputs let particles drain independently of emitter retirement;
the source contains no host scene references or Godot physics.
