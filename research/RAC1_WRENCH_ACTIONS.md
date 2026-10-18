# R&C1 wrench actions and animated attachment

Recovered 18 October 2026 from the authorized NTSC-U original source. This
supersedes the timing, damage and socket uncertainties in the earlier
`RAC1_WRENCH_COMBAT.md` investigation. It does not claim complete combat parity.

## Source chain

The verified LEVEL0 overlay SHA-256 is
`eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804`.
Its seven serialized sections must be mapped independently. Native 60-Hz timing
globals at `0x15ed6c/70/74` precede the overlay and belong to the boot executable;
they are dt, dt squared and dt cubed respectively.

`Rac1WrenchNativeSourceTests` checks function-block hashes, dispatch entries,
item/socket selectors and every field of the first five attack rows directly
against those local retail bytes. It retains no executable payload.

| Concern | Source address / result |
|---|---|
| Input and chain admission | `0x228068..0x2284a8`; fresh Square edges and bounded press ages |
| Entry | `0x225bdc`; actions 0x13/14/15 share the domain-6 entry dispatcher |
| Update/recovery | `0x22b168`; authored-time chain, jump and finish gates |
| Motion | `0x21a92c` Hyper, `0x21ae00` Comet, `0x21b1d0` Multi-Strike |
| Wrench Moby | class71, update `0x2a84c8`, owner pointer +0xb8 |
| Hit gate | `0x2a883c..0x2a8880`; no player preset blend, inclusive row hit interval |
| Contact/damage | `0x2a8d98` descriptor construction, `0x259d08` four-point contact helper |
| Release | `0x217658..0x217958`, threshold crossing helper `0x211870`, authored time 33 |
| Flight | `0x2a9134..0x2a9404`; class71 states10/11, catch at the animated hand |
| Bolt attraction | `0x2a82c0..0x2a8418`; distance below 2.7, followed by bolt-homing helper |
| Body loop | `0x2325e0/0x232864/0x2328c8`; loop6..21 and recovery frame26 |

The missing bridge in the earlier investigation was the separately scheduled
class71 update. Player action19 owns movement; the wrench object owns contacts.
`0x257880` selects a Moby animation. It is not an audio call.

## Profiles and animation

Rows at `0x17bc28`, stride44 bytes, contain eleven signed words: family, combo
index, chain-before, buffer reference, chain-from, jump-from, finish-after,
hit-from, hit-through, turn-from and turn-through. All times below are authored
animation coordinates, not elapsed fixed-update counts.

| Profile | Attack | Body / own wrench sequence | Entry frame / blend updates | Hit interval | Chain from / before | Finish after |
|---|---|---|---|---|---|---|
| 0 | first swing | 23 / 3 | 0 / 5 | 17..23 | 26 / 33 | 31 |
| 1 | second swing | 24 / 4 | 1 / 7 | 8..13 | 12 / 25 | 23 |
| 2 | third swing | 25 / 5 | 2 / 7 | 12..16 | 23 / 0 | 21 |
| 3 | Comet-Strike | 26 / 6 | 0 / 10 | separate flying object | 88 / 99 | 91 |
| 4 | Hyper-Strike | 43 / 10 | 4 / 11 | 1..28 | 28 / 99 | 30 |

The ordinary chain advances modulo three only within the previous row's
chain-before gate. Held Square does not repeatedly manufacture press edges.
Press age must be below clamp(2*(floor(time)-bufferReference),1,15).
Ground admission uses a seven-update press buffer. Air admission requires age
greater than10, uses ten updates (eighteen for eligible flip/boost), and retains
the native action/time gates. Clearance selects Hyper above .1 before the apex,
or .57 after the apex/in glide. Pack stretch/high actions are not silently added
to the recovered air allowlist.

Ordinary damage is one, Hyper damage two, with flags `0x10000`. The class749
consumer subtracts without clamping at `0x2d5288/0x2d52a4`; its bounded one-health
victim therefore becomes -1 under Hyper and enters the same lethal state12.
The live transport identifies the separate class71 player-equipment emitter and
Ratchet owner. Its equipment id8 is a logical host identity, not an authored
Moby-instance index. The older synthetic contact API retains its player stimulus.

## Motion and flight

