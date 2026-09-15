# R&C1 minimal weapon inventory and selection

## Scope and authority

This lane promotes only the state needed to represent the wrench and the first
ranged weapon in `OBP.RAC1`: ownership, equipped selection, and ranged ammo.
It does not create a trilogy inventory framework, vendor system, quick-select UI,
projectile model, damage model, ammo capacity, or acquisition flow.

Primary authority is the local NTSC-U retail executable (`SCUS-97199`) observed
through an isolated PCSX2/PINE session. The isolated session used savestate slot
1 and a separate PINE port, so the observations did not depend on another lane's
emulator state.

## Retail selection ids

After loading slot 1, two independent live `u32` witnesses at `0x00140408` and
`0x00141424` both contain `8`. Holding the retail weapon-selection input and
choosing the witnessed first-ranged direction changes both values to `10` while
leaving ranged ammo unchanged. The core therefore promotes only these two ids:

- `8`: wrench;
- `10`: first ranged weapon.

The two live addresses are retained as corroborating mirrors, not promoted as a
stable serialized layout. `Rac1WeaponId.FirstRanged` deliberately uses a role
name so Lane C can own the final weapon-facing name without a merge-sensitive
rename in this lane.

## Retail ammo witness

A 37-entry `s32` table begins at live address `0x0013D428`. In slot 1 every
entry is zero except index `10`, which begins at `6`. After selecting native id
`10`, each accepted Circle attack enters Ratchet sequence `44` and changes that
entry by exactly one:

`6 -> 5 -> 4 -> 3 -> 2 -> 1 -> 0`.

A seventh attempt at zero does not enter sequence `44` and does not underflow
the counter. Selection itself does not spend a round. This is the complete ammo
contract promoted here: accepted ranged use consumes one round; zero ammo rejects
the ranged use; wrench use consumes none. No retail ammo maximum is inferred.

A separate memory sweep over three consecutive accepted attacks independently
found the same `6 -> 5 -> 4 -> 3` progression at `0x0013D450`, which is exactly
`0x0013D428 + (10 * 4)`.

## Ownership boundary

Retail memory also contains 37-byte Boolean progression tables whose contents
change across the available savestates. They include the promoted ids in the
early slot and expand as later slots are loaded. Their exact `Items` versus
`Unlocks` semantics were not pinned strongly enough to expose as a decoder, so
this lane does not commit those addresses or invent acquisition behavior.

Instead, `Rac1WeaponInventory` receives first-ranged ownership as explicit RAC1
gameplay state. Wrench ownership is fixed true for this bounded slice; the slot-1
retail selection begins on native id `8`. An unowned ranged weapon cannot become
the equipped selection.

## Secondary corroboration and non-claims

Pinned Wrench recovery commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`
independently describes RAC1 save blocks for 37-entry ammo, unlock and item arrays,
quick-select state, last-equipped gadget state, and equipped gadgets; it also labels
gadget ids `8` and `10` as Wrench and Bomb Glove. That source is corroboration only.
The executable observations above remain the authority for the state promoted here.

The core intentionally makes no claim that either observed equipped-id address is
the canonical save field, that the unpromoted Boolean arrays have been semantically
decoded, or that sequence `44` defines all behavior of native id `10`. Lane C owns
fire/projectile consequences and may promote the presentation name separately.

`Rac1WeaponInventoryTests` pins the bounded consequences:

- retail ids `8` and `10` stay stable;
- first-ranged ownership gates switching;
- switching itself preserves ammo;
- accepted ranged uses consume exactly one round and reject at zero;
- wrench use is ammo-free;
- invalid negative ammo and an unowned equipped ranged state are rejected.
