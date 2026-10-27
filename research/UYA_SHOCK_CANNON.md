# UYA Shock Cannon V5: native weapon/session boundary

Authority is original NTSC-U `SCUS-97353`, build `rac3-ntscu-original`.
All addresses below are **TABLE1 loaded EE addresses**, except the boot/global
disc ranges explicitly identified. This slice supplies engine-neutral requests
and damage intents; it does not put a firing UYA weapon into Godot.

The separate [UYA particle milestone #284](RAC3_PARTICLES.md) now recovers the
pre-ready charging and charged-shot glow recipes through source-owned type 97.
It consumes the current weapon frame without advancing this controller or
executing damage again. Ready-charge extra families remain explicitly unrecovered;
host composition remains #285. Existing beam meshes stay separate from particles.

## Identity and upgrade admission

The native item table is `0x002C5640`, 156 rows, stride `0xF0`.
The resource slot remains **39**; byte map `0x001425C0[39]` selects its current
definition. The decoded original boot initializes this map to bytes `0..155`.
The English global text directory corroborates the names independently of
public weapon lists: each `0x10` row contains text offset at `+0`, text ID at
`+4`. IDs are **not** directory indices.

| Definition | Native level byte | Text ID/name | Previous/next | Held class | Capacity |
|---:|---:|---|---|---|---:|
| 39 | 0 | `AA0` Shock Blaster | 0 / 40 | `1863` | 30 |
| 40 | 1 | `A6C` Shock Blaster V2 | 39 / 41 | `1863` | 35 |
| 41 | 2 | `A6D` Shock Blaster V3 | 40 / 42 | `1863` | 40 |
| 42 | 3 | `A6E` Shock Blaster V4 | 41 / **43** | `1863` | 40 |
| **43** | **4** | `A6F` **Shock Cannon** | 42 / 44 | **`1882`** | **50** |
| 44 | 5 | `DB7` Mega Shock Cannon | 43 / 45 | `1882` | 50 |
| 45 | 6 | `DB8` Giga Shock Cannon | 44 / 46 | `1882` | 55 |
| 46 | 7 | `DB9` Omega Shock Cannon | 45 / 0 | `1882` | 60 |

Table fields: level `+1`, name `+4`, held class `+10`, next `+46`, previous
`+48`, XP threshold `+68`, damage `+6C`, pickup amount `+88`, capacity `+8A`.
All offsets/IDs in that sentence are hexadecimal. V5 pickup amount is 4.
Its row is `0x002C7E90`, damage 5; Mega/Giga/Omega damage is 55/70/105.
This implementation admits **only definition 43**; sharing a model does not
make the stronger variants equivalent.

`0x0050FC90..0x0050FEDC` scans resources, tests ownership/type/native level,
compares resource XP `0x00142DE0[slot] >> 5` with row `+68`, and coordinates
the equipped upgrade presentation or the direct upgrade service.
V4 has threshold 1000; V5 has 0, ending ordinary XP progression there.
`0x00416640..0x004166EC` follows signed row `+46`, writes the new definition
byte **at the original slot**, and refills weapon ammo to the new `+8A`.
Thus 42 -> 43 is a native upgrade, not an OBP rename. This session does not
implement earning XP, the vendor, challenge-mode purchasing or upgrade UI.

## Held controller, timing and ammo

The dispatch row at `0x002F6ED4` binds class `0x1882` to update `0x00385948`.
`0x0050DA08` reads current ammo through the slot/variant map.
`0x0050D8F8` spends the requested count only if enough remains; a definition
with zero capacity is an unlimited resource. This weapon has nonzero capacity.
`0x0050D980` is the indexed add/clamp service.

The ordinary controller path is update-counted:

- `0x00385B78..0x00385BC8`: holding an admitted trigger increments PVar `+0C`.
  The first update requests an ordinary shot. At **79** held updates it latches
  PVar `+60` and requests the charged path; holding can repeat charged shots.
  Release clears the count but preserves a completed charge request that update.
- Charge admission uses the held input, ammo and player `+25ED` lock. Starting
  an action separately requires Moby state 1, PVar float `+0 == 0`, player
  `+1220 >= 23` and the `+28BC` gate (`0x00385FFC..0x00386044`). These are
  explicit host/player facts, not guessed automatically by the weapon session.
- `0x0038606C..0x003860C4`: sequence equals definition `+24`, player `+25CC`
  nonzero, or player `+2874` nonzero selects the primary timing; otherwise the
  alternate timing applies. Primary windup is **5**, alternate **2** updates.
  Constants are at `0x001DDDF0`; other visual counters there are 2/8/8.
- `0x003860C8..0x00386110`: ordinary shot spends **1**. Charged shot calls
  `spend(1)` **ten times**, ignoring each failure. **1..9 remaining ammo still
  gives a full charged beam**, consuming only the remaining ammo.
- Bottom-of-update `0x003868E4..0x003869E4` decrements the windup, changes
  state 2 -> 3 with muzzle/audio, then next update 3 -> 6 emits the spread
  damage queries and sets a **20-update** recovery. State 6 uses PVar `+4`
  before returning to state 1 (`0x003859D0..0x00385A04`).
- In primary timing, a tap at update 1 spends immediately, produces muzzle
  at 5, spread damage at 6 and is ready at 26. Holding from update 1 produces
  a spread admission at 1 and charged admissions at 79, 104, 129, ... while ammo remains.
- The charged admission sets the independent global beam counter to **30**
  (`0x003861A0..0x003861D0`). Its collision block runs that same update,
  preceding the muzzle state machine, and runs once per subsequent live update.

The session advances exactly once per native update. It does not integrate a
variable delta or borrow R&C1 cooldown/ammo values. Reset/unequip invalidation
and initial full ammo are explicit **OBP session policies**, not recovered UYA
save or weapon-switch rules.

## Queries, damage and targets

Ordinary V5 spread is not a moving generic pistol projectile:

- `0x00386A50..0x00386BA4`: up to four close sphere queries, radius
  `1.5 + 0.25*i`, native Moby `+D0` offset `-1.35*i` and `+C0` offset
  `-0.175*i`, `i=1..4`. The native reduced-budget/special-level branch keeps
  just the first sphere. These use base damage 5.
- `0x00386CC8..0x00386DD4`: **23** random rays; rotation arguments derive from
  `randFloat(0,-17)` and `randFloat(-170,170)`, multiplied by native degrees
  to radians. `0x00386E28..0x00386EE8` adds **3** fixed rays at rotation
  `-0.2617994`, second-axis offsets `-0.34906584, 0, +0.34906584`.
  These fields retain the native rotation-call axes; the host must not assume
  Godot Euler conventions. Length parameter is **32**, with a **1.5** endpoint
  adjustment inside `0x00386FB0`. Muzzle/aim basis is supplied by the host.
- `0x003870C8..0x0038713C`: native hit-distance damage factors are **1** below
  10, **0.75** below 14, **0.5** below 20, **0.125** otherwise.
- `0x00386390..0x003864D0`: beam emits **20** sphere queries, radius **1.5**,
  along the live native aim basis at distances **2,4,...,40**, with damage
  **5*10 = 50**. No static-world line-of-sight test is inferred for this beam.

Spread flags are **`0x00410000`**, beam flags **`0x00210000`**. Both carry
record kind **3**, owner kind **2**, source pointer the held Moby, not an
invented projectile. `0x003FBA28..0x003FBA78` requires target Moby `+34 & 4000`
before storing beam damage. The native query excludes Ratchet as supplied
owner. Ray collision uses `0x003F8448`; the host must supply an admitted
nearest hit, including obstruction. Collision cannot be inferred from class ID.

`0x00443840..0x00443934` coalesces a target's native update damage record:
maximum damage, flags ORed, not damage multiplied by intersecting rays.
`ResolveContacts` models this boundary once per returned frame. A later beam
update may emit another intent; the target controller still owns invulnerability,
resistance, life and death. Existing class-500 and class-5821 masks include
`0x00010000`, present in both attack flags. A retail-backed fixture routes a
beam intent into the existing class-500 consumer and observes deactivation.

## Asset and fusion-host requirements

Every shot/query/audio/effect/damage envelope carries
`rac3/rac3-ntscu-original/item:43`; targets have their own source-game identity.
The UYA damage adapter refuses a foreign target rather than treating its native
class number as UYA. Cross-game target resistance requires an explicit host adapter.

| Required source asset or channel | Proven identity / requirement |
|---|---|
| Held model/controller | UYA class **`0x1882`**, not Blaster class `0x1863` |
| Audio | Class `1882` **local sound indices**: 3 charge loop (play flags 4), 1 initial empty press, 2 recovery, 4 ordinary fire, 0 charged fire |
| Audio resolver | `0x004A7200` indexes the source class definition's `+28` sound table in `0x20`-byte rows; these are not universal VAG indices |
| Muzzle particles | Procedural routine **`0x003877D8`** |
| Spread trails / impacts | Procedural **`0x003871E8` / `0x003887F8`** |
| Beam presentation | Procedural **`0x003873F8`**, called while beam counter is active |
| Charge particles | Calls to native emitter **`0x0046DCB8`** in the held controller |
| Ratchet animation | Definition fields `+24/+28/+2C = 49/-1/65`; selector compares `+24` at `0x0038606C`; player action **29** requested at `0x00385A44` |

`UyaGlobalGadget` now extracts the original resident gadget package. Header
`TOC+4588` is `3C8` bytes, payload LBA `E51DE`, with 48 rows of `0x14` bytes.
Class `1882` is row **14**: model relative sector **811**, **22** sectors;
audio relative sector **833**, **24** sectors. The first 16 model bytes contain
two compressed offset/size pairs relative to the gadget payload: class and
texture WADs. They are neither EE pointers nor a level Moby remap.

The admitted model has **2,205 vertices, 1,866 triangles, 10 joints**, and
three source clips of **11/2/11 frames** at speed `.5`. Pose it through
`GcUyaGadgetPose`: global gadget vertices are in model bind space. The class's
0x40-byte joint records supply global bind orientation in the 3x3 block and
inverse bind translation in row 3. Their last column is local translation and
the final word is packed flags, not an affine homogeneous coordinate. Apply
inverse bind then animated forward globals (Numerics row convention). The
sequence-0 rest pose reproduces stored model-space vertices within 0.00002
native units; applying animated inverse joints alone folds the parts together.
This is a retail bind/rest witness, not an EE animation oracle or a recovery of
the extra per-frame channels. The existing level-Moby path is unchanged.
The texture bundle has one **128x128 PSMT8/RGBA32 PIF**. The source assets expose
explicit sequence/frame selection; a recovered weapon-idle selector and Ratchet
mount are not inferred from the availability of these clips.

The separate **SBlk v3** bank contains six sounds, 20 compact grains and eight
unique ADPCM voices. A compact grain is eight bytes: opcode (type in high byte,
body offset/immediate in low 24 bits) and signed 240-Hz delay. `GrainData` at
metadata `+34` supplies tone bodies. Control parameters are three signed bytes;
ordinary fire uses `RAND_PB`, `SET_REGISTER_RAND` and register-selected tone
volume. The shared parser preserves the packed opcode as well as normalized
parameters; the existing grain interpreter supplies both mixed and independent
voice rendering.

| Local cue | Voices / source timing | SoundDef max volume / range / pitch |
|---|---|---|
| 0 charged fire | Five tones at 0, 20, 28, 78, 84 ticks | 1843 / 35 / 0 |
| 1 empty | One tone | 1536 / 35 / 381 |
| 2 recovery | Original silent 11-frame ADPCM sample | 2048 / 64 / 0 |
| 3 charge | Intro, then sustained voices at 225 and 275 ticks | 1945 / 35 / 0 |
| 4 ordinary fire | Five tones at 0, 0, 0, 8, 18 ticks | 1126 / 35 / 0 |
| 5 unassociated | Decodable; not requested by this controller slice | 1024 / 35 / 0 |

Charge's two sustained tones use the same ADPCM loop at different pitches.
`UyaShockCannonAssets.Audio` schedules them independently, each with its own
intro/loop range; replaying the entire mixed charge clip introduces a seam.
Stop the complete owned charge program, including pending tones, on StopLoop.
`0x004A53D0` supplies integer distance attenuation, linear unless flags bit 0
or the caller forces quadratic falloff. Native SoundDef volume units remain
available. Normalization by 2048 is **OBP playback policy**; pitch interpolation,
ADSR rendering and host mixing do not claim bit-exact SPU output or native RNG.
SoundDef pitch modifies the tone cursor at 48 kHz, preserving the envelope and
240-Hz grain clock instead of changing the playback rate of an entire program.

### Charged beam rendering

`UyaShockCannonBeam` projects `0x003873F8` and `0x00409A50`. The latter draws
ellipsoid rings, **22 vertices per alternating strip**, with azimuth samples at
ten divisions and polar steps of **pi/10**. Native local +Z points against the
aim: length scale **15**, translation **muzzle + aim*14.5**, so the first pole
is at muzzle minus half a unit. This visual extent is distinct from damage's
40-unit sampling. The host supplies the proven muzzle and aim-to-matrix axes.

| Layer | Fx texture | Rings | Base RGBA word | Alpha | Scroll per draw |
|---|---|---|---|---|---|
| 1 | 23 | 8 | `00804020` | remaining counter <<25 | .15 |
| 2 | 23 | 8 | `00801040` | remaining counter <<25 | .10 |
| 3 | 23 | 8 | `00804010` | remaining counter <<25 | .05 |
| Outer | 10 | 6 | `00D20A40` | remaining counter <<24 | .15 |

Rotate both cross-section axes +pi/4 around the native axial basis before the
first layer, then -pi/2 before the next two. Outer widths are multiplied by 1.1.
Widths start at 1, grow by .1 while the decremented counter is at least 16,
then shrink by .075/.125 on the respective axes. Scrolls wrap at 7; V advances
one repeat per polar ring. Colours fade to black preserving the source blend
alpha, using `0x0040D020`'s weighted VU MAC/conversion order. Reassociating that
calculation changes colour bytes on the six-ring layer. Blend is source-alpha
additive (`GS ALPHA=8000000048`), with repeating linear-filtered textures.

Loader `0x00428FE8..0x00429010` connects core `+58/+5C` to `0x004225E8` and the
runtime Fx table at `001E7D00`; its independent image bank is core `+68`.
`UyaFxTextures` admits the observed PSMT8 palette/pixel/width/height rows.
TABLE1 has Fx **23 = 32x32** and **10 = 64x64**. These come from UYA, not RAC1
Tesla art. Neutral meshes retain source texture identity, vertex RGBA, wrap and
blend requirements. Host shader, lighting and output colour-space fidelity
remain presentation responsibilities.

Item field `+3A = 1883` is retained as an unresolved secondary class reference;
this Cannon path does not justify spawning class 1883 as its projectile.

See [the player-animation boundary](UYA_PLAYER_ANIMATION_SELECTION.md): UYA
special Ratchet pose bodies are not decoded by the ordinary avatar decoder.
Sequence 49/65 requirements do not establish a renderable UYA pose, nor the
meaning of every sequence field. No RAC1 model, pose, SFX, shader or orange
sphere silently substitutes for those missing channels.

## Reproduction and remaining limits

`UyaShockCannonRetailTests` reads the original ISO, verifies bounded hashes,
resolves all eight names, checks item scalars/upgrade links, checks original
boot map initialization, and pins control-flow regions plus key instruction
operands/call targets. No retail payload is stored in Git.

- TABLE1 overlay: 3,441,828 bytes; SHA-256
  `093fb8641cb2bf48073793f6cfac726cb7321654d285b848658f3e957df1dcec`.
- Original SCUS ELF: ISO LBA 1463, 771,008 bytes; SHA-256
  `200068cb4186715521cdc1c20bc6c5a88054d0da54aa336ffdb2022633351c7d`.
  WAD starts at ELF `+7080`; decoded boot is 3,521,764 bytes.
- English names: HUD header `TOC+4950`, first language range at `+1838`,
  relative sector `238F`, 136 sectors; SHA-256
  `86e0709dcf702e10a5dac79b15dced6de014bcfb5b7c293ab623a6dbd58c135e`.

Run `tools/test-retail.ps1 -Configuration Release` with authorized local ISOs.
Focused tests: `dotnet test tests/OBP.Tests -c Release --filter FullyQualifiedName~UyaShockCannon`.
Asset tests additionally pin the gadget directory, decoded class/texture and Fx
RGBA hashes, every sound program, exact clip counts and loader/draw instructions.

`tools/uya-shock-cannon-native-trace.py --iso <original> --reference-root <MIT-reference>
--cxx <C++20-compiler>` builds the optional original-instruction oracle and runs
its managed comparisons. Zig's executable is accepted as the compiler. Original
code remains in ignored `captures/`; only the harness is distributed. The oracle
loads **UYA's** boot VIF0 math chain at `0010F1A0`, not the interpreter's RAC1
default. It executes original ring geometry/colour/UV and aim-to-layer setup,
across six aim directions including both vertical poles, and compares both
attenuation branches. GPU submission and muzzle position are
external facts. Ring positions agree within 2e-6, full beam placement within
2e-5; packed colours, strip/UV order and layer arguments agree. Trigonometry and
matrix arithmetic remain float host projections rather than bit-exact VU ports.

`tools/uya-shock-cannon-preview.ps1` launches a separate Godot source-asset
fixture using `OBP_UYA_ISO`. Space/Circle exercises the session; 1/2/3 selects a
source clip. `-Smoke -Rendered -Capture` stages a real Godot input hold/release,
renders offscreen, checks two shot admissions, all 29 beam updates and owned
audio cleanup, and saves an ignored preview image. It uses the neutral assets
and audio preparation boundary. Its unlit model view, camera, mount and gain
are preview policy, **not ordinary integrated gameplay or a live PCSX2 A/B**.
R&C1 Quick Select and OBPGame are untouched.

Remaining: native global RNG state; Ratchet pose/hand attachment and clip
selection; charge/muzzle particles, spread trails and impacts beyond their
emitter requests; ancillary status helper `0x0034BF98`; special modes and UYA
weapon-switch continuation. Native direct damage remains unchanged; electric
chains/stun are not invented from the weapon's name.

In particular, normal contacts also call `0x0034BF98`. Its initial path checks
resource flag bit 1, target definition kind 5, target mode `1000`, live state,
health pointer and a 30-slot status pool. Cannon initialization ORs resource
flags with `22` (`0x003859C4`). The helper's complete status recurrence and
secondary damage are **not** projected; the session's damage amounts above are
the direct shot/beam records, not a claim about total damage including modifiers.

Public search leads were [OpenRAC rac3-decomp at `3d9bb40`](https://github.com/OpenRAC/rac3-decomp/tree/3d9bb40cd871a14b3f868b5fac61864de75c8dfd)
(generic function maps; GPL-3.0) and [Wrench at `46e8043`](https://github.com/chaoticgd/wrench/tree/46e80430158a4a11e9cbd51424c84a015bbab058)
(global text header offsets; GPL-3.0). No GPL implementation was copied.
Names, fields, code paths and timing above were independently read from the
authorized original. Multiplayer weapon IDs/table strides from public UYA
patches were not transplanted into this single-player slice. The optional
instruction oracle and shared 989snd semantics use the separately acknowledged
MIT/ISC references in [THIRD_PARTY_NOTICES](../THIRD_PARTY_NOTICES.md).
