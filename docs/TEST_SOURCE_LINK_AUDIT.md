# OBP.Tests linked host source audit (#384)

**Audit bases:** issue freeze `3d9c4b5` (49 files), current dev `1aab1b4` (159 files).
**Disposition:** retain the 49 frozen links, remove no links, add no projects, and harden coverage attribution plus future compilation-mode guards.

## Findings

The 49 explicit linked sources in the issue snapshot are **all still present**, in the same physical paths: 19 in game/scripts and 30 in game/diagnostics. Later decomposition PRs added 110 independent links. The current total is **50 scripts and 109 diagnostics**. The regression checks evaluate **all current links**, not a frozen count, so additional independent source/diagnostic moves do not need a second hard-coded list.

Every linked path is compiled canonically into **OBP.Game** through game/OBP.Game.csproj and is compiled a second time for portable **OBP.Tests**. These two host type instances are deliberately different assembly identities. OBP.Tests references the existing OBP.Core, OBP.Runtime, OBP.Fusion, OBP.Godot and other shared assemblies directly; it does **not** reference OBP.Game. No linked host declaration collides with a type in a referenced shared assembly. There is no demonstrated two-consumer canonical host-type identity requirement that warrants moving these contracts into shared projects.

**Important build-mode difference:** the Godot SDK defines GODOT, GODOT4, Godot version/platform symbols, TRACE and RELEASE for the game. The test project defines TRACE and RELEASE, but not the Godot symbols. Both projects use C# 12, nullable enabled and implicit usings enabled. The 159 currently linked files have no #if, #elif, #define or #undef directives, so this real symbol difference does not currently change their compiled source. The new test guard rejects future conditional compilation in linked files until the divergent mode is deliberately addressed.

The word "portable" means **not requiring a running Godot editor or retail source files**; it does not mean every file is pure .NET. ApplicationPointer.cs in the baseline uses the Godot API and retains private static pointer state. Its portable test exercises the pure ResolveDesktopMode method and does not prove live desktop behaviour. The later ActorContactGeometry.cs link also uses Godot types. LegacyDiagnosticCancellation has a per-assembly AsyncLocal and DiagnosticAvatarProvider has a singleton. None of these test instances should be treated as the runtime instance. Source/link type identity is an intentionally limited testing technique, not an inter-assembly contract.

**Concrete incorrect coverage attribution reproduced and fixed:** CoverageReport accepted a real game/scripts file attributed to OBP.Core, or a src/OBP.Core file attributed to OBP.Runtime or OBP.Tests. The new regression demonstrated all three falsely accepted reports before the fix. Cobertura and OpenCover source paths are now checked against their module: a library module can claim only src/<same-module>/, and OBP.Tests can claim only game/scripts/ sources explicitly linked by its project or the existing explicitly permitted linked diagnostics. A regression rejects real, unlinked game-script paths even when both collector formats agree. Coverage for linked host code remains labelled as portable xUnit execution, **not** engine/game binary coverage.

## Frozen 49 links, individual disposition

Each file below is owned by OBP.Game, and each is **retained**. The production and xUnit consumers shown are actual named call sites, harnesses or tests, not claims of coverage completeness. Some contracts are consumed transitively through a linked scenario. The sources are managed .NET unless otherwise marked.

