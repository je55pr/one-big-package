# R&C1 wrench combat archaeology

Status: evidence checkpoint for issue #55. This document records retail-backed findings only; implementation is intentionally deferred until the native hit/contact path is fully recovered.

## Proven player-side state

- A grounded Square press enters Ratchet internal action state `0x13` (19).
- Neutral is action `0`, jump is action `7`, and crouch is action `4` in the same state field.
- Action `0x13` dispatches through the player jump table to handler `0x0021B1D0`.
- Sequence 23 is the visible animation anchor for the first grounded swing, but is not used as proof of hit-active timing.
- The first grounded swing uses attack-profile index `0` via `P+0xA60`.
- Its 0x2C-byte profile row begins `[0, 0, 33, 19, 26, 24, 31, 17, 23, 17, 24]`.
- These profile values are small integer timing/state data, not evidence of spatial hit radii.
- `P+0xA5C` is initialized to Ratchet's attack-facing yaw when action 19 begins.
- `P+0xA54`, `P+0xA58`, and `P+0xA64` are cleared at ordinary first-swing entry.
- `0x00214ED8` is a target/facing acquisition helper which can store a Moby pointer in `P+0xA54` and bearing in `P+0xA5C`; it is not the damage emitter.

## Proven wrench lunge recurrence

Paired neutral and Square captures isolate a controller-owned forward lunge during the attack:

- `G+0x110` becomes `0.07333334` for eight sampled updates while remaining zero in neutral.
- `G+0x114` ramps from zero to `0.07333334`, then decays back to zero.
- The `G+0x114` magnitude matches the observed XYZ movement magnitude during the native wrench lunge.
- This proves attack-owned movement only. It does not establish the hit window or wrench range.

## Proven native damage-record contract

Class-500 crates consume the game's native damage records rather than receiving a wrench-specific callback:

- `Moby+0xA4` is an 8-bit damage-record slot index; `0xFF` means no record.
- The record pool contains 64 entries of 0x40 bytes, based at `0x00178100` in the observed retail state.
- Crate lookup routine `0x0025A420` validates the slot and returns the record for that victim Moby.
- Class-500 crate logic tests record `+0x2C` as a float and only breaks when the damage is positive.
- `0x00259A88` copies a richer source descriptor into one of these victim records.
- Compact constructor `0x00259BC8` writes caller-supplied fields including flags, record `+0x2C` damage, victim Moby at `+0x34`, then assigns the slot index to victim `Moby+0xA4`.
- Engine contact routine `0x001F2868` traverses level/Moby collision data and can bind contacted Mobies into the same damage-record pool.
- When supplied a descriptor, `0x001F2868` reads descriptor `+0x1C` and compares it against an existing victim record's `+0x2C`, directly linking the descriptor damage field to the crate-visible native record.

## Ordinary action-19 call boundary

The action `0x13` case body at `0x0021B1D0..0x0021B698` makes 12 calls to 11 unique functions:

`0x2167D0`, `0x1FEF20`, `0x1FF488`, `0x1FF218`, `0x25B868`, `0x200130`, `0x2001F0`, `0x211E30`, `0x214ED8`, `0x212088`, and `0x2120D8` (twice).

There is no direct call in this case body to `0x259888`, `0x259A88`, `0x259BC8`, or `0x1F2868`. Several neighboring player actions do use those generic damage/contact routines, so ordinary Square must reach contact processing by a different path or a separately scheduled update.

## Active crate witness selection

A canonical slot-1 census found 77 active class-500 crates. The nearest intact record from the fixed Veldin start was live index 50 / UID 50 at approximately `(169.976, 160.987, 31.234)`, about 43.2 world units from Ratchet.

Earlier nearby class-500 records around 9 units away were inactive/destroyed records. Native Square traces against one such record produced no Moby or PVar transition, so those traces are retained only as negative controls.

The direct geometric route to the nearest active crate crosses real canyon topology. No hit/range conclusion should be inferred from inability to walk straight to that record.

## Explicitly rejected inferences

- Sequence 23 duration is not the native wrench hit-active window.
- The attack lunge recurrence is not the hit volume or range.
- Damage `1.0` observed in a player damage loop gated on action `0x10` is not ordinary-wrench damage evidence.
- Damage `3.0` supplied to `0x259888` in action `0x20` is not ordinary-wrench damage evidence.
- Generic `0x1F2868` calls in neighboring actions do not prove that action `0x13` uses that same path directly.
- Repeated attacks, combo progression, stagger, cooldown, recovery, and target filtering remain unresolved unless separately evidenced.

## Resume point

Next, inspect second-level callees reachable from the exact action-19 call set, especially the remaining opaque helpers, for a bridge into contact/damage handling. If no such bridge exists, test the separately-scheduled-object hypothesis by comparing live Moby/update state during neutral versus action `0x13` and identifying a wrench/contact object whose update ultimately produces the native victim damage record.

Implementation should begin only after the retail chain `Square/action 0x13 -> active contact test -> eligible victim -> native positive damage record` is proven. The first integration target remains a class-500 crate so that positive damage can feed the already-recovered crate break and bolt-collection path from issue #60.
