# RAC1 Clank and pack presentation

Authority is the original NTSC-U LEVEL0 gameplay overlay, SHA-256
`eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804`.
The runtime loads the models, textures, animation pairs, blink layers and jet
geometry from the user's verified source. No extracted payload is committed.
The public [rac1-native-game reference](https://github.com/daino-selvatico/rac1-native-game)
at `3fd14c12e4dff2fd4b6661a6e54187faea3f76c8` was a navigation aid;
the addresses and data below were checked against the retail overlay.

## Models and attachment

| Moby class | Role | Vertices / joints / sequences | LEVEL0 asset SHA-256 |
| --- | --- | --- | --- |
| 601 | Clank | 2020 / 33 / 18 | `92077a25250584e210d3a477a80d1ca1958fa495d93d22dda930f1e3b9d421fa` |
| 607 | Heli-Pack | 1613 / 36 / 9 | `2c46e695f91166fbc1455db8bb2969a738ca3f0b9e46da0a1f618f0879d0c3da` |
| 608 | Thruster-Pack | 527 / 15 / 9 | `f73c7eba18df8ad28c18c5735f5604a532e1eab85306b9b545bbfd83c1091e01` |
| 609 | Hydro-Pack | 638 / 10 / 5 | `05fef95614ee0e1656b84dd6136f84483d405e170e283c9543873fa51c5d1679` |

These are local static classes, not global-Moby entries. Clank and each upgrade
attach at Ratchet semantic socket 5, joint 5 (`20f580`, `20f7b0`). Normalize
the socket's axes and retain its translation. There is no fitted back offset.
Keep the socket through ordinary, equipment, arm-hold and joint-ease poses.
These skins retain their stored inverse-bind translations; Ratchet's separate
common-pivot reconstruction produces incorrect pack geometry.

## Selection and timing

`231e78` selects paired animations, `231ff8` handles random pairs, `231db0`
resolves held animation state and `232128` propagates playback multipliers.
Rows are `(Ratchet, upgrade, Clank)`:

| Pack / retail table | Rows |
| --- | --- |
| Heli / `17bbf0` | `(0,4,11) (0,3,10) (0,7,14) (21,5,12) (18,8,15)` |
| Thruster / `17bbd0` | `(0,3,3) (0,4,4) (0,8,5) (0,5,6) (0,6,7) (42,7,8)` |
| Hydro / `15f6b8` | `(101,3,16) (101,4,17)` |

Do not invent pairs for Thruster sequences 17/38 or Hydro sequence 61.
The Heli class updater `2d37d0` selects upgrade sequence 6 during glide,
with a ten-update blend. Idle closure/fidgets (`22a090`, `22a364`) use the
head-ease angles, action age, native completion flags and 110..270 update
intervals. Leaving Heli glide closes over 19 updates; ordinary close is 7.
Equipment replacement (`20fea0`, `20fca8`) closes upgrade sequence 2 after
that update's animation advance, disables readiness, retires on the loop
flag, and spawns the next upgrade on the following update. Clank is retained.

The Moby clock (`250480`) is separate from Ratchet's animation clock. It
uses native per-key or constant rates, freezes blends when the multiplier
is zero, snaps float bits `3f7f0000..3f808000` to one, rescales blend excess
into the new rate, and raises the loop flag when the *next* key becomes zero.
Default negative blend requests read the two integers at `15f6e0` (11,16).

Clank's run head spring (`215ef8`, `205168`) retains its own angular and
translation velocities. Blink (`2059d8`) reads the four absolute eye layers
at `17c1e0..17c2a0` and the 22-step weight curve at `17c2a0..17c2f8`
(exclusive end), with 50..200-update intervals.
`2059d8` multiplies the absolute blink-node translation by Moby scale before
the ordinary local-to-world conversion. These tables therefore use
`Mesh.Scale * Mesh.Scale / 1024`; ordinary joint translations use one factor.

## Effects, audio and ownership

Thruster admission/ramp is `2b4198`: native action, apex, authored-frame and
support predicates; two-update rise and eight-update decay.
Slam's post-age-33 predicate is **unsupported** (`P+30e != 0`, `2b42d8`),
so the jets decay on contact and remain off during grounded recovery. Effects
read the current movement action; a retained animation label is not authority
for the action or its age after recovery ends. The procedural
plume (`2b3628`) reads 196 vertices at `1d9470`, UVs at `1da0b0`, two strips,
and cross-plane templates at `1da9e0`. Nozzle bases are formed in native world
space from semantic socket pairs 0/1 and 2/3. Texture definitions 10/11/12
supply the plume, glow and flicker; `2b3c50` emits kind-15 flashes, updated
by `26ccb0`, including alternating-frame children.

Hydro action `35` exhaust (`218bd4`) alternates its two sockets, uses previous
contact-processed motion, and varies emission delay with swim scalar.
Kind-22 dust (`26f080`, `26f280`) supplies native growth, lifetime, color,
motion, floor-height and camera-heading behavior with texture definition 34.

Existing Ratchet animation cue dispatch and class-0 sound ownership remain
the audio path. Heli, Thruster and Hydro loops use native slots 2,18,19;
pack model sequences contain no additional direct/timed sound cues.
No substitute audio or arbitrary timer is introduced.

Hydro is independently enabled alongside the remembered aerial pack. Water
entry requests Hydro; water exit restores the aerial pack through the same
retirement sequence. Fast swimming requires the requested pack to be ready.
Equipment and asset selection belong to the persistent player, independent
of destination origin. Development loadout availability remains an explicit
OBP policy, not a claim about retail starting inventory.

## Validation and limits

Manual checks on the authorized original source compared every authored
model frame against `Rac1MobyPose.PoseLocalHierarchy`; maximum position error
was below `5e-7` native world units. Rendered inspection covered attachment,
all upgrades, paired jumps, glide, run head motion, blink, Hydro exhaust,
Thruster jets, replacement and restoration. The existing airborne diagnostic
passed both packs' high/stretch jumps, glide and Power Slam. A temporary
water inspection confirmed ready pack 4 and Hydro fast action `35` in decoded
Novalis water; temporary fixture code and captures were removed from the patch.
Focused held-Cross inspection retained Thruster high action 13 through the
script, reached a 3.353-unit in-game apex, and entered glide only after the
script ended. Slam inspection observed jet strength decay in eighths to zero
while action 34 was still in its grounded recovery, with no standing restart.

Existing audio smoke passed pack loop ownership, pause/release and the earlier
movement/weapon cases, then failed its unrelated fatal-hit held-sound fixture.
The full swim diagnostic failed its underwater HUD assertion. Both failures
also reproduced on unmodified dev commit `bc7a37a4` with the same toolchain.

This is source-backed gameplay/presentation recovery, not complete hardware
equivalence: Godot's materials, additive blending and particles differ from
the GS renderer; Clank's native glow/tint modulation is not yet reproduced.
The existing audio backend is not a bit-exact SPU2 implementation. Cosmetic
randomness uses an OBP forked stream so it cannot perturb gameplay/audio;
retail's global random-call ordering is not claimed. A synchronized retail
capture comparison is still needed to certify full audiovisual parity.
