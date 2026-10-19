# R&C1 Bomb Glove firing slice

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`, ISO SHA-256 `ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d`). Loaded Veldin retail state supplies overlay/runtime code absent from the boot ELF. The controlled live witness uses the retained fixed Veldin state plus a managed PCSX2 movie/PINE trace; committed evidence remains payload-free.

## Candidate selection, input and ammo

Native item `10` is the early-state ranged weapon identified publicly as Bomb Glove. Ammo remains indexed at `0x0013d428 + itemId * 4`, so item `10` uses `0x0013d450`; its descriptor maximum is `40`.

The fixed state begins with item `8` equipped and item-10 ammo `6`. A deterministic two-frame Circle input changes both equipped-id mirrors to `10` without consuming ammo. After the equip settles, a second two-frame Circle input consumes one round, `6 -> 5`. This is a controlled input witness, not a complete quick-select/input-state model.

The live item-10 weapon is native class `0xc0` with loaded update `0x002c26c8`. Its accepted fire path calls `0x00233db8(-1, 1)` at `0x002c28d0` and enters weapon native state `3`.

## Projectile ownership and launch

Weapon state `3` loads its staged object from weapon PVar `+0x50`, calls constructor `0x002ace70`, and launches through `0x002acfd8`. The constructor requests native class **`0x79` (121)** from the common Moby allocator. This corrects the earlier class-`0x4a` attribution.

The class-`0x79` constructor stores the firing weapon Moby at projectile PVar `+0x50`. The launch helper writes projectile native state `1`, establishing the source chain:

`class-0xc0 weapon Moby -> class-0x79 projectile -> projectile PVar+0x50 source`.

The shared `Rac1GameplayDamageEvent` preserves that distinction: the damage **source** is the launched class-`0x79` projectile identity, while recovered **owner** provenance is native class `0xc0`. OBP leaves the owner's runtime instance unset rather than inventing a stable weapon-Moby id from the raw pointer.
The accepted-fire update clears the old staged pointer, then the weapon tail can immediately construct and store a replacement class-`0x79` object at weapon PVar `+0x50`. The controlled live trace shows the launched carrier at `0x01858b80` while the replacement is already staged at `0x01858c80` on the ammo-decrement frame. The older 10-tick rearm claim belonged to a different class-`0xc0` update and is not Bomb Glove truth.

The weapon retains a recovered 20-native-tick fire gate. OBP therefore keeps the 20-tick accepted-shot gate, but no longer models a 10-tick projectile rearm delay.

## Ballistic motion and countdown boundary

The controlled projectile uses loaded update `0x002adb30`. In native state `1`, the update calls common vector-add helper `0x001ff278` with:

- destination = Moby position;
- left-hand vector = current Moby position;
- right-hand vector = projectile PVar `+0x00..+0x08`.

So ordinary ballistic motion is `position = position + step` once per native update.

The same branch reads native frame-scale `0x0015ed70`, whose authority-state float32 value is `0.00027777778450399637` (`0x3991a2b4`, approximately `1/3600`). Native single-precision multiplication by `11.0f` rounds to float32 word `0x3b483fb8`, or `0.003055555745959282`; that rounded value is subtracted from PVar `+0x08` per native update.

The managed live trace agrees. At launch the step is approximately `(0.06286147, 0.12695619, 0.09166652)`; one update later the horizontal components are unchanged and the vertical component is approximately `0.08861096`.

Launch also initializes two countdown-like fields:

- PVar `+0x54 = time(300)`, decremented by `0x001fef98`;
- PVar `+0x6a = time(30)`.
The short countdown reaches zero while the controlled projectile continues flying. The representative shot contacts while the long countdown is still `235`, so that trace does not witness natural expiry. Further static recovery identifies the long countdown's zero consumer at `0x2ae444..0x2ae460`: it selects the impact/effect path. OBP uses 300 ticks to enter the same blast state if geometry is never encountered; 30 is not an expiry.

## Contact, splash damage and terminalization

The class-`0x79` state-1 path calls shared world query `0x001efc70` and common gameplay contact routine `0x001f2868`. On the retained contact branch it supplies native damage scalar **`2.0`** and flag word **`0x00830000`**, retains the class-`0x79` projectile Moby as source, then calls splash distributor `0x0025a9f8`.

The distributor iterates candidate Mobies and calls per-victim damage writer `0x00259a88`. It explicitly skips a candidate whose Moby pointer equals the retained source. The per-victim record retains source, damage flags, scalar and victim. This is a source-aware **contact-volume/splash** handoff, not the earlier direct-victim `1.0 / 0x00010000` record.

The controlled shot follows native states `1 -> 2 -> 0xfe`: state `1` begins on trace frame 41, contact state `2` begins on frame 107, and common terminal state `0xfe` appears on frame 122. Position is stationary during the state-2 interval in this witness.

The class-`0x4a` `1.0 / 0x00010000` direct-record path remains valid evidence for a **separate projectile family**. It is no longer used as Bomb Glove provenance.

## Held assets and throw presentation (19 October recovery)

The equipment row at `0x179ac0 + 10*0x4c` selects attachment slot6 and class192.
Slot6 in `0x209240`'s semantic-joint query list is semantic6; class0's bounded
attachment chain ends at **Ratchet joint54**, independently of wrench joint56.
`0x20f6f8` applies the selected socket translation and matrix.

Class192 is in the compressed global gadget directory, not a level-local model
payload. Its LEVEL0 compressed size is 19180 and SHA-256 is
`cddebd40b4c00a06edbe306c671f2d2f10e474e0e1b1455f325afa2a57a02d4f`.
The decoded mesh has 1140 vertices, 1200 triangles and 17 joints. Its sparse
sequence table has three populated slots: sequence0 has eleven frames at rate
0.5; sequences1/2 are static poses. The host uses sequence1 while held and
sequence0 on firing; this visible selection remains an adapter policy until the
generic weapon-animation selector is recovered. The mesh and frame poses are
retail data. Sequence0's timed local sound1 supplies the throwing sound.

`0x2c2710..0x2c27c8` constructs the staged bomb position from class192 semantic0
and its local offset `(0,-.09,-.02)`. The staged and flying bomb both use actual
class121 geometry: 186 vertices, 160 triangles, three packets and no joints.
Neither the glove nor bomb uses a procedural sphere or a calibrated root socket.

Player action35 entry at `0x2262ec` selects body sequence44, frame9, blend7.
The sequence contains 27 frames at rate0.25, with authored timeline positions
0,2,...52. `0x231bac..0x231c44` permits input cancellation after authored time19
and returns to the ordinary action at21. The host plays the grounded throw at
the recovered entry/rate and returns its animation to the ordinary native clock.
Airborne/traversal movement retains its owning animation. Full action35 movement
and its contextual cancellation predicates are not recovered by this change.

## Ordinary launch solution

`0x2c2954..0x2c29c8` constructs the ordinary launch origin relative to native
body origin P+80: planar radius f32 `0x3f5be9fb`, angle offset f32 `0xbeb953df`,
height f32 `0x3efb4cc2`. The host maps its feet origin back to the body origin
using the same geometric-base alignment as the avatar.

The unupgraded ordinary target is 8.5 units ahead. `0x2c2488` caps a target
farther than8.5 to8.49, selects native facing for a target closer than1.5 or
outside the f32 `0x3f5f66f3` angular gate, and sets horizontal step `8.5*dt`.
`0x25abf0` solves vertical step from planar travel time and gravity; the caller
caps upward step at `gravity*(8.5/horizontalStep)/2`. This recovers the retained
approximately .0916665 upward step. World position advances before gravity is
subtracted. Native spin advances X by `2.09439516*dt`, Y by `7.85398149*dt`.
Launch `0x2ad350..0x2ad390` also queries the body-to-launch segment at the
launch height and clips an obstructed origin, preventing spawning past a thin wall.

The host's target discovery cone, target centre and line-of-sight allowance are
OBP policies. Native target history, special moving-target prediction,
platform inheritance, strafe/first-person variants and upgraded aiming are
outside this ordinary launch contract.

## Blast and effects

`0x2af30c..0x2af3f8` hides the bomb, retains its impact position, queries an
expanding volume and terminalizes after counter15. With upgrade byte0 the
query radius interpolates **.5 to2.5** over ages0..15. Damage remains2 with
flags `0x00830000`. The session keeps each victim's consumption of a projectile
distinct while allowing later expansion to reach additional targets. Veldin
toad/robot consequences now use the independently recovered enemy damage paths.

The source effect path creates ten small, four large and one additional class122
fragments through `0x2af450`. Small lifetime is60..120, large lifetime60..90;
class122 update `0x2af5b8` applies position-plus-step, gravity `14.6*dt²`, spin
and a last-quarter shrink. Fragment impact-basis construction remains an OBP
upper-hemisphere approximation. The source mesh has29 vertices/16 triangles.

`0x2df808` creates expanding class1192 models. Registration at `0x1ea6c0`
binds1192 to `0x2acad8`, which grows scale linearly, rotates each axis byπ*dt
and fades alpha in the final half. The ordinary effect layers supplied at
`0x2aeb90..0x2aec8c` have sizes4/3.5/3 and lifetimes20/27/29, coloured
orange/red/dark red. Two additional white/orange layers are distance-gated.
The decoded burst mesh has229 vertices/320 triangles. Local sound0 on class121
is the ordinary explosion association at `0x2aee18..0x2aee24`; class-remapped
bank audio is decoded through the same bounded single-tone path as crate audio.

The particle lookup is separate from Moby textures. Loader `0x2447f8..0x244818`
passes core+64 (Part pixel base) and core+6c (lookup descriptor) to `0x23f630`.
Class122's trail constructor `0x269958` reads pool type4's initial texture,
which is Part texture0 in LEVEL0. Class79 emits150 type34 sparks through
`0x26f080`, which reads lookup34 at `0x1b2108`: LEVEL0 Part texture33.
OBP uses these actual textures. Godot billboarding, particle dynamics, colour
ramps, size projection and blending remain presentation approximations; native
pool scheduling/collision, complete texture programmes and GS equations are not
claimed exact. Shader/texture/mesh resources are shared between emitters.

The Part table's 16-byte records are **palette offset, GS flags, pixel offset,
square side length**. Both offsets are relative to the Part bank at core+64;
its palettes are in that bank, independently of ordinary Moby GS-RAM palettes.
The bounded decoder applies the shared PS2 CLUT reorder and alpha conversion.
Synthetic contrasting palette/pixel offsets and actual rendered textures guard
against the initial incorrect interchange of these offsets. The format is also
corroborated by [Wrench's particle unpacker at the pinned reference revision](https://github.com/chaoticgd/wrench/blob/e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb/src/wrenchbuild/level/level_textures.cpp#L328),
used as corroboration rather than game-behaviour authority.

## Current validation and boundaries

`Rac1BombProjectileTests` pins the new executable instructions, global asset
hash, skeleton/socket tracks, sequence44, particle lookup/texture identities,
launch recurrence, blast expansion and burst lifetime. `Rac1BombGloveTests`
also verifies once-per-victim consumption across successive blast queries.
All source-dependent checks read the authorized local ISO; no payload is stored.

`tools/rac1-bomb-glove-smoke.ps1` checks actual host models, animated staging,
sequence44, the visible arc, floor/wall impacts, two-crate splash, held cadence,
empty ammo, run/throw/idle interpolation, weapon switching and effect cleanup.
`-Rendered -CaptureDirectory <directory>` additionally produces GPU captures in
an unfocusable offscreen window without writing personal window preferences.
It is a staged integration fixture, not a live retail experiment.

Collision is explicitly host-owned: a Godot swept sphere for terrain, bounded
enemy/crate admission and an occlusion adapter. The exact native query primitive,
native actor collision volumes and contextual aiming remain unresolved.
The host occlusion ray excludes damageable enemy/crate bodies; their own bounds
must not reject their damage query or shield a whole stack. Terrain and walls
still occlude the ray, without a fixed endpoint allowance. A saved Veldin throw
reproduced the original self-occlusion bug and now breaks the nearby stack;
the integration gate separately checks that a wall protects a crate.
Faction/team rules beyond the admitted enemy/crate slice are not generalized.

Validation on 19 October: Release and authorized retail suites each pass1414
tests. The Bomb Glove gate passes headlessly and with inspected GPU captures;
the existing wrench, Veldin enemy, combat-contract and ordinary-play gates pass.
The latter two use their prescribed seed1440. New world queries also exposed
inactive crates retaining invisible physics bodies; presence changes now update
crate collision layers consistently with enemies, with a direct query regression.