| Source under game/ | Purpose / specific reason for retaining | Production consumer (example) | Portable test consumer (example) |
|---|---|---|---|
| scripts/CommandLineArgs.cs | CLI flags, source aliases and replay arguments; preserve wire grammar | BugReportCapture.cs | CommandLineArgsTests |
| scripts/AutomationRunPolicy.cs | Headless/automated safety policy | ApplicationPointer.cs | AutomationDesktopSafetyTests |
| scripts/DiagnosticSnapshotGate.cs | Source-ready diagnostic snapshot gate | No current named caller; compiled into game | DiagnosticSnapshotGateTests |
| scripts/SourceSetupPolicy.cs | Ordinary vs diagnostic source requirements | OBPGame.cs | SourceReadinessTests |
| diagnostics/DiagnosticContracts.cs | Scenario identity, source requirements, snapshots, status | OBPGame.cs, CaptureDiagnosticHarness.cs | DiagnosticScenarioTests |
| diagnostics/DiagnosticInputActions.cs | Scoped input actions and input lease | GodotDiagnosticAdapters.cs | DiagnosticScenarioTests |
| diagnostics/DiagnosticScenarioRunner.cs | Run/cancel/exit handling | OBPGame.cs | DiagnosticScenarioTests |
| diagnostics/SourceFreeStartupScenario.cs | Source-free startup acceptance | SourceFreeStartupProbe.cs | DiagnosticScenarioTests |
| diagnostics/AcceptanceEvidence.cs | Immutable acceptance evidence records | PlayerProfileDiagnosticFixtures.cs | DiagnosticRegistryTests |
| diagnostics/DiagnosticScenarioRegistry.cs | Scenario registration | WorldSmokeHarness.cs | DiagnosticRegistryTests |
| diagnostics/LegacyDiagnosticScenario.cs | Process outcome and cancellation bridge, per-assembly AsyncLocal; depends on OBP.Godot | diagnostic scenario composition | DiagnosticRegistryTests |
| diagnostics/ExistingDiagnosticWorld.cs | Existing-world observation contract | OBPGame.cs | linked diagnostic fixtures |
| diagnostics/WorldSmokeHarness.cs | World smoke entrypoint selection | OBPGame.cs | MovementDiagnosticTests |
| diagnostics/UiDiagnosticPorts.cs | Menu, campaign and source-repair observation ports | GodotUiDiagnosticFixtures.cs | UiDiagnosticScenarioTests |
| diagnostics/OrdinaryUiScenario.cs | Ordinary UI acceptance flow | UiDiagnosticHarness.cs | UiDiagnosticScenarioTests |
| diagnostics/SourceSetupScenario.cs | Source setup acceptance flow | UiDiagnosticHarness.cs | UiDiagnosticScenarioTests |
| diagnostics/StartupVisibilityScenario.cs | Startup visibility acceptance flow | UiDiagnosticHarness.cs | UiDiagnosticScenarioTests |
| diagnostics/Rac1VeldinRetailPauseScenario.cs | Veldin pause acceptance | UiDiagnosticHarness.cs | RetailPauseScenarioTests |
| diagnostics/Rac1RetailNavigationScenario.cs | Retail pause/navigation checks | UiDiagnosticHarness.cs | RetailPauseScenarioTests |
| diagnostics/DiagnosticProcessScenario.cs | Diagnostic exit handling adapter | CaptureDiagnosticHarness.cs | UiDiagnosticScenarioTests |
| diagnostics/UiDiagnosticHarness.cs | UI scenario dispatcher | OBPGame.cs | UiDiagnosticScenarioTests |
| diagnostics/PlayerDiagnosticPorts.cs | Persistent player and world-lifecycle fixtures | GodotPersistentPlayerFixture.cs | PlayerDiagnosticTests |
| diagnostics/PersistentPlayerScenario.cs | Player persistence across world changes | PlayerSessionDiagnosticHarness.cs | linked player scenario fixtures |
| diagnostics/PlayerProfileDiagnosticPorts.cs | Shared profile/equipment observations | PlayerSessionDiagnosticHarness.cs | CaptureDiagnosticTests |
| diagnostics/SharedProfileScenario.cs | Shared profile round-trip | PlayerSessionDiagnosticHarness.cs | linked player scenario fixtures |
| diagnostics/PlayerAudioDiagnosticPorts.cs | Audio and button sampling ports | GodotPlayerAudioFixture.cs | Rac1PlayerAudioPresenterTests |
| diagnostics/Rac1PlayerAudioScenario.cs | Audio acceptance assertions | PlayerSessionDiagnosticHarness.cs | linked player scenario fixtures |
| diagnostics/DiagnosticPlayerAudioCollector.cs | Audio event/transition records | Rac1PlayerAudioPresenter.cs | Rac1PlayerAudioPresenterTests |
| diagnostics/PlayerCapabilityDiagnosticPorts.cs | Health/contact/capability observations | GodotPlayerCapabilityFixtures.cs | PlayerDiagnosticTests |
| diagnostics/PlayerCapabilitiesScenario.cs | Player capability acceptance flow | PlayerSessionDiagnosticHarness.cs | linked player scenario fixtures |
| diagnostics/PlayerCapabilityTrial.cs | Portable capability trial helper | PlayerCapabilitiesScenario.cs | PlayerDiagnosticTests |
| diagnostics/PlayerSessionDiagnosticHarness.cs | Player suite coordination | OBPGame.cs | PlayerDiagnosticTests |
| diagnostics/PlayerProfileDiagnosticFixtures.cs | Source-free owned profile fixtures | OBPGame.cs | PlayerDiagnosticTests |
| diagnostics/Rac1DiagnosticCollision.cs | Typed collision bridge | GodotRac1DiagnosticSurface.cs | PlayerDiagnosticTests |
| scripts/WorldSceneLifetime.cs | World lease/retirement sequencing | ApplicationNavigationCoordinator.cs | WorldSceneLifetimeTests |
| scripts/HostOperationScope.cs | Host operation cancellation and error isolation | ActorWorldHost.cs | ActorSourceCacheTests |
| scripts/ActorSourceCache.cs | Source-stamped cache and invalidation | ActorSceneFactory.cs | ActorSourceCacheTests |
| scripts/SourceVerificationOperation.cs | Version-qualified verification lifetime | SourceVerificationCoordinator.cs | SourceVerificationOperationTests |
| scripts/ApplicationPointer.cs | Pointer safety adapter, *Godot API* and per-assembly static mode | GodotPlayerLifecycleHost.cs | AutomationDesktopSafetyTests (pure method) |
| scripts/FusionWeaponSession.cs | Fusion weapon session and source identity | FusionWeaponHost.cs | FusionWeaponTests |
| scripts/PlayerSessionNativeState.cs | Native player state ownership | ActorWorldHost.cs | PlayerCapabilitiesTests |
| scripts/PlayerEquipmentRegistrations.cs | Player equipment registration | PlayerEquipmentFacade.cs | PlayerEquipmentFacadeTests |
| scripts/PlayerHostProfiles.cs | Player host/profile defaults | PlayerSessionHost.cs | PlayerCapabilitiesTests |
| scripts/DiagnosticAvatarProvider.cs | Synthetic non-retail avatar singleton | PlayerAvatarPresentationHost.cs | linked player-host tests (indirect) |
| scripts/FusionLabWorldFactory.cs | Synthetic Fusion Lab world setup | ApplicationNavigationCoordinator.cs | FusionLabDefinitionTests |
| scripts/FusionDamagePolicy.cs | Fusion damage calculation | ActorCombatContactService.cs | FusionWeaponTests |
| scripts/FusionWeaponData.cs | Fusion assets and preparation contracts | FusionWeaponSourceHost.cs | FusionWeaponPreparationTests |
| scripts/FusionWeaponMount.cs | Weapon muzzle feedback positions | FusionWeaponWorldActions.cs | linked fusion tests (indirect) |
| scripts/MovementReproState.cs | Movement replay state/serialization | MovementReproductionService.cs | MovementReproStateTests |

