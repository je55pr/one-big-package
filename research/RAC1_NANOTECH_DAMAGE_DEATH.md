# R&C1 Ratchet Nanotech, damage, death and respawn

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`). The findings below come from loaded retail executable disassembly plus controlled Veldin PINE witnesses. Local raw traces, disassembly dumps and retail payloads remain outside Git.

## Nanotech storage and reduction

Retail routine `0x2050c0` updates the player Nanotech integer at address `0x001415f8` (`player base + 0x22a8`). It subtracts incoming integer damage and clamps negative results to zero before calling the following player update helper.

The controlled Veldin savestate reads `4` at `0x001415f8`. This document treats four only as the witnessed Veldin initial/respawn value; it does not infer a trilogy-wide maximum-health rule.

## Representative enemy consequence

The recovered class-749 hostile state machine enters attack state 7 and sequence 5. At native animation marker `34.0`, it calls the generic damage-record path with literal damage magnitude `1.0`.

Player-side routine around `0x222e00` polls that damage queue. When a record is present, it converts the record magnitude to an integer and calls `0x2050c0`; if no record is returned, the same path supplies literal `1`. This ties the recovered class-749 attack event to a one-Nanotech player consequence without inventing a generic enemy-damage model.

## Zero-health and death boundary

Player routine `0x222490` checks the same `player + 0x22a8` Nanotech field and takes its zero-health path when the value reaches zero. OBP therefore exposes zero Nanotech as a bounded dead state, but does not assign an unverified combat death animation or checkpoint rule.

Controlled Veldin fall trials provide a separate environmental-death witness. Ratchet enters native death sequences 10 then 11 while Nanotech remains `4`. At the kill/reset boundary the field becomes `0`; when Ratchet is restored near the authored class-0 start, Nanotech returns to `4`.

The previously recovered movement work independently establishes that respawn placement is approximately `(132.09, 115.48, 31.4266)` and clears controller momentum. This Nanotech slice owns only health/life state: host placement and `Rac1RatchetMovementController.Reset()` remain separate responsibilities.

## Runtime promotion

`Rac1RatchetNanotechSession` is RAC1-owned and engine-independent. It starts at the witnessed Veldin value `4`, accepts only the recovered class-749 marker-34/damage-1 event, reaches dead state at zero, models the witnessed environmental reset boundary, and restores `4` on respawn.

The API deliberately rejects unproven attack shapes and does not add shared Runtime or Godot health semantics.

## Deliberately unresolved

- broader checkpoint selection outside the witnessed Veldin authored start;
- invulnerability windows, knockback and combat death presentation;
- Nanotech upgrades or health rules outside this bounded early-game witness;
- damage consequences for enemy classes other than the recovered class-749 representative.
