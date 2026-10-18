# R&C1 movement parity audit

Audit started 2026-10-18 against `699a292` on `dev`. Authority is the original
NTSC-U `SCUS-97199` (`rac1-ntscu-original`). Scope is regular Ratchet, including
movement equipment and combat-related motion; races, minigames, vehicles and
Clank-only sections are excluded. GC/UYA provider support must remain intact.

This is a working evidence inventory, not a declaration of complete parity.
**Verified** means a bounded recovered fact with executable or controlled retail
support. **Approximation** means the implementation extrapolates from those facts
or uses host policy. **Unknown** means the required law is not established.
Retained historical evidence and experiments rerun in this audit are distinguished.

## Broad offline movement pass, 2026-10-18

The working implementation now includes source-backed special jump profiles,
second-Cross aerial boost, directional flips, both aerial packs' high/stretch
jumps and glides, Thruster Power Slam, wall-jump admission and motion, ledge
grab/hang/shimmy/jump/drop, ice motion, and attached Swingshot pull/pendulum
previews. Native equations and selectors remain in `OBP.RAC1`; Godot supplies
contacts and presentation. The new evidence is described in
[airborne movement](RAC1_AIRBORNE_AND_MOVEMENT_EQUIPMENT.md),
[ledge traversal and collision](RAC1_LEDGE_TRAVERSAL_AND_COLLISION_PROFILE.md),
[surface movement](RAC1_SURFACE_MOVEMENT.md),
[equipment](RAC1_TRAVERSAL_EQUIPMENT.md), and
[pendulum motion](RAC1_SWINGSHOT_PENDULUM.md).

This improves coverage without establishing complete movement parity. The
ordinary jump retains its measured trace model. Special action-age ordering,
some animation exit flags, wall/ledge query adaptation and native joint blending
still have host approximations. The recovered collision dimensions constrain a
Godot capsule; the retail radial/axial contact executor is not reproduced.
Equipment previews are explicit development controls and do not fabricate
inventory ownership or mission progression. Swingshot preview starts at
attachment rather than simulating the gadget projectile/equip lifecycle.

Water/mud collision-query membership, free swimming/Hydro-Pack, Magneboot
orientation, grind rails, ziplines and parts of combat movement remain outside
the playable boundary. Unsupported surface motion stops cleanly instead of
throwing or being treated as ordinary ground. Fresh matched PCSX2 trials remain
outstanding; deterministic synthetic Godot fixtures demonstrate integration,
not retail trajectory equivalence. See [the testing guide](../docs/RAC1_MOVEMENT_TESTING.md).

The final source/model suite for this pass passed **1,273/1,273** canonical
retail-backed tests, with zero skips. The portable run passed **1,141** with
132 retail-only skips; all **81** Python tooling tests and required formatting
verification passed. The expanded LEVEL0 Godot movement smoke passed ordinary
locomotion/jumps plus the new pack, aerial boost, glide, flip, Power Slam,
ledge, wall-jump, ice and both Swingshot-family fixtures. It also acquires a
target that becomes eligible while use remains held. A separate flat-ice
host coast difference remains documented in the surface note; source
coefficients were not retuned to conceal it.

The separate unstaged Veldin ordinary-play gate also passed: clean opening,
natural fall/death/automatic restart, recovered camera input, Bomb Glove use,
natural hostile attack/damage and ordinary hostile/crate wrench contacts.
That gate uses public input without transform staging or development respawn.

## Landing regression follow-up

User testing exposed cases omitted by the first broad smoke: tiny-velocity and
flip landings, downhill jump anticipation and both long jumps. The corrective
pass recovers separate landing motion/selector ownership, contact carry caps,
Heli stretch's missing forward ramp and its correctly attributed target. Flip
controlled scalar and flight request are separate, with the recovered sine
projection and strict predicted-landing threshold. It
also replaces discrete idle fallback with physics-sampled standing and native
ground animation handoffs. See [landing ownership](RAC1_LANDING_OWNERSHIP.md).

The descending-slope fixture first reproduced an admitted jump being cancelled
before launch. The missing native 48/3600 prelaunch request and the controller's
support-loss cancellation are corrected; no floor snap, arbitrary slope tangent
or coyote-time rule was added. Five ramp/seam cases pass; see
[slope support](RAC1_SLOPE_JUMP_SUPPORT.md).

