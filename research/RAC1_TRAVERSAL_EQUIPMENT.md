# R&C1 regular Ratchet traversal and equipment

Authority is original NTSC-U SCUS-97199. ISO SHA-256 is
`ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d`;
the Veldin gameplay overlay SHA-256 is
`eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804`.
Addresses below refer to that loaded overlay, not same-address boot executable
bytes. `P = 0x13f350`; coordinates in source models have Z up.

## Regular movement inventory

This inventory distinguishes a named retail move from a recovered motion law.
The native English HelpDesk and item descriptions establish the names and
controls below. Their instructions describe the game; they are evidence rather
than instructions to an agent. Vehicles, hoverboard races and Clank-only gameplay
are outside this pass.

| Movement family | Retail controls/context | Source boundary |
| --- | --- | --- |
| Standing, walking, running, facing, braking | Left stick and release | Ordinary movement, yaw, stop and animation contracts are retained separately. |
| Crouch, crouch-turn and carried slide | R1, then left/right stick | Action 4 and distinct ice carry are recovered; do not invent an extra normal-floor friction multiplier. |
| Ordinary jump and airborne steering | Cross, held/released pressure | Jump action family and launch-context sequences are separate from pack features. |
| Side flip/back flip | Crouch, snap stick sideways/back, press Cross | Retail HelpDesk names these. Their exact action/entry predicates belong to airborne recovery. |
| Wall jump | Cross toward an authored jump slot, press Cross at the wall | Retail explicitly describes jump-slot surfaces. Arbitrary-wall jumping is not established. |
| Ledge grab, pull-up, drop, traverse | Jump to ledge; Cross to pull up, R1/R2 to drop, stick sideways to traverse | Geometry admission and actual motion must be recovered separately. |
| Glide | Heli/Thruster Pack; Cross again in air, then hold | Not sufficient evidence for an equipment-free generic double jump. |
| Stretch Jump | Heli/Thruster Pack; running crouch plus Cross | Different native pack actions; do not copy another game's long-jump values. |
| Boost Jump | Heli/Thruster Pack; stationary crouch plus Cross | Different native pack actions and source equipment readiness gates. |
| Power Slam | Thruster Pack; R1 during jump | Distinct from Hyper-Strike; also drives specific world locks. |
| Hyper-Strike | Jump, then Square | Downward wrench attack, with combat consequences distinct from pack slam. |
| Comet-Strike | Crouch, then Square | Wrench throw, not generic grounded melee. |
| Swimming at surface and underwater | Square descends, Cross ascends; pressure affects ascent | Domains `0x12` surface and `0x11` underwater; shallow wading is separate. |
| Hydro-Pack swimming/current resistance | Owned pack automatically equips for swimming; R1 accelerates underwater | Source-owned item 4 and action `0x35`; current producers and full swim motion still need integration. |
| Swingshot pull and swing | Equip Swingshot, hold Circle through traversal; directional input assists swing | Class 758 supports two code modes but all 55 authored placements select pull; class 803 is a separate second target family. Acquisition/projectile routing remains distinct from motion. |
| Grindboots/rails | Jump onto authored rail | Native spline/path fields, branch points, obstacles and rail jumps. No arbitrary nearest-mesh-edge substitution. |
| Magneboots/Magne-Strips | Automatic equipment on eligible contact | Special support class and orientation basis; allows walls/inverted travel only on eligible strips. |
| Zipline | Stand under authored wire and jump | HelpDesk-confirmed ordinary traversal. This is not established as the same motion controller as grinding. |
| Ice, mud, water, moving supports/conveyors | Authored collision/contact state | Distinct native actions; material identity alone does not recover the motion controller. |

The source English text block at ISO byte `0x2381820` starts with count 1762,
then its size and sixteen-byte records. Each record gives relative string offset,
text id, and two additional words. Tests resolve item names through actual text
ids rather than infer their identity from model shape. Move/control evidence is
also present around ISO bytes `0x238c380..0x238cc60`, pack descriptions around
`0x238fd98..0x2390014`, zipline help at `0x2399b44`, and swimming/rail help in
the same English block. These locations are provenance, not production offsets
for a general language loader.

