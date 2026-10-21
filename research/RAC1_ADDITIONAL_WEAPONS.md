# R&C1 Blaster, Pyrocitor, Mine Glove, Glove of Doom and Suck Cannon

Follow-up: [ordinary arsenal](RAC1_ARSENAL.md) adds the remaining weapons and
corrects the earlier Blaster damage/query interpretation below. Its shot now
uses swept travel with raw damage0.25/flags`0x10001`; the shared enemy reaction
replaces that damage by victim kind (1 for the Veldin toad, about0.667 for the
robot). It is not a damage1 hitscan.
Pyrocitor ammo also now follows the global ten-tick phase across equip changes.
The later [presentation/audio fix](RAC1_WEAPON_PRESENTATION_AUDIO_FIXES.md) replaces
the former torso vertex cut, fixes idle/use ownership and interprets the admitted
sound programs with explicit loop start/stop.

Authority: original NTSC-U SCUS-97199; LEVEL0 executable overlay SHA-256
`eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804`.
The authorized disc remains local. This note and tests retain identities,
addresses, scalar facts and hashes, without retail payloads.

## Equipment and assets

The source table is `0x179ac0`, stride `0x4c`. Fields `+0xc`, `+0x10` and
`+0x24/+0x28/+0x2c` bind hand semantic, class and Ratchet pose selectors.

| Equipment | Item | Class / update | Hand semantic / Ratchet joint | Body pose |
| --- | --- | --- | --- | --- |
| Pyrocitor | 16 | 176 / `0x2b77f0` | 0 / 56 | 51 |
| Blaster | 15 | 168 / `0x2b49a8` | 0 / 56 | 56, 57, 62 |
| Mine Glove | 17 | 190 / `0x2c1ad0` | 6 / 54 | 44, 66, 44 |
| Glove of Doom | 20 | 229 / `0x2c7a58` | 6 / 54 | 44, 66, 44 |
| Suck Cannon | 9 | 849 / `0x2da790` | 0 / 56 | 51 |

**Item18/class180 is the Walloper.** It must not be substituted for the Suck
Cannon. Suck's store is not the ordinary ammo array. The source capacity is
five objects, or ten with the gold upgrade (only the ordinary variant is hosted).

`Rac1EquipmentAssets` decodes all five compressed global skins with pinned
compressed hashes and the canonical LEVEL0 texture remaps. Local child classes
74, 186 and 230 supply the mine, Doom bot and canister. Their true native local
frames, skin weights, inverse bind transforms, durations, attachment sockets,
direct sounds and timed animation cues are decoded. No model is authored anew.
Class168 retains scaled bind parents; class849 also has a reflected collapsed
attachment joint. Ordinary full-matrix skinning admits these affine transforms;
Ratchet's separate rigid hierarchy gate stays unchanged.

## Recovered behavior used by the runtime

- **Blaster:** class168 supplies the shot and class305 its travelling carrier.
  The six-tick gate is at `0x2b4e40..4e4c`; the raw hit record carries
  damage0.25/flags`0x10001`, followed by the shared `0x25a478` enemy override.
  Speed40 is converted to a 60-Hz step; its ordinary visual carrier lasts35
  ticks (`0x2cb084/0x2cb094`). Source pool27 supplies its sprite art;
  packed muzzle/impact colors are `0x2f4f7f7f` / `0x7f2f4f6f`. Firing uses
  gun sequence4 (`0x2b5284..2b52ac`).
- **Pyrocitor:** initialized overlay SDATA is mapped, unlike the boot executable's
  zero-filled counterparts. With gp`0x166c00`, `0x161530` gives the ten-tick
  ammo interval and `0x161520` gives ordinary reach12. `0x2b80f8` consumes ammo
  on the global-frame modulo gate. The host follows the global ten-tick phase
  across weapon changes; it does not restore the original game's global clock.
  `0x2b8538..2b857c` builds damage1/flags`0x10000` for individual flame-particle
  volume queries. Types12/25/2 are flame/smoke/secondary particle families.
