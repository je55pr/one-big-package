# R&C1 held-weapon grip audit

8 November 2026. This follows reports of RYNO, Pyrocitor, Suck Cannon,
Morph-o-Ray, glove and Walloper alignment, an oversized non-rotating held mine,
and Tesla Claw using a two-handed attack pose. It is a partial correction and
visual audit, not acceptance of every reported issue.

## Authority

The local NTSC-U LEVEL0 executable overlay has SHA-256
`eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804`.
Its item records and the decoded class assets were checked directly. Address
annotations and control-flow explanations also use
[RAC-Native/rac1-native-game revision 7365860f0375015f03cc2e3ad56171bf7f34089e](https://github.com/RAC-Native/rac1-native-game/tree/7365860f0375015f03cc2e3ad56171bf7f34089e).
The decompilation is a reference; the local retail data remains authority.

## Corrections

- Arm classes 1/2 pack their rotations in bank order but their sparse scale and
  translation records name full Ratchet joints. `Rac1RatchetArmHold` previously
  treated the sparse IDs as offsets into a 20-entry array, discarding all the
  real arm translations. They now resolve through the bank map, like the shared
  later-game skeletal adapter. For example, the right bank includes full joints
  52, 54 and 56; the left bank includes 72, 74, 76 and 86. Every decoded sparse
  record in both retail arm classes was checked to belong to its bank.
- The glove branch of `hero_slot_mobys_update` (`0x20f580`) does not play a
  separate ready/firing finger pose. For items 10/17/20/25 it calls
  `moby_pose_encode` (`0x2092c8`) with list `0x17a5c0`, then displays that encoded
  pose on the glove. The list attaches at joint 54 and copies local joints
  55..70 into target joints 1..16, after an identity root. OBP previously played
  an independent glove clip, allowing the regular hand to disagree with its
  shell. Bomb, Mine, Doom and Decoy gloves now use the current resolved player
  finger channels, source quantization and each glove's own bind/skin data.
  Sampling after the procedural layer retains its uneased body-pose reference,
  including body transitions, before reapplying the same joint offsets.
- `mine_spawn` (`0x2a9ed0`) halves the class scale. Held mines now use that scale.
  The Mine Glove update (`0x2c1ad0`) places its payload at semantic socket 0 plus
  native `(0, -0.09, -0.02)`. The held mine update (`0x2aa670`, held branch near
  `0x2aa884`) takes that socket's orientation and turns it by pi about native X.
  Previously OBP copied only a point, drew the mine at full size, and kept its
  rotation fixed in avatar space. The copied glove pose now supplies a complete
  animated payload socket, composed with the sampled hand before host conversion.
- Doom's payload origin likewise includes the native glove-local offset
  `(0.01, -0.14, -0.05)` from `doom_glove_update` (`0x2c7a58`). Its existing held
  quarter-scale and independent payload orientation remain unchanged.
- Restoring the native arm translations exposed an incorrect Visibomb launch
  origin: its missile used the animated tip and missed the controlled crate
  lane. The original ordinary launch (`0x2b32c0..0x2b32e4`, handed to
  `0x2b58d8` at `0x2b331c`) instead starts 0.6 units ahead of the held Moby's
  root along the level-facing direction. That source distinction is now
  retained separately from the model's feedback tip. Pitched and hidden-hero
  launch modes remain host approximations; this does not recover those branches.

No calibrated grip offsets, replacement animations or hand-removal masks were
introduced. This correction is confined to held presentation and payload-origin
queries; the existing thrown-mine scale and flight adapter are separate work.

The ordinary held-Moby matrix and native smoothed gun-orientation ring remain
outside this correction. The plain native branch normalizes its matrix columns,
whereas the copied glove branch retains them. Applying that normalization in
isolation to the host's currently reconstructed matrices changed Walloper's
punch incorrectly; it was excluded pending a complete treatment of the native
placement/scale paths.

## Visual audit

Private before/after sweeps use the same baseline commit, LEVEL0 source, seed 121,
synthetic floor, facing, camera and scripted input. Twelve weapons are captured
from both sides at idle, at use ages 6/18/36, and with movement input at ages 12/24.
The nine reported weapons are included; Blaster, Devastator and Visibomb are
controls. Screenshots remain private and are not committed. The capture-only
driver edits are temporary and are not part of the production change.

| Report | Result / remaining boundary |
| --- | --- |
| RYNO grip | Shared arm translations corrected; idle/support-hand placement changes visibly. No weapon-specific grip offset added. |
| Pyrocitor handles | Shared arm translations corrected; support-hand placement changes. Exact handle contact throughout firing remains a visual acceptance boundary. |
| Suck Cannon grip | Receives the same two-arm correction; inspect idle, firing and running comparisons. |
| Morph-o-Ray | Receives the right-arm translation correction. No independent model/socket substitution found or applied. |
| Mine held too large and fixed in orientation | Corrected: half-size, native socket-relative offset and wrist-following orientation. |
| Bomb / Doom / Mine glove overlap | Missing native finger-pose copying corrected. The shell now changes with the regular hand instead of retaining its independent ready pose. Inspect both sides and throws for any residual surface overlap; no body-hand removal mask was invented. |
| Walloper alignment | Still needs visual follow-up. The retail item selects semantic 1, joint 55; changing it to the gun socket would contradict the item record. |
| Tesla two-handed use | Still visible. Retail item 19 has one arm slot but standing use sequence 51, shared with Pyrocitor/Suck Cannon/Taunter. `tesla_update` actually calls `hero_hold_start` (`0x2b8f40`, `0x2b9010`), which reads item-table `+0x24`. A different clip or unilateral arm mask is not established by those records. |

The before/after audit does not supply a matched retail-rendered reference for
the remaining Walloper alignment and Tesla pose. Numeric native table agreement alone
does not prove that those visuals are correct.

## Validation

The production game build passed with zero warnings and errors; solution-format
verification and the Godot UID check (447 scripts) also passed. The existing
rendered Bomb Glove and Wrench smoke scenarios passed. The focused rendered
capture sweeps exercise the real source assets and production
equipment paths. They are manual inspection material, not a new automated test.
Twenty-four sampled glove/player joint comparisons (classes 192/190/229/562,
standing and throw source frames) keep corresponding attachment origins within
0.000146 scene units after source quantization. This checks the local-channel
mapping, not bit-exact original instruction execution or complete surface parity.
The existing full weapon smoke failed on the unmodified baseline after its
Suck Cannon save/reload: its fixture retained a retired world lease. Do not count
that full scenario as passing or attribute its failure to the grip correction.

An isolated run of the existing arsenal scenario also checks Devastator,
Visibomb, RYNO, Decoy, Drone, Tesla, Morph, Walloper and Taunter before reaching
the same retired-world fixture failure after reload on both baseline and fix.
These individual phases passed; the full scenario remains failed, not waived.
