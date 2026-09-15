# R&C1 Bomb Glove firing slice

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`, ISO SHA-256 `ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d`). Loaded Veldin retail state is used for overlay/runtime code that is not present in the boot ELF. Public tooling is used only to corroborate the familiar name **Bomb Glove** for native item `10`; gameplay promotion below comes from retail evidence.

## Candidate selection and ammo

The controlled Veldin witness has native item `10` selected, item `10` as the only unlocked ranged weapon in that early state, and an ammo count of `6`. That makes it the smallest evidence-rich candidate for the Goal 1 ranged loop rather than choosing a later weapon by familiarity.

Retail indexes ammo at `0x0013d428 + itemId * 4`, so item `10` uses `0x0013d450`. Its descriptor is `0x001c40b0 + itemId * 0x18`; item `10` therefore starts at `0x001c41a0`, and the descriptor maximum-ammo field at `+0x0e` is `40`.

`0x00233e98(-1)` queries ammo for the current weapon. The accepted fire path calls `0x00233db8(-1, 1)` at `0x002c1cc8..0x002c1cd0`; the helper resolves the current item, requires sufficient ammo, and subtracts exactly one round. The bounded OBP session therefore admits initial ammo only in `0..40` and spends exactly one round per accepted shot.

## Input and fire acceptance

The held weapon uses native class `0xc0` and loaded update `0x002c1ad0`. The path at `0x002c1c64..0x002c1cd8` reads the native player input/action mask, requires its weapon-fire gate plus available ammo, invokes the one-round consumption helper, then writes weapon native state `3`.

The physical PS2 pad bit that feeds that native action mask was not uniquely recovered in this lane. `OBP.RAC1` therefore accepts a host-resolved `fireRequested` boolean; this is an input seam, not a claim that an arbitrary Godot button is retail truth.

## Projectile ownership and launch

When no projectile is staged and current-weapon ammo is available, the tail of the weapon update (`0x002c2420..0x002c2444`) calls the Bomb Glove projectile constructor and stores the resulting object at weapon PVar `+0x50`. The constructed carrier is native class `0x4a` in native state `0`.

Weapon state `3` dispatches at `0x002c22b4`. It reads that exact stored object, calls `0x002aa008`, then clears weapon PVar `+0x50`. `0x002aa008` writes the projectile to native state `1` at `0x002aa098`. This closes a retail creation-to-launch path without assigning an external class name or inventing projectile travel equations.

After launch the weapon writes two recovered timers. It stores `0x001fef20(10)` at weapon PVar `+0x54` and `0x001fef20(20)` at `+0x58`, then enters native state `4`. In the normal Veldin authority state the helper's time-scale source is `1.0`, giving 10 native ticks before a replacement projectile may be staged and 20 native ticks before another shot may be accepted. OBP preserves those native-tick gates directly.

## Hit filtering and damage record

Native class `0x4a` dispatches through loaded update `0x002a84c8`. Its post-launch candidate scan walks live Mobies and rejects terminal native states `0xfd` and `0xfe` before later class/distance/contact helpers. The broader retail exclusion list and collision geometry are intentionally not generalized by this milestone.

On the recovered impact path, the class-`0x4a` update calls record writer `0x00259bc8` with scalar `1.0` and flag word `0x00010000`. The existing representative Veldin class-749 hostile has a separately recovered one-health damage consequence: `1.0 -> 0.0`, entering native damage state `12` before a later terminal `0xfd/0xfe` status.

For Goal 1, `Rac1BombGloveSession` therefore admits only the already-bounded R&C1 class-749 hostile, rejects recovered terminal target states, emits the proven `1.0 / 0x00010000` result once per launched projectile, and delegates that result to `Rac1Class749HostileSession`. The class-749 restriction is an OBP milestone admission, **not** a claim that retail Bomb Glove can damage only that class.

## Promotion boundary

Promoted into `OBP.RAC1`: item-10 ammo capacity/accounting, class-`0xc0` firing ownership, class-`0x4a` staged projectile identity and `0 -> 1` launch state, 10/20 native-tick gates, terminal-target rejection, and the representative Goal 1 impact/damage consequence.

Deliberately unpromoted: physical controller button mapping, exact projectile trajectory/ballistics, generic explosion radius, the full native class exclusion list, arbitrary target classes or health values, visual/audio effects, and any claim that the community names for class `0x4a` describe this retail carrier. Godot remains responsible for input mapping, collision queries and presentation; `OBP.RAC1` remains responsible for admitted gameplay truth.
