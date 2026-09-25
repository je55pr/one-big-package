# Going Commando Aranos opening gameplay

**Target:** `rac2:LEVEL0` — Aranos opening slice
**Authority:** `rac2-ntscu-v1.01` / `SCUS-97268`
**Retail ISO SHA-256:** `9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5`

This note records only evidence relevant to making the ordinary LEVEL0 opening
playable. It does not assign gameplay identities to unresolved classes.

## Baseline audit

The existing GC provider already reconstructs Aranos through the ordinary
`GcWorldImport` path:

- unchunked tfrag terrain and decoded textures;
- TIE and shrub classes plus authored placements;
- octree collision from the level core;
- level atmosphere/death height and the settings ship tuple;
- authored Moby geometry/placement in the static render path;
- class-500 Mobies as individually preserved `RuntimeDynamicObject` values.

The LEVEL0 retail baseline currently contains 43 authored class-500 instances.
Before this goal, class 500 was the only GC class lifted into the per-instance
gameplay path; other Aranos Mobies remained welded/static except for the existing
bounded animation preview.

The old GC crate interaction host was also a development harness: primary input
fed a synthetic `flags=1, hp=1` event into the recovered class-500 predicate.
It did not transport the retail player damage tuple.

## Authored player start

LEVEL0 level settings contain ship position `(20,20,20)` and yaw `0`.
That tuple is also present on several special/non-planet retail levels and is not
Ratchet's Aranos opening placement.

Across all 27 retail GC level files, gameplay data contains exactly one
`oClass == 0` Moby and it is always authored instance index 0. On LEVEL0:

- instance index: `0`
- UID: `0`
- native position: `(247, 194, 49.89)`
- native Z yaw: `pi/2`

The importer therefore now projects that unique class-0 placement to
`RuntimeWorld.PlayerStart` as OBP Y-up `(247, 49.89, 194)`, yaw `pi/2`.
The settings tuple remains separately preserved as `RuntimeWorld.Ship`.
`RuntimeWorld.PreferredPlayerStart` consequently selects the authored class-0
entry without erasing ship metadata.

Pinned public Wrench data independently labels GC class 0 as Ratchet. That is
corroboration only; the production rule is established from the all-level retail
placement invariant and is covered by authority tests.

## Aranos class-500 crate family

LEVEL0's loadable overlay has the normal GC section family, including
`lvl.vtbl` at `0x002A2E80` and `.text` at `0x002A3B80`.
The Aranos vtable resolves classes 500, 501 and 511 to the same loaded update:

`0x0039B440`

The class-500 update independently reproduces the previously recovered crate
contract on Aranos itself:

- Moby `+0x68` supplies the PVar pointer;
- Moby `+0x20` dispatches seven states `0..6`;
- the event query constructs mask `0x05830001` and calls loaded helper
  `0x0031CA70`;
- returned event record `+0x24` is rejected when exactly `0x01000000`;
- state 1 reads record `+0x2C` and admits the break only when it is positive;
- the class-family code tests the expected 500/501/502/505/511/512 numeric ids;
- the recovered break helper later writes native state 3 and timer 15.

All 43 authored Aranos class-500 PVars are `0x110` bytes and the retail census
has `PVar+C8 == 0` and `PVar+CC == 0` for every instance. Sampled authored bolt
fields are 58.

The host now publishes the exact recovered GC player state-20 tuple through
`GcDamageTransportSession` and lets RAC2 class-local logic decide the class-500
consequence. The old synthetic `flags=1/hp=1` ordinary strike is gone. Optional
crate-focus tooling remains a development targeting aid and is not required for
normal aim-based acquisition.

The existing physical Bolt payout presentation is still explicitly incomplete:
selector/progression/RNG session inputs are deterministic host choices within
recovered native domains, and the visible pickups are placeholder orbs. Do not
cite that presentation as recovered native scatter/magnet/effect behaviour.

## Opening authored population

Distances below are from the authored class-0 player start and are used only to
prioritize archaeology, not to infer identity:

