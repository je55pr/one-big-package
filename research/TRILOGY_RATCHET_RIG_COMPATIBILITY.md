# Trilogy Ratchet rig compatibility

Implementation snapshot: 2026-10-24. Supported NTSC-U retail bytes and
routines are authority; public projects supply labelled corroboration and
research leads. The shared sampler and native equipment readers now replace
the forced R&C1 Blaster pose and hand-tuned foreign mounts.

## Result and scope

The three sampled playable Ratchet models have the same 111-joint hierarchy,
the same complete common-translation records, and the same semantic joint-chain
records, including their animation-layer joint lists. They also have the same
class coordinate scale. This is substantially stronger evidence for a common
Ratchet rig than visual similarity alone.

This sample covers class 0 on R&C1 Veldin, GC Oozla and UYA Veldin. It does not
establish compatibility for every armour, unlockable skin, disguise, cutscene
model or alternate playable character. Their joint topology and bindings still
need admission checks. Nor does a shared skeleton establish equivalent clip
numbers, animation state rules or packed animation formats.

| Source | Retail build | Model carrier | Joints | Class scale |
|---|---|---|---:|---:|
| R&C1 | `rac1-ntscu-original`, SCUS-97199 | Veldin level 0, class 0 | 111 | 0.14583333 |
| GC | `rac2-ntscu-v1.01`, SCUS-97268 | Oozla LEVEL1 core, class 0 | 111 | 0.14583333 |
| UYA | `rac3-ntscu-original`, SCUS-97353 | Veldin TABLE1 core, class 0 | 111 | 0.14583333 |

## Direct retail comparison

All three samples produce these SHA-256 signatures:

| Compared representation | SHA-256 |
|---|---|
| Parent indices, signed little-endian int32, self-parent normalized to -1 | `16460b6fab59e95ab095a580d1db58bfab2d1145b538486d385afe76396cac20` |
| Complete 111 common-translation records, including their parent words | `96f52cf1d8e4caf9dd4be8b16c441c6b17a5ac996086fbcb8fac1376ff7d8f1c` |
| Concatenated semantic-chain records in table order, without relocated pointers | `5eb76521e3fa2488059ff4498692eb1b23e420a8d39d99d18e15c3a3a8b66a05` |

Each class has 31 chain entries. Their target joints and layer lists agree
byte for byte. R&C1's recovered consumer identifies semantic 0 as the
right-hand/wrench attachment and semantic 6 as the Bomb Glove attachment;
see [R&C1 skinning](RAC1_MOBY_SKINNING.md) and
[Bomb Glove](RAC1_BOMB_GLOVE.md). The same chain data in GC/UYA is a concrete
mapping candidate. It does not by itself establish every later game's
consumer, attachment transform or animation-layer policy.

The complete class+0x14 joint/bind record arrays are **not** byte-identical.
For the affine fields used by the current gadget skinning path, R&C1 to GC
has a maximum orientation-component difference of about 6.25e-8 and maximum
inverse-translation difference of 0.000412 in stored units. GC to UYA has
about 1e-14 maximum orientation difference and equal inverse translations.
One other stored column component changes between R&C1 and GC. Keep each
skin's original bind records; do not replace the entire record table with a
single canonical byte array merely because topology and common translations
match.

These measurements support testing an identity joint mapping for these three
models. Cross-game animation application and final deformed vertices have not
yet been validated against the native pose evaluators.

## Carried-over weapons as reference cases

R&C1's global class 0x00C0 and GC's GADGET.WAD class 0x00C0 are particularly
useful: both Bomb Glove samples have 17 joints, with identical parent indices,
complete common-translation records, complete bind records and semantic chains.
Both have three populated class-local sequences; R&C1 declares five slots,
two empty, while GC declares three. Their complete asset payloads differ.

Their shared bind-record SHA-256 is
`6d534df1cb8e4af5969b0b642ba616127f652f900780bf4eebdfb095ebf0a954`.
This makes the glove a strong first specimen for comparing the native avatar
hand transform and equipment layer in the two games. Equivalent final poses
and attachment transforms still require that consumer comparison.

Four additional classes are present in both GC and UYA global gadget
directories. Names in this table come from the pinned public model lists
below; directory membership and structural comparisons were measured from
retail. This table does not recover their complete gameplay definitions.

