using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OBP.Game;

public partial class OBPGame
{
    private DestinationSelectorUi? _destinationSelector;
    private Rac1PlanetTravelUi? _rac1PlanetTravelUi;
    private PlayerHost? _rac1PlanetMapPausedPlayer;
    private Node.ProcessModeEnum? _rac1PlanetMapPreviousPlayerMode;
    private Input.MouseModeEnum _rac1PlanetMapPreviousMouseMode;
    private bool _planetMapOwnsPause;
    private int _preMapHeldButtons;
    private ObpDestination? _activeDestination => _worldScenes?.NativeDestination;
    private string? _currentDestinationId => _worldScenes?.DestinationId;
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
    /// <c>rac2:LEVEL1</c>) and enter it through the production provider route.
    /// </summary>
    public void OpenDestinationFromBootstrap(string destinationId)
    {
        EnsureSourceLibraryInitialized();
        if (destinationId == "obp:fusion-lab") { EnterFusionLab(); return; }
        var destination = TrilogyWorldProviders.Resolve(destinationId);
        if (destination is null)
        {
            GD.PrintErr($"[destinations] unknown destination id '{destinationId}'");
            BuildSourceManagerScreen();
            _sourceManager?.SetStatus($"Unknown destination id '{destinationId}'. Use a canonical id such as rac2:LEVEL1.", error: true);
            return;
        }

        _genericNavigationActive = _args.CaptureFrame is null;
        if ((_args.PlaytestRequested || _args.FusionSmoke || _args.PlayerSessionSmoke || _args.PlayerCapabilitiesSmoke || _args.TrilogyGameplaySmoke) && _fusionPreparation is not null)
        {
            WorldScenes.PrepareLater(token => EnterWorldAfterSharedPreparationAsync(destination, token), ReportWorldPreparationFailure);
            return;
        }
        OnDestinationChosen(destination);
    }