| oClass | authored count | nearest distance | PVar bytes | mesh tris | sequences |
|---:|---:|---:|---:|---:|---:|
| 2759 | 3 | 7.52 | 32 | 500 | 0 |
| 2753 | 1 | 8.94 | 192 | 961 | 0 |
| 2779 | 9 | 31.60 | 48 | 1296 | 0 |
| 2612 | 18 | 40.01 | 0 | 146 | 1 |
| 2755 | 10 | 52.92 | 0 | 120 | 4 |
| 2549 | 9 | 53.73 | 0 | 296 | 1 |
| 500 | 43 | 53.73 | 272 | 113 | 0 |
| 2827 | 31 | 66.48 | 1584 | 2761 | 28 |
| 511 | 10 | 79.43 | 272 | 113 | 0 |
| 4858 | 18 | 80.82 | 16 | 660 | 0 |
| 2826 | 14 | 93.79 | 1232 | 2988 | 29 |
| 2460 | 13 | 137.40 | 912 | 1133 | 6 |
| 2754 | 1 | 144.00 | 176 | 1417 | 3 |

Later Insomniac-family public class tables attach suggestive names to several of
these numbers. Those names are not GC authority and are retained only as search
leads. Production identities remain unknown until matched to GC retail behaviour.

LEVEL0 `lvl.vtbl` provides bounded update addresses for the next archaeology
slice, including 2754 `0x003CE278`, 2755 `0x003CEC50`, 2826
`0x003D54B0`, and 2827 `0x003D7BD0`.

Class 2755 is now admitted narrowly for opening instance 166, the first
double-door immediately beyond the recovered opening lift. The LEVEL0 handler at
`0x003CEC50` is small and complete enough to reproduce this instance's route
behavior without assigning a broader family identity:

- native state 0 computes 3D distance from Moby `+0x10` to Ratchet and enters
  state 1 only for `distance < 6.0` (strict);
- state 1 selects native sequence 1 and waits for Moby `+0x60 & 2`, the
  animation-complete condition;
- sequence 1 has 30 authored frames at speed 0.5, so the retail 60 Hz state
  clock reaches completion after 60 native ticks;
- completion enters state 2 and selects sequence 2, a one-frame latched-open
  pose;
- the class model is 7-joint / 120 triangles. Both LEVEL0 class-2755 instances
  remain preserved as dynamic render objects, but only authored instance 166 is
  bound to this opening-door behavior.

The browser bakes sequence 1 plus the final sequence-2 pose into a 31-frame,
30 fps one-shot dynamic-object clip. This is a presentation projection of the
retail sequence clock, not a generic dynamic-Moby animation claim.

## First opening hostile: class 2827 / MSR I

LEVEL0 class 2827 is now promoted from static render soup to 31 authored dynamic
instances. The identification is supported by converging GC evidence: the two
nearest instances occupy the first enemy room after the opening elevator/door,
the family has a medium humanoid/spider-legged model with 28 animation sequences,
and its loaded update is the first route-local full hostile state machine.
Contemporary walkthroughs independently describe that room as the first MSR I
chainsaw encounter; that external name is corroboration, not the runtime source.

Retail class-2827 authority on Aranos:

- `lvl.vtbl` update: `0x003D7BD0`;
- PVar size: `0x630` for all 31 authored instances;
- initial HP at PVar `+0x20`: `2.0` for every instance;
- initial hit cooldown at PVar `+0x26`: `0`;
- authored Bolt field: `43`;
- Moby state `+0x20` dispatches 14 active states (`1..14`);
- damage service query mask: `0x00010000`;
- accepted damage subtracts record `+0x2C` from PVar `+0x20`;
- an accepted hit writes a 15-tick cooldown to PVar `+0x26`;
- health `<= 0` selects the native death/terminal animation path.

The 14-state jump table is now pinned directly from LEVEL0. In particular,
state 12 resolves to `0x003D90A0`, state 13 to `0x003D92FC`, and state 14 to
`0x003D95EC`. The first four route-local MSR I instances are authored with
PVar `+0x27C == 1`; the full family census is 11 mode-1 and 20 mode-0 instances.

GC helper `0x0031A658` is the native Moby state-transition primitive, not an
animation helper. It writes the new state to Moby `+0x20`, preserves the old
state at `+0x94`, resets the 16-bit state timer at `+0x96`, and replaces
transition mode `+0x95` unless the caller passes `-1`. The common update
saturates that timer at `0xFFFF`. OBP now preserves this bookkeeping through
`GcNativeStateSession` instead of parallel host-only state flags.

