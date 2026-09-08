# Going Commando player attack / damage archaeology

**Authority:** `rac2-ntscu-v1.01` / `SCUS-97268`
**Retail ISO SHA-256:** `9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5`

This note records retail-backed player attack and collision-damage findings. Retail executable and level-overlay bytes were inspected only on Jess-Laptop and are not stored here.

The current scope is deliberately narrow: identify native damage metadata emitted by player attack states strongly enough for `OBP.RAC2` to reproduce object interaction semantics. This is not yet a complete wrench controller, animation, hit-volume, or thrown-weapon reconstruction.

## Collision-damage record

Oozla's loaded code constructs compact damage input descriptors through `0x00311E90` and inserts/merges them into per-Moby damage records through `0x003121E0`. The class-500 crate path consumes the resulting record through `0x00312B68`.

GC retail dataflow establishes the fields used by that pipeline. Later public PS2 Ratchet structures independently corroborate the semantic names `damageFlags`, `damageClass`, `damageStrength`, `damageIndex`, and `damageHp`. The pinned corroboration used here is `Horizon-Private-Server/horizon-uya-patch` commit `5798bdc57c388020c3ee4aea4321136da8363c97` (`libuya/include/collision.h`, `player.h`, and `moby.h`); it is not authority for GC.

| input offset | output/event offset | meaning | status |
|---:|---:|---|---|
| `+0x14` | `+0x24` | damage flags | **CONFIRMED layout/use; name CORROBORATED** |
| `+0x18` | `+0x28` | damage class byte | **CONFIRMED layout/use; name CORROBORATED** |
| `+0x19` | `+0x29` | damage strength byte | **CONFIRMED layout/use; name CORROBORATED** |
| `+0x1A` | `+0x2A` | damage index halfword | **CONFIRMED layout/use; name CORROBORATED** |
| `+0x1C` | `+0x2C` | damage HP float | **CONFIRMED layout/use; name CORROBORATED** |
| `+0x20` | `+0x30` | flags word | **CONFIRMED layout/use; exact semantics UNKNOWN** |

The input descriptor also carries a source Moby/pointer at `+0x10` and contact/vector data at `+0x00..+0x0F`.

## Player attack-state dispatch

Loaded Oozla code at `0x002BA208` reads the player state from global `+0x2294`, bounds it to 147 entries, and dispatches through the table at `0x002A1CE0`.

Relevant retail table entries are:

| state | loaded destination | later public naming |
|---:|---:|---|
| `19` | `0x002BB118` | `COMBO_ATTACK` |
| `20` | `0x002BA900` | `JUMP_ATTACK` |
| `21` | `0x002BAE08` | `THROW_ATTACK` |
| `31` | `0x002BB7CC` | `WALLOPER_ATTACK` |

The numeric states and destinations are **CONFIRMED from GC retail**. The descriptive names are **CORROBORATED only** by later public game structures; no GC retail symbol names were recovered.

## State 20 damage specimen

Inside state 20, loaded `0x002BAC48..0x002BACB8` constructs and submits one native damage descriptor. The recovered metadata is:

- source = the player Moby from global `+0x2290`;
- `damageFlags = 0x00010000`;
- `damageClass = 0`;
- `damageStrength = 1`;
- `damageIndex = 71`;
- `damageHp = 2.0`;
- descriptor flags word = `1`.

`0x00311E90` builds the descriptor and the state-20 path then enters the generic collision/damage routine at `0x002D68B8`. This exact tuple satisfies class 500's recovered native break predicate.

A systematic pass over all 31 Oozla callers of `0x00311E90` found state 20 to be the only constructor neighbourhood that actually builds a descriptor with damage index `71`. This strengthens the fingerprint but does not, by itself, turn `71` into a named wrench identifier.

## State 21 live weapon identity

State 21 still contains no direct call to `0x00311E90`, `0x003121E0`, or `0x002D68B8`, so its eventual damage descriptor remains **UNKNOWN**. The live Moby that the state launches, however, can now be identified from GC retail dataflow.