The expanded Godot movement smoke passes all five ramp/seam cases and the
quiet landing fixtures, including held crouch through a stationary side flip
and release to continuously sampled standing. Both neutral stationary flip
fixtures have zero measured touchdown coast. The synthetic Heli stretch reaches
9.2333 units/second in flight; neutral Thruster touchdown coasts 0.9363 units.
Those observations are integration checks, not matched retail measurements.
The remaining equipment, contact and Swingshot fixtures and the independent
ordinary Veldin play gate also pass.

The corrected portable suite passes 1,177 cases with 132 retail-only skips;
the canonical retail-backed suite passes all 1,309 with zero skips. Required
formatting verification passes. This does
not replace matched retail trajectory comparisons or establish that every
remaining slide-distance discrepancy is resolved.

## Initial matrix (historical starting point)

| Subsystem | Implementation and existing evidence | Remaining unknowns / experiment | Godot risk and regression coverage | Confidence |
| --- | --- | --- | --- | --- |
| Stick conditioning and heading | `Rac1AnalogueInput`, `Rac1RatchetYawController`: loaded-overlay 48/76 axis law, radial 0.25 activation, angle-space camera-relative target; dense byte sweeps | Exact translational walk/run selector within measured 62/63 bracket; repeat cardinal/diagonal transitions from two headings | Raw action input/calibration must precede retail conditioning; analogue, yaw and raw-input tests | Verified conditioner/heading; speed-band boundary approximation |
| Ground acceleration, walking, running | `Rac1RatchetMovementController`: retained 1/480 increments, ~0.015 walk plateau, 0.09500919 run cap | Broader terrain/history cases; flat right-to-left run slowdown trigger | Collision changes actual displacement; controller/trace tests and movement/contact smokes | Verified bounded witnesses; incomplete global law |
| Release and restart | Recorded walk and run handoff samples followed by 1/300 run decay | Exact phase-dependent release/restart laws; partially accelerated release and 180-degree restart | Hard-coded sample tables are evidence-backed approximations, not recovered general equations; release tests | Approximation |
| Facing and turns | Four state-dependent yaw recurrences, caps and overshoot clamp; movement follows current facing | Direction/history-dependent translation transient | Native/host basis and same-update ordering; yaw and ground-turn tests | Verified recurrences within retained states |
| Jump and air control | Eight-tick anticipation, empirical launch/held-rise tables, rise/fall branches, magnitude-scaled air steering | First anticipation update ordering, slopes, ceiling/edge interactions, equipment jumps | Host floor bits can abort anticipation or misclassify landing; jump/trace tests, movement smoke | Verified selected traces; tables and contact adaptation remain bounded |
| Ordinary support, slopes and ledges | 54/3600 down request, 25/3600 edge recurrence, f32 0.02 metric and 50-degree gate, directional slope projection | Full contact executor, descending seams, wall/step traversal and ledge grabs | Godot capsule dimensions, `MoveAndSlide`, six-slide cap and prospective-support envelope are host policy; contact/projection retail tests | Mixed verified request laws and host approximations |
| Moving supports and conveyors | Typed contact identities, anchor carry and separate conveyor channel | Live geometry coverage and conveyor producer integration | Adapter geometry may not match retail support; dynamic-support tests | Verified bounded semantics; incomplete integration |
| Locomotion animation / issue #151 | Offline action-2 helper and update order now drive a physics-clocked ordinary idle-entry session: looping 3/4, scalar-speed hysteresis, rate/phase mapping and default blend timing | Special entry history/guard writers, other-action handoffs, native joint blending; replay retail for exact cadence | Host support/scalar reconstruction and baked-vertex interpolation remain approximations. Smoke requires the native clock, sampled pose, steady-walk rate and frame wrapping | Bounded offline rules verified; full presentation parity remains incomplete |
| Idle, stop, crouch and jump animation | Clips and native sequence families decoded; jump 7/8 selection and direct return retained | Idle selector timing; exact stop 5/20/6 phase predicate; crouch controller state not projected by current semantic host | `PlayerHost.Animation` currently collapses crouch to visually neutral; selector and avatar tests | Partial; absent mappings must not be called parity |
| Wrench movement | `Rac1WrenchAttackSession` owns three-hit combo/Hyper/Comet motion and authored-time gates; throw has its own recurrence | Compare auto-targeting, alternate-axis actions and exact collision against live retail | Source contracts, attack/throw input smoke, ordinary Veldin play; see `RAC1_WRENCH_ACTIONS.md` | Native profiles/motion recovered; collision and several contexts remain bounded |
| Water, mud, ice, Magneboots | Surface identities and actions recovered; ordinary controller rejects unsupported motion | Alternate motion laws and transitions, swimming versus wading; dedicated progressed anchors | Reaching these surfaces currently throws instead of providing playable native motion; surface routing/reachability tests | Verified selectors; motion unknown/unsupported |
| Swingshot, Heli-Pack/Thruster-Pack, rail and other traversal | Native action vocabulary exists; no complete integrated movement controller established by this audit | Progressed save, equipment/entry-state verification, action-specific overlay and motion archaeology | Requires native action ownership and appropriate host contacts, not guessed generic traversal | Unknown/unsupported |
| Death and respawn | Veldin fall death and authored-start reset, motion/yaw reset | Broader checkpoint selection, interruption of special actions | Lifecycle/presentation reset ordering; lifecycle tests and Veldin play smoke | Verified bounded Veldin behavior |
| Ordinary camera | `Rac1OrdinaryCameraController`: type-0 manual damping, vertical follow/orbit, radius and bounded obstruction response | Profile triggers, vertical fallback writer, obstruction primitive and physical multi-contact convergence | Host ray is not proven native collision geometry; camera tests and live camera smoke | Verified recurrence stages; profile/contact approximation |
| Retail/Godot comparison | Existing per-frame PINE tools and deterministic C# traces | No unified first-divergence comparison with demonstrated capture validity | Existing capture assumes frame advance succeeded, fixes player Moby address to Veldin and lacks sufficient input/menu/death gates | Unsupported as general parity sign-off |