| Class | Public model label | GC / UYA joints | Parent hierarchy | Common and bind records | Populated clips, GC / UYA |
|---|---|---:|---|---|---:|
| 0x05C9 | Lava Gun | 21 / 21 | Different | Different | 3 / 3 |
| 0x05CA | Plasma Storm | 16 / 16 | Equal | Different | 7 / 3 |
| 0x0C4A | Plasma Coil | 17 / 17 | Equal | Different | 3 / 3 |
| 0x0E14 | Bouncer | 8 / 9 | Different | Different | 3 / 3 |

The class number and familiar weapon appearance therefore cannot authorize
reusing its joint layout, bind pose or animation clip indices. These remain
excellent comparison specimens, including negative cases that prevent an
overbroad compatibility assumption. Public class 0x0E9E was not present in
GC's global gadget directory in this probe; a level-class name list is not
a global held-gadget inventory.

## Recovered animation decoding

| Source | Populated external sequences | Frame references | Reference high nibble |
|---|---:|---:|---|
| R&C1 Veldin | 134 | 2,558 | 0 |
| GC Oozla | 102 | 2,408 | 0xF |
| UYA Veldin | 127 | 2,968 | 0xF |

`Ps2SkeletalAnimation` evaluates ordinary and packed frames. External player
references are sequence-relative after masking the high nibble; ordinary arm
references are class-relative. The descriptor follows frame and sound-reference
arrays. Its partitions separate ten-bit deltas, signed literals, byte deltas
and quaternion W signs. Sparse vectors retain full-skeleton joint tags even
in a 20-channel arm carrier.

The independent oracle executes GC `0x00308B20` and UYA `0x004397F8`.
All 5,376 packed frames agree exactly in quaternion XYZ, scale/translation
vectors and joint tags. W differs by at most one fixed-point unit out of 32768.
The C# implementation was derived from supported retail routines without
translating public implementation code. Wrench supplied an initial format lead.

## Recovered arm layers and hand attachment

GC and UYA each have class 1 and 2 animation-only arm banks: 20 channels and 24
sequences per side. Their class+0x1C scatter maps match body semantic lists 12
and 13: joints 52..71 and 72..91. Original arm selection uses these banks while
the ordinary body sequence is below 23. Right-arm channels match between these
samples; UYA revised left-arm sequence 20. Rig compatibility does not authorize
substituting one source's animation data.

| Native operation | GC | UYA |
|---|---|---|
| Allocate equipment layer | `0x003069D8` | `0x00436BD8` |
| Expand mapped arm frame | `0x00306EF8` | `0x00437200` |
| Bake evaluated layer buffer | `0x0030AB00` | `0x0043B7D0` |
| Evaluate semantic joint matrix | `0x003072F0` | `0x00437600` |
| Weapon use animation selection | `0x002B0020` | `0x004E6A08` |

Layer baking starts mapped joints with the target skeleton's common
translation and unit scale, then applies sparse vectors. Inheriting the body's
animated translations instead creates a sequence 20 error despite appearing
correct when standing.

Lancer descriptor 30 selects one arm, hand matrix 0, standing use 56, moving
use -1, crouch use 62, sustained ownership and a five-update entry blend.
Cannon definition 43 selects two arms, hand matrix 0, standing use 49, moving
use -1, crouch use 65, one use cycle and a two-update entry blend. Source
adapters also supply recovered carry-in and use-out increments. Equipped idle
uses the carrying bank; equipping does not request firing.

Hand matrix 0 is semantic 0 / joint 56. Held-gadget consumers copy its origin
and basis, normalizing the basis before placement. The host uses this evaluated
attachment with the existing mesh/avatar axis conversions. The Cannon
quarter-turn and manually chosen grip translations were removed. The remaining
barrel feedback endpoint is labelled OBP geometry-derived presentation, rather
than a recovered native muzzle socket.

The oracle compares all 31 semantic matrices on first/middle/last body frames
and applicable carrying frames, including layer weights 0.25, 0.5 and 1.
Assertions bound basis error to 0.0003 and origin error to 0.0002 native world
units, covering fixed-point reconstruction and single-precision evaluation.
Packed-vector comparison remains exact.
The pinned local run covers 1,059 poses / 32,829 semantic matrices. Its maximum
observed basis error is 0.000136495 and origin error is 0.000046194 native units.

The public NTSC-U UYA decomp's `0x004DC0D0` context helper and
`0x004E6CB0` release helper corroborate the retail investigation. Patch headers
supplied useful blender names; multiplayer/version-dependent offsets were not
accepted as single-player authority.

## Common representation and demo integration -- OBP design

