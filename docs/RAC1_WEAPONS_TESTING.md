# R&C1 arsenal testing

In an R&C1 level, press **0** to enable a session-only weapon preview and refill
its ammo. This does not acquire the weapons in the campaign inventory. Press
**Shift+0** to return to campaign equipment. Ordinary acquisition still uses the
source inventory; weapon vendors are not added by this pass.

With a controller, use **Start → Testing Equipment → Toggle Weapon Preview**.
The same menu restores campaign equipment and offers **Refill Preview Ammo**.

| Key | Equipment |
| --- | --- |
| 1 | Wrench |
| 2 | Bomb Glove |
| 3 | Pyrocitor |
| 4 | Blaster |
| 5 | Glove of Doom |
| 6 | Mine Glove |
| 7 | Suck Cannon |
| 8 | Devastator |
| 9 | Visibomb Gun |
| Shift+3 | Taunter |
| Shift+4 | Walloper |
| Shift+5 | Tesla Claw |
| Shift+6 | Morph-o-Ray |
| Shift+7 | RYNO |
| Shift+8 | Drone Device |
| Shift+9 | Decoy Glove |

Hold **Triangle** and point with the **left stick / D-pad**, then release to
equip. **L1 / R1** changes page; double-tap Triangle to swap the previous item.
Keyboard **[ / ]** still cycles owned weapons. Outside the selector, the D-pad
moves Ratchet. The preview includes
all fifteen ordinary weapons and the wrench. Gold upgrades remain unimplemented.

Hold **E / Circle** to use the selected weapon. **X / Square** continues to
use the wrench. Selecting a weapon exits the Swingshot preview.

After selecting a weapon, release all input: Ratchet should return to ordinary
idle with his head attached, and no firing loop should continue. Check this again
after a short shot, held fire, running, jumping and switching weapons.

- Pyrocitor: hold fire near wooden crates or enemies, then release. Flames should
  travel outward, stop at walls, and die away when released. Ammo should decrease
  six units per second during uninterrupted ordinary firing.
- Blaster: tap and hold fire; check the animated gun, warm-colored bolt effects,
  six-tick cadence and nearby enemy aim assistance. Fire while running and jumping
  to check that the legs keep their movement animation. A Veldin toad takes one
  shot; a full-health robot takes three, including shots during its hit reaction.
  A tap should produce one audible burst; held fire should sustain one voice
  rather than layer several complete four-burst samples.
- Mine Glove: throw onto the ground. The actual mine should settle above the
  surface, animate, seek a nearby enemy and detonate with a local blast.
- Glove of Doom: throw a canister. It should emit four animated Doom bots, eight
  ticks apart, then disappear. Bots seek enemies and explode on contact. The
  source population gate admits at most eight bots; another canister waits.
  The staged capsule is quarter scale, growing to half scale in flight. Bots
  should travel on a flat floor without constantly circling from floor contact.
- Suck Cannon: hold fire while empty near a toad or wooden crate. The object
  should shrink into the cannon. Keep holding to fill the five-object store;
  release, then fire to launch captured models. The HUD reports captured objects,
  rather than an invented conventional ammo supply. Untransformed robots and metal
  crates are outside the current capture adapter; transformed chickens can be
  captured and retain their appearance through saving, loading and release.
- Devastator: fire at a crate or enemy; check the source rocket, acceleration,
  local blast and the gap before another shot.
- Visibomb: fire into open space. WASD / the movement stick steers the missile;
  the camera follows it while Ratchet stays put. Crouch or the wrench button
  detonates it. Impact, weapon switching and loading must restore the player camera.
- RYNO: one use launches seven missiles nine ticks apart, using the rotating
  muzzle sockets. It spends one ammo for the salvo; check target distribution
  when several enemies are ahead.
- Drone Device: one use deploys up to six drones. They orbit, intercept nearby
  enemies and are consumed on contact. A full formation must not spend ammo.
- Decoy Glove: the landed device inflates into Ratchet and attracts enemies.
  Inflation should sound once. The source grounded lifetime is about ten seconds;
  it should fold away over the following second instead of vanishing abruptly.
- Tesla Claw: hold fire with one or two enemies ahead; check the arcs, ammo,
  damage, firing/lock sounds and immediate visual stop on release. There should
  also be emitter feedback when there is no eligible target.
- Morph-o-Ray: keep the beam on an enemy until it becomes an animated chicken.
  It should stop attacking without granting death rewards. F12 / Shift+F12 retain
  its transformed identity. Check movement and wrench contact with the chicken.
- Taunter: hold fire in front of wooden crates, enemies and planted mines.
  Check expanding effects, crate interaction and expanded mine trigger range.
- Walloper: punch a nearby target. Its source punch pose and lunge have a delayed
  contact window; it should not apply damage immediately on pressing fire.

**F12 / Shift+F12** preserve captured object identities and the preview flag along
with the existing world removals and inventory. Flying shots, planted mines and
Doom bots, drones, decoys and missiles remain transient; loading resets those
controllers. Active Morph transformations retain their identities.

Run `./tools/rac1-weapons-smoke.ps1` for the synthetic integration fixture. Add
`-Rendered -CaptureDirectory captures/weapons-re/check` to inspect private captures.
The wrapper uses an unfocusable offscreen window and does not replace saved window
placement. It checks skins/sockets, ammo/HUD, flame contact, mine proximity/blast,
bot seeking, capture/release, a cargo save/load roundtrip and every added weapon's
main function. It is not a matched retail gameplay replay. See
[arsenal evidence and remaining approximations](../research/RAC1_ARSENAL.md) and
[the earlier five-weapon pass](../research/RAC1_ADDITIONAL_WEAPONS.md).