## Initial observations and priorities

1. The unchanged headless `rac1:LEVEL0 --movement-smoke` reproduces #151:
   `native-timed sustained low-stick locomotion ... current=3`. This is an
   observed failing assertion, not yet proof of a runtime selector defect.
2. Audit and strengthen experiment integrity before admitting new measurements:
   identity, active gameplay, accepted input, update cadence, player identity,
   support/death/control conditions, final action and repeatability. Invalid
   trials must not be reduced into movement laws.
3. Establish a fresh-game ordinary Veldin anchor and repeat neutral, long walk,
   full-input acceleration and walk-to-run; compare sequence, animation frame,
   displacement and facing. Preserve the original smoke failure until the
   acceptance conflict has an evidence-backed disposition.
4. Add aligned per-update comparison and first-divergence reporting, keeping
   invalid, unsupported and implementation failures distinct. Validate coordinate
   conventions, input transformation and camera update ordering first.
5. Work through jump/contact seams, animation mappings and wrench motion next.
   Then use verified progressed anchors for equipment and special surfaces.

## Evidence entry points

- [Offline gameplay-code access](RAC1_LEVEL_OVERLAY.md): Veldin's on-disc overlay
  now reproduces all 12 retained loaded movement signatures without PCSX2.
  [Ordinary locomotion animation](RAC1_LOCOMOTION_ANIMATION.md) traces the complete
  helper and resolves the timed-switch versus looping conflict in #151.
- [Movement](RAC1_PLAYER_MOVEMENT.md), including its later contact and acceptance notes.
- [Camera](RAC1_CAMERA_ARCHAEOLOGY.md).
- [Animation selection](RAC1_PLAYER_ANIMATION_STATES.md) and [avatar playback](RAC1_PLAYER_AVATAR.md).
- `generated/rac1-analogue-input-law.json`, `generated/rac1-ground-turn-response.json`,
  `generated/rac1-ratchet-movement-controller.json` and the contact/camera reductions.