- **Mine:** the retained `Rac1Class4aWeaponFamilySession` owns twenty-tick firing
  and ten-tick staging. Class190 owns class74 through PVar`+0x30`. Native child
  enumeration caps the live list at eight. `0x2abd58..2abda4` supplies ordinary
  radius2, damage3 and flags`0x00810000`. Source sequences0/1/2 provide carried,
  arming and armed poses.
- **Doom:** class229 creates class230; `0x2c9080..2c91c4` admits four class186
  agents, eight ticks apart, with population cap8 and a fifteen-tick full-population
  retry. Agent damage at `0x2c1388..2c13c0` is3/flags`0x10000`. The native
  near-owner/chase clips are6/7 (`0x2c0b90`, `0x2c0cc0`); clip1 is a leap.
- **Suck Cannon:** `0x2da660..2da768` recounts the captured-Moby pointer store at
  player`+0x2050`, with states3/4 counted separately at `+0x207c/+0x2078`.
  The ordinary/gold capacity test appears at `0x2da8c0..2da8d4` and in the
  capture admission `0x2da0c4..2da11c`. Release scans occupied slots from index0
  (`0x2db210..2db250`) and invokes the victim's own release callback
  (`0x2db718..2db738`). The fire gate is twenty-five ticks at `0x2db12c..2db138`.
  The held source sequences include suction3, loaded6 and firing5.

`0x257880` selects a Moby animation; its arguments must not be mislabeled as
sound requests. The hosted equipment view plays decoded direct/timed sound cues
from its selected clips. Admitted bank tones still use OBP's approximate pitch
and mixing. The admitted grain programs now retain their timing, branching,
envelope and sample loop regions; unsupported programs are not flattened into a
guessed representative tone.

## Explicit OBP reconstruction boundaries

These are working source-asset implementations, not complete retail parity.

- Godot supplies aim discovery, swept contact, mesh bounds, line of sight and
  support. Blaster aim/history/strafe/ricochet rules and the full native primitive
  query implementation are not recovered. Its visible carrier steers toward
  the selected aim point and damage uses swept contact during travel.
- Equipment now uses the source arm joint lists and composes local channels
  before skinning. Ordinary legs keep their clocks and unmodified vertices.
  Interrupted base-pose handoffs and the full native override system remain open.
- Pyrocitor sprite motion, spreading, visual sizing, wall clipping and a
  twelve-tick cross-particle victim guard are explicit host policies. The complete
  native flame pool, flags, repeat filtering and stream acceleration remain open.
- Mine/canister flight currently uses the recovered Bomb launch-origin family
  and Bomb arc as an adapter. Complete class74/230 integration, bounce, surface
  attachment, Mine seeking and bot navigation/lifetime remain approximations.
  Bots use ground/contact bodies and avoid unsupported forward steps. Mine/Doom
  explosions reuse retail Bomb burst/debris art and its host renderer until their
  separate effect recipes are complete.
- Suck capture accepts Veldin class749 and ordinary wood class500 under a host
  range/cone test. The full victim callback eligibility, suction motion, outgoing
  damage envelope and reward timing are unresolved. Captured originals are removed
  without invented death rewards; the visible model shrinks into the cannon and
  is released as a projectile. Its current outgoing contact uses a bounded
  damage1/flags`0x10000` adapter, not a recovered Suck damage assertion.
- Preview loadout/refill controls and movement bookmarks are OBP tooling. Campaign
  equipment is retained separately and preview ammo is not written to its save.
  Bookmarks retain payload-free cargo identities and preview status, not live
  projectiles, mines, bots or complete native PVars.

## Validation

`Rac1AdditionalWeaponTests` verifies descriptor rows and instruction words against
the authorized overlay, all eight decoded models, native timing/capacity and
ownership, cargo/HUD accounting, and movement-clock preservation during gun
poses. `MovementReproStateTests` covers cargo serialization and malformed states.
The Godot `--rac1-weapons-smoke` fixture checks actual presentation/contact and
cargo reload with rendered captures supported by its wrapper. Captures stay local.
