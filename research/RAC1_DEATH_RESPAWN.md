# R&C1 death and respawn state boundary

Authority: NTSC-U original retail (`SCUS-97199`, build
`rac1-ntscu-original`). This note reconciles the retained Veldin fall/death
witness with the separately recovered Nanotech, player-start, crate-persistence,
weapon-ammo and campaign-state work. It intentionally does not promote a
checkpoint system from host behavior.

The payload-free machine-readable boundary is frozen in
[`generated/rac1-death-respawn-boundary.json`](generated/rac1-death-respawn-boundary.json).

## Proven opening-Veldin environmental restart

The controlled retail Veldin fall witness is the only restart path with a
recovered automatic gameplay-side death gate. Level 2 separately proves an
environmental death/restart redirect once an active checkpoint record already
exists, but its checkpoint writer/activation trigger remains unrecovered.

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

The authored Veldin class-0 yaw is `0.6627015` radians. A new controlled
`.05` environmental-death trace independently observes retail restore yaw
`0.6627014875`, matching that authored value. The same trace starts from native
position `(106.5274, 230.3423, 34.4037)` with Nanotech `1`, reaches Nanotech `0`
at the death boundary, then restores Nanotech `4` together with the authored
class-0 position and yaw. Yaw reset is therefore proven for this Veldin restart
rather than inferred from the authored record.

## State preservation/reset matrix

| State | Retail death/restart result | Evidence boundary |
|---|---|---|
| Nanotech | **reset to 4** | Veldin fall witness: 4 through sequences 10/11, 0 at reset, 4 after restore |
| player position | **reset to authored Veldin class-0 position** | post-restart position matches class-0 / instance-0 start |
| player/controller motion | **reset** | retained movement witness observes momentum cleared |
| player yaw | **reset to authored Veldin class-0 yaw** | controlled `.05` environmental restart restores `0.6627014875 rad`, matching authored class 0 |
| checkpoint selector/record | **level-2 active record and non-class-0 restart placement proven; generic selector/activation trigger unresolved** | Veldin `.05` still rebuilds from class 0, while level 2 consumes active `0x001bb830` through loaded routine `0x00286520` and redirects restart to its stored transform |
| equipped weapon | **unresolved across death** | live equipped ids are recovered separately, not their death semantics |
| ammo | **unresolved across death** | item-10 ammo storage/accounting is recovered separately |
| Bolt wallet | **unresolved across death** | global Bolt counter is recovered separately |
| transient Bolt pickups | **unresolved across death** | pickup classes/lifecycle are recovered, not restart treatment |
| class-500 destroyed/UID state | **preserved across the controlled Veldin environmental restart** | both recovered 0x100-byte UID maps remain byte-identical through the exact Nanotech-0 -> class-0 rebuild boundary; planet/future-checkpoint clearing remains unresolved |
| hostile/enemy state | **unresolved across death** | class-749 live state/health are recovered separately |
| level-script state | **unresolved across death** | no death-owned script reload/reset boundary is retained |
| campaign CurrentLevel / discovery / completion | **not a checkpoint contract** | their runtime/save ownership is recovered separately; no death mutation is inferred |

A value being stored in retail memory or serialized to the memory card does not
by itself establish its behavior during death. Likewise, an OBP object surviving
a host-side teleport is not evidence that retail preserves that object.

## Native Veldin rebuild path

Loaded routine `0x00204c60` scans the live Moby pool for class `0`, grounds the
selected Moby through helper `0x00259698`, copies its position into player state
at `0x0013f3d0`, and carries the class-0 yaw into the reset path. The new
controlled death returns exactly the authored class-0 position and yaw.

The tempting record at `0x0013e090..0x0013e0af` is not checkpoint authority.
When global `0x00160540` is zero, `0x00204dcc..0x00204dd8` copies the rebuilt
player-state prefix into that record. Editing only its XYZ/yaw before a controlled
death does not redirect respawn: retail overwrites the edit with authored Veldin
values at restart. `RAC1_CHECKPOINTS.md` retains that negative Veldin boundary
alongside the positive level-2 checkpoint restart witness and its still-unresolved activation trigger.

## Positive level-2 checkpoint restart boundary

A separate controlled level-2 environmental death now proves checkpoint-directed restart placement. The loaded live class-0 start is `(210.5431519, 170.2037964, 25.3327236)`, yaw `2.3095138`, while an active record at `0x001bb830` stores `(205.5801544, 163.0475159, 26.0592937)`, yaw `0.7809665`. Retail reaches Nanotech `0`, briefly rebuilds the class-0 start, restores Nanotech to `4`, then redirects player/live-Moby state to the record transform about 11 ms later.

Loaded level-2 routine `0x00286520` independently consumes the active record and copies record `+0x10..+0x2f` into player state at `0x0013f3d0`. This proves checkpoint restart placement/consumption, but not the gameplay writer, trigger or policy that originally activates/populates the record. The level itself was entered through a synthetic harness handoff into the native loader, so that entry path is not ordinary ship-travel evidence; the subsequent death/restart transition is native retail runtime behavior.

No level-2 checkpoint claims are made here for equipped weapon, ammo, Bolts, transient pickups, class-500 UID maps, hostiles or broader script state because those fields were not sampled across this checkpoint redirect.

## Combat death is a separate boundary

The recovered class-749 attack can reduce Nanotech to zero, and the player
zero-health path is proven. A combat-death presentation sequence, checkpoint
selection and restart transition are not retained, however. Environmental
sequences 10/11 must therefore not be aliased onto combat death.

`Rac1RatchetNanotechSession` records this distinction explicitly:
`CombatZeroNanotech` versus `RecoveredEnvironmental`. Its recovered `Respawn()`
operation accepts only the environmental cause. The only automatic host producer
remains the recovered Veldin death-plane gate; level 2 is exercised only by an
explicit retained-witness smoke injection because its gameplay trigger is still
unknown. The Godot host therefore still cannot turn class-749 zero-health into an
environmental restart.

## Host placement boundary

R&C1 level entry and recovered environmental restart placement consume the
engine-neutral `Rac1LevelCheckpointSession` rather than `DebugPlayer`'s cached
development spawn. A full level load starts a fresh session from decoded authored
class 0; same-world environmental restart resolves that session and therefore
uses an active recovered checkpoint when one has independently been supplied.
The host does not infer checkpoint activation from position or script state.

Godot remains presentation/collision hosting only. `RuntimeSpawnSceneAdapter`
mirrors the engine-neutral transform into Godot and adds the existing +3 unit
collision/grounding lift before the player is snapped to collision. Restart also
zeros host velocity/controller state and restores the recovered restart yaw;
those hosting details do not become native checkpoint fields.

The admitted restart resets only the state supported by retained evidence:
Nanotech and player placement/heading/motion. It does not recreate the level-local
crate, pickup, hostile or script owners, and it does not reload campaign/weapon
persistence. Class-500 UID persistence is the one independently measured local
exception: its two recovered maps remain byte-identical across the controlled
Veldin restart. Other untouched fields remain conservative implementation policy,
not promoted retail preservation semantics.

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
- `RAC1_CHECKPOINTS.md`: controlled `.05` Veldin death, class-0 rebuild dataflow,
  positive level-2 checkpoint restart/consumer, reset-snapshot causality and retained-route exclusions.

Further checkpoint work may replace individual `unresolved` entries only with controlled retail before/death/restart evidence. The recovered level-2 placement must not be generalized into an activation policy, and reset rules must not be derived from the debug host, memory-card persistence, or savestate restoration alone.
