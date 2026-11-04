# Root ownership and decomposition (#333 / #376)

`OBPGame` is the Godot composition root. It receives engine lifecycle and input callbacks, creates owned
components lazily, and delegates through narrow ports. New gameplay policy, native encounter state, source
algorithms and diagnostic assertions belong in their actual owners. Existing world, session, actor and operation
owners stay authoritative; extraction must not create replacement sessions or another all-capabilities manager.

The [diagnostic scenario contract](DIAGNOSTIC_SCENARIOS.md) defines the focused observation/action ports and the
bounded external startup proof. The per-file history of the original 122-file root is in
[OBPGAME_ROOT_CENSUS.md](OBPGAME_ROOT_CENSUS.md).

Godot C# node scripts still need `partial` for source generation. Ordinary components do not become nodes merely
to be independently owned. Moving a file without removing privileged access is not encapsulation. Script UIDs,
scene associations, native authority, callback phase/order and all CLI/profile/save/replay wire identities are
preserved.

## Frozen source and scope

The starting commit is `eea806399f864f30949d665701d39d510549a7ac`: 122 root-declaring files (121 `OBPGame*.cs`
plus the `ControllerSmoke.cs` fragment), 27,362 file lines, 756 direct methods, 457 direct fields (312 mutable),
44 exposed direct members and 2,654 distinct cross-file root references. These are measurements, not size quotas.
The frozen origin ledger in `tests/OBP.Tests/root-boundary-policy.json` is hash-pinned and may not be expanded.

## Final architecture

| Responsibility | Owner and lifetime |
| --- | --- |
| Prepare/revalidate a candidate and commit its lease | `WorldSceneLifetime`; candidate/live world |
| Activate source modules, actors, equipment, effects and tick schedule | `GodotWorldResources`; world lease |
| Gameplay ticks and world-specific input routing | registered schedule in `GodotWorldResources` / `WorldGameplayTickBindings`; world lease |
| Failed-entry recovery and Back/menu routing | `ApplicationNavigationCoordinator`; application |
| Scene roots, player node, cameras, HUD, inspection | `GodotApplicationPresentation`; application |
| Ordered shutdown, audio-mix retirement | `GodotApplicationShutdown`; application |
| Selected avatar attachment, ordered physics callback, retirement | `PlayerAvatarPresentationHost`; selected player/world binding |
| Persistent player session, equipment catalogue, profile | `PlayerSessionHost`, `PlayerEquipmentFacade`, `ProfileCoordinator`; persistent player |
| Pause menu and Quick Select projection | `UiModalCoordinator`, `PauseMenuUi`, `WeaponQuickSelectUi`; presentation |
| Source setup, restoration, readiness | `SourceSetupController` / `SourceSetupCoordinator`; application |
| Campaign save admission, reporting, durability | `Rac1CampaignPersistenceHost`; source campaign |
| Capture, replay, playtest, movement reproduction, verification | `CaptureComposition` binding `CaptureReplayCoordinator` and friends |
| Player lifecycle, Fusion weapons, combat input, profile, actor host wiring | `PlayerServicesComposition` |
| R&C1 / R&C2 world-module ports | `Rac1WorldComposition` / `Rac2WorldComposition`; world lease |
| Diagnostic fixtures, scenarios, shot/stress CLI | `ApplicationDiagnostics`, `DiagnosticScenarioRegistry`, `WorldSmokeHarness` |
| Native weapon, enemy, loot, mission, taxi and mechanism behaviour | the source-game modules (`OBP.RAC1/2/3`) and their lease-scoped hosts |

Composition modules (`ApplicationDiagnostics`, `CaptureComposition`, `PlayerServicesComposition`,
`Rac1WorldComposition`, `Rac2WorldComposition`) derive from `CompositionBase` over `ApplicationComponents`. They are
composition roots: they read the application through a small set of shared views and bind owned components to
narrow ports. They are not read-only and are not a service locator (nothing resolves by type). Gameplay classes,
fixtures and scenarios never receive them; `CompositionBaseArchitectureTests` pins the permitted derived types,
forbids use outside composition files and ratchets the shared surface so it can only shrink. A view needed by one
module belongs in that module. The modules still read the application through those shared views, so replacing
them with per-module port records remains a possible further narrowing rather than something this design claims.

