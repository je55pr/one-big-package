# R&C1 ordinary arsenal

This pass hosts all fifteen ordinary weapons, alongside the previously hosted
wrench. It is a functional reconstruction, not complete retail parity. Gold
upgrades and weapon vendors are not implemented by this pass.

The [presentation/audio follow-up](RAC1_WEAPON_PRESENTATION_AUDIO_FIXES.md) corrects
the interactive findings from the first pass, including detached-head poses,
idle firing, missing/wrong/repeated sounds, deployed scale and Doom floor turns.

## Source and provenance

Retail authority remains NTSC-U SCUS-97199 and the existing LEVEL0 overlay hash
`eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804`.

The public MIT-licensed
[rac1-native-game](https://github.com/daino-selvatico/rac1-native-game) revision
`3fd14c12e4dff2fd4b6661a6e54187faea3f76c8` supplies a function/address map and
reference implementations. Its Blaster, Taunter, Walloper, weapon registration,
hero state-entry code, generator specifications and generated local weapon
routines were inspected. New facts are checked against the authorized disc;
upstream labels and completeness claims are not automatically retail evidence.
Attribution and its MIT notice are in `THIRD_PARTY_NOTICES.md`.

The clone, original-machine-code listings and generated C++ remain in ignored
local research storage. Production remains C#; no C++ runtime, generated retail
translation, original executable, model, texture or capture is distributed.

## Native identities

Held identities and poses come from table `0x179ac0`, stride `0x4c`. Every new
compressed global model is hash-pinned in `Rac1WeaponProfiles`. Source decoding
retains bind transforms, weighted skins, local animation frames, attachment
points, materials and animation sound cues.

| Item | Weapon | Held class | Update / relevant source | Additional models |
| --- | --- | --- | --- | --- |
| 9 | Suck Cannon | 849 | `0x2da790` | actual captured victim |
| 10 | Bomb Glove | 192 | existing Bomb pass | 121 |
| 11 | Devastator | 157 | `0x2b2100`, rocket `0x2b03a0` | 153 |
| 13 | Visibomb Gun | 163 | `0x2b3058`, missile `0x2b6140` | 172 |
| 14 | Taunter | 175 | `0x2b6f10`, query `0x2b6bc8` | source particle art |
| 15 | Blaster | 168 | `0x2b49a8`, bolt `0x2cb418` | source particle art |
| 16 | Pyrocitor | 176 | `0x2b77f0` | source particle art |
| 17 | Mine Glove | 190 | `0x2c1ad0` | 74 |
| 18 | Walloper | 180 | `0x2bb558`, hero state `0x20` | source held model |
| 19 | Tesla Claw | 177 | `0x2b87e0`, beam `0x2b94d0` | beam renderer |
| 20 | Glove of Doom | 229 | `0x2c7a58` | 230, 186 |
| 21 | Morph-o-Ray | 185 | `0x2bc7e8` | chicken 270 |
| 23 | RYNO | 454 | `0x2cc608`, missile `0x2cd1f0` | 457 |
| 24 | Drone Device | 483 | `0x2cdcc0`, agent `0x2ce358` | 479 |
| 25 | Decoy Glove | 562 | `0x2d2670`, decoy `0x2c4380` | 203 |

Walloper attaches at semantic1 / Ratchet joint55, distinct from ordinary gun
semantic0 / joint56 and glove semantic6 / joint54. Its punch uses Ratchet
sequence103 (`hero_anim_set(0x67,4,-1)`), not wrench sequence43. The source punch
has seventeen frames at transition rate1/4; the hosted full-body pose uses that
clock. The lunge is18 units/s between ticks10 and18; the native sweep remains
active until tick22. Host hit volumes and collision resolution remain adapters.
Devastator54/73 and RYNO81 are also decoded. Static single-frame clips retain
finite host presentation durations without inventing native playback rates.

## Corrections and recovered rules

- Blaster: the former raw damage1 interpretation was incorrect: the apparent
  `1` word was another hit-record field. The raw shot descriptor carries
  damage0.25/flags`0x10001`. Shared reaction `0x25a478` replaces that damage for
  attacker classes168/305 using victim health-state kind and table `0x160080`:
  `1`, float bits`0x3f2ac083` (about0.667), `0.5`, `0.5`. The hosted toad uses
  kind0; the robot initializer sets kind1 at `0x2e0cb8`. Consequently the
  one-health toad takes one shot and the two-health robot takes three. Weak
  bullet reaction11 retains repeated-hit damage during the reaction timer;
  the robot's blanket cooldown must not discard those shots. The transported
  event retains its raw descriptor. Damage follows swept travel rather than a
  fire-time hit. Speed40, lifetime35 and cadence6 remain. Steering uses the
  selected aim point; its rate integrator remains an OBP approximation. Gold
  bounce behavior is not admitted.
- Pyrocitor: ordinary ammo uses the global ten-tick phase, including after
  weapon switching. Previously the interval restarted with the first shot.
- Devastator: native fire recovery35 (`0x2b2d48`), rocket class153, lifetime300,
  initial speed20 and ordinary maximum70. Blast damage3/flags`0x830000` is the
  ordinary `0x2b0e60..0x2b0e90` list-hit envelope.
- Visibomb: native class172 and lifetime3000; ordinary blast radius4,
  damage6/flags`0x830000` at `0x2b69ec..0x2b6aa4`. The host routes steering
  to the missile, holds Ratchet stationary, and restores the player camera on
  impact, cancel, death, weapon change or world reset.
- RYNO: seven missiles (`0x2cccf0`), nine ticks apart (`0x2ccb90`), then
  sixty recovery ticks (`0x2ccd20`). A salvo spends one ammo. The native
  barrel index wraps modulo9; all nine animated source socket tracks are
  decoded and used. The source missile is457, initially speed8, maximum20,
  lifetime250. Host target distribution and accelerated homing remain bounded
  adapters. The source contact record is damage3/flags`0x830000`.
- Drone Device: one use fills the six source agent slots. A full formation
  spends no ammo. Ordinary contact damage1/flags`0x10000` comes from
  `0x2ceb04..0x2ceb1c`; agents are consumed by interception. Orbit geometry,
  target discovery, interception and splash presentation remain host policy.
- Decoy: native recovery20 (`0x2d29c4`), class203, initial relative scale0.5
  and growth0.02/update. On landing it plays source inflation1, then inflated
  idle2, and stays above support as the mesh changes height. The grounded
  timer600 is at `0x2c4a78`. Enemy retargeting, health and population limits
  remain explicit host adapters.
- Tesla: ordinary ammo interval10 (`0x2b897c`), range20, and the beam query's
  damage2/flags`0x210000` (`0x2b99bc..0x2b99d8`). It selects up to two
  visible enemies. Exact dual charge timers, individual victim filtering,
  beam segments and full native effects remain incomplete.
- Taunter: native reach30 and facing half-angle bits`0x3f75be0b`. Ordinary
  crate classes500/501/505/511 are also hit within12. It marks nearby mines;
  their hosted trigger range expands threefold. Sound-step scheduling, ring
  rendering, alert ownership and the common target-list implementation remain
  adapters.
- Morph-o-Ray: uses actual chicken270 and source animation; the victim stops
  hostile AI without being killed or rewarded. It has a small collider and
  remains damageable through its retained identity. The exposure/decay model,
  roaming motion and victim eligibility remain host approximations. Bookmarks
  retain transformed identities; they do not snapshot chicken navigation.

## Explicit remaining boundaries

`Rac1ArsenalHostPolicy` and `Rac1WeaponReconstructionPolicy` retain provisional
choices. Godot supplies world queries, contacts and presentation; source rules
and native facts stay in `OBP.RAC1`. The native R5900 oracle was not built or run
for this integration. Instruction/asset checks do not prove a whole weapon's
state machine, floating-point arithmetic or rendered result identical to retail.

All explosive weapons currently reuse the Bomb pass's decoded burst/debris art
and renderer. They do not yet reproduce every separate native particle recipe,
pool, camera shake, water response or 989snd program. Beams, ribbons, lock-on,
guided camera and target selection need further matched retail comparisons.
Mine/Doom flight and navigation and the Suck Cannon victim callbacks retain the
boundaries in `RAC1_ADDITIONAL_WEAPONS.md`. Gun arm layers now use source joint
lists and channel composition before skinning; interrupted base-pose handoffs
retain a host sampling boundary. Wrench and Bomb retain their earlier native
controllers. New enemy interactions are tested against the hosted Veldin classes;
the rest of the campaign's enemies are outside the current enemy implementation.

Preview acquisition/refill is session-only. Normal source inventory and ammo
accounting remain separate from campaign acquisition. Bookmark reload clears
transient missiles, mines, canisters, drones, decoys, beams and salvos; it retains
captured identities and active Morph transformations. Full native PVars are not
serialized, and global Gold upgrades are not emulated.

Transformed victims can be captured as chickens, including a transformed robot.
The captured appearance is retained through bookmark reload separately from the
original victim identity; it does not revert to a frog/robot on release.

## Verification

`Rac1ArsenalTests` covers source table and instruction witnesses, all decoded
held/deployed models, salvo/barrel order, blocked-use ammo, cooldowns across
taps/switches, non-ammo equipment, the global flame phase and projectile bounds.
The Godot weapon fixture exercises every weapon's primary behavior, contacts,
camera return, inflation, transformation and world-state boundaries in a synthetic
scene. Blaster coverage includes the actual toad and robot consumers, shared
damage-kind override and repeated bullet contacts. Rendered evidence is private;
this is not an interactive retail replay.

The initial full Release and retail-authority suites each passed 1,441 tests with
zero skips. Solution formatting verification and the Debug host build pass.
The complete weapon fixture passes headless and rendered, and the existing
Veldin enemy regression passes after the shared Blaster reaction correction.
The 20 October presentation/audio follow-up passes1,448 tests in both suites and
adds the idle, sound lifecycle, deployed-scale and floor-contact checks described
in its linked evidence note.

The ordinary Veldin route also exposed an outdated diagnostic assumption: its
approach helper stopped at the old generous planar wrench cone and expected the
new animated hand/tip sweep to reach the same extent. It now walks closer using
ordinary input before asserting actual contact. Production wrench reach is not
enlarged to satisfy that diagnostic.
