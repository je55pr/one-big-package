# Testing Ratchet's movement

Relaunch OBP with the desktop shortcut to build the latest changes. Ordinary
R&C1 play now has the second-jump aerial boost and directional flips, plus
automatic ledge grabs, hanging, sideways traversal and a jump back onto the top.

- Jump: Space / south face button. Press again in the air for the aerial boost.
- Flip: hold C / right shoulder, move the stick sideways or backwards, and jump.
- Ledge: jump toward a reachable edge. Move sideways while hanging, jump to
  climb, or press crouch again to drop. Overhangs and unsuitable surfaces reject grabs.
- Wall jump: jump toward a close, tall wall and press jump again. Repeated wall
  jumps need opposing wall normals, as in jump slots; short walls and nearby
  ledges are handled by separate probes.
- Equipment preview: F10 cycles Heli-Pack, Thruster-Pack, then the campaign
  equipment. The preview never changes ownership or the save.
- Pack high jump: hold crouch briefly, then jump while stationary.
- Pack stretch jump: run forward, hold crouch, then jump while momentum remains.
- Glide: with an aerial pack, press jump again in the air and hold it. Holding
  the original jump does not automatically enter the ordinary-jump glide.
- Thruster Power Slam: jump, centre the stick, then press crouch in the air.
- Swingshot preview: F11 toggles it. Face an authored target and hold E / east
  face button; release to detach. Both pull and pendulum targets are supported
  in the bounded preview. Region-controlled and moving-facing targets are excluded.

Useful checks are whether the second jump feels natural, whether flips retain
their direction, whether feet/head clip through nearby surfaces, and whether
ledge grabs and sideways movement behave sensibly. With F10, compare the two
packs' high/stretch jumps and glide speeds. Hoven's ice should retain momentum
and brake more slowly while crouching than ordinary ground.

For the landing corrections, try a jump with a small amount of movement and
release the stick before touching down, then repeat with a side flip. Check
for a brief run pose and for stepping or freezing in the subsequent idle.
Running jumps should also launch on shallow descending ground and across a
flat-to-downhill seam. For each pack's stretch jump, compare the flight distance
and touchdown carry with crouch released before landing. Holding crouch through
touchdown deliberately selects the separate native crouch carry/brake branch.

The collision envelope is smaller and uses recovered dimensions, but still
uses a Godot capsule and separate probes. It is not a complete reproduction of
the retail collision solver. Pack/gadget meshes, animation blending and several
action handoffs remain presentation approximations. Swingshot preview begins
at attachment; its native projectile and equip lifecycle are not implemented.

Swimming/Hydro-Pack, Magneboot orientation, grind rails, ziplines and some
combat-related movement are still incomplete. Water and mud require different
collision-query membership from solid terrain; ordinary motion stops at those
unsupported boundaries instead of treating them as normal floors.

The implementation was recovered from the verified local retail bytes and
checked with deterministic source and Godot integration tests. Fresh matched
PCSX2 trajectory comparisons remain outstanding.
