using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OneBigPackage;

public partial class OBPGame
{
    private DestinationSelectorUi? _destinationSelector;
    private Rac1PlanetTravelUi? _rac1PlanetTravelUi;
    private DebugPlayer? _rac1PlanetMapPausedPlayer;
    private Node.ProcessModeEnum? _rac1PlanetMapPreviousPlayerMode;
    private Input.MouseModeEnum _rac1PlanetMapPreviousMouseMode;
    private ObpDestination? _activeDestination;
    private string? _currentDestinationId;
    private bool _genericNavigationActive;

    private bool Rac1PlanetMapOpen =>
        _rac1PlanetTravelUi is not null && IsInstanceValid(_rac1PlanetTravelUi);

    /// <summary>Open the neutral world browser from the trilogy source screen.</summary>
    private void BrowseWorlds() => ShowDestinationSelector();

    /// <summary>Deterministic/debug bootstrap entry used by --test-scene worlds.</summary>
    public void ShowDestinationSelectorFromBootstrap()
    {
        EnsureSourceLibraryInitialized();
        ShowDestinationSelector();
    }

    /// <summary>
    /// Resolve a canonical neutral destination id (for example
    /// <c>rac2:LEVEL1</c>) and enter it through the same provider routing path.
    /// Legacy --planet/--gc-level options remain supported separately.
    /// </summary>
    public void OpenDestinationFromBootstrap(string destinationId)
    {
        EnsureSourceLibraryInitialized();
        var destination = TrilogyWorldProviders.Resolve(destinationId);
        if (destination is null)
        {
            GD.PrintErr($"[destinations] unknown destination id '{destinationId}'");
            BuildSourceManagerScreen();
            _sourceManager?.SetStatus($"Unknown destination id '{destinationId}'. Use a canonical id such as rac2:LEVEL1.", error: true);
            return;
        }

        _genericNavigationActive = _args.CaptureFrame is null;
        OnDestinationChosen(destination);
    }

    /// <summary>
    /// Smoke/bootstrap convenience that enters the persisted native CurrentLevel
    /// through the ordinary provider route without changing campaign state.
    /// </summary>
    public void OpenRac1CampaignCurrentFromBootstrap()
    {
        EnsureSourceLibraryInitialized();
        OpenDestinationFromBootstrap(
            $"rac1:LEVEL{_rac1CampaignSession.Campaign.CurrentLevel}");
    }

    /// <summary>
    /// Ordinary R&C1 campaign travel entry point for ship/map gameplay. The target
    /// must already be admitted by recovered progression and the active world must
    /// match campaign CurrentLevel. Loading still uses the normal provider/world
    /// teardown, import, adoption and player-spawn path.
    /// </summary>
    public Rac1PlanetTravelStartResult TravelRac1CampaignTo(int nativeLevelId)
    {
        EnsureSourceLibraryInitialized();

        if (_world is not { Game: "rac1" } current ||
            current.LevelId != _rac1CampaignSession.Campaign.CurrentLevel)
            return Rac1PlanetTravelStartResult.DestinationUnavailable;

        var destination = TrilogyWorldProviders.Resolve($"rac1:LEVEL{nativeLevelId}");
        var provider = destination is null ? null : TrilogyWorldProviders.Find(destination.Game);
        var source = _sources.Get(ObpSourceGame.Rac1);
        if (destination is null || destination.Game != ObpSourceGame.Rac1 ||
            provider is null || source is null || !provider.CanLoad(destination))
            return Rac1PlanetTravelStartResult.DestinationUnavailable;

        Rac1PlanetTravelStartResult start = _rac1CampaignSession.BeginTravel(nativeLevelId);
        if (start == Rac1PlanetTravelStartResult.Started)
            OnDestinationChosen(destination);

        return start;
    }

