# OBPGame root census

Final census for issue #376 (parent #333). Source: `dotnet run --project tools/OBP.ArchitectureTool -c Release -- census .`
on `dev` after the final composition extractions, measured against the frozen origin commit
`eea806399f864f30949d665701d39d510549a7ac`. The historical ledger tracks 121 `OBPGame*.cs` files plus one extra
root fragment, `ControllerSmoke.cs` (122 semantic files). The guard is `... -- check .`; see
[ROOT_DECOMPOSITION.md](ROOT_DECOMPOSITION.md) for the architecture and rules.

| Metric | Frozen origin | Final |
| --- | ---: | ---: |
| Root-declaring files | 122 | 1 |
| Root-declaring file lines | 27,362 | 926 |
| Direct methods | 756 | 90 |
| Fields / mutable fields | 457 / 312 | 24 / 17 |
| Exposed members | 44 | 39 |
| Reviewed composition members (all `permanent`) | n/a | 150 |

The remaining root is `game/scripts/OBPGame.cs`. Its line count includes the Godot lifecycle/input callbacks,
bootstrap command-line handling, world-entry/activation wiring, modal/pause factories and the public host
contracts that the bootstrap scene and diagnostic fixtures call. Fewer exposed members is not the objective:
public host contracts are preserved.

Composition moved out of the root, by owner:

| Responsibility | Owner |
| --- | --- |
| Diagnostic fixture catalogue and shot/stress CLI services | `ApplicationDiagnostics` (`game/diagnostics`) |
| R&C1 world-module and enemy-host port binding | `Rac1WorldComposition` |
| R&C2 world-module port binding | `Rac2WorldComposition` |
| Capture, replay, playtest, movement reproduction, source verification | `CaptureComposition` |
| Player lifecycle, Fusion weapons, R&C1 combat input, profile, actor host | `PlayerServicesComposition` |
| Shared read views for composition code only | `CompositionBase` over `ApplicationComponents` (ceiling-pinned) |

File-by-file inventory. "Original owner ticket" is the original migration assignment. Every original file is accounted for:
121 `OBPGame*.cs` partials plus the `ControllerSmoke.cs` fragment; only `OBPGame.cs` remains as a root declaration.

