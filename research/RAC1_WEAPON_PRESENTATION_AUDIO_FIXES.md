# R&C1 weapon presentation and audio follow-up

20 October 2026. This corrects the defects reported during interactive testing
of the initial arsenal pass. Its original fixtures exercised models, ammunition
and contacts, but did not establish correct idle poses or audible sound lifecycle.
They must not be treated as a retail presentation acceptance result.

Authority remains the pinned NTSC-U LEVEL0 overlay and locally decoded assets.
The same rac1-native-game revision named in `RAC1_ARSENAL.md` supplies address
annotations. The bundled ISC-licensed 989snd implementation supplies the grain
and SPU envelope reference; its notice is in `THIRD_PARTY_NOTICES.md`.

## Pose ownership

`0x20e4c8` selects the full holding pose from item table+0x24 only while holding,
and the moving layer from+0x28. `0x20e698` releases that ownership. Applying the
holding pose continuously to an equipped weapon was incorrect. Source51/56/54/81
now belongs to an active use; release returns to the ordinary pose with a fade.
Moving Blaster/Morph use57 and Devastator uses73. A missing moving layer is
respected rather than substituting the full stationary pose.

The previous vertex torso cut could move one part of the neck independently of
the head. `0x24f028` obtains its joint list from the second list of a class+0x1c
chain. Ratchet chains12/13 contain joints52..71/72..91. The new sampler blends
local quaternion/scale/translation channels over these lists, then composes the
hierarchy and skins the body. Unaffected ordinary vertices and clocks remain
unchanged. Cached bind data avoids allocating matrices inside vertex loops.
Base poses during interrupted ordinary blends still have a host sampling boundary;
this is not a complete implementation of every native animation record/override.

## Sound roles and programs

- Ordinary glove throws request hero sound26 (`hero_sound_start(0x1a,0)`), not
  class192 sound1, which belongs to its equip mechanism. Equip sequence0 is no
  longer replayed as the Bomb Glove's throw animation.
- Blaster class168 sound1 maps to bank116 and is marked looping. Its stored
  9,912 samples at the unmodulated host rate22,050 contain four bursts over about
  0.45 seconds. Starting that full clip on every projectile produced the reported
  four-shot sound. It now owns one sustained voice. A tap stops at the host's
  six-tick shot window, before the later stored bursts; held fire does not stack
  copies. Exact native release/ADSR tail timing remains a comparison boundary.
- Pyrocitor176/1, Suck849/2 and Tesla177/4 have owned, moving sustained voices.
  Tesla acquisition uses177/1. Morph185/0 retains its two-tone key-off transition
  and a separate steady loop region. Taunter advances actual class175 slots0..5
  and retires its active sound when released/switched.
- Ratchet sequence103 supplies Walloper cues at time2.5/sound53 and time4/sound30.
  These replace guessed weapon sounds; sound53 retains a bounded set of the
  native register/probability program outcomes.
- Doom explosion186/4 replaces the inappropriate vocal sound2; spawned bots do
  not emit a damage voice. Mine blasts use74/2, not the seeking loop74/1. RYNO
  launches use457/1 and impacts457/0. Shared explosion visuals no longer add a
  second Bomb Glove explosion sound to these events.

The SBlk parser retains grain parameters. A bounded renderer interprets the
admitted tone, delay, random branch/pitch, register/test/marker, LFO and key-off
instructions with240-Hz grain scheduling and SPU ADSR fields. Unsupported programs
remain unsupported instead of being replaced by an arbitrary tone. Native sample
loop boundaries survive neutral audio and Godot playback. Pitch quantization,
resampling, mono mixing, LFO waveform approximation, eight cached probability
variants and host stop tails are not bit-exact 989snd emulation.

The duplicate decoy inflation cue exposed a second shared bug: the player frame
clock flags a transition towards frame0 when it enters the final keyframe. The
equipment sound code mistook this for reaching frame0 and replayed early cues.
Equipment cue wrapping now follows the actual keyframe wrap, and a one-shot
does not trigger its start cues again while returning to its neutral pose.

## Models and deployed behavior

Doom canister state1 grows to a quarter of class scale; ordinary flight state2
grows to a half, in0.05 relative increments (`0x2c89e8`). Full class scale was
incorrect for the staged capsule. RYNO constructor `0x2ccee0` sets class457 to
quarter scale (`0x2ccf88..2ccfa0`); the identity was already correct.

The Doom navigation adapter rotated its wander direction on every slide contact,
including the floor. It now responds to wall contacts. Its navigation remains
an OBP adapter, not a recovered implementation of every native target/path query.

Decoy state3 has a600-tick timer (`0x2c4a78`) and then folds towards sequence0
over60 ticks (`0x2c50d0..2c5108`). It now shows that transition instead of being
removed abruptly. Repeated contact from the same attacker is filtered for30 ticks
(`0x2c4c9c..2c4cc4`). Hosted health/target discovery still have explicit boundaries.
Tesla and Morph also provide emitter feedback with no eligible target; the beam
renderer remains an approximation rather than a reproduction of all native FX.

## Verification scope

New source tests check the actual arm lists, complete-pose skinning equivalence,
unchanged non-arm vertices, sound remaps/programs/loop regions, grain delays,
key-off, branch selection and explicit unsupported programs. The Godot weapon
fixture now checks every weapon at idle, actual sustained-voice start/stop,
single-tap Blaster behavior, sound-step advancement, target-free emitter feedback,
straight Doom travel on a flat floor, source rocket scale and one inflation cue
followed by a complete folding transition. Existing contact/cargo/reload checks
remain. Private rendered captures are inspected; there is no matched live retail
audio/trajectory replay, and these checks do not establish complete retail parity.

Validation for this follow-up: the required full Release and retail-authority
suites each pass1,448 tests with zero skips. Solution formatting verification and
the Debug host build pass. The complete rendered weapon fixture and the existing
wrench, Bomb Glove and Veldin enemy regression fixtures pass.