`PlayerSkeletalAsset` retains source identity, joint parents, per-skin binding,
vertex weights, timed local tracks, optional translation/scale channels,
attachment maps and layer masks. `PlayerSkeletalSampler` blends local channels
before hierarchy evaluation and skinning, preserving non-rigid matrices and
each skin's binding. R&C1 equipment now uses this sampler. Retail tests compare
converted R&C1 surfaces with the existing decoder and exercise later-game arms
over the admitted R&C1 skin.

`GcRatchetSkeletalSource` and `UyaRatchetSkeletalSource` prepare native channels,
masks and equipment rules on the background preparation path. Godot consumes
poses and attachments. Native format definitions and source selectors remain
in their owning layers; the narrow presentation layer adapts supported PS2
materials and skeletal assets to neutral runtime types.

R&C1 movement still drives this demo. Where shared movement clips have different
frame partitions, OBP preserves cycle phase when sampling source arms. That
timing adaptation and the bridge from admitted weapon events into presentation
are OBP policy, not a complete GC/UYA player-controller port.

The equipment attachment is evaluated during jump/traversal clocks too. An
earlier host guard skipped that path whenever a traversal selection was present,
leaving the visible gun on an old transform with locked axes. Standing and
moving jump smoke checks now require both position and basis to follow the hand.

During a body transition the native frame clock's fraction is progress from a
saved pose to a frozen target, rather than interpolation inside the target clip.
The equipment path retains the complete evaluated local-joint pose and uses
that same native progress. An interrupted transition snapshots the current
blended pose. The composed pose supplies both the skinned character and its hand
matrix, so arms and the mounted weapon cannot independently select the target
while the body is still blending. Base-pose metadata follows the existing
standing, locomotion, stop, crouch and traversal clock handoffs.

Runtime tests cover joint-space midpoint rotation and interrupted blends;
retail controller tests exercise idle/run, stop/restart and jump/landing
boundaries for both source profiles. Rendered checks compare the entire skin
and the gun's position and axes at transition entry, including actual movement
and jump-to-standing changes. The transition curve remains source-controlled;
this does not introduce a separate gun-transform smoothing timer.

`PlayerHeldEquipmentSession` owns the hand independently of source-qualified
selection. R&C1's original Circle draw timing adapts to that shared owner;
foreign sessions also observe it. Wrench attacks retain the wrench after
recovery, suspend a foreign weapon's use/charge and leave selection/ammo intact.
An admitted equipment draw or explicit selection takes hand ownership again.
This shared gameplay rule does not impose R&C1's native draw delay on GC/UYA.

The sampled GC/UYA class 0 carriers contain skeletons and animation without
geometry: their player skins stream separately. This change uses the current
R&C1 skin and supplies all three skeletal sources; it does not add every armour
or a skin selector. The common format supports independent vertex streams.
Admission checks parents, common translations and attachment maps, retaining
each skin's binding. Further skins need extraction and measured admission.
Head/face behavior and complete native held-gadget clip selection remain
separate recovery work.

## GC/UYA Ratchet skeletal reader mechanics (#484)

The supported GC Oozla and UYA Veldin class-0 readers formerly duplicated
111-joint skeleton projection, 256-entry body sequence decoding, two auxiliary
arm banks, sparse scale/translation application, and frame timing conversion.
`Ps2RatchetSkeletalDecoder` now owns those **format mechanics** in `OBP.PS2`;
the native `Ps2Skeleton` and `Ps2SkeletalAnimation` decoders remain authoritative.

Both source adapters retain the native carrier directory lookup and bounds
validation, build/source qualification, avatar model identity, and authored
equipment selection. No new animation policy, sampler or Godot dependency
was introduced. The supported-carrier assumptions (111 joints, 256 body slots,
two arm classes, semantic layers 12/13) remain intentionally specific to the
verified retail Ratchet carriers, not a claim about arbitrary PS2 characters.

| Retained source decision | GC / Lancer | UYA / Shock Cannon |
|---|---|---|
| Identity | `Rac2`, original GC build and model ID | `Rac3`, original UYA build and model ID |
| Active arms | 1 | 2 |
| Standing/crouching use sequence | 56 / 62 | 49 / 65 |
| Sustained use | yes | no |
| Entry blend updates | 5 | 2 |
| Native diagnostic context | GC | UYA |

Both source adapters are substantially shorter, with identical decoding
mechanics moved to one shared reader. Carrier selection and equipment policy
remain explicit in the adapters; the goal is to remove duplicated logic, not
to optimise a physical line count.