| Original root file | Original owner ticket | Baseline lines | Root today | Current lines |
| --- | --- | ---: | --- | ---: |
| --- | --- | ---: | --- | ---: |
| `game/diagnostics/ControllerSmoke.cs` | #339 | 583 | Non-root helper (not an OBPGame partial) | 585 |
| `game/diagnostics/OBPGame.AcceptanceEvidence.cs` | #336 | 40 | Retired | 0 |
| `game/diagnostics/OBPGame.BugReporterSmoke.cs` | #344 | 176 | Retired | 0 |
| `game/diagnostics/OBPGame.FusionWeaponsSmoke.cs` | #341 | 493 | Retired | 0 |
| `game/diagnostics/OBPGame.MovementSmoke.cs` | #339 | 686 | Retired | 0 |
| `game/diagnostics/OBPGame.MovementStateSmoke.cs` | #344 | 141 | Retired | 0 |
| `game/diagnostics/OBPGame.OrdinaryUiSmoke.cs` | #337 | 82 | Retired | 0 |
| `game/diagnostics/OBPGame.PlayerCapabilitiesSmoke.cs` | #338 | 215 | Retired | 0 |
| `game/diagnostics/OBPGame.PlayerSessionSmoke.cs` | #338 | 206 | Retired | 0 |
| `game/diagnostics/OBPGame.PlaytestSmoke.cs` | #344 | 82 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1AirborneSmoke.cs` | #339 | 125 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1AridiaConstructorbotSmoke.cs` | #340 | 758 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1ArsenalSmoke.cs` | #341 | 158 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1BombGloveSmoke.cs` | #341 | 231 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1CampaignSmoke.cs` | #342 | 268 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1CombatContractSmoke.cs` | #340 | 806 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1CrateSmoke.cs` | #342 | 172 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1EnemySmoke.cs` | #340 | 300 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1EnvironmentalDeathSmoke.cs` | #339 | 132 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1FrogSmoke.cs` | #340 | 612 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1LandingRegressionSmoke.cs` | #339 | 267 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1MetropolisTaxiSmoke.cs` | #343 | 332 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1MissionPlaySmoke.cs` | #342 | 122 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1NovalisBirdbotSmoke.cs` | #340 | 47 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1NovalisMusicSmoke.cs` | #343 | 47 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1NovalisPeckbotSmoke.cs` | #340 | 73 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1NovalisRouteSmoke.cs` | #343 | 147 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1NovalisSwimSmoke.cs` | #339 | 269 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1PlacedBoltSmoke.cs` | #342 | 117 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1PlayerAudioSmoke.cs` | #338 | 210 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1SlopeJumpSmoke.cs` | #339 | 134 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1SmallStepSmoke.cs` | #339 | 144 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1StartupVisibilitySmoke.cs` | #337 | 72 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1SwingshotSmoke.cs` | #339 | 158 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1TraversalContactSmoke.cs` | #339 | 271 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1VeldinPlaySmoke.cs` | #340 | 726 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1VeldinRetailPauseSmoke.cs` | #337 | 72 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1WeaponsSmoke.cs` | #341 | 196 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1WorldRetailReviewSmoke.cs` | #337 | 230 | Retired | 0 |
| `game/diagnostics/OBPGame.Rac1WrenchSmoke.cs` | #341 | 240 | Retired | 0 |
| `game/diagnostics/OBPGame.ReconciliationSmoke.cs` | #344 | 71 | Retired | 0 |
| `game/diagnostics/OBPGame.SharedProfileSmoke.cs` | #338 | 64 | Retired | 0 |
| `game/diagnostics/OBPGame.SmokeDispatch.cs` | #336 | 155 | Retired | 0 |
| `game/diagnostics/OBPGame.SourceSetupCheck.cs` | #337 | 42 | Retired | 0 |
| `game/diagnostics/OBPGame.TrilogyGameplaySmoke.cs` | #344 | 211 | Retired | 0 |
| `game/scripts/OBPGame.ActorAdapters.cs` | #359 | 109 | Retired | 0 |
| `game/scripts/OBPGame.ActorContacts.cs` | #359 | 89 | Retired | 0 |
| `game/scripts/OBPGame.Actors.cs` | #359 | 200 | Retired | 0 |
| `game/scripts/OBPGame.Audio.cs` | #354 | 33 | Retired | 0 |
| `game/scripts/OBPGame.BugReports.cs` | #355 | 379 | Retired | 0 |
| `game/scripts/OBPGame.CaptureDiagnostics.cs` | #356 | 163 | Retired | 0 |
| `game/scripts/OBPGame.CaptureSession.cs` | #356 | 84 | Retired | 0 |
| `game/scripts/OBPGame.ControllerSettings.cs` | #348 | 306 | Retired | 0 |
| `game/scripts/OBPGame.CrateBolts.cs` | #365 | 108 | Retired | 0 |
| `game/scripts/OBPGame.cs` | #376 | 840 | Thin Godot composition root (permanent) | 926 |
| `game/scripts/OBPGame.Destinations.cs` | #347 | 353 | Retired | 0 |
| `game/scripts/OBPGame.EquipmentPresentation.cs` | #350 | 151 | Retired | 0 |
| `game/scripts/OBPGame.FusionLab.cs` | #347 | 100 | Retired | 0 |
| `game/scripts/OBPGame.FusionLabSmoke.cs` | #345 | 75 | Retired | 0 |
| `game/scripts/OBPGame.FusionWeapons.cs` | #352 | 577 | Retired | 0 |
| `game/scripts/OBPGame.MovementRepro.cs` | #358 | 360 | Retired | 0 |
| `game/scripts/OBPGame.Navigation.cs` | #347 | 37 | Retired | 0 |
| `game/scripts/OBPGame.PlayerAvatar.cs` | #350 | 287 | Retired | 0 |
| `game/scripts/OBPGame.PlayerEquipment.cs` | #351 | 140 | Retired | 0 |
| `game/scripts/OBPGame.PlayerHealth.cs` | #353 | 105 | Retired | 0 |
| `game/scripts/OBPGame.PlayerSession.cs` | #349 | 134 | Retired | 0 |
| `game/scripts/OBPGame.Playtest.cs` | #356 | 251 | Retired | 0 |
| `game/scripts/OBPGame.PlaytestBugReports.cs` | #357 | 153 | Retired | 0 |
| `game/scripts/OBPGame.PlaytestDriver.cs` | #357 | 538 | Retired | 0 |
| `game/scripts/OBPGame.Profile.cs` | #349 | 164 | Retired | 0 |
| `game/scripts/OBPGame.QuickSelect.cs` | #348 | 89 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Amoeboids.cs` | #364 | 241 | Retired | 0 |
| `game/scripts/OBPGame.Rac1AridiaEnemies.cs` | #363 | 1017 | Retired | 0 |
| `game/scripts/OBPGame.Rac1AridiaScripted.cs` | #363 | 68 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Arsenal.cs` | #367 | 593 | Retired | 0 |
| `game/scripts/OBPGame.Rac1BombEffects.cs` | #369 | 172 | Retired | 0 |
| `game/scripts/OBPGame.Rac1CampaignPersistence.cs` | #372 | 174 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Checkpoints.cs` | #372 | 79 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Combat.cs` | #368 | 198 | Retired | 0 |
| `game/scripts/OBPGame.Rac1CrateSupply.cs` | #365 | 107 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Enemies.cs` | #361 | 764 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Explosives.cs` | #366 | 162 | Retired | 0 |
| `game/scripts/OBPGame.Rac1FrogCarriers.cs` | #362 | 154 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Frogs.cs` | #362 | 506 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Gameplay.cs` | #360 | 378 | Retired | 0 |
| `game/scripts/OBPGame.Rac1GoldBolts.cs` | #365 | 75 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Hostiles.cs` | #361 | 65 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Lifecycle.cs` | #372 | 126 | Retired | 0 |
| `game/scripts/OBPGame.Rac1MetalDetector.cs` | #367 | 141 | Retired | 0 |
| `game/scripts/OBPGame.Rac1MetropolisTaxis.cs` | #371 | 573 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Missions.cs` | #372 | 167 | Retired | 0 |
| `game/scripts/OBPGame.Rac1NovalisBirdbots.cs` | #361 | 120 | Retired | 0 |
| `game/scripts/OBPGame.Rac1NovalisBreakables.cs` | #366 | 78 | Retired | 0 |
| `game/scripts/OBPGame.Rac1NovalisCranks.cs` | #370 | 160 | Retired | 0 |
| `game/scripts/OBPGame.Rac1NovalisMechanisms.cs` | #370 | 72 | Retired | 0 |
| `game/scripts/OBPGame.Rac1NovalisMusic.cs` | #370 | 99 | Retired | 0 |
| `game/scripts/OBPGame.Rac1NovalisPeckbots.cs` | #361 | 48 | Retired | 0 |
| `game/scripts/OBPGame.Rac1NovalisWater.cs` | #370 | 51 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Pickups.cs` | #365 | 273 | Retired | 0 |
| `game/scripts/OBPGame.Rac1PlacedBolts.cs` | #365 | 89 | Retired | 0 |
| `game/scripts/OBPGame.Rac1PlayerAudio.cs` | #354 | 131 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Presentation.cs` | #360 | 72 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Projectiles.cs` | #368 | 263 | Retired | 0 |
| `game/scripts/OBPGame.Rac1PulsingOrbs.cs` | #370 | 62 | Retired | 0 |
| `game/scripts/OBPGame.Rac1QueenLinkedChildren.cs` | #362 | 117 | Retired | 0 |
| `game/scripts/OBPGame.Rac1RedPlants.cs` | #370 | 65 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Seekers.cs` | #361 | 116 | Retired | 0 |
| `game/scripts/OBPGame.Rac1SuckCannon.cs` | #367 | 107 | Retired | 0 |
| `game/scripts/OBPGame.Rac1WeaponAudio.cs` | #369 | 73 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Weapons.cs` | #367 | 417 | Retired | 0 |
| `game/scripts/OBPGame.Rac1WeaponWorld.cs` | #368 | 294 | Retired | 0 |
| `game/scripts/OBPGame.Rac1Wrench.cs` | #368 | 178 | Retired | 0 |
| `game/scripts/OBPGame.Rac2AranosOpeningHost.cs` | #373 | 412 | Retired | 0 |
| `game/scripts/OBPGame.Rac2CrateDebugHarness.cs` | #345 | 100 | Retired | 0 |
| `game/scripts/OBPGame.Rac2Gameplay.cs` | #373 | 323 | Retired | 0 |
| `game/scripts/OBPGame.Rac3Gameplay.cs` | #374 | 411 | Retired | 0 |
| `game/scripts/OBPGame.ReplayBookmarks.cs` | #358 | 116 | Retired | 0 |
| `game/scripts/OBPGame.Shots.cs` | #345 | 157 | Retired | 0 |
| `game/scripts/OBPGame.Sources.cs` | #346 | 316 | Retired | 0 |
| `game/scripts/OBPGame.SourceSetup.cs` | #346 | 82 | Retired | 0 |
| `game/scripts/OBPGame.StressDiagnostics.cs` | #345 | 114 | Retired | 0 |
| `game/scripts/OBPGame.WorldEntry.cs` | #376 | 178 | Retired | 0 |