The generic player-state setter at loaded `0x002BEB90` takes the requested state in `a0` and writes it to player-global `+0x2294` at `0x002BECE4`. One concrete state-21 transition occurs at `0x002C52D4..0x002C52EC`:

- `0x002C52D4` reads the effective live weapon index from player-global `+0x1248`;
- `0x002C52D8` loads `10`;
- `0x002C52DC` is `bnel` and skips the state-21 call when the index is not `10`;
- on equality, `0x002C52E4..0x002C52EC` calls `0x002BEB90(21, 1)`.

The weapon allocator at loaded `0x002B0608` explains what `+0x1248` means. It starts from the selected/requested weapon index at `+0x22E4`, resolves any fallback into an effective index in `s2`, stores that index to `+0x1248` at `0x002B072C`, allocates the corresponding live Moby, and stores the returned pointer to `+0x1220` at `0x002B0788`. Teardown around `0x002B1CC8` clears `+0x1248` while destroying and clearing the paired `+0x1220` object. Index `10` also has an explicit post-allocation special path beginning at `0x002B07C4`.

The allocator maps effective indices through the byte table whose **correct** EE address is `0x00139568`. The `addiu` immediate at `0x002B06A8` is negative and sign-extends; interpreting it as unsigned would incorrectly produce `0x00149568`. In the retail executable file image, slot `10` of the correct table contains `10`, so effective index `10` selects metadata record `10`. Oozla's loaded metadata table starts at `0x002637A0` with stride `0xE0`; record `10` has word `71` at `+0x14` (`0x00264074`), and that `+0x14` word is the class argument passed to the normal live-Moby allocator at `0x002B0738`.

The other end of the chain is also explicit. State 21 checks that player-global `+0x2294` is still `21` at `0x002BB0A4` before calling helper `0x002B9EF0`. That helper loads the current live weapon Moby from `+0x1220` at `0x002B9FAC`, then writes live Moby state byte `10` to object offset `+0x20` at `0x002B9FE4`.

Therefore **GC retail confirms that player state 21 uses effective weapon index `10` and live Moby class `71`**. The later public name `THROW_ATTACK` remains corroborative rather than a recovered GC symbol, and the class-71 object's own collision/damage construction is not yet recovered. State 20 independently using damage index `71` is a useful numeric fingerprint, but it is not treated as proof that the damage-index field is itself a Moby-class field.

## Other wrench-family state leads

State 19 does not directly call the recovered descriptor constructor in its dispatch block. Its normal damage emission therefore remains **UNKNOWN**.

A later public UYA header names class `0x18AB` as `WEAPON_WRENCH`, but class `0x18AB` is absent from GC's parsed gameplay/vtable class sets across all 27 levels. It must not be imported as a GC class identity. GC's independently recovered state-21 class `71` is the relevant native identity for this path.

## Evidence status

**CONFIRMED:** GC damage-record byte layout/use; player-state dispatch table; state `19/20/21/31` destinations; state-20 descriptor tuple; state-20 descriptor qualifies for the class-500 break predicate; state 21 has no direct descriptor/collision call in its dispatch block; state-21 entry gate on effective weapon index `10`; effective-index/current-Moby pairing at `+0x1248/+0x1220`; retail index-10 mapping to metadata record 10; metadata record 10 live Moby class `71`; state-21 handoff to that current live weapon object.

**CORROBORATED:** collision-record field names from later public structures; later names `COMBO_ATTACK`, `JUMP_ATTACK`, `THROW_ATTACK`, and `WALLOPER_ATTACK` for the matching numeric state IDs; state 20 and state 21 as wrench-family player attack behaviour.

**UNKNOWN:** state-19 combo damage construction; class-71 thrown-object damage construction; exact semantic role of damage index `71`; native hit-volume timing/shape; animation timing; hit effects; upgraded wrench differences; damage interaction with non-crate targets.

The native implementation can therefore expose both the recovered state-20 damage metadata and the state-21 live weapon identity without inventing the unresolved thrown-object damage path.