For ordinary axis movement, profile0 targets 4.4*dt while authored time <18.
Profile1 targets 5.7/4.5/3*dt around times7/8/9 and its blend gate. Profile2
targets 3.7*dt while blending or time<12. Approach is 37*dt² up and 28*dt² down.
This replaces the old incorrect interpretation of eighteen elapsed lunge ticks.

Hyper brakes planar speed by 15*dt². Before age9 it approaches the height-gated
7.7*dt rise at 150*dt²; after age14 it descends at 130*dt², terminal -37*dt.
Playback is .33 before descent, then one, or two during supported recovery before
authored26. Comet brakes planar speed at 28*dt² and releases after authored33.

Outbound flight starts at 23*dt per update. Position first advances by the old
step; acceleration increases by 170*dt³ and reduces the step toward zero.
Return retains that acceleration, increases it by .9*dt² each update and adds it
to the step, travelling toward the current animated hand. Contact halves the
outbound step and selects return. Spin advances by 19.314491*dt.

## Animated socket

Item8's +0xc socket selector is zero and +0x10 class is71. `0x209240` queries
semantic joints [0,1,2,3,4,5,6,29,30] through `0x24f8f0/0x253fa0` into P+0xac0.
The bounded class+0x1c semantic0 chain has twelve ancestor bytes and ends at
Ratchet joint56. `0x20f6f8` applies that socket matrix and P+0xaf0 translation to
the held wrench. P+0x1070 retains the animated catch point.

OBP evaluates the recovered sparse hierarchy and publishes that hand transform
with every baked body frame. The wrench's twelve-joint skeleton runs its own
sequences under the hand transform; semantic1 ends at wrench joint5, the tip.
Current meshes, hand/tip contact points and throw catch use the same fixed update.
Interrupted baked-pose blends carry the corresponding attachment transforms.

The neutral class71 sequence remains1. Its mesh binding must retain the stored
inverse-bind translation (skeleton record +0x30/+0x34/+0x38). Applying Ratchet's
separate common-transform bind reconstruction cancels the wrench's mechanical
shaft/jaw retraction and leaves the wide bind shape visible. The corrected local
hierarchy produces a neutral head width of about .45074 instead of the bind
mesh's 1.01027 units, and total length about .85206. No model, animation selector
or arbitrary scale substitution is used. Synthetic binding and retail dimension
regressions cover this distinction.

The run-stop clock must also publish hand metadata with its interpolated body
vertices. `PublishStopPose` now uses the common pose sampler, retaining the hand
through the stop clip, its interrupted blends and its subsequent standing loop.
The earlier manual vertex-only interpolation left no metadata for that pose,
causing the view to remain at its last attached location. Retail and Godot
run/stop/idle checks now compare the rendered attachment on every update.

## Explicit host boundaries

- Target support remains class500 centre10 crates and the recovered class749
  population. Other enemies, switches and world interactions need their own consumers.
- Hand/tip sweep inputs, damage and hit intervals are native. Victim narrow phase
  uses rendered mesh bounds with a .05 host margin; the flying radius is native .4.
  World obstruction uses a host ray. Native query membership, segment extensions,
  detailed collision volumes and shared hit-record cooldowns are incomplete.
- Per-target hits are coalesced once per attack/flight as host policy.
- The native bolt radius is used, but collection uses the existing immediate
  host pickup adapter rather than the randomized native homing path.
- Auto-target yaw/gain, alternate-axis profile rows, first-person aimed throws,
  the Hyper projected-ground extra query and near-ground playback adjustment,
  context-specific entry blends and exact catch-loop queue delay remain incomplete.
- Host ledge, tether and surface actions keep priority over unrecovered wrench
  interrupts. Their queued Square input is discarded rather than replayed later.
- Affine/vertex blends are the existing baked-pose presentation approximation.
  The exact hand/socket selection and model identity are recovered.
- Wrench SFX, trails, impact effects and bolt meshes are not reconstructed here.

Validation: portable/source tests, wrench Godot input/contact smoke and separate
ordinary Veldin play. The wrench smoke labels its staged contact fixtures and
does not claim fresh matched PCSX2 captures. See `docs/RAC1_WRENCH_TESTING.md`.
