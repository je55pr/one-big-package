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

The async-supervision step leaves 121 partials, 470 fields and 314 mutable
fields. It removes the two shared verification-result/progress fields, adds one
verification owner, a readonly root-operation owner and a bug-view cancellation
scope. Total field count is not the goal: background jobs can no longer publish
into unrelated root fields or outlive the resources they use.

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
| Actor runtime set, views, placements, reward/contact bodies | `ActorWorldHost` for the current world lease; actor controllers remain source-owned | Compose factories and source tick bridges; see [ACTOR_ADAPTERS.md](ACTOR_ADAPTERS.md) |
| Actor source tasks and animation libraries | `ActorSourceCache`, source/build/path/length/write stamp | Attachment invalidation and root shutdown; world transitions retain only decoded data |
| Cameras, menus, overlays and HUD | Root UI/camera objects plus world-local subscriptions/views | Input/composition bridge; binding retirement runs while retiring-world facts remain available |
| Capture/profile/report writers | `HostOperationScope` for launched work; existing persistent writer owns flushing | Root drains work before teardown; report views have narrower cancellation; preserve schema/build checks |
| Optional source hash verification | `SourceVerificationOperation`, exact source/build/path/length/write stamp | Root renders only the current attachment's progress/outcome; retirement cancels the old job |

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
   Each scene lease cancels its own work before disposing scene resources;
   cancellation cannot carry into a replacement world.

Only the source entry adapter can override the active lease's authored start.
The ownership fingerprint changes for that adapter retain its original rationale;
it gains no player-global mutation. The destination admission adapter also
cancels older pending navigation before it checks the requested source/provider.

## Async audit and staged follow-ups

| Work | Current observation/lifetime | Decision |
|---|---|---|
| Deferred bootstrap waiting for shared weapons | Previously discarded task and uncancellable frame wait | Now owned by scene lifetime; newer entry/menu/quit cancels it; shutdown awaits completion; unexpected failures are reported with superseded status |
| Shared weapon preparation | `FusionWeaponPreparation` stores completion/cancellation; main thread publishes or reports failure | Retain attachment ownership; level teardown cannot cancel persistent source assets |
| Actor imports/placement/animation jobs | `ActorSourceCache` owns source-stamped imports, `ActorWorldHost` owns pending activation | Root operation scope drains native readers; version/source retirement cancels and observes late outcomes; factories own construction rollback |
| `RunCaptureAsync`, `RunShotsAsync`, `RunStressSwitchAsync` | `HostOperationScope` stores completion and observed failures; cross-world traversal is intentional | Quit cancels signal/capture waits, drains admitted tasks and reports failures with a nonzero status before releasing resources |
| Movement-state load / forward replay | Root-owned launch; existing player check after collision registration; forward recording links to the current scene lease | Root cancellation cannot restore/update a released player; world change ends sampling and detaches the physics observer |
| Bug save / export / replay | Existing save task is awaited to finish persistent IO; export/replay use root supervision plus a view token | Cancellation is forwarded through recording validation, hashes and bounded ZIP copying. Temporary export cleanup is retained. Automated children are stopped on every exit path; user-opened reproduction windows remain independent. Import remains synchronous |
| Recording save and replay bookmarks | Root-owned completion; persistent writer flush remains deliberately non-cancellable | Shutdown admits no new save jobs and waits for saved prefixes before releasing writer resources; the existing final recording/replay callbacks, schemas and native tick order stay unchanged |
| Optional native hash verification | `SourceVerificationOperation` owns immutable attachment stamp, progress and one outcome | Attach/detach or quit cancels it; metadata is checked at most every 250 ms during progress and always before publishing an outcome; reader closure is awaited |
| Explicit world/player/native smoke commands | Most have top-level failure-to-exit handling; several intentionally traverse worlds | Root-lifetime diagnostics, not automatically canceled on every world transition; preserve native tick ordering |

Cancellation is expected only for the operation's own token. Other faults remain
observable, including faults arriving after supersession; a superseded failure
cannot request a new world's exit. Godot frame subscriptions are removed in the
owned wait's `finally`. No broad catch-and-ignore or world-origin player gate is
introduced.

`HostOperationScope` is a bounded task owner, not a service container. It records
work before invoking it, so a task which immediately requests quit cannot escape
the drain or deadlock on its own registration. Cancellation suppresses only an
`OperationCanceledException` carrying that job's canceled token. Other failures,
including faults after supersession and throwing cancellation callbacks, remain
in an explicit queue drained by the host and by quit preparation. A shutdown
failure upgrades an earlier successful quit result. External engine teardown also
requests cancellation; normal app exits await completion before releasing audio,
scene and source resources.

Superseded failures remain logged without quitting a replacement view/world.
During shutdown, they still produce a failing exit status. A failed report save
is observed without skipping the subsequent writer flush or audio drain. The
complete movement-state diagnostic is admitted to the root owner, including its
existing finite settling checks and replay sampling, so cleanup waits for its
whole task rather than only the inner load/replay helpers.

`GodotAsyncWait` disconnects parameterless signal subscriptions on completion or
cancellation. Successful waits retain the signal's callback phase, including
`FramePostDraw`, so a capture cannot pair an earlier image with later metadata.
Capture watchdog subscriptions are removed on every outcome, including IO failure
and cancellation. Unsuccessful captures/reports remain failures, not silent passes.

The remaining order is to move actor factories, source-stamped import caches and
registration cleanup under #309 using this lease boundary, while #310 measures
portable contracts independently. Actor imports retain their existing poll/reset
behavior until that focused migration; do not claim they are already supervised
by this step. Do not rename host files or absorb native enemy/weapon behavior
changes into these extractions.

## Validation

`WorldSceneLifetimeTests` exercise candidate rejection, failed scene construction,
stale player generation, partial activation, repeated native/authored transitions,
authored starts, binding-retirement order, cancellation, late failure observation
and disposal. `WorldSceneOwnershipArchitectureTests` reject reintroduced writable
world fields and unowned deferred bootstrap entry.

`HostOperationScopeTests` exercise root/UI cancellation, cross-world root work,
synchronous/asynchronous faults, unrelated cancellation, late errors, immediate
quit reentrancy, callback failure and a complete shutdown drain.
`SourceVerificationOperationTests` check delayed results from retired attachments,
exact stamp admission and progress/outcome ownership. Architecture guards reject
unowned critical launches and reintroduced shared verification fields.
The invocation guard scans all `OBPGame` partials with Roslyn, including diagnostic
files; targeted syntax cases cover discarded/assigned calls and ownership wrappers.

The headless `tools/player-session-smoke.ps1` checks real native/Lab transitions,
one player/scene, retained session state, missing-source/failed-stage isolation
and canceled navigation, signal-phase retention, subscription cleanup and a
canceled capture that never reads the viewport or writes output. The full reconciliation gate still covers profile/save,
exact replay, bookmarks and fresh-process bug-report reproduction. Portable tests
prove host contracts, not retail-native equivalence; authorised retail and actual
visual/audio/controller checks retain their existing boundaries.