    /// <summary>
    /// Open the ordinary R&C1 campaign destination presentation without entering
    /// the developer world browser. The native session supplies both unlock order
    /// and current selection; Godot only renders and forwards a travel request.
    /// </summary>
    public bool TryOpenRac1PlanetMap()
    {
        if (Rac1PlanetMapOpen)
            return true;
        if (_mode != Mode.World || _args.CaptureFrame is not null ||
            _world is not { Game: "rac1" } current ||
            current.LevelId != _rac1CampaignSession.Campaign.CurrentLevel ||
            _rac1CampaignSession.Travel.TransitionActive)
            return false;

        Rac1PlanetMapSnapshot map = _rac1CampaignSession.Travel.OpenPlanetMap();

        _rac1PlanetMapPausedPlayer = _player is not null && IsInstanceValid(_player)
            ? _player
            : null;
        if (_rac1PlanetMapPausedPlayer is { } player)
        {
            _rac1PlanetMapPreviousPlayerMode = player.ProcessMode;
            player.ProcessMode = Node.ProcessModeEnum.Disabled;
        }

        _rac1PlanetMapPreviousMouseMode = Input.MouseMode;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _rac1PlanetTravelUi = new Rac1PlanetTravelUi();
        _rac1PlanetTravelUi.TravelRequested += OnRac1PlanetTravelRequested;
        _rac1PlanetTravelUi.BackRequested += () => TryCloseRac1PlanetMap();
        AddChild(_rac1PlanetTravelUi);
        _rac1PlanetTravelUi.Populate(map);

        GD.Print($"[rac1-travel] planet map shown — {map.UnlockedDestinations.Count} recovered destination(s)");
        return true;
    }

    public bool TryCloseRac1PlanetMap()
    {
        if (!Rac1PlanetMapOpen)
            return false;

        _rac1PlanetTravelUi!.QueueFree();
        _rac1PlanetTravelUi = null;

        if (_rac1PlanetMapPausedPlayer is { } player &&
            IsInstanceValid(player) &&
            _rac1PlanetMapPreviousPlayerMode is { } previousMode)
        {
            player.ProcessMode = previousMode;
        }

        _rac1PlanetMapPausedPlayer = null;
        _rac1PlanetMapPreviousPlayerMode = null;
        Input.MouseMode = _rac1PlanetMapPreviousMouseMode;
        return true;
    }

    private void OnRac1PlanetTravelRequested(int nativeLevelId)
    {
        Rac1PlanetTravelStartResult result = TravelRac1CampaignTo(nativeLevelId);
        switch (result)
        {
            case Rac1PlanetTravelStartResult.Started:
                TryCloseRac1PlanetMap();
                break;
            case Rac1PlanetTravelStartResult.AlreadyCurrentLevel:
                _rac1PlanetTravelUi?.SetHint("That is the current world.", error: true);
                break;
            case Rac1PlanetTravelStartResult.DestinationUnavailable:
                _rac1PlanetTravelUi?.SetHint("That destination is no longer available.", error: true);
                break;
        }
    }

    private void ShowDestinationSelector()
    {
        EnsureSourceLibraryInitialized();
        _mode = Mode.Selector;
        _sceneKind = "destinations";
        _genericNavigationActive = true;
        TeardownWorld();
        EnsurePlainEnvironment();

        _selector?.QueueFree();
        _selector = null;
        _pickerPanel?.QueueFree();
        _pickerPanel = null;
        _sourceManager?.QueueFree();
        _sourceManager = null;
        if (_worldHud is not null && IsInstanceValid(_worldHud))
        {
            _worldHud.Visible = false;
        }

        _camera.Current = true;
        _activeCamera = _camera;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _destinationSelector?.QueueFree();
        _destinationSelector = new DestinationSelectorUi();
        _destinationSelector.DestinationChosen += OnDestinationChosen;
        _destinationSelector.BackRequested += () =>
        {
            _genericNavigationActive = false;
            _destinationSelector?.QueueFree();
            _destinationSelector = null;
            BuildSourceManagerScreen();
        };
        AddChild(_destinationSelector);
        _destinationSelector.Populate(_sources, TrilogySourceDefinitions.All, TrilogyWorldProviders.All, _currentDestinationId);

        GD.Print($"[destinations] selector shown — {TrilogyWorldProviders.All.Count} runtime provider(s)");
    }

