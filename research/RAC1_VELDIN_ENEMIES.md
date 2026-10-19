# Opening Veldin enemies

Scope: NTSC-U original `SCUS-97199`, LEVEL0. The 16 class749 placements
143..158 and nine class1440 placements 189..197 now have live behaviour,
retail models and source-clocked skeletal animation. LEVEL18 and other enemy
classes remain outside this implementation.

Authority is the locally authorized ISO and its verified Veldin overlay.
`generated/rac1-veldin-enemies.json` retains block hashes, instruction signatures
and interpreted constants, without executable, model or capture payloads.
Reproduce it with:

```powershell
python tools/rac1-veldin-enemy-probe.py --iso $env:OBP_RAC1_ISO --output research/generated/rac1-veldin-enemies.json
```

## Class-local behaviour

| Family | Implemented script behaviour |
|---|---|
| 749, melee creature | Authored activation polygons; state5 search, state6 pursuit, state7 sequence5 melee, state8 return-home; linked i154 watches i197 until its terminal lifetime; one health; lethal state12, sequence6/7. |
| 1440, flame robot | Script initializes health to two despite authored health one; region-triggered entrance paths, state3 target tracking, three shared attack slots, sequence5 wind-up, sequence6 sweeping fire, contact/timeout recovery, nonlethal sequence8 and lethal sequence11. |

Class749 navigation at `0x2d54c8` uses native model X as forward, acceleration
8/3600, braking 6/3600 and maximum displacement 4/60 per update. Turning uses
4pi/3600 acceleration/braking and 4pi/60 maximum angular displacement. The
authored activation offset is sampled within +/-20 degrees and applies beyond
distance 2. Script initialization multiplies class scale by .75.

Initialization and return-home completion select rest sequence1. The state5
routine at `0x2d4a9c` selects locomotion4 and calls `0x261630`, which supplies
roaming displacement and turns. OBP does not host that roaming routine yet:
its stationary search presentation therefore uses retail rest clip1. The native
controller continues to report locomotion4; this stationary presentation choice
is an explicit host stand-in, not a recovered change to the state5 selector.
Pursuit and return-home movement continue to use locomotion4.

Class1440 initialization copies path point0 into Moby+0x10 at
`0x2e0d20..0x2e0d28`, before waiting for its activation region. Editor placements
can differ substantially from that initial position, including placements at
the route's far end. Runtime initialization preserves the authored transform
basis and immutable placement while replacing its live translation. Godot
applies that initialized state to both the model and contact body. Omitting
this copy made i189 take a long detour and i190 backtrack across the gap.

Class1440 entrance navigation at `0x2e0e24` follows its authored waypoint list
from index 1, using acceleration 8/3600, braking 16/3600 and maximum step 12/60.
Waypoint arrival calls full XYZ vector length at `0x2e0f2c`, and includes height.
The path displacement is added directly into Moby+0x10 by
`0x2e0fa4..0x2e0fb0`; it does not call the shared navigation contact solver.
The host therefore moves the model and contact body along that XYZ path without
wall sliding or floor projection. Floor snapping could otherwise strand i192
about .075 above its destination even with its correct XY arrival.
The script's ordinary target range is planar distance 24 inclusive. Helper
`0x1ff488` operates on native XY; it is not a squared-distance helper. The
near alert boundary is planar distance 20 exclusive. The linked target of i197
is i154 and replaces the player target when nearer (`0x2e1a38..0x2e1a7c`).

Class749 attack entry requires distance<2 and facing error<raw `0x3e32b8c2`.
Sequence 5 crosses authored marker 34 after 68 phase updates and repeats over 102
updates, excluding its seven-update entry blend. Marker emission precedes the
animation-end range/facing reconsideration. The shared emitter resolves semantic
socket 0 and queries radius raw `0x3eaa7efa` (.333), with damage 1.
The query origin pointer is preserved in s6 (`0x2598bc`) and passed unchanged
as a0 (`0x25998c`) to `0x1f2868`. The separate forward/up vector belongs to
the damage descriptor; it does not offset the sphere origin. The host checks
that sphere against its actual player capsule dimensions, including queries
that begin inside the capsule. Capsule admission and ray occlusion remain
host collision policy.