- [Issue 151](https://github.com/je55pr/one-big-package/issues/151).

Raw experiments, savestates, saves, screenshots, local emulator profiles and
machine-specific paths stay under ignored local storage. This document retains
only derived observations and reproducible repository-relative instructions.

## Offline checkpoint, 2026-10-18

After the Windows capture failure blocked visual retail experiments, offline
work recovered Veldin's gameplay overlay directly from the verified ISO.
`rac1-level-overlay-probe.py` resolves seven serialized sections and matches all
12 retained movement signatures. The player frame-wrap path is now available for
further analysis, independently of the boot-ELF selector census. See the linked
overlay note for the bounded finding and remaining caller/timing questions.

Validation: all 75 Python tooling tests pass, the canonical retail-backed C#
suite passes 1083/1083 with zero skips, and `dotnet format` verification passes.
These checks validate the tooling and existing code/data contracts. They do not
resolve #151 or establish new live movement parity.

## Offline locomotion follow-up, 2026-10-18

The initial checkpoint above preceded caller recovery. The ordinary action-2
helper at `0x00228c18` now establishes guarded, hysteretic speed selection:
3 -> 4 above `2.35f * tickSeconds`, 4 -> 3 below `1.9f * tickSeconds`. It also
establishes rate multipliers, entry frame 8 for sequence 3, and directional
integer phase offsets. The ordinary selector clears the optional subrange loop;
the advance routine wraps the whole selected sequence.

The earlier smoke conflict therefore has an evidence-backed disposition. A
long low-stick walk must keep playing sequence 3. The non-looping presentation
flag is fixed, and the smoke now requires both frame advancement and a wrap
while retaining synchronized sequence 3. Full-input progression still requires
sequence 4. Neither behavior is inferred from a clip's nominal duration.

Only the loop flag is integrated into the host in this change. The new
`Rac1OrdinaryLocomotionAnimation` contract retains the recovered selector/rate/
phase arithmetic for subsequent native-facts integration. Current host timing,
entry-frame choice and semantic walk/run selection remain approximations. No
new retail gameplay trace was captured.

Validation of this follow-up:

- Python tooling: 77 passed.
- Portable C# suite: 979 passed, 122 retail-dependent tests skipped.
- Canonical retail-backed suite: 1101 passed, zero skipped. The added retail
  test independently maps the seven sections and checks the routine hash,
  thresholds, entry frames and phase offsets against the source bytes.
- The 18 animation-contract tests were rerun with the retail source after the
  final test-assertion formatting correction; all passed.
- `dotnet format OneBigPackage.sln --verify-no-changes --no-restore` and
  `git diff --check` passed.
- Godot host build: zero warnings/errors. Headless R&C1 movement smoke passed,
  including an explicit sequence-3 advancement/wrap check. Its crouch trial now
  resets to a settled spawn, preventing the preceding traversal's environmental
  restart from invalidating crouch admission. UYA's representative smoke passed.
- GC's representative smoke failed at the initial low-stick plateau assertion,
  before the modified checks; its log changes from Walk to Fall during that
  interval. This remains an unresolved smoke/setup or support issue. A baseline
  comparison was not run, so no new claim that the trilogy smoke is green is made.

Local logs are under ignored `captures/movement-offline-*.log`. Passing these
checks does not validate the unintegrated native blend/rate behavior or replace
a retail gameplay comparison.

## Physics-clock integration follow-up, 2026-10-18

The regular update block at `0x207184` advances animation before input/counters,
action selection and mode-0 movement execution. The integration now preserves
that order: advance using the previous multiplier, increment substate age,
select/rate from the pre-movement scalar (corrected to the updated controller
scalar by the later momentum audit below), then use that new multiplier on the
next update. `Rac1LocomotionAnimationClock` owns default looping, fraction snaps,
frame-rate carry, entry frame 8 and eight-update sequence blends. Rendering uses
an explicit sampled pose and cannot advance this clock.

The bounded host path starts from ordinary idle and remains active through
held-input walk/run and the short ordinary edge interval. Reset/respawn/fly
boundaries clear it. Release and other action entries retain existing semantic
fallbacks. The recovered entry block also has 12/15/18/13-tick history overrides;
the host does not yet carry those predicates. Baked-vertex interpolation is an
OBP visual approximation, not native joint blending or procedural pose work.
These limits prevent calling the complete presentation retail-accurate.

The first integrated smoke passed native walking-clock/rate/loop checks but
failed crouch after a development reset. The spawn ray admitted the player's
own collision body, and the smoke could inspect an old floor flag before node
physics callbacks completed. Placement now excludes the player's RID. The
smoke waits for three completed, supported updates before beginning a trial.
R&C1 resets now repeatedly resolve the same ground point; crouch and the
subsequent jump/fly/respawn checks pass. This is a host placement/test correction,
not a recovered retail collision rule.

Validation:

- Portable C#: 993 passed, 122 retail-dependent tests skipped.
- Canonical retail-backed C#: 1115 passed, zero skipped. Source checks now also
  pin update-call order and the frame-advance block hash.
- Python tooling: 77 passed. Overlay report regenerated from the full verified
  ISO, including update order and entry-history block hashes/predicates.
- Godot Debug build: zero warnings/errors. Formatter verification passed.
- R&C1 headless movement smoke passed; its steady-walk check requires a native
  clock sample, multiplier near 2.355, frame advancement and wrap.
- UYA `rac3:TABLE1` movement smoke passed after the shared placement change.
  GC `rac2:LEVEL1` still fails its initial low-stick plateau assertion, with
  Walk -> Fall in the interval. That unresolved failure was also observed at
  the preceding checkpoint; it is not treated as a passing trilogy regression.
- `git diff --check` passed. Changes remain on `dev`; no retail payloads were
  added to tracked output.

Local logs use ignored `captures/movement-clock-*.log`. No new PCSX2 gameplay
trial or visual retail comparison was performed.

## Offline crouch/stop follow-up

The [new recovery note](RAC1_CROUCH_AND_STOP_ANIMATION.md) records the action-4
entry, angular-velocity turn predicate, release age and preset stand-up curve.
OBP now presents sequences 13/14/15 with those frame/blend rules through the
same 60 Hz sampled-pose seam used for ordinary locomotion. Entering crouch
from walking, reversing a turn, repressing during stand-up, jumping and reset
have deterministic transition coverage. The default action-3 5/6 stop selector
is exposed and tested separately; complete release/restart is still pending.

Manual OBP checks requested (no emulator needed):

1. Open the desktop launcher, select R&C1 and Veldin, and use clear ground.
2. Hold C or the controller's right shoulder button. Ratchet should visibly
   crouch and remain crouched while held.
3. While held, turn with A/D or the left stick. Reverse direction, then release
   directional input. Check the feet/legs for a turn animation and settling.
4. Release crouch, then try short taps and repressing during stand-up. Check
   for sudden pose pops, twisted limbs, freezing or a stuck crouch.
5. Enter crouch from moving, resume movement, and jump after release. Check
   that the ordinary movement/jump animation resumes normally.

These are visual/interaction checks, not a retail-parity certification. Baked
vertex blending, the short-tap presentation/movement handoff, and unrecovered
procedural poses remain explicit limits. The earlier GC smoke failure remains
unresolved; this follow-up changes the R&C1 presentation path only.

Validation of the final follow-up:

- Portable C#: 1001 passed, 122 retail-dependent tests skipped.
- Canonical retail-backed C#: 1123 passed, zero skipped, including independent
  overlay mapping, instruction-block hashes and the stand-up curve values.
- Python tooling: 77 passed; the authority-verified overlay report regenerated
  successfully with crouch/stop blocks and constants.
- Godot Debug build: zero warnings/errors. Final R&C1 headless movement smoke
  passed crouch entry, turn reversal, settling and release as well as its
  existing walking, running, jumping, air-control, fly and respawn checks.
- Formatter verification and `git diff --check` passed.

Logs are local-only under ignored `captures/crouch-animation-*.log`. No new
PCSX2 trial or visual comparison was performed. Changes remain uncommitted on
`dev` for review and the manual OBP check above.

## Manual feedback: run-to-crouch coast

The user reported that the other crouch/turn/stand-up checks looked okay, but
full-run-to-crouch preserved substantial motion. They confirmed that the coast
stops within about one second. The new Veldin host trial measures 53 updates
and approximately 2.45 units, both while holding forward and after releasing
it; it checks monotonic deceleration and continued rest while crouch stays held.
Pure controller coverage also checks that turning does not redirect carry.

The verified action-4 entry/decay code supports gradual braking at about .0018
units/update; the existing measured .001802944 value remains unchanged. See
the [momentum audit](RAC1_CROUCH_AND_STOP_ANIMATION.md) for the cap, decay and
snap predicates and the distinction between native code and host observations.

Tracing the nested call also corrected an earlier update-order error:
`0x211670` calls the movement executor at `0x211728` before the action selector;
`0x216f10` is delayed-action timer processing. The host now passes the updated
controller scalar to walk/run presentation, removing a one-update delay.
No translational movement rule was changed by this correction.

Validation: portable C# 1003 passed / 122 retail-dependent skipped; full
retail-backed C# 1125 passed / zero skipped; Python tooling 77 passed; verified
overlay report regenerated; Godot Debug build zero warnings/errors; formatter
and diff checks passed. The expanded R&C1 movement smoke passed, reporting
53-update coasts of 2.4512 units with direction held and 2.4503 units after
release. Logs remain ignored under `captures/crouch-momentum-*.log`.

### Clarification: distance remains an issue

The user clarified that the concern is how far Ratchet slides, even though the
stopping time is acceptable. Their one-second answer must not be interpreted
as approval of the distance. Other crouch animation checks remain reported okay.
Current OBP travel is about 1.71 standing heights. The native entry/decay audit
has not established a matched contact-resolved retail distance. The tests now
record the travel instead of requiring the existing 2.45-unit result; the
motion, support and timing guards remain. Distance parity is an open issue.

Validation after this correction: portable suite 1003 passed / 122 skipped;
Godot Debug build zero warnings/errors; formatter and diff checks passed;
expanded R&C1 smoke passed, recording 1.7142 standing heights with forward held
and 1.7136 with direction released. These results validate the diagnostic path,
not retail distance parity. Local logs use `captures/crouch-distance-*.log`.

## Contact-derived crouch entry

[The follow-up](RAC1_CONTACT_MOTION_CARRY.md) recovered P+0x110 as a projection
of the ordinary request against primary contact displacement, preserving the
request's direction. It is not final P+0x100 displacement or the running scalar.
The R&C1 host now supplies completed static-contact feedback, and grounded
crouch entry copies/caps it once before the existing decay. Dynamic/external
transport is excluded from this adapter until its separate native pass can be
represented faithfully. GC/UYA do not supply the new feedback.

The expanded synthetic-wall smoke verifies that blocked running cannot leave
phantom full-speed carry to be released after crouch begins. Real Veldin travel
remains substantial (2.4512 and 2.2193 units in the two trials). This is a concrete
entry correction, not resolution of the complete distance-parity concern.

Validation: portable 1011 passed / 122 skipped; full retail 1133 passed / zero
skipped; Python tooling 77 passed; Debug game build zero warnings/errors; R&C1
movement smoke, formatter and diff checks passed. Source hashes/report regenerated.
Logs use ignored `captures/contact-carry-*.log`; no new PCSX2 trial was run.

## Surface braking follow-up

The user still reports excessive crouch travel and raised Hoven ice as a
possible missing material modifier. The [surface audit](RAC1_CROUCH_SURFACE_BRAKING.md)
recovers P+0x12e2 as a class-7 ice flag and verifies both Veldin and Hoven code:
action 4 uses `6.48f` by default or `1.7f` on ice, with the same squared-update
scale. This is a bounded ice-braking fact, not complete ice-action recovery.

OBP already uses the ordinary-ground rate. No additional floor-indexed
coefficient was found in this branch, so this result does not resolve the
distance concern or justify invented friction tuning. Native contact execution
and entry alignment remain open; unsupported special-surface action execution
is unchanged. Source/report checks are extended without changing motion values.

## Ordinary stop/restart presentation

At the user's request, crouch-slide distance is deferred. The next bounded pass
[recovers and integrates ordinary release/restart presentation](RAC1_STOP_AND_RESTART_ANIMATION.md):
previous-update capped input, the independent unclamped magnitude history,
contact-derived P+0x160, authored animation time, sequences 5/6/20, the -1
standing curve and interrupted-pose restart at sequence 3/frame 8.

The release path is admitted from completed static contact without external
transport. Other contacts/actions retain the existing fallback. Translational
release tables and braking remain unchanged. Native entry scalar seeding and
opposed-facing reset are identified but are not implemented by the presentation
controller; reverse-restart coverage establishes animation behavior only.

## Manual feedback: slow locomotion after jumping

The [jump-return correction](RAC1_LOCOMOTION_ANIMATION.md#jump-return-clock-handoff-correction)
fixes loss of the physics locomotion clock after an airborne semantic handoff.
Continued running previously stayed on the fallback's unscaled 30 FPS instead
of the recovered approximately 40 FPS full-run rate. Supported active movement
now reconnects its selected sequence/phase to the native-rate clock, including
walk/run hysteresis. This is an OBP handoff fix; native landing blends and
airborne presentation remain incomplete. Translational movement is unchanged.