    private void OnDestinationChosen(ObpDestination destination)
    {
        var provider = TrilogyWorldProviders.Find(destination.Game);
        var source = _sources.Get(destination.Game);
        if (provider is null)
        {
            DestinationError($"No production world provider is registered for {destination.Game} yet.");
            return;
        }
        if (source is null)
        {
            DestinationError("That game source is no longer attached. Return to Game Sources and select it again.");
            return;
        }
        if (!provider.CanLoad(destination))
        {
            DestinationError($"Provider {provider.BuildId} rejected destination {destination.DestinationId}.");
            return;
        }

        _currentDestinationId = destination.DestinationId;
        _activeDestination = destination;
        _destinationSelector?.QueueFree();
        _destinationSelector = null;
        _selector?.QueueFree();
        _selector = null;

        EnterProviderWorld(provider, source.Path, destination);
    }

    /// <summary>
    /// Complete game-neutral world-entry path: provider imports native bytes into
    /// RuntimeWorld, then the shared Godot builder renders/collides/spawns it.
    /// No source-game catalogue or native level type is consulted here.
    /// </summary>
    private void EnterProviderWorld(IObpWorldProvider provider, string sourcePath, ObpDestination destination)
    {
        TeardownWorld();
        ClearBootstrapSceneArtifacts();

        string gameSlug = destination.Game.ToString().ToLowerInvariant();
        string placeSlug = destination.PlanetLabel.ToLowerInvariant().Replace(' ', '-');
        _sceneKind = $"{gameSlug}-{placeSlug}";

        RuntimeWorld world;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            world = TrilogyWorldProviders.Registry.Load(sourcePath, destination);
            sw.Stop();
            GD.Print($"[destinations] imported {destination.DestinationId} / {destination.DisplayName} in {sw.ElapsedMilliseconds} ms — " +
                     $"{world.Meshes.Count} meshes / {world.TotalRenderTriangles:N0} tris / {world.TotalCollisionTriangles:N0} coll tris");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[destinations] import of {destination.DestinationId} failed: {ex.Message}\n{ex.StackTrace}");
            if (destination.Game == ObpSourceGame.Rac1 &&
                _rac1CampaignSession.Travel.TransitionActive &&
                !_rac1CampaignSession.Travel.CurrentLevelCommitted)
            {
                _rac1CampaignSession.AbandonUncommittedHostLoad();
            }
            _world = null;
            if (_args.CaptureFrame is not null)
            {
                ApplicationLifecycle.RequestQuit(this, "destination-capture-import-failed", 1);
                return;
            }

            ShowDestinationSelector();
            _destinationSelector?.SetHint($"⚠ {destination.DisplayName} failed to import: {ex.Message}", error: true);
            return;
        }