## Equipment identity and progression

The equipment descriptor table is `0x179ac0`, stride `0x4c`. It is distinct
from the ammo/price descriptor table at `0x1c40b0`. The relevant fields are name
text id `+0`, category `+8`, equipment domain `+0xc`, primary class `+0x10`,
secondary class `+0x14`. These source domains do not imply a neutral engine API.

| Item | ID | Name text ID | Category/domain | Primary class |
| --- | ---: | ---: | --- | ---: |
| Heli-Pack | 2 | `0x4e6e` | 3 / 5 | `0x25f` |
| Thruster-Pack | 3 | `0x4e6d` | 3 / 5 | `0x260` |
| Hydro-Pack | 4 | `0x4e6f` | 3 / 5 | `0x261` |
| O2 Mask | 6 | `0x4e6b` | 2 / 4 | `0x509` |
| Swingshot | 12 | `0x4e61` | 0 / 1 | `0xd0` |
| Magneboots | 28 | `0x4e67` | 1 / 2 | `0xad` |
| Grindboots | 29 | `0x4e68` | 1 / 2 | `0xc3` |

Ownership remains the native item byte at `0x13d4c0 + id`, represented by
`Rac1WeaponInventory`. It is not inferred from unlock flags, current held item,
remembered gadget, current planet, or a developer preview setting.
`Rac1MovementCapabilities.FromInventory` projects only those ownership facts.
It never acquires items or changes persisted loadout.

Save block 32's remembered backpack entry is slot 3. Live pack readiness is
separate: helper `0x20d498(3)` returns `P+0x11a8` only while the corresponding
equipment state at `P+0x11a4` is 2; otherwise it returns -1. Restoring remembered
equipment into live requests is itself a transition. The capabilities API calls
its slot projection `RememberedBackpack` to retain that distinction.

Native automatic equipment admission includes:

- `0x2100b8..0x21012c`: domain `0xf` and owned item 29 request Grindboots.
- `0x210130..0x21019c`: `P+0x12e7` and owned item 28 request Magneboots.
- `0x2101a8..0x210290`: water/death/oxygen contexts and owned item 6 can request
  the O2 Mask, while retaining the previous head equipment for restoration.
- `0x21029c..0x210318`: water-domain helper or action `0x12`, and owned item 4,
  request Hydro-Pack and remember the previous pack.
- `0x21031c..0x210350`: action 8 and remembered pack 4 request Heli-Pack 2.

These are bounded request predicates, not permission to immediately change
every live equipment field. Campaign acquisition producers and transitions are
not bypassed by the model. An explicitly marked OBP development loadout is
separate host policy and must not alter save-backed ownership.

## Ordinary Swingshot targets and constraint

The held Swingshot is item 12/class `0xd0`; the registered Veldin updater is
`0x2c6158`. The ordinary target producer `0x222158` admits class `0x2f6`
(758), not the held gadget class. Class 758 has a 64-byte authored PVar on all
placed examples in the retained census. Relevant fields are:

| PVar field | Consumer |
| --- | --- |
| `+0` | Must be zero for ordinary candidate admission. |
| `+4` | Authored target tether radius, copied to `P+0x980`. |
| `+8` | Mode, copied to `P+0x978`: zero pull, nonzero swing. |
| `+0x1c` | Region/polygon admission; negative bypasses that helper. |
| `+0x20` | Maximum 3D range; zero uses 30 units. |

Candidates also require live Moby flag bit 0 clear, source-facing yaw within
`0.5235988` radians, and planar distance at least 4. Non-aiming source scoring is
`distance * (1 + abs(yaw error))`, plus 10 when the native visibility byte is
zero and plus 7 when helper `0x25e3b8` returns nonzero. Lowest score wins. The
region helper and a ray from player position plus 0.5 native Z must admit it.
Camera-aim actions 1/`0x1e` use different angular gates and scoring and are not
represented by the bounded ordinary `CandidateScore` method.

