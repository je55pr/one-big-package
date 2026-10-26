# OBPGame ownership and staged extraction (#308)

This describes OBP host design, not recovered native behavior. The player/world
contract in [PLAYER_WORLD_CONTRACT.md](PLAYER_WORLD_CONTRACT.md) remains authority
for session boundaries. Native decoders/controllers remain in their source
libraries; Godot observes contacts and renders their results.

## Source census and first boundary

At `fc2f529` there are 121 `OBPGame*.cs` partial files. A syntax census of field
declarations directly inside `OBPGame` finds 474 fields, including 320 without
`readonly` or `const`. This counts declared variables, not regex matches or
compiler-generated fields. Nested helper classes are excluded. These numbers
locate coupling; they are not size gates or proof that every field is misplaced.

The scene-lifetime extraction leaves 121 partials, 469 fields and 314 mutable
fields. Seven former shared mutable fields become read-only projections:
`_worldHost`, `_world`, `_sceneResult`, `_activeDestination`,
`_currentDestinationId`, `_worldPlayerEntry`, `_worldEntrySnapReach`.
The root retains one lifecycle-owner reference and a readonly, unloaded
presentation host for pre-world diagnostic calls. This is a state boundary,
not a claim that the remaining host has been completely separated.

`WorldSceneLifetime<GodotWorldResources>` owns the staged candidate, current scene
lease, entry facts, small last-destination metadata and deferred navigation work.
`GodotWorldResources` owns the staged root, world presentation and bound transient
player node. `WorldEntrySession` in Fusion still owns request generations,
selected-player validation and the entry-policy lease. There is no second player
session or native world loader. The generic resource seam permits deterministic
portable lifetime tests without inventing fake Godot objects.

## Ownership map

| State/callsites | Actual lifetime and owner | Root's remaining role |
|---|---|---|
| `OBPGame.PlayerSession`, `PlayerSessionNativeState`, equipment/health/profile partials | Persistent `ObpPlayerSession` and selected native-state adapters | Compose ports, observe input and render UI; transitions never restore the session |
| `OBPGame.WorldEntry`, destination entry and teardown | `WorldSceneLifetime` and its candidate/live lease | Resolve registered providers and wire Godot scene, selected player and source hooks |
| World root, sky, collision, environment, level audio | `GodotWorldResources` delegates presentation to `WorldHost` | Presentation ticks and contacts; immediate audio retirement at process exit remains explicit |
| Authored entry, R&C1 checkpoint/travel, water/environment context | Source adapters and source campaign session | Supply world-local facts; cannot select player-global profiles or services |
| `OBPGame.Sources`, readiness/config and verification cache | Source-attachment/application lifetime | Configure genuine sources and invalidate consumers after attachment changes |
| Shared weapon assets/preparation, selected avatar/wrench assets | Source attachment/build/file stamp, independent of a destination | Main-thread upload/binding; cancel source preparation and release assets on invalidation/exit |
| Actor runtime set, views, placements, reward/contact bodies | Current world lease; actor controllers remain source-owned | Current binding/reset callbacks; registration/factory ownership is #309 |
| Actor source tasks and animation libraries | Source/build/path/length/write stamp | Existing pruning and source-change reset retained; move ownership with #309 |
| Cameras, menus, overlays and HUD | Root UI/camera objects plus world-local subscriptions/views | Input/composition bridge; binding retirement runs while retiring-world facts remain available |
| Capture/profile/report writers | Persistent player/report operation, not destination geometry | Preserve schema/build checks and supervise critical work in the next bounded #308 step |

All world entry paths, including the authored Lab, use the same owner. Lab keeps
its authored destination and no native destination descriptor. A world still
selects only content and authored context.

## Prepare, activate, retire

1. Resolve selected player assets and capture the destination source stamp.
2. Fusion prepares a plan using the persistent player and registered provider.
3. The host scene owner holds a disposable candidate while Godot stages geometry.
4. Revalidate selected assets, source stamp and ordinary setup before commit.
   Rejection disposes only the candidate and retains the current player/scene.
5. Fusion validates the plan generation before calling the scene activation port.
   Binding retirement can read old-world facts; it completes before the old scene
   lease is released. The new scene is then published for Godot wiring.
6. Activation failure retires partial bindings/resources and returns to selection
   with the persistent player retained. It does not promise geometry rollback
   once old-scene retirement has begun.
7. Retire/dispose is idempotent. Candidate and live resources are independent.
   Old player collision is detached before replacement placement rays run.

Only the source entry adapter can override the active lease's authored start.
The ownership fingerprint changes for that adapter retain its original rationale;
it gains no player-global mutation. The destination admission adapter also
cancels older pending navigation before it checks the requested source/provider.

## Async audit and staged follow-ups

| Work | Current observation/lifetime | Decision |
|---|---|---|
| Deferred bootstrap waiting for shared weapons | Previously discarded task and uncancellable frame wait | Now owned by scene lifetime; newer entry/menu/quit cancels it; shutdown awaits completion; unexpected failures are reported with superseded status |
| Shared weapon preparation | `FusionWeaponPreparation` stores completion/cancellation; main thread publishes or reports failure | Retain attachment ownership; level teardown cannot cancel persistent source assets |
| Actor imports/placement/animation jobs | Source-stamped tasks and world dictionaries; tick polls completion and reset clears bindings | #309 must own admission/versioning, disposal and late completion observation |
| `RunCaptureAsync`, `RunShotsAsync`, `RunStressSwitchAsync` | Root-launched diagnostic work, with cross-world traversal intentional | Next bounded #308 step: root-lifetime supervision, cancellation and failure exit behavior |
| Movement-state load and bug import/export/replay | Existing in-flight state and local error/UI handling | Audit cancellation after awaited IO and root/menu teardown in the same supervision step |
| Optional native hash verification | Background catch reports outcome, but progress/result fields live in root | Give it attachment/operation ownership and reject stale publication in the supervision step |
| Explicit world/player/native smoke commands | Most have top-level failure-to-exit handling; several intentionally traverse worlds | Root-lifetime diagnostics, not automatically canceled on every world transition; preserve native tick ordering |

Cancellation is expected only for the operation's own token. Other faults remain
observable, including faults arriving after supersession; a superseded failure
cannot request a new world's exit. Godot frame subscriptions are removed in the
owned wait's `finally`. No broad catch-and-ignore or world-origin player gate is
introduced.

The remaining order is: finish critical async supervision under #308, move actor
factories/registrations under #309 using this lease boundary, and measure portable
contracts under #310. Do not rename host files during extraction or absorb native
enemy/weapon behavior changes into these steps.

## Validation

`WorldSceneLifetimeTests` exercise candidate rejection, failed scene construction,
stale player generation, partial activation, repeated native/authored transitions,
authored starts, binding-retirement order, cancellation, late failure observation
and disposal. `WorldSceneOwnershipArchitectureTests` reject reintroduced writable
world fields and unowned deferred bootstrap entry.

The headless `tools/player-session-smoke.ps1` checks real native/Lab transitions,
one player/scene, retained session state, missing-source/failed-stage isolation
and canceled navigation. The full reconciliation gate still covers profile/save,
exact replay, bookmarks and fresh-process bug-report reproduction. Portable tests
prove host contracts, not retail-native equivalence; authorised retail and actual
visual/audio/controller checks retain their existing boundaries.