## Remaining root members

Every remaining `OBPGame` member is a reviewed composition member recorded in
`tests/OBP.Tests/root-boundary-policy.json` with owner `#376`, disposition `permanent` and a one-sentence reason;
`RootBoundaryArchitectureTests` pins that disposition and a ceiling of 150 that can only go down, and the guard fails
on any new root member. The reviewed members fall into five kinds:

| Kind | Count | Why it belongs on the root |
| --- | ---: | --- |
| Application-lifetime owner references and lazy accessors | 27 | The Godot node is the composition point; it creates owners lazily and holds no rules or state. |
| Established public host contracts | 31 | The bootstrap scene, navigation, pause menu and diagnostic fixtures call these; each is a one-line forward to its owner. |
| Lazy factories | 3 | Bind an owner's narrow ports to the owned components; rules, formulas and ticks stay in the owner. |
| Read-only projections | 48 | Short views of an owner used by the root's remaining wiring and lifecycle callbacks; not a second source of state. |
| Private one-line forwards | 41 | Used by the root's wiring and lifecycle callbacks. |

The Godot lifecycle and input overrides (`_Ready`, `_Process`, `_PhysicsProcess`, `_UnhandledInput`, `_Notification`,
`_ExitTree`) are not listed because the script is the Godot entry point.
