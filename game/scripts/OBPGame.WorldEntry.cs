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
        if (!RequireOrdinarySourceSetup("world entry")) return false;
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
            if (!RequireOrdinarySourceSetup("world activation"))
                return false;
            _worldEntries.Commit(plan, WorldScenes);
            _lastCommittedProfileDestination = destinationId;
            if (nativeDestination is not null)
            {
                var scenarios = WorldSmokeHarness.Create(_args, nativeDestination, _worldSwitches,
                    new WorldSmokeHarness.Entrypoints(definition => CreateRac1EncounterDiagnosticHarness().CreateVeldin(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateMovementContact(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateCombat(definition),
                        RunRac1CampaignSmokeAsync, definition => CreateMovementDiagnosticHarness().CreateMovement(definition),
                        RunRac1CrateSmokeAsync, RunRac1PlacedBoltSmokeAsync, definition => CreateWeaponDiagnosticHarness().CreateWrench(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateEnemies(definition), RunMovementStateSmokeAsync, definition => CreateWeaponDiagnosticHarness().CreateBomb(definition),
                        definition => CreateMovementDiagnosticHarness().CreateSwim(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreatePeckbot(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateBirdbot(definition),
                        RunRac1NovalisMusicSmokeAsync, RunRac1WeaponsSmokeAsync, definition => CreatePlayerDiagnosticHarness().CreateAudio(definition),
                        RunRac1MetropolisTaxiSmokeAsync, RunRac1MissionPlaySmokeAsync, RunRac1NovalisRouteSmokeAsync,
                        definition => CreateRac1EncounterDiagnosticHarness().CreateFrogs(definition)), !_diagnosticSourceAccess, TrilogySourceDefinitions.All);
                var runner = new DiagnosticScenarioRunner(
                    new GodotDiagnosticSources(() => [.. _sources.Attached.Values.Select(source =>
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
            if (_args.FusionSmoke && _worldSwitches == 1 && !_args.PlaytestRequested) _ = RunFusionWeaponsSmokeAsync();
            if (_worldSwitches == 1 && PlayerSessionDiagnosticHarness.IsRequested(_args)) CreatePlayerDiagnosticHarness().StartRequested();
            if (_args.TrilogyGameplaySmoke && _worldSwitches == 1) _ = RunTrilogyGameplaySmokeAsync();
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
        ClearBootstrapSceneArtifacts();
        BeginPlayerHealthWorld(entry.Plan.Request.WorldInstanceId, entry.Plan.Request.Generation);
        _destinationSelector?.QueueFree(); _destinationSelector = null;
        _sourceManager?.QueueFree(); _sourceManager = null;
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
            if (_player is { } player) StartFusionLabTraversalSmoke(player);
        }
        ConfigureRegisteredActors(lab);
        EnsurePlayerHud(); EnsureWorldHud();
        if (OrdinaryPlayerSession) PublishPersistentPlayerHud();
        UpdatePlayerHud(); UpdateWorldHud();
        CommitSourceWorldEntry(world);
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
