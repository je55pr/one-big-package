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

Class 2755's small loaded update is already structurally informative: state 0
checks a helper result against 6.0 before entering state 1; state 1 checks Moby
byte `+0x60` bit 1 before entering state 2. That is not yet enough evidence to
call the class an enemy or to reproduce its behaviour.

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
and `(9,0.35)`. PVar byte `+0x34` is converted to float and passed as the second
contact-volume scalar; it is authored as `1` on all 31 LEVEL0 instances. That
scalar is therefore preserved as an attack-contact extent, not labelled as
Nanotech damage until the downstream collision/damage helper proves that meaning.

The recovered player state-20 tuple also uses damage mask `0x00010000` and deals
`2.0` HP, so one admitted state-20 hit exactly exhausts an authored MSR I. OBP
now carries this through `GcClass2827HostileSession`; ordinary GC primary aim can
select class 2827, publish the recovered damage tuple, and terminalise the
instance. Attack admission and contact geometry are recovered, while authored
approach/root-motion execution and the resulting player Nanotech damage handoff
remain to be wired.

A live-retail visual attempt was made with the preserved GC PCSX2 profiles under
goal-owned MjauRunner runs. Both the normal and no-card profiles visibly stopped
at a `Memory Card Read Failed` dialog, so no gameplay or visual claim was taken
from those runs.

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
- recovered state-12 attack admission plus state-13 contact timing/geometry.

Still required for the goal:

- recover MSR I activation and approach/root-motion execution;
- recover the downstream contact-to-player damage/Nanotech semantics;
- establish any opening door/trigger/gate/checkpoint behaviour that blocks the
  ordinary route;
- replace or further bound the showcase Bolt reward assumptions where needed;
- add an honest ordinary-route Aranos gameplay smoke;
- complete the human-playable Jess signoff and document failures verbatim.

The next smallest slice is retail recovery of class 2827's activation and
approach/root-motion path, followed by the downstream contact-to-player Nanotech
handoff. The attack admission/contact-volume portion no longer needs guessing.