        AdoptRuntimeWorld(world, sourcePath, destination);
    }

    /// <summary>
    /// Host-only half of world entry. Everything before this point is source-game
    /// parsing; everything from here on is generic RuntimeWorld presentation.
    /// This is the seam future R&C1/UYA providers plug into.
    /// </summary>
    private void AdoptRuntimeWorld(RuntimeWorld world, string sourcePath, ObpDestination destination)
    {
        _world = world;
        _worldSwitches++;

        bool framedCapture = _args.CaptureFrame is not null
            && (_args.DirectLoad || _args.Destination is not null)
            && _args.TestScene != "player"
            && !_args.AnimSolo
            && !_args.CrateFocus && !_args.CrateAutoStrike;
        bool staticCamera = framedCapture || _args.ShotsPath is not null;

        var result = _worldHost.Load(this, _worldRoot, world, $"World_{destination.Game}_{destination.NativeDestinationId}", new WorldHost.Options
        {
            IncludeMobyMarkers = !(_args.CaptureFrame is not null && !framedCapture),
            IncludeSky = !_args.AnimSolo,
            IncludeCollision = true,
            ShowCollisionDebug = _args.CollisionDebug,
            OnlyAnimatedMobies = _args.AnimSolo,
        });
        Rac1LevelEntryKind? rac1Entry = world.Game == "rac1"
            ? _rac1CampaignSession.CommitLoadedLevel(world.LevelId)
            : null;
        if (world.Game == "rac1")
        {
            RuntimeSpawn authoredClass0 = world.PlayerStart
                ?? throw new InvalidDataException("R&C1 world is missing its recovered authored class-0 player start.");
            _rac1CampaignSession.StartLevelCheckpointSession(world.LevelId, authoredClass0);
        }
        _sceneResult = result;
        SetupOverlay(result, world);
        ConfigureCrateDebugHarness();
        if (!staticCamera)
        {
            ConfigureRac1Gameplay(world, result);
        }

        if (_args.AnimSolo)
        {
            FrameAnimatedMobies(world);
        }
        else if (staticCamera)
        {
            FrameShowcaseCamera(world);
        }
        else
        {
            SpawnPlayer(world);
        }

        if (rac1Entry is { } completedRac1Entry)
        {
            _rac1CampaignSession.FinishLoadedLevel(completedRac1Entry);
            if (completedRac1Entry == Rac1LevelEntryKind.CampaignTravel)
                PersistRac1CampaignState("completed campaign travel");
        }

        _mode = Mode.World;
        EnsurePlayerHud();
        EnsureWorldHud();
        UpdatePlayerHud();
        UpdateWorldHud();

        _loadSummary = $"✓ {destination.DisplayName} · {result.MeshInstances} meshes / {result.Triangles:N0} tris / {result.CollisionBodies} colliders";
        GD.Print($"[destinations] world ready — {_loadSummary} (switch #{_worldSwitches})");

        if (_args.Rac1VeldinPlaySmoke && _worldSwitches == 1 && destination.Game == ObpSourceGame.Rac1)
        {
            _ = RunRac1VeldinPlaySmokeAsync();
        }

        if (_args.Rac1CombatContractSmoke && _worldSwitches == 1 && destination.Game == ObpSourceGame.Rac1)
        {
            _ = RunRac1CombatContractSmokeAsync();
        }

        if (_args.Rac1CampaignSmoke && _worldSwitches == 1 && destination.Game == ObpSourceGame.Rac1)
        {
            _ = RunRac1CampaignSmokeAsync();
        }

        if (_args.MovementSmoke && _worldSwitches == 1)
        {
            _ = RunMovementSmokeAsync(destination);
        }

        // Exact verification remains game-specific today. Preserve the mature GC
        // opt-in while avoiding an implicit multi-GB hash for other providers.
        if (_args.VerifyHash && _worldSwitches == 1 && destination.Game == ObpSourceGame.Rac2)
        {
            StartBackgroundVerify(sourcePath);
        }
    }

    private void DestinationError(string message)
    {
        GD.PrintErr($"[destinations] {message}");
        if (_destinationSelector is not null)
        {
            _destinationSelector.SetHint(message, error: true);
        }
        else if (_sourceManager is not null)
        {
            _sourceManager.SetStatus(message, error: true);
        }
    }

    /// <summary>
    /// Called in the early input phase so an interactive world entered from the
    /// generic browser returns to Worlds rather than the legacy GC selector.
    /// </summary>
    public bool TryReturnWorldToDestinations()
    {
        if (!_genericNavigationActive || _mode != Mode.World || _args.CaptureFrame is not null)
        {
            return false;
        }

        ShowDestinationSelector();
        return true;
    }
}