Class1440 fires a socket 0 to forward 5 segment, plus the socket-to-root+.5
query. Damage becomes active after authored position 1 and completion of the
entry blend. A player contact releases the attack slot and selects recovery7;
four completed fire cycles select recovery12. Nonlethal intake selects state7,
sequence8 and a 60-update cooldown. Lethal intake selects state8, sequence11.

The formerly unresolved outgoing descriptor is now traced through query
`0x1efc70`: `0x1f0ccc/0x1f0cd0` load its two quadwords and
`0x1f0cdc/0x1f0ce0` copy them into damage record+0x10/+0x20. Descriptor+0x1c
therefore reaches damage record+0x2c. The admitted event has damage1 and flags
`0x00010001`. Both enemy intake paths request mask `0x00330000`, admitting the
bounded player wrench/Bomb Glove descriptors; class749 also accepts the recovered
class1440 contact descriptor. Moby contact does not invent a player-recovery
transition for the firing robot.

## Reactions, rewards and player damage

The common reaction initializer `0x25c558` installs planar/vertical launch;
`0x25c698` subtracts gravity and approaches planar speed toward zero. The class
parameter blocks and nonlethal modifiers yield:

| Reaction | Gravity / 3600 | Drag / 3600 | Planar launch / 60 | Vertical launch / 60 |
|---|---:|---:|---:|---:|
| class749 lethal | 40 | 14 | 13 | 14 |
| class1440 lethal | 28 | 5 | 10 | 9.5 |
| class1440 nonlethal | 28 | 5 | 3.5 | 4.75 |

Godot derives direction from the live attacker/projectile position and supplies
physical contact response; this does not claim the complete native descriptor
normal/impulse construction.
Reaction animation remains visible before terminal removal. A lethal body's
host collision layer is removed immediately so emitted bolts cannot settle on
a temporary corpse capsule. Root scale and mesh deformation remain independent
of that collision adapter.

Both lethal intake paths call `0x257470` before reaction completion. Authored
reward centres are 7 and 22. They use the existing recovered reward interval,
denomination partition, emitted bolt models, flight/bounce/attraction and arrival
wallet credit. A shared `Rac1BoltPickupSession` prevents enemy/crate identity
collisions and duplicate credit; each victim emits its reward once.

Ratchet's ordinary hurt entry (`0x22666c..0x226678`) sets P+0x1c0 to 77. Common
intake (`0x210d34..0x210d44`) rejects damage while nonzero; the common update
decrements it via `0x22163c/0x221640` and `0x1fef68`. The Nanotech session now
shares that 77-update protection between the two enemy families.

Combat death offers an explicit **Retry level** button. That is OBP application
policy: it loads the current level again through the ordinary provider route.
It does not claim retail combat checkpoint/respawn semantics and does not call
the separately recovered environmental-death reset.

## Reusable boundaries and presentation limits

Native health, selectors, targeting, steering, reaction parameters and clocks
remain in `OBP.RAC1`. Shared foundations include `Rac1MobyAttachment`, local pose
evaluation, `Rac1MobyAnimationAssets`, `Rac1MobySequenceClock`, authored path
decoding, Moby damage dispatch, reaction motion and emitted bolt accounting.
The older `Rac1Class1440ContactFamilySession` remains a lower-level consumed-record
fixture API; normal Veldin uses the opening controller described here.

`RuntimeObjectAnimationSample` carries opaque clip/frame/blend samples into the
neutral renderer. Godot updates existing vertex buffers and bounds without
rebuilding materials or interpreting native sequence ids. The 53-joint/8-slot
and 35-joint/13-slot assets bake all source frames and semantic contact sockets.
Class749 requires admission of its unflagged scale-channel records; their full
control-bit/inheritance semantics remain a presentation parity limit.

Remaining approximations are explicit:

- Godot upright capsules, support sweeps, wall sliding and ray/player contact
  replace the retail common collision solver and per-part primitives. Ground
  placement and support use sweeps of the actual capsule, excluding Ratchet and
  other enemies. Scripted
  robot entrances use their recovered direct XYZ position update instead.
