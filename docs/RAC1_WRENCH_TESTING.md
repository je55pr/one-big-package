# Testing the R&C1 wrench

Relaunch OBP with the desktop shortcut to build the current working tree.

- Swing: X / west face button. Tap again near the end of the first and second
  swings for the three-hit Multi-Strike. Holding the button does not auto-repeat.
- Hyper-Strike: jump with Space / south face, then press X / west while airborne.
  Ratchet rises briefly, drives the wrench down and recovers after landing.
- Comet-Strike: hold C / right shoulder, then press X / west. The wrench flies
  outward and returns to the animated hand. Try it against crates and bolts.
- Equipped item: E / east face. Square/west always uses the wrench, including
  while Bomb Glove is selected with 2. The combo must not consume its ammo.
- Select the wrench with 1. Swingshot preview on F11 still uses held E / east.

Check that the wrench stays in the hand through jumps, flips and all three
swings; that recovery does not produce the earlier run/idle flash; and that
running/jumping resume after each attack. Try an attack beside several crates,
and a throw near a wall. Let OBP finish the catch before beginning another throw.

The default wrench now uses its retracted neutral shape. Compare jump-to-idle
and run-to-idle: both must keep the wrench moving with the hand during idle,
including the stop animation after a run.

The model, textures, attack animations, timing and ordinary-axis motion come
from the verified local retail source. Enemy support is still the bounded
class749 population and centre10 bolt crates. Collision uses OBP mesh bounds;
auto-aim, additional target classes, wrench sounds/trails/impact effects and
native bolt homing remain incomplete. See `research/RAC1_WRENCH_ACTIONS.md` for
the precise boundary. No fresh PCSX2 comparison is claimed.

Developer checks: `tools/rac1-wrench-smoke.ps1` exercises real input and animation
plus clearly staged contact fixtures. `tools/rac1-veldin-play-smoke.ps1` checks
ordinary Veldin play without staging. Run the standard portable and retail test
scripts and the movement smoke when changing attack/movement handoffs.