## Newer links, kept under the same guard

The post-freeze 110 links comprise 31 scripts and 79 diagnostics. Their owners and reasons are the same in kind, but were introduced by independent feature/diagnostic decomposition work after the frozen census. The diagnostics represent scenario runners, named scenario implementations, typed observations and fixtures for movement, weapons, combat, routes, capture, replay, profiles and campaign tests. The scripts represent small host policies, lifetime state, actor/player composition, capture/replay and world/weapon adapters. None is automatically promoted into OBP.Core, OBP.Runtime, OBP.Fusion or OBP.Godot. The current compile-link guard checks all 159 filenames, ownership boundary, canonical type separation, conditional compilation and link virtual paths. The existing explicit diagnostic coverage permissions test covers all 109 current diagnostic links.

## Verification and future moves

- LinkedHostCompilationBoundaryTests checks duplicate/invalid Include/Link pairs, missing physical files, accidental OBP.Game project reference, shared-project type-name conflicts and conditional preprocessor changes. It includes synthetic GODOT/DEBUG/define regressions proving the mode guard is effective, while ignoring harmless pragma warnings.
- CoverageReportTests.CrossAssemblySourceAttributionIsRejected reproduces three cross-assembly misattributions and verifies they cannot be published. The existing ExplicitDiagnosticCoveragePermissionsMatchThePortableProjectLinks test still binds the diagnostics coverage allowlist to the current project.
- No scene, Godot .cs.uid, native source parser, save format, replay format or CLI wire option changed. No mandated second xUnit assembly or broad source migration is proposed.

**Change rule:** when a linked source is moved by another issue, update the Compile Include and Link, coverage allowlist if diagnostic, and any architecture tests. Only extract a type to an existing shared assembly when there is specific evidence that two separately compiled consumers need its canonical identity. This audit does not replace the engine-integrated or retail reconciliation tests.
