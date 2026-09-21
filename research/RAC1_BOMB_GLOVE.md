# R&C1 Bomb Glove firing slice

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`, ISO SHA-256 `ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d`). Loaded Veldin retail state supplies overlay/runtime code absent from the boot ELF. The controlled live witness uses the retained fixed Veldin state plus a managed PCSX2 movie/PINE trace; committed evidence remains payload-free.

## Candidate selection, input and ammo

Native item `10` is the early-state ranged weapon identified publicly as Bomb Glove. Ammo remains indexed at `0x0013d428 + itemId * 4`, so item `10` uses `0x0013d450`; its descriptor maximum is `40`.

The fixed state begins with item `8` equipped and item-10 ammo `6`. A deterministic two-frame Circle input changes both equipped-id mirrors to `10` without consuming ammo. After the equip settles, a second two-frame Circle input consumes one round, `6 -> 5`. This is a controlled input witness, not a complete quick-select/input-state model.

The live item-10 weapon is native class `0xc0` with loaded update `0x002c26c8`. Its accepted fire path calls `0x00233db8(-1, 1)` at `0x002c28d0` and enters weapon native state `3`.

## Projectile ownership and launch

Weapon state `3` loads its staged object from weapon PVar `+0x50`, calls constructor `0x002ace70`, and launches through `0x002acfd8`. The constructor requests native class **`0x79` (121)** from the common Moby allocator. This corrects the earlier class-`0x4a` attribution.

The class-`0x79` constructor stores the firing weapon Moby at projectile PVar `+0x50`. The launch helper writes projectile native state `1`, establishing the source chain:

`class-0xc0 weapon Moby -> class-0x79 projectile -> projectile PVar+0x50 source`.
The accepted-fire update clears the old staged pointer, then the weapon tail can immediately construct and store a replacement class-`0x79` object at weapon PVar `+0x50`. The controlled live trace shows the launched carrier at `0x01858b80` while the replacement is already staged at `0x01858c80` on the ammo-decrement frame. The older 10-tick rearm claim belonged to a different class-`0xc0` update and is not Bomb Glove truth.

The weapon retains a recovered 20-native-tick fire gate. OBP therefore keeps the 20-tick accepted-shot gate, but no longer models a 10-tick projectile rearm delay.

## Ballistic motion and countdown boundary

The controlled projectile uses loaded update `0x002adb30`. In native state `1`, the update calls common vector-add helper `0x001ff278` with:

- destination = Moby position;
- left-hand vector = current Moby position;
- right-hand vector = projectile PVar `+0x00..+0x08`.

So ordinary ballistic motion is `position = position + step` once per native update.

The same branch reads native frame-scale `0x0015ed70`, whose authority-state float32 value is `0.00027777778450399637` (`0x3991a2b4`, approximately `1/3600`). Native single-precision multiplication by `11.0f` rounds to float32 word `0x3b483fb8`, or `0.003055555745959282`; that rounded value is subtracted from PVar `+0x08` per native update.

The managed live trace agrees. At launch the step is approximately `(0.06286147, 0.12695619, 0.09166652)`; one update later the horizontal components are unchanged and the vertical component is approximately `0.08861096`.

Launch also initializes two countdown-like fields:

- PVar `+0x54 = time(300)`, decremented by `0x001fef98`;
- PVar `+0x6a = time(30)`.
Neither field is promoted as a generic projectile lifetime. The short countdown reaches zero while the controlled projectile continues flying. The representative shot contacts while the long countdown is still `235`, so the trace does not witness natural expiry at zero.

## Contact, splash damage and terminalization

The class-`0x79` state-1 path calls shared world query `0x001efc70` and common gameplay contact routine `0x001f2868`. On the retained contact branch it supplies native damage scalar **`2.0`** and flag word **`0x00830000`**, retains the class-`0x79` projectile Moby as source, then calls splash distributor `0x0025a9f8`.

The distributor iterates candidate Mobies and calls per-victim damage writer `0x00259a88`. It explicitly skips a candidate whose Moby pointer equals the retained source. The per-victim record retains source, damage flags, scalar and victim. This is a source-aware **contact-volume/splash** handoff, not the earlier direct-victim `1.0 / 0x00010000` record.

The controlled shot follows native states `1 -> 2 -> 0xfe`: state `1` begins on trace frame 41, contact state `2` begins on frame 107, and common terminal state `0xfe` appears on frame 122. Position is stationary during the state-2 interval in this witness.

The class-`0x4a` `1.0 / 0x00010000` direct-record path remains valid evidence for a **separate projectile family**. It is no longer used as Bomb Glove provenance.

## Promotion boundary

Promoted into `OBP.RAC1`: item-10 ammo accounting, class-`0xc0` weapon ownership, class-`0x79` projectile identity, `0 -> 1` launch, immediate replacement staging, 20-tick fire gate, source ownership at projectile PVar `+0x50`, position-plus-step ballistic recurrence, authority-state float32 vertical decrement `0.003055555745959282`, the 300/30 countdown fields as countdowns only, state-`2` contact transition, observed `0xfe` terminalization, and the `2.0 / 0x00830000` contact-volume damage envelope.
Deliberately unpromoted: a universal launch-vector formula, natural lifetime/expiry at either countdown, native contact radius/shape, a generic explosion radius, class-749 health consequence for the `2.0` splash envelope, faction/team/friendly immunity beyond source-self exclusion, visual/audio effects, and any claim that unrelated class-`0x4a` behavior is Bomb Glove behavior.

The live Godot host therefore still owns unresolved presentation launch geometry, contact geometry and timeout presentation. It no longer converts the recovered Bomb splash envelope into the previously assumed class-749 `1.0 -> 0.0` consequence.