`ReadAuthored(RuntimeDynamicObject)` owns the PVar parsing inside R&C1 and
converts the imported Y-up matrix translation back to native `(X,Y,Z)` as
`(matrix[12], matrix[14], matrix[13])`. The host must still supply actual live
transform/visibility/contact facts. Region-bearing targets remain unsupported
until their region producer is hosted; negative region ids are unrestricted.

The gadget's own projectile arrival path selects action `0x26` for nonzero
mode and action `0x25` for zero at `0x2c68e0..0x2c690c`. Directly selecting
an action from a host key is an OBP development preview, not recovery of that
projectile/arrival lifecycle.

Action `0x26` entry `0x223b64` sets domain `0xd`, clears `P+0x960` and
`P+0x974`, stores actual player-anchor distance in `P+0x97c`, and selects
sequence 48 with five blend ticks and selection flags 1.

The motion block `0x219d74..0x219e70` is implemented by `StepSwing`:

1. Seek current radius `P+0x97c` toward authored radius `P+0x980` by at most
   `10 * dt` per update, using the native bounded seek helper.
2. Copy full XYZ contact motion `P+0x110`, multiply by native f32 `0.9`, and
   subtract `15 * dt²` from native Z.
3. Add this request to current position.
4. Project the predicted position onto the tether sphere about the target.
5. Emit projected position minus current position as the primary contact request.

Here `dt=1/60`. Host velocity, post-external combined displacement and platform
carry are not substitutes for `P+0x110`. The contact solver still owns blocking
and collision after this request. The pure helper uses the existing double
precision native-vector boundary, so equations are source-derived without a
claim of instruction-by-instruction PS2 floating-point equivalence.

On release, update `0x22c7e0` retains the current action while the native use
mask is held. Otherwise it caps scalar `P+0x194` at `7*dt`, returns action 0
when support distance `P+0x2dc` is strictly below native f32 `0.4`, and selects
fall action 6 otherwise. Fall entry `0x2243c4..0x2243dc` copies full XYZ
`P+0x110` to `P+0xe0`; it does not discard tangential/vertical contact motion.

Class `0x323` (803) is the second ordinary authored target family selected by
`0x221ee0` and action `0x2c`. It must not use the action-26 solver. Follow-up
[pendulum recovery](RAC1_SWINGSHOT_PENDULUM.md) now implements its attached
normal-gravity/static-target motion, separate native radius correction, initial
boost, input pumping and release. Live target-facing behavior, projectile
attachment and source body-pose corrections remain separate gaps.

Class-758 placements exist on levels 2,3,4,5,7,10,11,12,13,14,15,16,17,18.
Veldin level 0 has no ordinary Versa-Targets, so a Veldin movement smoke cannot
prove a naturally placed Swingshot route.

All 55 class-758 authored placements have PVar `+8 == 0`; the swing-mode branch
is executable-backed but is not exercised by their default placement data.
Aridia level 2 contains eleven pull targets. Instance 629 has native anchor
`(136.16592, 253, 34.999657)`, radius 7, unrestricted region -1 and default
range 30. Instances 636/638 have authored region ids and must remain excluded
until the region admission helper is hosted. The other nine are unrestricted.

### Pull action 25

`Rac1TraversalEquipmentPull` implements the source motion after attachment,
including both native phases, rather than applying the swing constraint to a
pull target. Entry `0x223b20` selects sequence 48 with ten blend ticks and
selection flags 1, sets domain `0xd`, and clears speed/near-arrival state.

At `0x219a0c..0x219d74`:

1. Remaining time starts at 60 updates. If previous speed is positive, it becomes
   player-anchor distance divided by current full contact-motion length `P+0x160`.
2. Phase 0 seeks `27*dt` speed by `80*dt²`; phase 1 seeks zero by `42*dt²`.
   The phase-1 target is zero because `f12` is explicitly cleared at `0x219a38`.
3. Solve the quadratic with coefficients `(-0.5*42*dt², speed, -(distance-1.9))`.
   Helper `0x2595a0` sorts its roots descending. If a real first root is positive,
   use that larger root as remaining time and switch to braking phase 1.
