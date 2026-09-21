# R&C1 projectile and native hit patterns

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`, ISO SHA-256 `ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d`). Loaded Veldin retail state supplies overlay code absent from the boot ELF. Generated evidence contains only addresses, isolated instruction words, hashes and derived scalars/state observations.

This slice separates reusable projectile/hit **patterns** from weapon-specific behavior. The controlled item-10 Bomb carrier is class `0x79`; class `0x4a` remains a distinct projectile family useful for the direct-victim handoff witness.

## Projectile ownership

The item-10 class-`0xc0` weapon update `0x002c26c8` stages class `0x79` through constructor `0x002ace70`. The constructor forwards class id `0x79` to the common Moby allocator and stores the firing weapon Moby at projectile PVar `+0x50`.

The reusable ownership fact is therefore source-Moby retention across a projectile handoff. The exact PVar offset remains projectile-family specific: item-10 class `0x79` uses `+0x50`; the separate class-`0x4a` family uses a different layout.

## Motion and lifetime boundary

The class-`0x79` state-1 update `0x002adb30` advances position through vector-add helper `0x001ff278`:

`position = position + step`.

The step vector lives at projectile PVar `+0x00..+0x08`. After the position update, the vertical component at `+0x08` is reduced by single-precision `frameScale * 11.0f`. The fixed authority frame-scale is float32 `0x3991a2b4` (`0.00027777778450399637`); the multiplication rounds to float32 `0x3b483fb8` (`0.003055555745959282`) per update.

This is promoted as a reusable **position-plus-step ballistic recurrence**, parameterized by the owning projectile's initial step and vertical delta. Launch-vector initialization remains weapon-specific.
The item-10 launch path also establishes PVar `+0x54 = time(300)` and `+0x6a = time(30)`. These are countdowns, not yet a proven lifetime contract. In the controlled trace, `+0x6a` reaches zero and flight continues; the shot contacts at `+0x54 = 235`. No natural expiry-at-zero witness has been recovered.

The live state path is `1 -> 2 -> 0xfe`: ordinary ballistic state, contact/post-contact state, then common terminal state. This proves contact-driven termination for the representative shot, not a universal projectile lifetime.

## Direct-victim damage handoff

A separate class-`0x4a` projectile path calls native writer `0x00259bc8` with:

- source = class-`0x4a` projectile Moby;
- one preselected victim Moby;
- damage scalar `1.0`;
- flags `0x00010000`.

The writer retains source, flags, scalar and victim in the native damage record. This is the reusable **direct-victim record** shape. It is intentionally retained as a generic engine pattern and intentionally detached from Bomb Glove identity.

## Contact-volume / splash handoff

The controlled item-10 class-`0x79` path calls common gameplay contact routine `0x001f2868`, then splash distributor `0x0025a9f8`. Its retained envelope is:

- source = class-`0x79` projectile Moby;
- damage scalar `2.0`;
- flags `0x00830000`;
- victims = Mobies discovered by the contact query.

`0x0025a9f8` iterates candidate Mobies and calls per-victim writer `0x00259a88`. That writer materializes one damage record for each admitted candidate while retaining the source projectile and caller envelope.
This is the reusable **contact-volume** handoff. It differs from the direct-victim shape in who chooses the victim: direct callers arrive with one victim already selected; contact-volume callers provide geometry/source and let the common query discover victims.

## Ownership and friendly filtering

Two independent common paths prove source-self exclusion:

- `0x001f2868` skips a scanned candidate whose Moby pointer equals the incoming source Moby;
- item-10 splash distributor `0x0025a9f8` likewise skips a candidate equal to its retained source.

That is the only generic friendly/filtering rule promoted here. Additional bitmask/class/filter logic exists, but no team, faction or ally-immunity interpretation is strong enough to freeze as retail semantics.

## Environmental collision pattern

The shared world-query routine `0x001efc70` appears in the item-10 launch helper and class-`0x79` state-1 update, and independently in the class-`0x4a` family. That establishes a reusable environmental/world-contact boundary distinct from Moby contact.

The exact geometric primitive is unresolved. The call sites do not justify naming the query a ray, sphere, capsule or sweep. Hosts should pass collision facts across the gameplay boundary instead of manufacturing a native shape.

## Promoted source contract

`Rac1NativeHitSemantics` exposes:

- `DirectVictimRecord`: source retained, one victim preselected;
- `ContactVolume`: source retained, victims query-discovered, source excluded from candidates;
- `Rac1MobyContactFacts`: a geometry-neutral host contact result carrying the candidate, native state and source-self fact;
- `Rac1NativeProjectileMotion`: position-plus-step ballistic recurrence with caller-owned vertical decrement.

For the bounded Goal 1 Bomb Glove slice, the host supplies a batch of those contact
facts after its collision query. `Rac1BombGloveSession` owns candidate admission
and retires the launched projectile when at least one contact is admitted, matching
the recovered contact-driven termination path. The current visible timeout remains
an explicitly host-owned presentation fallback because natural expiry is unresolved.

The generic layer deliberately does **not** assign universal launch speed, lifetime, radius, damage scalar, damage flags, faction filtering or post-contact state machine. Those remain projectile/weapon owned.
For Bomb Glove specifically, `Rac1BombGlove` freezes class `0x79`, source PVar `+0x50`, state `1 -> 2`, observed terminal `0xfe`, authority-state float32 vertical decrement `0.003055555745959282`, countdown values `300/30`, fire gate `20`, and splash envelope `2.0 / 0x00830000`.

The separately retained class-`0x4a` family is now represented by `Rac1Class4aWeaponFamilySession`. It keeps its recovered `0 -> 1` projectile launch, PVar `+0x30` source ownership, 10/20 staging/fire cadence and `1.0 / 0x00010000` direct-victim envelope, but deliberately has no `Rac1WeaponId`, ammo capacity, target filter or implicit projectile-completion rule. Those remain blocked on item/family binding evidence.

The previously promoted class-749 Bomb consequence has been withdrawn: the retained class-749 witness proves a `1.0 -> 0.0` consequence, while the actual Bomb splash envelope is `2.0`. No retail witness yet proves how that envelope maps onto the representative class-749 health state.

## Reproduction

Run:

`tools/rac1-projectile-hit-pattern-probe.py --savestate <SCUS-97199 state> --zstd-dll <zstd.dll> --out research/generated/rac1-projectile-hit-patterns.json`.

The report verifies exact constructor, launch, motion, countdown, world-query, contact, splash, direct-record and source-self-filter instruction witnesses. The embedded managed-live trace contains only derived addresses/scalars/state transitions and no retail payload bytes.