- Class749 idle roaming is not hosted; stationary creatures use retail rest
  clip1 while the native controller's roaming selector remains sequence4.
- Skeletal frame and selector blending interpolate baked vertices rather than
  retail quaternion/joint poses. Native completion-flag scheduling is represented
  by the source clip cycle boundary.
- The launch/drag/gravity recurrence is recovered; native reaction rebound and
  landing substates are not fully translated. World contact uses Godot sliding.
- Fire uses a host procedural particle presentation. Robot fragments use retail
  model classes1933/1934/1935/1963 with the existing debris scatter adapter;
  complete native burst/particle programs and fragment allocator budgets remain
  separate work. Creature death uses its retail reaction and mapped sound.
- Sequence direct sounds and timed cues use class-local retail sound remaps.
  Only admitted single immediate bank tones are decoded; native SPU envelopes,
  mixer/pitch quantization and complex grain programs are not claimed exact.
- Global random seed/call order is not a retail replay. `--fixed-seed` supplies
  a reproducible host stream for diagnostics.
- Ratchet hurt presentation/impulse and automatic combat checkpoint restart
  remain separate from the now-admitted health/protection consequence.

## Validation

Source tests cover authored population/paths, all baked poses/sockets, variable
marker timing, shared attack slots, four fire cycles, script health, nonlethal
cooldown, delayed removal, linked targeting, reaction recurrence, cross-family
player protection and shared crate/enemy bolt arrival accounting. Retail tests
bind the interpreted constants and descriptors to verified overlay blocks.
Entrance regression checks replay all eight retail routes from their script
initialization positions, bounding route deviation, travel and endpoint error.

`tools/rac1-enemy-smoke.ps1` is a staged integration fixture. It checks the 25
animated/colliding placements, reaction/death/reward flow, live authored
entrance routes, stationary toad rest clips, robot socket/player damage, ordinary
wrench and Bomb Glove input, world occlusion and the explicit OBP retry UI. Its staged transforms and final
admitted death descriptors are not ordinary-play or emulator evidence.

`tools/rac1-veldin-play-smoke.ps1` remains the separate input-only route from the
authored opening. Both it and the older combat-contract smoke accept a fixed
host seed (default 1440) for reproducibility. The input-only route observes
either member of the first native activation group
delivering a natural melee hit, then proves a practical animated wrench kill and
crate break. It does not privilege i143 over i144 or measure facing along the old
placeholder axis. Its approach slows near the creature and releases movement
during wind-up, rather than driving through the attack volume into the contact
body. This is test input policy, not an alteration to retail attack dimensions.
See `../docs/RAC1_VELDIN_SMOKES.md`.

Local validation on 2026-10-19 passed the Release and retail-authority suites
(1,392 tests each), 81 Python tooling tests and formatting verification.
Headless host checks passed the input-only Veldin route, staged enemy integration
(including a query beginning inside the player collider), combat unload/reload,
wrench attacks, crates on LEVEL0/1 and placed bolts on LEVEL0/8. The rendered
placed-bolt gate also completed LEVEL11. Earlier rendered enemy fixtures were
inspected for mesh animation, reactions, robot fragments and procedural flame
presentation. These are host checks, not a live retail replay.

The idle/entrance regression gate also passed all eight live robot entrance
routes and the stationary toad rest selector. At fixed host seed1440, maximum
observed route deviation was .048 and maximum endpoint error was .080 units
(including host grounding after arrival). It retained the complete staged
weapon, player-contact, death/reward and retry checks.

The opening-pair ground regression observes i143 and i144 at their original
placements, then stages Ratchet inside their activation region and observes
180 approach frames without moving either creature. It independently checks
spawn clearance, below-floor clearance, local minimum height and actual pursuit.
Before correction, i144 began .4914 units below the imported surface and fell
below Y=-11. Initial capsule placement now settles it above that surface before
idle/pursuit. Capsule support sweeps preserve the shape's slope clearance rather
than pushing its origin back into a centre-ray floor point. Both creatures then
complete the approach with less than .001 unit of observed floor penetration.
These ground placement/contact choices remain host adapters, not a recovered
retail collision solver.