For authored mode-1 class-2827 instances, LEVEL0 initialization calls the helper
with state `3` and transition mode `2`. Transition mode 2 is a separate
pre-dispatch combat/damage lane: it services target/damage bookkeeping, queries
mask `0x00010000`, applies the 15-tick hit cooldown, and on lethal HP calls the
same transition helper with native state `15` and transition mode `5`.
It is therefore not evidence for a fabricated proximity aggro radius.

State 12 provides a bounded attack-admission contract: it measures distance to
the current target, requires `< 2.8`, computes wrapped absolute yaw error through
helper `0x002EF1E0`, and requires that error below `0x3E567751` radians
(~12 degrees) before selecting the state-13 attack path. State 13 accepts native
sequences `0x1B` and `0x10`; only animation frames 19 through 25 emit contact
volumes. The emitted joint/radius pairs are `(0,0.35)`, `(1,0.15)`, `(2,0.15)`,
and `(9,0.35)`. PVar byte `+0x34` is authored as `1` on all 31 LEVEL0
instances. Initialization at `0x003D99EC..0x003D9A24` converts that byte to
float and stores it at PVar `+0x2CC`; state 13 passes that runtime float into
all four contact registrations. The earlier provisional "extent" label is
therefore retired: this is the class-local attack damage scalar.

The player-side handoff is also now recovered from the exact v1.01 EE image.
Opening player state stores current Nanotech at `0x0018C2EC`; the preserved
opening state is `4`, with the adjacent maximum value also `4`. Player damage
handling at `0x002B2F2C` queries Ratchet's Moby through shared helper
`0x0031CA70` with mask `1`. When a record exists, record `+0x2C` is rounded
with `cvt.w.s` and passed to `0x002A4EF8`, which subtracts the integer amount
from Nanotech and clamps below zero to zero. The class-2827 authored scalar of
`1` therefore produces one Nanotech of opening MSR I contact damage. OBP now
carries this bounded consequence through `GcRatchetNanotechSession`; repeated
contact suppression remains deliberately outside that session until the shared
collision/reaction path is recovered.

The recovered player state-20 tuple also uses damage mask `0x00010000` and deals
`2.0` HP, so one admitted state-20 hit exactly exhausts an authored MSR I. OBP
now carries this through `GcClass2827HostileSession`; ordinary GC primary aim can
select class 2827, publish the recovered damage tuple, and terminalise the
instance. Attack admission, contact geometry, and the player Nanotech
consequence are recovered. Authored approach/root-motion execution and actual
host-side contact overlap admission remain to be wired.

Earlier live-retail attempts with the preserved GC PCSX2 profiles stopped at a
memory-card warning and were not used as gameplay evidence. Generation 5 repaired
that witness path without bypassing MjauRunner ownership. Goal-owned run
`run-000018` loaded the preserved v1.01 Aranos opening state; after dismissing
the game's autosave/card warning, ordinary input visibly moved Ratchet from the
authored class-0 opening position.

A separate controlled PINE probe on that same owned retail run was used only for
archaeology, not ordinary-route proof. Ratchet's authoritative player-state
position and Moby transform were placed beside authored class-2827 instance 205
at native `(245.42667, 230.9, 104)`. The synchronized capture visibly shows the
opening MSR I pair surrounding/striking Ratchet. Current Nanotech at
`0x0018C2EC` then changed `4 -> 3 -> 2 -> 1 -> 0`; each nonlethal decrement
entered player state `22` and returned to state `0`, while the lethal
decrement entered state `22` before reaching state `57`. The observed sample
timestamps are not promoted to a hit-stun or repeat-hit timing contract. The
contact sheet is retained at
`runtime/mjaurunner/logs/run-000018/captures/20260925T065334389669Z-contact.png`.

Generation 6 then measured the missing approach loop with two further
MjauRunner-owned retail witnesses. In `run-000023`, Ratchet was placed about
9.5 native units from opening instances 205/206, solely as a controlled
archaeology probe. The synchronized contact sheet visibly shows both blue MSR I
moving down the corridor, surrounding Ratchet, and attacking:
`runtime/mjaurunner/logs/run-000023/captures/20260925T082203287500Z-contact.png`.
The live Mobies are `0x01968D00` / `0x01968E00`, with PVars
`0x019BB3C0` / `0x019BB9F0`. Both begin in native state 3 targeting Ratchet.
The observed engagement chain is `3 -> 5 -> 4 -> 8 -> 12`; state 12 translates
and steers toward Ratchet, state 13 holds position for the chainsaw attack,
state 14 recovers, then the controller returns to state 12.

