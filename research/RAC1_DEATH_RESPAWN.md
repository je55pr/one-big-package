# R&C1 death and respawn state boundary

Authority: NTSC-U original retail (`SCUS-97199`, build
`rac1-ntscu-original`). This note reconciles the retained Veldin fall/death
witness with the separately recovered Nanotech, player-start, crate-persistence,
weapon-ammo and campaign-state work. It intentionally does not promote a
checkpoint system from host behavior.

The payload-free machine-readable boundary is frozen in
[`generated/rac1-death-respawn-boundary.json`](generated/rac1-death-respawn-boundary.json).

## Proven opening-Veldin environmental restart

The controlled retail Veldin fall witness is the only restart path currently
strong enough to promote.

- Ratchet enters native death sequences 10 then 11 while Nanotech remains 4.
- At the recovered reset boundary the player enters native state `0x77`,
  sequence 11 frame 0, and Nanotech becomes 0.
- The restored player returns to Nanotech 4.
- The post-restart native position is approximately
  `(132.09, 115.48, 31.4266)` in retail X/Y/Z coordinates, matching the
  authored class-0 / instance-0 Veldin player start.
- The recovered movement witness independently shows player/controller motion
  cleared across this restart.

This proves an opening-Veldin environmental restart to the authored player-start
position. It does **not** prove that class 0 is a universal death checkpoint, or
that every level/death type reloads the initial player start.

The authored Veldin class-0 yaw is `0.6627015` radians. No retained
post-death yaw sample independently proves that retail restores that yaw, so yaw
reset remains unresolved rather than being inferred from the authored start
record.

## State preservation/reset matrix

| State | Retail death/restart result | Evidence boundary |
|---|---|---|
| Nanotech | **reset to 4** | Veldin fall witness: 4 through sequences 10/11, 0 at reset, 4 after restore |
| player position | **reset to authored Veldin class-0 position** | post-restart position matches class-0 / instance-0 start |
| player/controller motion | **reset** | retained movement witness observes momentum cleared |
| player yaw | **unresolved** | authored start yaw exists, but no retained post-death yaw comparison |
| checkpoint selector/record | **unresolved beyond opening Veldin** | no independent checkpoint owner/record is recovered |
| equipped weapon | **unresolved across death** | live equipped ids are recovered separately, not their death semantics |
| ammo | **unresolved across death** | item-10 ammo storage/accounting is recovered separately |
| Bolt wallet | **unresolved across death** | global Bolt counter is recovered separately |
| transient Bolt pickups | **unresolved across death** | pickup classes/lifecycle are recovered, not restart treatment |
| class-500 destroyed/UID state | **unresolved across death** | UID persistence bits correlate with destroyed crates and survive savestate restore; their death/checkpoint/planet clear event is not proven |
| hostile/enemy state | **unresolved across death** | class-749 live state/health are recovered separately |
| level-script state | **unresolved across death** | no death-owned script reload/reset boundary is retained |
| campaign CurrentLevel / discovery / completion | **not a checkpoint contract** | their runtime/save ownership is recovered separately; no death mutation is inferred |

A value being stored in retail memory or serialized to the memory card does not
by itself establish its behavior during death. Likewise, an OBP object surviving
a host-side teleport is not evidence that retail preserves that object.

## Combat death is a separate boundary

The recovered class-749 attack can reduce Nanotech to zero, and the player
zero-health path is proven. A combat-death presentation sequence, checkpoint
selection and restart transition are not retained, however. Environmental
sequences 10/11 must therefore not be aliased onto combat death.

`Rac1RatchetNanotechSession` records this distinction explicitly:
`CombatZeroNanotech` versus `VeldinEnvironmental`. Its recovered
`Respawn()` operation accepts only the Veldin environmental cause. The Godot
host likewise no longer turns class-749 zero-health into the old development
"Veldin respawn" convenience.

## Host placement boundary

`DebugPlayer.ResetToSpawn()` remains a host placement/movement seam. In the
Veldin world its stored spawn originates from the decoded class-0 player start,
then Godot applies its existing presentation/grounding placement policy. Calling
that method after the admitted Veldin environmental death is consistent with
the recovered position/momentum witness.

The method currently preserves the live RAC1 yaw controller value rather than
asserting the authored class-0 yaw as a retail death reset. That is deliberate:
post-death yaw is still evidence-gated.

No crate, pickup, hostile, Bolt-wallet, weapon-ammo, campaign or level-script
session is recreated by the admitted host respawn. That implementation fact is
**not** promoted as retail preservation. Those fields remain untouched precisely
because their native death semantics are unresolved.

## Related retained evidence

- `RAC1_NANOTECH_DAMAGE_DEATH.md`: Nanotech address/reduction, zero-health path
  and Veldin environmental death sequence.
- `RAC1_PLAYER_MOVEMENT.md`: native movement and restart momentum witness.
- `RAC1_VELDIN_SPAWN.md`: authored class-0 player-start transform.
- `RAC1_WEAPON_INVENTORY.md`: equipped-id and item-10 ammo storage/accounting.
- `RAC1_CRATES_PICKUPS.md`: Bolt wallet/pickups and class-500 UID persistence.
- `RAC1_HOSTILE_COMMON_STATE.md`: representative class-749 live state/health.
- `RAC1_CAMPAIGN_TRAVEL.md`: campaign/save ownership explicitly separate from
  checkpoint state.

A future checkpoint-recovery lane can replace individual `unresolved` entries
only with a controlled death/restart comparison. It must not derive reset rules
from the debug host, memory-card persistence, or savestate restoration alone.
