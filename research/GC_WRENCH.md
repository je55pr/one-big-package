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

## Other wrench-family state leads

State 19 does not directly call the recovered descriptor constructor in its dispatch block. Its normal damage emission therefore remains **UNKNOWN**.

State 21 likewise contains no direct call to `0x00311E90`, `0x003121E0`, or `0x002D68B8`. This is consistent with throw-state code launching/managing another object whose later update owns collision damage, but that object identity and damage tuple are still **UNKNOWN**.

A later public UYA header names class `0x18AB` as `WEAPON_WRENCH`, but class `0x18AB` is absent from GC's parsed gameplay/vtable class sets across all 27 levels. It must not be imported as a GC class identity.

## Evidence status

**CONFIRMED:** GC damage-record byte layout/use; player-state dispatch table; state `19/20/21/31` destinations; state-20 descriptor tuple; state-20 descriptor qualifies for the class-500 break predicate; state 21 has no direct descriptor/collision call in its dispatch block.

**CORROBORATED:** collision-record field names from later public structures; later names `COMBO_ATTACK`, `JUMP_ATTACK`, `THROW_ATTACK`, and `WALLOPER_ATTACK` for the matching numeric state IDs; state 20 as part of the wrench-family player attack behaviour.

**UNKNOWN:** state-19 combo damage construction; thrown-wrench live object identity and damage construction; exact semantic role of damage index `71`; native hit-volume timing/shape; animation timing; hit effects; upgraded wrench differences; damage interaction with non-crate targets.

The native implementation should therefore expose the recovered state-20 damage metadata without pretending the unresolved ground/throw paths are known.