4. Remaining time below 20 sets the sticky near-arrival flag. Source pitch/yaw
   steering also consumes it; the numerical request API takes current native
   facing from the caller and does not claim that orientation presentation.
5. The attachment point is player position plus
   `(-sin(yaw)*0.57, cos(yaw)*0.57, 0)`. Normalize anchor minus that attachment
   to current speed. When remaining time is below 30, subtract
   `29*dt²*(30-remaining)` from the resulting native-Z request.

The model retains the f32 0.57/1.9 literals while using the existing double-vector
boundary. A zero contact length with positive prior speed produces an unbounded
time estimate until the independent quadratic can replace it; no fabricated
contact movement or teleported position is substituted.

Update `0x22c5c8..0x22c7e0` exits when use is released, or when braking and
speed is strictly below `4*dt`, even if use remains held. It caps scalar speed
at `7*dt`. Support distance strictly below f32 0.3 returns action 0; otherwise
it selects fall action 6, retaining full contact momentum through that entry.
Fall fields `P+0x940`/`P+0x944` become `29*dt²`/`42*dt²`. Their later combined
fall recurrence has a separate owner. Releasing with remaining time over 25
also sets a premature-release flag used by the held-gadget lifecycle.

The source uses sequence 10 during near arrival/release in some paths. This
does not repurpose the environmental-death presentation selector: admission
depends on the actual source action context. The bounded session reports native
motion and exits and leaves this additional presentation work separate.

## Swimming, Hydro-Pack and oxygen

Underwater domain is `0x11`; surface-swimming domain is `0x12`. This follows
the oxygen consumer and depth transition, not animation appearance. Actions:

- `0x33`: moving underwater, sequence 59 and multiplier
  `clamp(P+0x194 * 15, .25, .75)`.
- `0x34`: underwater idle, sequence 60.
- `0x35`: Hydro-Pack underwater, sequence 61; ownership of item 4 plus held
  crouch-mask `0xa` selects it from the normal underwater family.
- `0x36`: moving surface swim, sequence 101, blend 17; `0x37`: surface idle,
  sequence 58, blend 22. Both use playback multiplier `.6`.
- `0x73`: moving wade, sequence 96. The older `0x72` routing label is a
  locked idle/carry action, not freely controlled shallow-water walking.

Surface swim accepts buffered Cross `0x40` to action `0x12` and Square
`0x80` to underwater action `0x33`/Hydro-Pack `0x35`; the source includes
cooldowns and contact guards. Idle surface Cross requires age >10; moving
surface Cross does not. Square has a seven-update press history; Cross eight.
Underwater-to-surface requires leaving dive substate 1, positive final vertical
contact motion and feet above `water-.4`. A blocked ray from `water+.01` to
`water+.31` instead forces a dive. A floor triangle tagged water is insufficient to
reconstruct depth, currents or water height.

`0x227778..0x2278e4` owns oxygen at `P+0x22a0`, maximum 10000. In underwater
domain, ownership byte 6 suppresses consumption; otherwise integer decrement is
`10000 / (15 * 60) = 11` each update. From full, 909 updates leave 1 and the
910th reaches zero. Leaving underwater refills unless action is drowning
`0x6a` or water-death `0x76`. Exhaustion is only part of the death predicate:
source depth, pitch, movement and surface context still matter. `StepOxygen`
therefore returns a counter and does not manufacture a death event.

### Novalis water motion and host boundary

The original LEVEL1 overlay is pinned in
`generated/rac1-water-native-witness.json`. `rac1-water-native-trace.py` rebuilds
private EE RAM from the ISO and executes the original routines using the
native-port's R5900 interpreter. Its clear contact queries, supplied conditioned
input/animation facts and suppressed presentation are explicit limits; these
numeric witnesses are not an ordinary-gameplay test.

`Rac1WaterMovementSession` owns the regular-axis actions and motion recovered
from Novalis `2378bc` (underwater), `238a24` (surface), `2406b0` (oxygen),
`2408e8` (entry), `240c78` (waterline bob) and `2435b8..243b64` (input).
Surface entry measures feet plus `.45`, falling reach `max(.2,abs(vz)+.07)`
and depth `>.8`. Native wading flags use strict `.25<depth<.85`.