    private async Task EnterWorldAfterSharedPreparationAsync(ObpDestination destination, CancellationToken cancellationToken)
    {
        // Capture source identities before the first deterministic simulation step.
        // Yield frames so disc verification never blocks the application window.
        long firstFrame = _frame;
        while (_fusionPreparation is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForWorldPreparationFrameAsync(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        GD.Print($"[fusion] source setup remained responsive for {_frame - firstFrame} frames before fixture/recording entry");
        if (_fusionAssetFailure is not null)
        {
            GD.PrintErr("[playtest] " + _fusionAssetFailure);
            ApplicationLifecycle.RequestQuit(this, "shared-equipment-preparation-failed", 2);
            return;
        }
        OnDestinationChosen(destination);
    }

    private Task WaitForWorldPreparationFrameAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GodotAsyncWait.SignalAsync(GetTree(), SceneTree.SignalName.ProcessFrame, cancellationToken);
    }

    /// <summary>
    /// Translate the historical GC --planet / --gc-level convenience options
    /// into the canonical destination model. No separate GC world lifecycle is
    /// retained for these aliases.
    /// </summary>
    public void OpenLegacyRac2DestinationFromBootstrap()
    {
        int levelId = _args.Planet is { } token
            ? OBP.RAC2.Rac2PlanetCatalogue.Resolve(token) ?? _args.Rac2Level
            : _args.Rac2Level;
        OpenDestinationFromBootstrap($"rac2:LEVEL{levelId}");
    }

    public void RunRac2StressSwitchFromBootstrap(string sequence) =>
        _hostOperations.Start("stress-switch", token => RunStressSwitchAsync(sequence, token), lifetime: CancellationToken.None);

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

        if (_world is not { Game.SourceGame: ObpSourceGame.Rac1 } current ||
            current.LevelId != _rac1CampaignSession.Campaign.CurrentLevel)
            return Rac1PlanetTravelStartResult.DestinationUnavailable;

        var destination = TrilogyWorldProviders.Resolve($"rac1:LEVEL{nativeLevelId}");
        var provider = destination is null ? null : TrilogyWorldProviders.Find(destination.Game);
        var source = _sources.Get(ObpSourceGame.Rac1);
        if (destination is null || destination.Game != ObpSourceGame.Rac1 ||
            provider is null || source is null || !provider.CanLoad(destination))
            return Rac1PlanetTravelStartResult.DestinationUnavailable;

        Rac1PlanetTravelStartResult start = _rac1CampaignSession.BeginTravel(nativeLevelId);
        if (start == Rac1PlanetTravelStartResult.Started && !OnDestinationChosen(destination))
            return Rac1PlanetTravelStartResult.DestinationUnavailable;

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
        if (_mode != Mode.World || PauseMenuOpen || ControllerSettingsOpen || QuickSelectOpen || GetTree().Paused || _args.CaptureFrame is not null ||
            _world is not { Game.SourceGame: ObpSourceGame.Rac1 } current ||
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

        _rac1PlanetMapPreviousMouseMode = ApplicationPointer.MouseMode;
        _preMapHeldButtons = _player?.MenuHeldButtons ?? 0;
        ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;

        _rac1PlanetTravelUi = new Rac1PlanetTravelUi { ProcessMode = ProcessModeEnum.Always };
        _rac1PlanetTravelUi.TravelRequested += OnRac1PlanetTravelRequested;
        _rac1PlanetTravelUi.BackRequested += () => TryCloseRac1PlanetMap();
        AddChild(_rac1PlanetTravelUi);
        _rac1PlanetTravelUi.Populate(map);
        GetTree().Paused = true;
        _planetMapOwnsPause = true;

        GD.Print($"[rac1-travel] planet map shown — {map.UnlockedDestinations.Count} recovered destination(s)");
        return true;
    }

    public bool TryCloseRac1PlanetMap()
    {
        if (!Rac1PlanetMapOpen)
            return false;

        _rac1PlanetTravelUi!.ProcessMode = ProcessModeEnum.Disabled;
        _rac1PlanetTravelUi.QueueFree();
        _rac1PlanetTravelUi = null;
        if (_planetMapOwnsPause) { GetTree().Paused = false; _planetMapOwnsPause = false; }

        if (_rac1PlanetMapPausedPlayer is { } player &&
            IsInstanceValid(player) &&
            _rac1PlanetMapPreviousPlayerMode is { } previousMode)
        {
            player.ProcessMode = previousMode;
        }

        _rac1PlanetMapPausedPlayer = null;
        _rac1PlanetMapPreviousPlayerMode = null;
        ApplicationPointer.MouseMode = _rac1PlanetMapPreviousMouseMode;
        _player?.SuppressButtonsUntilRelease(_preMapHeldButtons);
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
        if (!RequireOrdinarySourceSetup("world selection")) return;
        ClearTitleScreen();
        _mode = Mode.Selector;
        _sceneKind = "destinations";
        _genericNavigationActive = true;
        TeardownWorld();
        EnsurePlainEnvironment();

        _sourceManager?.QueueFree();
        _sourceManager = null;
        if (_worldHud is not null && IsInstanceValid(_worldHud))
        {
            _worldHud.Visible = false;
        }

        _camera.Current = true;
        _activeCamera = _camera;
        ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;

        _destinationSelector?.QueueFree();
        _destinationSelector = new DestinationSelectorUi { DebugOutputEnabled = _debugOutputEnabled, DiagnosticSources = _diagnosticSourceAccess };
        _destinationSelector.DestinationChosen += destination => OnDestinationChosen(destination);
        _destinationSelector.FusionLabRequested += () => EnterFusionLab();
        _destinationSelector.BackRequested += () => TryReturnSelectorToMenu();
        AddChild(_destinationSelector);
        _destinationSelector.Populate(_sources, TrilogySourceDefinitions.All, TrilogyWorldProviders.All, _currentDestinationId);

        GD.Print($"[destinations] selector shown — {TrilogyWorldProviders.All.Count} runtime provider(s)");
    }

    private bool OnDestinationChosen(ObpDestination destination)
    {
        _worldScenes?.CancelPreparation();
        if (!RequireOrdinarySourceSetup("destination entry")) return false;
        var provider = TrilogyWorldProviders.Find(destination.Game);
        var source = _sources.Get(destination.Game);
        if (provider is null)
        {
            DestinationError($"No production world provider is registered for {destination.Game} yet.");
            AbandonFailedRac1Travel();
            return false;
        }
        if (source is null)
        {
            DestinationError("That game source is no longer attached. Return to Game Sources and select it again.");
            AbandonFailedRac1Travel();
            return false;
        }
        if (!provider.CanLoad(destination))
        {
            DestinationError($"Provider {provider.BuildId} rejected destination {destination.DestinationId}.");
            AbandonFailedRac1Travel();
            return false;
        }

        return EnterProviderWorld(source.Path, destination);
    }

    /// <summary>
    /// Complete game-neutral world-entry path: provider imports native bytes into
    /// RuntimeWorld, then the shared Godot builder renders/collides/spawns it.
    /// No source-game catalogue or native level type is consulted here.
    /// </summary>
    private bool EnterProviderWorld(string sourcePath, ObpDestination destination) =>
        EnterWorld(destination.DestinationId, () => TrilogyWorldProviders.Registry.Load(sourcePath, destination), destination, sourcePath);
    private void AbandonFailedRac1Travel()
    {
        if (_rac1CampaignSession.Travel.TransitionActive &&
            !_rac1CampaignSession.Travel.CurrentLevelCommitted)
            _rac1CampaignSession.AbandonUncommittedHostLoad();
    }

    /// <summary>
    /// Host-only half of world entry. Everything before this point is source-game
    /// parsing; everything from here on is generic RuntimeWorld presentation.
    /// This is the seam future R&C1/UYA providers plug into.
    /// </summary>
    private WorldHost.Options WorldOptions(RuntimeWorld world)
    {
        bool framedCapture = _args.CaptureFrame is not null
            && (_args.DirectLoad || _args.Destination is not null)
            && _args.TestScene != "player"
            && !_args.AnimSolo
            && !_args.CrateFocus && !_args.CrateAutoStrike;
        return new WorldHost.Options
        {
            IncludeMobyMarkers = !(_args.CaptureFrame is not null && !framedCapture),
            IncludeSky = !_args.AnimSolo,
            IncludeCollision = true,
            AdmitCollisionFace = world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 1 }
                ? AdmitRac1NovalisCollisionFace
                : null,
            ShowCollisionDebug = _args.CollisionDebug,
            OnlyAnimatedMobies = _args.AnimSolo,
        };
    }

    private void DestinationError(string message)
    {
        GD.PrintErr($"[destinations] {message}");
        if (_destinationSelector is not null)
        {
            _destinationSelector.SetHint(_debugOutputEnabled ? message : "That world could not be opened. Please choose another world.", error: true);
        }
        else if (_sourceManager is not null)
        {
            _sourceManager.SetStatus(message, error: true);
        }
    }

    /// <summary>
    /// Called in the early input phase so an interactive provider world returns
    /// to the neutral Worlds browser.
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
