using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OneBigPackage;

public partial class OBPGame
{
    private RuntimeSpawn? _worldPlayerEntry;
    private float _worldEntrySnapReach = 8;

    private sealed class WorldSource(Func<WorldEntryRequest, PreparedWorldEntry> prepare) : IWorldEntrySource
    {
        public PreparedWorldEntry Prepare(WorldEntryRequest request) => prepare(request);
    }

    private sealed class EntryHost(Func<WorldEntryPlan, IDisposable> activate) : IWorldEntryHost
    {
        public IDisposable Activate(WorldEntryPlan plan) => activate(plan);
    }

    private sealed class SceneLease(WorldHost host, PlayerHost? player) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            host.Unload();
            if (GodotObject.IsInstanceValid(player) && !player!.IsQueuedForDeletion()) player.QueueFree();
        }
    }

    private bool EnterWorld(string destinationId, Func<RuntimeWorld> load, ObpDestination? nativeDestination,
        string? sourcePath, FusionLabDefinition? lab = null)
    {
        RuntimeWorldScene.Result? preparedScene = null;
        bool activating = false;
        try
        {
            var worldSourceStamp = SourceFileStamp(sourcePath);
            PrepareSelectedPlayerAssets();
            var request = _worldEntries!.Request(destinationId, PlayerEntryMode);
            var source = new WorldSource(entry =>
            {
                var world = load();
                var spawn = ValidateSourceEntry(world);
                return new(world, spawn, []); // Mixed native actor adapters are registered by #256.
            });
            var plan = _worldEntries.Prepare(request, source, PlayerAvatarProviders);
            preparedScene = WorldHost.PrepareScene(plan.Prepared.World, destinationId, WorldOptions(plan.Prepared.World));
            // Recheck attachments before the destructive boundary. Native source
            // data and selected player assets are independent dependencies.
            PrepareSelectedPlayerAssets();
            if (SourceFileStamp(sourcePath) != worldSourceStamp)
                throw new InvalidDataException("The world source changed during preparation. Reattach it in Game Sources.");
            var staged = preparedScene;
            _worldEntries.Commit(plan, new EntryHost(entry =>
            {
                activating = true;
                TeardownWorld();
                ClearBootstrapSceneArtifacts();
                _worldHost = new();
                _activeDestination = nativeDestination; // Custom destinations keep their real identity.
                _currentDestinationId = entry.Request.DestinationId;
                _worldPlayerEntry = entry.Prepared.EntryPoint;
                _worldEntrySnapReach = SourceEntrySnapReach(entry.Prepared.World);
                BeginPlayerHealthWorld(entry.Request.WorldInstanceId, entry.Request.Generation);
                _destinationSelector?.QueueFree(); _destinationSelector = null;
                _sourceManager?.QueueFree(); _sourceManager = null;
                ActivateWorld(entry.Prepared.World, staged, nativeDestination, lab);
                return new SceneLease(_worldHost, _player);
            }));
            preparedScene = null;
            if (nativeDestination is not null)
            {
                DispatchRequestedWorldSmokes(nativeDestination);
                StartRequestedSourceVerification(nativeDestination, sourcePath!);
            }
            _playtest?.WorldEntered();
            if (_args.FusionSmoke && _worldSwitches == 1 && !_args.PlaytestRequested) _ = RunFusionWeaponsSmokeAsync();
            if (_args.PlayerSessionSmoke && _worldSwitches == 1) _ = RunPlayerSessionSmokeAsync();
            if (_args.PlayerCapabilitiesSmoke && _worldSwitches == 1) _ = RunPlayerCapabilitiesSmokeAsync();
            return true;
        }
        catch (Exception error)
        {
            if (activating) TeardownWorld();
            if (preparedScene is not null && GodotObject.IsInstanceValid(preparedScene.Root) && !preparedScene.Root.IsInsideTree())
                preparedScene.Root.Free();
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
        if (world.Game == "rac1" && world.PlayerStart is null)
            throw new InvalidDataException("R&C1 world is missing its authored player start.");
        return WorldEntryPlanning.ResolveEntryPoint(world);
    }

    // World-local authored placement clearance, not a player capability/profile.
    private static float SourceEntrySnapReach(RuntimeWorld world) =>
        world.Game == "fusion" || (world.Game == "rac2" && world.LevelId == 0 && world.PlayerStart is not null) ? .75f : 8f;

    private void ActivateWorld(RuntimeWorld world, RuntimeWorldScene.Result prepared,
        ObpDestination? destination, FusionLabDefinition? lab)
    {
        _world = world;
        _worldSwitches++;
        BeginSourceWorldEntry(world);
        _sceneResult = _worldHost.Load(this, _worldRoot, world, _currentDestinationId!, WorldOptions(world), prepared);
        SetupOverlay(_sceneResult, world);
        ConfigureGcGameplayHost();
        bool staticCamera = _args.ShotsPath is not null || _args.CaptureFrame is not null &&
            (_args.DirectLoad || _args.Destination is not null) && _args.TestScene != "player" &&
            !_args.AnimSolo && !_args.CrateFocus && !_args.CrateAutoStrike;
        if (!staticCamera)
        {
            ConfigureRac1Gameplay(world, _sceneResult);
            ConfigureUyaGameplay(world, _sceneResult);
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
        EnsurePlayerHud(); EnsureWorldHud();
        if (OrdinaryPlayerSession) PublishPersistentPlayerHud();
        UpdatePlayerHud(); UpdateWorldHud();
        CommitSourceWorldEntry(world);
        _loadSummary = $"✓ {destination?.DisplayName ?? (lab is not null ? "Fusion Lab" : world.DisplayName)} · {_sceneResult.MeshInstances} meshes";
        GD.Print($"[destinations] world ready — {_loadSummary} (switch #{_worldSwitches}, player generation {_playerSession!.Capture().Generation})");
    }

    private void BeginSourceWorldEntry(RuntimeWorld world)
    {
        if (world.Game != "rac1") return;
        _rac1CampaignSession.StartLevelCheckpointSession(world.LevelId, world.PlayerStart!);
        _worldPlayerEntry = _rac1CampaignSession.LevelCheckpoint!.AuthoredClass0;
    }

    private void CommitSourceWorldEntry(RuntimeWorld world)
    {
        if (world.Game != "rac1") return;
        var entry = _rac1CampaignSession.CommitLoadedLevel(world.LevelId);
        _rac1CampaignSession.FinishLoadedLevel(entry);
        if (entry == Rac1LevelEntryKind.CampaignTravel) PersistRac1CampaignState("completed campaign travel");
    }
}