Surface paddling accelerates its scalar towards `3/60` by `4/3600`, brakes
by `5/3600`, retains a 45-update stroke and samples the 30-float curve at
`17c440`. Its sampler uses authored animation **advance**, not only phase:
`frac*curve[i] + (advance-frac)*curve[i-1]`, with wrap selecting entry 29.
Contact carry decays separately at `4.2/3600`. Waterline height settles around
`water-.12` through the original bob and height springs.

Underwater Square pitches down, Cross pitches up, and a surface dive retains
substate 1 for 40 updates. Native pitch/yaw/roll springs steer spherical motion.
Normal speed targets `3/60` (dive entry `6/60`), while an owned Hydro-Pack
targets `7/60` under held crouch. Stroke motion follows the authored curve;
Hydro-Pack motion uses the scalar directly. The host supplies full digital
button pressure; DS2 pressure variation/non-pressure-pad ramps are not claimed.

Water exit is action `12`, sequence 114/frame 1/blend -1, not an ordinary
jump. Depth <1.25 selects heights 1.95..1.98 and launch age 15; deeper water
selects the native impulse script at `17c3e0`, ages 2..36. Air steering is
clamped to `3.7/60`. The initial dip must not prematurely re-enter swimming.
Sequence 114 retains its variable retail frame rates.

At zero oxygen, the native escape exception requires feet above `water-2`,
pitch below `-.87266463` and final vertical contact motion above `1/60`.
Otherwise action `6a` selects drowning sequence 95. OBP lets that animation
complete before its existing death/checkpoint host runs. The normal HUD displays
the source oxygen counter; its plain bar layout is OBP presentation policy.

Remaining boundaries: Godot approximates native mask-2/0x24 membership using
the authored class-zero footprint plus solid/ceiling rays and retains an upright
host collision capsule. Exact native swimming body queries, moving water,
currents, water Moby ownership, submerged camera/fog, splash/wake/bubble particle
generators, joint bending, global action cooldown producers and water-jump
animation retiming remain unrecovered here. Shore handoff resumes the ordinary
ground controller; the complete moving-wade action 73 is not implemented.
No numerical PS2 bit-exactness or complete water reconstruction is claimed.

## Remaining contextual ownership

Magneboot action `0x3f` has movement `0x21bdb4`, involving source orientation
basis, turn-dependent speed attenuation and dedicated support snapping. It is
not ordinary walking rotated onto any class-2 triangle. The existing special
contact route also requires contacted class `0xad`.

Rail actions `0x28..0x2b` and `0x42` share motion `0x21ca68`; domain is
`0xf`. Fields `P+0x560/+0x564/+0x568/+0x584` represent authored path,
segment/parameter and traversal context. Entry calls the spline helper
`0x25d808`; branch switches involve `0x25df68`. Source spline/path decoding,
rail region admission, branch/jump/obstacle behavior and their complete host
ownership remain necessary before claiming playable native grinding.

Zipline's authored wire admission and motion are still unresolved. The source
HelpDesk establishes its existence and jump-to-attach control, not a numerical
physics model. These remaining systems are documented instead of substituting
a generic grapple, rail collider or arbitrary surface rotation.

## Verification

`Rac1MovementCapabilitiesTests` verifies ownership/loadout separation and resolves
each equipment descriptor's name through the canonical retail English table.
Its retail case pins descriptor/equipment/target/swing/oxygen code block hashes
and source action selectors against independently loaded serialized sections.
`Rac1TraversalEquipmentTests` covers malformed targets, source identity and
coordinate conversion, geometric admission, tether radius/damping/gravity
invariants, release boundary and integer oxygen timing. No payload bytes are
retained by these tests.

`Rac1TraversalEquipmentPullTests` pins the entry/motion/exit/quadratic code
blocks, checks real Aridia placement data, and exercises acceleration, larger-root
braking, full request trajectories, automatic completion and release boundaries.
The initial targeted suite passed 33 cases including the canonical retail checks.