`OBPGame.WorldEntry.cs` and every other root partial are retired; the guard rejects a new root partial or member.
The single remaining script keeps its UID, scene association and the public host contracts the bootstrap scene and
fixtures call.

## Remaining root members

Every remaining member is a reviewed composition member with owner `#376`, disposition `permanent` and a
one-sentence reason in the root-boundary policy. They fall into owner references and lazy accessors, established
public host contracts, lazy factories, read-only projections and private one-line forwards; the counts and reasons
are in [OBPGAME_ROOT_CENSUS.md](OBPGAME_ROOT_CENSUS.md#remaining-root-members). A ceiling pinned by
`RootBoundaryArchitectureTests` can only go down. Composition code may expose an existing source implementation or a
native controller's facts, but may not implement its formula, tick, damage dispatch or scenario assertions.

## Extending without touching `OBPGame`

- **A new enemy family** adds a source-qualified recipe/factory to `ActorNativeAdapters`/`ActorAdapterRegistry`; the
  existing actor host binds it. `ActorAdapterRegistryTests.ANewFamilyRegistersWithoutChangingTheWorldHost` proves
  distinct source/class registrations and rejects duplicates.
- **A new authored R&C1 mechanic** implements `Rac1AuthoredMechanism` and registers in `Rac1AuthoredMechanismCatalog`;
  the world registry supplies configure, tick/physics phases and reverse-order retirement.
- **A new weapon** registers its source-qualified implementation with the Fusion/equipment catalogue and, for
  R&C1, its lease-scoped host module; equipment state lives in the persistent player session.
- **A new smoke or diagnostic scenario** registers its stable identity and admission in
  `WorldSmokeHarness`/`DiagnosticScenarioRegistry`, binds focused ports in `ApplicationDiagnostics`, and uses an
  existing typed harness. A new CLI flag belongs in `CommandLineArgs`. `DiagnosticRegistryTests` and
  `WorldSmokeArchitectureTests` pin aliases, admission, owner selection and bounded dispatch.

## Reproducing the measurements

From the repository root, without retail sources:

```text
dotnet run --project tools/OBP.ArchitectureTool -c Release -- check .
dotnet run --project tools/OBP.ArchitectureTool -c Release -- census .
```

The census writes JSON to stdout with per-file/direct-member totals, member keys/hashes and bound cross-file
reference edges; no assets or machine paths are emitted. The native CI job publishes it as the
`root-ownership-census` artifact. Compare exposed ports and ownership as well as field/line totals. The same-build
replay gate validates each revision; recordings from other builds need explicit cross-build investigation.

## #374 R&C3 original-world gameplay owner

The former `OBPGame.Rac3Gameplay.cs` fragment is retired. The world lease's
`GodotWorldResources.Rac3Gameplay` now owns the R&C3 class-500 node roster,
a Godot-free `Rac3Class500WorldRuntime` native-session/admission owner, original-host contact envelope, class-5821 admission
counts, and optional original-world HUD facts. `ActorNativeAdapters` captures
the specific lease module for class-500 observations and source-damage dispatch,
so callbacks referring to a retired world cannot dispatch into the next world.
The native session has source/build admission, original class-500 damage and terminal-state tests, rejection tests and idempotent retirement tests. The existing class-500 host interaction still uses native UYA damage and state
transitions; the source-neutral shared player/weapon selection is unchanged.
This path is event-driven: the former root per-frame R&C3 method advanced only
the diagnostic snapshot gate, not a native gameplay clock.

The R&C3 class-5821 Ratchet-target/pose sampler now belongs to a read-only,
explicit diagnostic scenario (`--rac3-target-seed-audit` on a compatible R&C3
world). The scenario is admitted through the existing bounded source/build/world
runner, waits for entry settling and samples the recovered native selector once.
It does not run on the ordinary gameplay tick or upon opening the inspector,
and it does not mutate an actor or the persistent player. Native subtype +0x95
and AI/player consequences remain unrecovered and are not synthesized.

The frozen root-origin ledger remains immutable. #374 marks all 33 retired
partial members in the retirement set, removes four obsolete source-origin
exceptions, and refreshes only the exact affected actor composition and debug
HUD adapter fingerprints. Full source-free architecture checks and optional
source-authorized UYA TABLE1 sampling cover the extraction.

## #369 R&C1 explosion and held-weapon audio presentation

`Rac1ExplosionPresenter` and `Rac1WeaponAudioPresenter` live under the existing
`PlayerEquipmentPresentationBinder`, which owns selected equipment presentation
and its current-world binding. The two former `OBPGame.Rac1BombEffects.cs` and
`OBPGame.Rac1WeaponAudio.cs` root fragments (including their fields) are retired;
their Godot UIDs move to the replacement presenters. Existing root call sites
remain narrow composition forwards, not caches or gameplay implementations.

The decoded `Rac1EquipmentAssets.Catalogue` and level sound banks remain
source-owned and are **borrowed** by the presenters. Particle sessions, fragment
and burst nodes, shader/mesh caches, native particle-pool views, the looping
weapon voice, and the Taunter voice/timer are current-world state. World
retirement clears scene nodes and CPU particle state, discards mesh materials,
stops voices immediately, and clears bounded diagnostic traces; source
replacement invalidates the binder before new source assets are installed.
Selection and player death continue to call the existing audio reset, while
pause suspends voices without restarting their native pitch or sound cursors.

Native bomb class 1192 bursts, class 122 fragments, explosion class 121 cue,
Mine Glove ground-pool selection, Pyrocitor emitter slots and flame contacts
all keep their original native recipes, original random calls, camera/distance
selection, frame stepping and source textures. Weapon loops retain
(168,1)/(176,1)/(849,2)/(177,4)/(185,0); Taunter remains class 175, steps
0..5, with `max(17, ceil(clip.DurationSeconds * 60))` native spacing.
The explosion owner exposes copied source-qualified frame/class/position event
observations (last 512) and bounded particle type observations (8192), while
weapon audio exposes copied native tick/class/sound/loop observations
(last 4096). Diagnostics consume snapshots instead of root mutable lists.

The frozen root origin remains unchanged: 24 removed members are explicitly
retired; twelve exact temporary composition forwards expire with #376. This
work does not alter the source-native effect generators or the dedicated
effects project (#280/#282-#285). Source-free and authorized native validation
should distinguish engine assertions from an optional human visual/audio
review. The latter remains a separate acceptance gate.

## #365 R&C1 loot and cross-game crate reward presentation

Five legacy root fragments were retired: `OBPGame.Rac1Pickups.cs`,
`OBPGame.Rac1CrateSupply.cs`, `OBPGame.Rac1PlacedBolts.cs`,
`OBPGame.Rac1GoldBolts.cs`, and `OBPGame.CrateBolts.cs`.
Despite the last fragment's old name, its physical class-500 bolts are
**R&C2**, so they are deliberately *not* processed by the R&C1 host.

`Rac1WorldModule.Loot` is the R&C1 world-lease-owned
`Rac1LootPresentation`. Its files hold retail pickup model/material caches,
the 60 Hz motion/attraction tick and sound throttle, authored placed-bolt
registration and delayed physics initialization, gold-bolt placement and
collectible visibility, crate colliders/falling motion, debris, and the
nanotech/ammo presentations. `Rac1WorldContext` retains the native crate,
bolt and placed-bolt sessions. `Rac1LootPorts` forward only explicit player
state, weapon/health checks, sound, source lookup, HUD feedback and campaign
persistence; R&C1's native payout/collection calculations and diagnostic data
remain unchanged. The shared effect RNG retains its across-world lifetime
in `PlayerSessionHost` and its replay-reset entrypoint.

`Rac2WorldModule.CrateBolts` owns the separate
`Rac2CrateBoltPresentation`. Native `Rac2FreshBoltSession` continues to
determine which class-500 pickup IDs and denominations may be collected.
`Rac2CrateBoltPorts` register and credit the *original* actor identity,
generation, pickup ID and build identifier through the persistent player-life
authority. The placeholder orb presentation is still explicitly provisional,
and a stale or disposed world lease can no longer accept pickup callbacks.
No gold-bolt, wallet, ammo, health or crate-reward formula moved into the
application root.

On world retirement, the R&C1 presenter clears transient nodes, collectors,
colliders, cached meshes, sound/tick state and authored-node references; it
does not free the original authored world roots. R&C2's presenter disables
and queues its owned pickup areas, and its native reward session ends with the
same scene lease. The world modules retire these presenters before the scene
root is unloaded. These owners are *not* a new cross-game reward service.

Root boundary policy records the exact removed members as retirements and
updates only the affected composition hashes; the frozen origin ledger is
intact. The existing host coordinator architecture assertion now checks the
new presentation owner instead of expecting a root partial.

## #366 R&C1 explosive crates and Novalis breakable ownership

`OBPGame.Rac1Explosives.cs` and `OBPGame.Rac1NovalisBreakables.cs`
are retired. The new `Rac1WorldModule.Breakables` owns a
`Rac1BreakableHost`, containing independent class-505 and authored Novalis
adapters for their source sessions, presented-node maps, frame accumulators,
hit histories, flash material overrides and destroyed state. The host borrows
world-local actors and presentation; it does not own the persistent player,
wallet, RNG, imported source assets or original authored scene roots.

Native class-505 registration still requires the exact R&C1 native record/PVar,
and uses the existing `Rac1ExplosiveCrateSession` 60 Hz fuse and expanding
state-3 blast. Class 754/704 admission remains restricted to authored R&C1
Novalis nodes and the existing `Rac1NovalisBreakableSession` policies. No
unrecovered fragment program or reward has been invented. #233's metal-crate
blast admission and #237's Novalis cave parity remain independently open.

The lease-specific `Rac1BreakablePorts.Current(owner)` check binds the
actual owning `Rac1WorldModule`, never whichever module happens to be current
later. The host rejects stale damage, bomb/wrench contacts and diagnostic actions
after retirement. R&C1's Moby damage flags, source reward selection and
player-owned random stream are unchanged. Detonation keeps the original
debris, bolt-emission, audio and collider-disable order; only the resulting
typed pickup facts reach the existing `Rac1LootPresentation`. Crate bodies and
falling support remain in that lease-owned presenter, with explicit idempotent
release and bounded contact lookup rather than an adapter reading private
mutable dictionaries. Temporary material copies are restored/released by the
breakable adapter. Both adapters also snapshot each imported node's original
entity state and visibility and restore them before retirement; the explosive
adapter drops any remaining owned contact body. The world module is strictly
single-configuration (a second Configure throws), so a retired lease never
re-registers colliders; the loot presenter frees all remaining bodies during
world teardown. The module's disposal chain releases all owners even if an
earlier owner's disposal throws.

The existing crate acceptance scenario has an optional, explicitly
test-only breakable fixture. It returns source/world-stamped immutable snapshots,
native explosive states and collider presence, with bounded touch/break actions
that reject stale generation/actor identities. The native-session tests, source-
free ownership guards and original engine crate scenario remain the authority
for behaviours that can be checked without inventing retail source facts.


## Acceptance evidence for #376

Recorded against `dev` after the composition extractions (the closing comment on #376 names the exact revisions):

- The portable suite, the Release solution build with warnings as errors, `dotnet format`, the root guard, UID
  coverage and the CI Godot smokes (headless startup, bootstrap lifetime, UV-scroll, foliage, materialless Moby,
  Ratchet visibility, R&C1 hand presentation) pass.
- The authorised retail-backed reconciliation (`tools/test-reconciliation.ps1`, all 17 phases, no skip flags) passed.
- Not performed by automation and still required for acceptance: the human visual, audible and controller review of
  the affected functionality. Optional native-oracle retail tests, PowerShell 7 tests and the symlink test skip when
  their prerequisites are absent; each skip is listed in the evidence rather than treated as a pass.
