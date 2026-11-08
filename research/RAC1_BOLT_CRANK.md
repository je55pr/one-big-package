# R&C1 bolt crank animation and turning

Authority is the supported original NTSC-U `SCUS-97199` disc. Novalis class
280 is updated at `0x002e0c68`; its 64-byte PVar supplies turns at `+0x30`
and the reusable flag at `+0x38`. Authored instances 296 and 297 both specify
one turn and are non-reusable.

The local read used overlay SHA-256
`81673b8dcf5930c7313a7c83d0f76e5e1948691dc09af63dd8c8f6a04f9a72ae`.
The bounded loaded-code range `[0x002e0c68, 0x002e1ac0)` hashes to
`97bcda3ab6e4bce412cd527c79f8cd417d506d76b8d2dda24034c64f98d6acb0`.
These identities can be reproduced with `Rac1ExecutableOverlay.Read` and
SHA-256 over its `Read(start, length)` span. No extracted assets, executable
bytes, private source paths or captures are retained here.

## Decompilation crosswalk

[Lombyte at be4134e4](https://github.com/lombyte-project/Lombyte/tree/be4134e4c9f559f6f0a022c59a443c5f6cc64d70)
provided the implementation leads: `bolt_crank_update` in
`src/overlays/shared/gameplay/entities/novalis_elevator.c` and
`HERO_STATE_CRANK` in `src/overlays/shared/gameplay/hero/hero_set_state.c`.
Its GPL-3.0 licence was inspected. No external C or assembly source is imported
into OBP. The C# controller is an independent implementation; overlay-specific
addresses must be resolved from the retail overlay rather than assumed from
shared decompilation extern labels.

Direct retail instruction inspection also identifies the crank's calls to
Novalis animation selector `0x00247a90` (including the idle selection call at
`0x002e1554`). The four clips below were decoded from the provider's verified
LEVEL0 carrier, not inferred from neighbouring sequence numbers.

| Owner | Sequence | Purpose | Frames | Timing |
|---|---:|---|---:|---|
| Ratchet | 68 / `0x44` | Wrench insertion | 13 | constant rate .25 |
| Ratchet | 63 / `0x3f` | Holding the crank | 13 | constant rate .25 |
| Ratchet | 64 / `0x40` | Slow turn | 60 | per-frame rates |
| Ratchet | 65 / `0x41` | Fast turn | 46 | per-frame rates |
| Wrench class 71 | 11 / `0x0b` | Inserted wrench | 12 | decoded native timing |

## Implemented rules

`Rac1BoltCrankSession` owns source action `0x3b`, the seven-update Square
buffer, idle cooldown, distance/facing/height gates, forward-stick projection,
speed acceleration and thresholds, radial spring, tangential facing, handle
rotation, angular progress, idle unwind and completed lowering. It uses the
authored turn count rather than a fixed progress increment.

Entry selects Ratchet 68 with eight blend ticks and wrench 11 with thirteen.
Insertion completion selects idle 63 with seven ticks. Idle-to-slow,
slow-to-fast and slow-to-idle use fourteen ticks; fast-to-slow uses sixteen.
The fast/slow thresholds are .6/.4 of the native maximum `3.7 / 60` movement
step; acceleration is `5.5 / 3600`. Square after the first sixty action ticks
or completed progress releases the interaction. Releasing the stick stops
turning while retaining the grip. Completed non-reusable cranks lower .5 units
at .5 units per second.

The existing player animation clock samples the clips and their hand socket.
The wrench retains its insertion terminal pose. Existing linked-door and
music mechanisms consume the same progress/completion; unwind updates door
positions and restores host door collision too. Cranking blocks equipment use
and Quick Select, and reset/world retirement releases scene-local ownership.

## Boundaries and manual checks

Godot supplies held-wrench availability, support, camera-relative input and
scene transforms. Planar crank motion follows the source controller while
vertical support remains the existing Godot contact adapter. The native crank
camera mount, mirror-animation option, nearby-hostile clearance scan and
native save-bit/callback machinery are outside this change. The active player
camera remains the control basis; this is not full retail camera or collision
parity. Native EE floating-point/trigonometric bit equivalence is not claimed.

Manual retail-loaded inspection exercised both authored cranks through
68 -> 63 -> 64 -> 65 -> 64 -> 63, resumed turning, completion, standing
handoff and lowering. A rendered Novalis run used the existing movement
bookmark boundary and ordinary named input actions to inspect insertion,
grip, turning, linked-door opening and subsequent ordinary movement. A
rendered cancellation/re-entry also released the grip, unwound the door and
restarted insertion before a subsequent completed turn. The direct temporary
SceneTree exit printed out-of-tree transform warnings from the existing
`Rac1NovalisRouteMechanism.Dispose`; no crank exception occurred.
Local capture/inspection drivers stay outside the repository; no test infrastructure
or retail evidence payload is added.