`Ps2RatchetSkeletalDecoderTests` builds in-memory native-format carriers
(no retail files) and checks rig topology, inverse binds, arm banks, sparse
channels, provenance, duration/timeline conversion and malformed input.
`TrilogySkeletalSnapshotTests` hashes every output joint, matrix, attachment,
frame, arm sequence, source-qualified identity and equipment policy under
the existing retail source gate. Golden SHA-256 baselines were captured from
the two original readers on the supported NTSC-U sources before extraction:

- GC: `4d423655da2f2dc5a9f427e16360bed333fb150430d0c56b3129fb370fe0465e`
- UYA: `d003243475bf1b8b5478f8aa673b98798c169f007bebd7119944e7964de3db60`

These fingerprints protect decoding outputs, **not** the downstream
body/equipment pose composition being redesigned separately in #481.

## Reproduction

Use supported sources identified by `Rac1Authority.Primary`,
`Rac2Authority.Primary` and `Rac3Authority.Primary`.

- Run `tools/test-retail.ps1` for compatibility, converted surfaces and
  cross-source arm playback.
- With all three ISO environment variables set, run
  `tools/trilogy-skeleton-native-trace.py --reference-root <pinned MIT checkout> --cxx <C++20 compiler>`.
  The script verifies the interpreter revision in `THIRD_PARTY_NOTICES.md`.
  Original code and channels are temporary files under ignored `captures/`;
  tests remove their own input directories.
- Run `tools/fusion-weapons-smoke.ps1 -Rendered` for selector, idle/use,
  authored contacts, source absence, recording and fresh-process replay.

For structural signatures, extract bounded class 0 payloads. Hash normalized
int32 parents and complete common records from class+0x18. For each chain at
class+0x1C concatenate its four-byte header and both joint lists, omitting
relocated pointers. Keep class+0x14 bind signatures distinct. Repeat for the
shared Bomb Glove and GC/UYA specimens above; familiar IDs do not establish
compatible weapon skeletons.

## Pinned public sources inspected

- [OpenRAC GC matching decomp, USA v1.01](https://github.com/OpenRAC/rac2-decomp/blob/d43c3b99b984fb63556c844a21ee99e8e31d2deb/README.md),
  especially its [context-selector research note](https://github.com/OpenRAC/rac2-decomp/blob/d43c3b99b984fb63556c844a21ee99e8e31d2deb/nonmatching/levels/func_L01_002A7248.c).
- [UYA NTSC-U matching decomp](https://github.com/vetusmagnus/rac3-uya-decomp/blob/fc118a0b7442665a2b181055dc352ce5d117c41b/README.md),
  especially [0x004DC0D0](https://github.com/vetusmagnus/rac3-uya-decomp/blob/fc118a0b7442665a2b181055dc352ce5d117c41b/src/levels/common/func_004DC0D0.c).
- [OpenRAC UYA US shared-code analysis](https://github.com/OpenRAC/rac3-decomp/blob/403f30a9561324342c87802960eefbaaae13b372/docs/us/shared_code_findings.md).
  Region/build mappings must accompany cross-version source comparisons.
- [Wrench animation reader](https://github.com/chaoticgd/wrench/blob/46e80430158a4a11e9cbd51424c84a015bbab058/src/engine/moby_animation.cpp).
  The special branch preserves packed data; its existence does not establish
  decoded local joint poses.
- [UYA patch player structures](https://github.com/Horizon-Private-Server/horizon-uya-patch/blob/737dd562c7ea142de3e708594f74c239f19af4b0/libuya/include/player.h)
  and [gadget definitions](https://github.com/Horizon-Private-Server/horizon-uya-patch/blob/737dd562c7ea142de3e708594f74c239f19af4b0/libuya/include/weapon.h).
  Multiplayer/version-dependent naming and layout leads only.
- Replanetizer [GC class labels](https://github.com/RatchetModding/Replanetizer/blob/2136bd9fbe4815b0119921e794a87d847da9816e/LibReplanetizer/ModelLists/MobyModelsRC2.txt)
  and [UYA class labels](https://github.com/RatchetModding/Replanetizer/blob/2136bd9fbe4815b0119921e794a87d847da9816e/LibReplanetizer/ModelLists/MobyModelsRC3.txt).

Public implementation code was not copied or translated. See the existing
[external-source policy](EXTERNAL_SOURCE_POLICY.md).