State-12 planar displacement ramps by approximately `0.005` native units per
60 Hz retail tick to a `0.1`-unit/tick cap, then remains capped while chasing.
The broad shared physics routine below `0x0032B9F0` owns the native Moby
physics implementation; reproducing that subsystem wholesale is outside this
opening-gameplay goal. OBP therefore projects only the measured source-tick
ramp/cap into `GcClass2827ApproachSession`, while retaining the separately
recovered 2.8-unit state-12 attack threshold.

`run-000024` corrected the animation-byte sampling. Chase uses sequence 11.
The leading instance's first state-13 attack uses sequence 16; subsequent
attacks and the second instance use sequence 27; state-14 recovery uses sequence
17. Retail Nanotech decrements occurred at state-13 timer 44-45. The observed
first sequence-16 attack lasted about 100 native ticks, sequence-27 attack
cycles about 114 ticks, and recovery about 90 ticks. These durations are kept
as opening-pair witness projections, not claimed as universal GC animation
laws.

State 13 submits four joint volumes through shared helper `0x0031C050`; that
wrapper builds contact geometry and feeds common collision accumulation through
`0x002E10E8`. Class 2827 never receives a per-volume acceptance result, while
Ratchet later consumes one mask-1 damage record. OBP mirrors that boundary with
`GcClass2827AttackCycleSession`: any number of overlapping joints from one
attack cycle collapse to at most one player damage record. This retires the old
per-joint/repeat-hit uncertainty without inventing a generic invulnerability
timer.

The browser host projection is deliberately limited to authored opening
instances 205/206. Their near-player activation and stagger/settle staging are
bounded by the direct opening-room witness above; the 10-unit host gate is not a
recovered global aggro-radius claim. The other 29 class-2827 instances remain
without fabricated autonomous AI until their route/activation conditions are
recovered.

## Current playable boundary

Working now:

- normal GC provider loading, geometry and collision on Aranos;
- authored class-0 player entry instead of the settings sentinel;
- 43 authored class-500 objects with preserved identity, placement and PVars;
- GC-local sequenced event/damage transport;
- ordinary non-RAC1 primary input can target authored class-500 crates or the
  opening class-2827 MSR I family and feed the recovered player state-20 tuple;
- the recovered Aranos class-500 break predicate and zero-C8 lifetime projection;
- all 31 authored MSR I instances preserved individually with retail HP/cooldown;
- recovered MSR I damage admission and lethal terminalisation;
- recovered state-12 attack admission plus state-13 contact timing/geometry;
- recovered GC player Nanotech damage path for opening MSR I contact, including
  one-Nanotech damage, state 22 hit reaction, and state 57 lethal handoff;
- opening instances 205/206 now have a source-tick browser projection of the
  witnessed approach/attack/recovery loop, using recovered attack distance and
  measured state-12 acceleration/cap;
- opening MSR I joint contacts are collapsed at the shared-collision boundary to
  at most one player damage record per native attack cycle;
- opening class-2755 instance 166 now uses the retail strict-6-unit proximity
  trigger, 60-tick sequence-1 opening clock, and sequence-2 latched-open pose.

Still required for the goal:

- recover route/activation conditions for later MSR I instances rather than
  generalising the opening-pair witness gate;
- establish any additional opening trigger/gate/checkpoint behavior beyond the
  first class-2755 door that still blocks the ordinary route;
- replace or further bound the showcase Bolt reward assumptions where needed;
- add an honest ordinary-route Aranos gameplay smoke;
- complete the human-playable Jess signoff and document failures verbatim.

The next smallest slice is an ordinary browser smoke through the recovered lift
and class-2755 door to the now-live 205/206 encounter without teleporting. Any
additional blocker encountered on that route should then be recovered from its
own LEVEL0 authority. Later class-2827 activation can be widened only where route
evidence supports it; the opening witness must not become a fabricated global
aggro rule.
