using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Game.Diagnostics;

namespace OBP.Game;

public partial class OBPGame
{
    private WorldSceneLifetime<GodotWorldResources>? _worldScenes;
    private WorldSceneLifetime<GodotWorldResources> WorldScenes => _worldScenes ??=
        new(RetireWorldBindings, ActivateOwnedWorld, () => _worldEntries?.Unload());
    private RuntimeSpawn? _worldPlayerEntry => _worldScenes?.PlayerEntry;
    private float _worldEntrySnapReach => _worldScenes?.EntrySnapReach ?? 8;

    private sealed class WorldSource(Func<WorldEntryRequest, PreparedWorldEntry> prepare) : IWorldEntrySource
    {
        public PreparedWorldEntry Prepare(WorldEntryRequest request) => prepare(request);
    }

    private bool EnterWorld(string destinationId, Func<RuntimeWorld> load, ObpDestination? nativeDestination,
        string? sourcePath, FusionLabDefinition? lab = null)
    {
        _worldScenes?.CancelPreparation();
        if (!Sources.RequireOrdinary("world entry")) return false;
        try
        {
            var worldSourceStamp = SourceFileStamp(sourcePath);
            PrepareSelectedPlayerAssets();
            if (_playtest is { WaitingForEntry: true } && OrdinaryPlayerSession && !_captureControllersInitialized)
            {
                _playerNative!.ResetReplayControllers(); _fusion = _playerNative.Weapons;
                _captureControllersInitialized = true;
            }
            var request = _worldEntries!.Request(destinationId, PlayerEntryMode);
            var source = new WorldSource(entry =>
            {
                var world = load();
                var spawn = ValidateSourceEntry(world);
                return new(world, spawn, []); // Mixed native actor adapters are registered by #256.
            });
            var plan = _worldEntries.Prepare(request, source, PlayerAvatarProviders);
            using var staged = WorldScenes.Prepare(plan, nativeDestination, lab, SourceEntrySnapReach(plan.Prepared.World),
                () => new GodotWorldResources(WorldHost.PrepareScene(plan.Prepared.World, destinationId, WorldOptions(plan.Prepared.World))));
            // Recheck attachments before the destructive boundary. Native source
            // data and selected player assets are independent dependencies.
            PrepareSelectedPlayerAssets();
            if (SourceFileStamp(sourcePath) != worldSourceStamp)
                throw new InvalidDataException("The world source changed during preparation. Reattach it in Game Sources.");
            if (!Sources.RequireOrdinary("world activation"))
                return false;
            _worldEntries.Commit(plan, WorldScenes);
            _lastCommittedProfileDestination = destinationId;
            if (nativeDestination is not null)
            {
                var scenarios = WorldSmokeHarness.Create(_args, nativeDestination, _worldSwitches,
                    new WorldSmokeHarness.Entrypoints(definition => CreateRac1EncounterDiagnosticHarness().CreateVeldin(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateMovementContact(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateCombat(definition),
                        definition => CreateRac1CampaignDiagnosticHarness().CreateCampaign(definition), definition => CreateMovementDiagnosticHarness().CreateMovement(definition),
                        definition => CreateRac1LootDiagnosticHarness().CreateCrates(definition), definition => CreateRac1LootDiagnosticHarness().CreatePlacedBolts(definition), definition => CreateWeaponDiagnosticHarness().CreateWrench(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateEnemies(definition), definition => CreateCaptureDiagnosticHarness().CreateMovement(definition), definition => CreateWeaponDiagnosticHarness().CreateBomb(definition),
                        definition => CreateMovementDiagnosticHarness().CreateSwim(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreatePeckbot(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateBirdbot(definition),
                        definition => CreateRac1WorldRouteDiagnosticHarness().CreateMusic(definition), definition => CreateWeaponDiagnosticHarness().CreateWeapons(definition), definition => CreatePlayerDiagnosticHarness().CreateAudio(definition),
                        definition => CreateRac1WorldRouteDiagnosticHarness().CreateTaxi(definition), definition => CreateRac1CampaignDiagnosticHarness().CreateMission(definition), definition => CreateRac1WorldRouteDiagnosticHarness().CreateRoute(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateFrogs(definition)), !(Sources.Setup.Access == SourceAccessMode.ExplicitDiagnostic), TrilogySourceDefinitions.All);
                var runner = new DiagnosticScenarioRunner(
                    new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(source =>
                        new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
                    new GodotDiagnosticFrameClock(this),
                    new ExistingDiagnosticWorld(new GodotDiagnosticWorldObservations(WorldScenes).ReadWorld, _hostOperations.Token));
                DiagnosticScenarioDispatch.Start(scenarios, _hostOperations, runner,
                    (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.PrintErr(message));
                StartRequestedSourceVerification(nativeDestination, sourcePath!);
            }
            SaveSharedProfile("world transition");
            if (_args.SourceSetupCheck && _worldSwitches == 1) CreateUiDiagnosticHarness().Start("source-setup-check");
            if (_playtest is { HasEntered: true }) RecordCaptureCommand(new("destination", destinationId));
            _playtest?.WorldEntered();
            if (_args.FusionSmoke && _worldSwitches == 1 && !_args.PlaytestRequested) CreateFusionWeaponDiagnosticHarness().Start();
            if (_worldSwitches == 1 && PlayerSessionDiagnosticHarness.IsRequested(_args)) CreatePlayerDiagnosticHarness().StartRequested();
            if (_args.TrilogyGameplaySmoke && _worldSwitches == 1) CreateCaptureDiagnosticHarness().StartTrilogy();
            return true;
        }
        catch (Exception error)
        {
            AbandonFailedRac1Travel();
            DestinationError($"{destinationId} could not load: {error.Message}");
            if (_world is null)
            {
                ShowDestinationSelector();
                _destinationSelector?.SetHint(error.Message, error: true);
            }
            if (_args.CaptureFrame is not null || (_args.PlayerSessionSmoke || _args.PlayerCapabilitiesSmoke) && _world is null)
                ApplicationLifecycle.RequestQuit(this, "world-entry-failed", 2);
            return false;
        }
    }

    private void ActivateOwnedWorld(WorldSceneLease<GodotWorldResources> entry)
    {
        entry.Resources.EffectScope = entry.Plan.Request;
        ClearBootstrapSceneArtifacts();
        BeginPlayerHealthWorld(entry.Plan.Request.WorldInstanceId, entry.Plan.Request.Generation);
        _destinationSelector?.QueueFree(); _destinationSelector = null;
        Sources.RetireView();
        ActivateWorld(entry.Plan.Prepared.World, entry.Resources, entry.NativeDestination, entry.Lab);
        entry.Resources.BindPlayer(_player);
    }

    private void ReportWorldPreparationFailure(Exception error, bool superseded)
    {
        GD.PrintErr($"[lifecycle] deferred world preparation failed (superseded={superseded}): {error}");
        if (!superseded && IsInsideTree()) ApplicationLifecycle.RequestQuit(this, "world-preparation-failed", 2);
    }

    private static (long Length, long Written)? SourceFileStamp(string? path)
    {
        if (path is null) return null;
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("The world source is missing. Reattach it in Game Sources.");
        return (file.Length, file.LastWriteTimeUtc.Ticks);
    }

    private void StartRequestedSourceVerification(ObpDestination destination, string sourcePath)
    {
        if (_args.VerifyHash && _worldSwitches == 1 && destination.Game == ObpSourceGame.Rac2)
            StartBackgroundVerify(sourcePath);
    }

    private static RuntimeSpawn ValidateSourceEntry(RuntimeWorld world)
    {
        if (world.Game == ObpSourceGame.Rac1 && world.PlayerStart is null)
            throw new InvalidDataException("R&C1 world is missing its authored player start.");
        return WorldEntryPlanning.ResolveEntryPoint(world);
    }

    // World-local authored placement clearance, not a player capability/profile.
    private static float SourceEntrySnapReach(RuntimeWorld world) =>
        world.Game == WorldContentIdentity.FusionLab || (world.Game == ObpSourceGame.Rac2 && world.LevelId == 0 && world.PlayerStart is not null) ? .75f : 8f;

    private void ActivateWorld(RuntimeWorld world, GodotWorldResources resources,
        ObpDestination? destination, FusionLabDefinition? lab)
    {
        _worldSwitches++;
        BeginSourceWorldEntry(world);
        resources.Load(this, _worldRoot, world, _currentDestinationId!, WorldOptions(world));
        SetupOverlay(resources.Scene, world);
        ConfigureRac2GameplayHost();
        bool staticCamera = _args.ShotsPath is not null || _args.CaptureFrame is not null &&
            (_args.DirectLoad || _args.Destination is not null) && _args.TestScene != "player" &&
            !_args.AnimSolo && !_args.CrateFocus && !_args.CrateAutoStrike;
        if (!staticCamera)
        {
            ConfigureRac1Gameplay(world, resources.Scene);
            ConfigureRac3Gameplay(world, resources.Scene);
        }
        if (_args.AnimSolo) FrameAnimatedMobies(world);
        else if (staticCamera) FrameShowcaseCamera(world);
        else SpawnPlayer(world);
        _mode = Mode.World;
        _sceneKind = lab is null ? "destination" : "fusion-lab";
        _genericNavigationActive = _args.CaptureFrame is null;
        if (lab is not null)
        {
            DecorateFusionLab(lab, _playerAvatarView is not null);
        }
        ConfigureRegisteredActors(lab);
        var effectScope = resources.EffectScope ?? throw new InvalidOperationException("Effects require the existing world lease.");
        try
        {
            resources.Effects = new(resources.Scene.Root, worldId: effectScope.WorldInstanceId,
                generation: effectScope.Generation, camera: () => GetViewport().GetCamera3D(),
                sources: () => Sources.Setup.Library.Attached.Values.ToArray(),
                actorAlive: key => _actorWorld.Views.TryGetValue(key.Target, out var actor) && actor.Registration.Instance == key && actor.Observe().Alive,
                placementOffset: () => lab is not null ? System.Numerics.Vector3.Zero :
                    WorldEffectHost.Runtime((_player?.GlobalPosition ?? Vector3.Zero) + EquipmentForward * 6 + Vector3.Up * 8) - new System.Numerics.Vector3(0, 0, 18),
                suspended: () => _movementStateLoading || _playtest is { WaitingForEntry: true } or { PreparingWorld: true },
                recordOperation: operation => RecordCaptureCommand(new("effects", operation)),
                commandAllowed: () => _playtest is null || EffectCaptureContract.Includes(_args, _playtest.Reader?.Header));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            resources.EffectsFailure = "Native effects setup is unavailable (" + error.GetType().Name + "). Check the experiment definition.";
            GD.PrintErr("[effects] " + resources.EffectsFailure);
        }
        EnsurePlayerHud(); EnsureWorldHud();
        if (OrdinaryPlayerSession) PublishPersistentPlayerHud();
        UpdatePlayerHud(); UpdateWorldHud();
        CommitSourceWorldEntry(world);
        if (_args.EffectsSmoke && !_args.PlaytestRequested && _worldSwitches == 1)
        {
            string report = _args.AcceptanceOut ?? ProjectSettings.GlobalizePath("res://captures/effects-285/report.json");
            var effectsFixture = new GodotEffectsFixture(this, () => _worldScenes?.Current?.Resources.Effects,
                () => _worldScenes?.Current?.Resources.Scene.Root,
                () => System.Text.Json.JsonSerializer.Serialize(new
                {
                    Configuration = _playerSession?.Capture().Configuration,
                    Loadout = _playerSession?.Capture().Loadout,
                    Actors = _actorWorld.Views.Values.Select(a => new { a.Registration.Instance, a.Observe().Health }).ToArray(),
                }), Path.GetDirectoryName(Path.GetFullPath(report))!, () => _worldScenes?.Current?.Resources.EffectsFailure);
            var definition = new DiagnosticScenarioDefinition("effects-room/v1", DiagnosticSourceAccess.OrdinaryPlay, DiagnosticSetup.ExistingWorld,
                [.. TrilogySourceDefinitions.All.Select(s => new DiagnosticSourceRequirement(s.Game, s.Authority.BuildId))], TimeSpan.FromMinutes(10), 30000, "obp:fusion-lab");
            var scenario = new EffectsExperimentScenario(definition, effectsFixture,
                new EffectDiagnosticNavigation(id => id == "obp:fusion-lab" ? EnterFusionLab() :
                    TrilogyWorldProviders.Resolve(id) is { } next && OnDestinationChosen(next)),
                new DiagnosticSourceAttachmentFixture(Sources.Setup.Library, () => { }), report, message => GD.Print(message));
            var runner = new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(s => new DiagnosticSourceSnapshot(s.Game, s.Identity.BuildId, true))]),
                new GodotDiagnosticFrameClock(this), new ExistingDiagnosticWorld(new GodotDiagnosticWorldObservations(WorldScenes).ReadWorld, _hostOperations.Token));
            DiagnosticScenarioDispatch.Start([scenario], _hostOperations, runner, (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.PrintErr(message));
        }
        _loadSummary = $"✓ {destination?.DisplayName ?? (lab is not null ? "Fusion Lab" : world.DisplayName)} · {resources.Scene.MeshInstances} meshes";
        GD.Print($"[destinations] world ready — {_loadSummary} (switch #{_worldSwitches}, player generation {_playerSession!.Capture().Generation})");
    }

    private void BeginSourceWorldEntry(RuntimeWorld world)
    {
        if (world.Game != ObpSourceGame.Rac1) return;
        _rac1CampaignSession.StartLevelCheckpointSession(world.LevelId, world.PlayerStart!);
        WorldScenes.OverrideAuthoredEntry(_rac1CampaignSession.LevelCheckpoint!.AuthoredClass0);
    }

    private void CommitSourceWorldEntry(RuntimeWorld world)
    {
        if (world.Game != ObpSourceGame.Rac1) return;
        var entry = _rac1CampaignSession.CommitLoadedLevel(world.LevelId);
        _rac1CampaignSession.FinishLoadedLevel(entry);
        if (entry == Rac1LevelEntryKind.CampaignTravel) PersistRac1CampaignState("completed campaign travel");
    }
}
