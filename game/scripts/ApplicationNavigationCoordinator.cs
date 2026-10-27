using System.IO;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// Owns ordinary application pages, selection intent, and navigation lifetime.
/// WorldSceneLifetime remains the only owner of loading and retiring worlds.
/// </summary>
internal sealed class ApplicationNavigationCoordinator
{
    private readonly ApplicationNavigationPorts _ports;
    private readonly Node3D _owner;
    internal ApplicationNavigationCoordinator(ApplicationNavigationPorts ports)
    {
        _ports = ports;
        _owner = ports.Host;
        _planetMap = new(_owner, ports.Player);
    }
    private TitleScreen? _title;
    private DestinationSelectorUi? _selector;
    private readonly Rac1PlanetMapPage _planetMap;
    private readonly FusionLabPresentation _lab = new();
    private bool _worldCanReturnToBrowser;
    private int _intentGeneration;

    internal TitleScreen? Title => _title;
    internal DestinationSelectorUi? Selector => _selector;
    internal bool PlanetMapOpen => _planetMap.IsOpen;

    internal void SetSelectorVisible(bool visible)
    {
        if (_selector is not null && GodotObject.IsInstanceValid(_selector))
            _selector.Visible = visible;
    }

    internal void RetireSelector()
    {
        if (_selector is not null && GodotObject.IsInstanceValid(_selector))
        {
            _selector.ProcessMode = Node.ProcessModeEnum.Disabled;
            _selector.QueueFree();
        }
        _selector = null;
    }

    internal void ClearTitle()
    {
        if (_title is not null && GodotObject.IsInstanceValid(_title))
        {
            _title.ProcessMode = Node.ProcessModeEnum.Disabled;
            _title.QueueFree();
        }
        _title = null;
    }

    internal void RetireMenuPages()
    {
        ClearTitle();
        RetireSelector();
    }

    internal void WorldActivated(bool interactive)
    {
        ++_intentGeneration;
        _worldCanReturnToBrowser = interactive;
    }

    internal void CancelNavigation()
    {
        ++_intentGeneration;
        _ports.CancelPreparation();
    }

    internal void ShowTitle()
    {
        CancelNavigation();
        if (!_ports.Sources().RequireOrdinary("title")) return;
        _worldCanReturnToBrowser = false;
        RetireMenuPages();
        _ports.EnterPage(true);
        _title = new TitleScreen { Notice = _ports.SaveNotice() };
        int generation = _intentGeneration;
        _title.Dismissed += () =>
        {
            if (generation == _intentGeneration && ReferenceEquals(_title?.GetParent(), _owner))
                ShowSelector();
        };
        _owner.AddChild(_title);
    }

    internal bool TryReturnSelectorToMenu()
    {
        if (!_ports.IsSelector() || _ports.Args().CaptureFrame is not null)
            return false;

        CancelNavigation();
        _worldCanReturnToBrowser = false;
        RetireSelector();
        if (_ports.Sources().Setup.Access == SourceAccessMode.ExplicitDiagnostic)
            _ports.Sources().Show();
        else
            ShowTitle();
        return true;
    }

    internal void ShowSelector()
    {
        CancelNavigation();
        var sources = _ports.Sources();
        sources.Initialize();
        if (!sources.RequireOrdinary("world selection")) return;
        RetireMenuPages();
        _worldCanReturnToBrowser = true;
        _ports.EnterPage(false);

        _selector = new DestinationSelectorUi
        {
            DebugOutputEnabled = _ports.DebugOutput(),
            DiagnosticSources = sources.Setup.Access == SourceAccessMode.ExplicitDiagnostic,
        };
        int generation = _intentGeneration;
        _selector.DestinationChosen += destination =>
        {
            if (generation == _intentGeneration) Choose(destination);
        };
        _selector.FusionLabRequested += () =>
        {
            if (generation == _intentGeneration) EnterLab();
        };
        _selector.BackRequested += () =>
        {
            if (generation == _intentGeneration) TryReturnSelectorToMenu();
        };
        _owner.AddChild(_selector);
        _selector.Populate(sources.Setup.Library, TrilogySourceDefinitions.All,
            TrilogyWorldProviders.All, _ports.CurrentDestinationId());
        GD.Print($"[destinations] selector shown — {TrilogyWorldProviders.All.Count} runtime provider(s)");
    }

    internal void OpenFromBootstrap(string id)
    {
        CancelNavigation();
        _ports.Sources().Initialize();
        if (id == "obp:fusion-lab") { EnterLab(); return; }
        var destination = TrilogyWorldProviders.Resolve(id);
        if (destination is null)
        {
            GD.PrintErr($"[destinations] unknown destination id '{id}'");
            _ports.Sources().Show();
            _ports.Sources().SetStatus(
                $"Unknown destination id '{id}'. Use a canonical id such as rac2:LEVEL1.", error: true);
            return;
        }

        _worldCanReturnToBrowser = _ports.Args().CaptureFrame is null;
        var args = _ports.Args();
        if ((args.PlaytestRequested || args.FusionSmoke || args.PlayerSessionSmoke ||
             args.PlayerCapabilitiesSmoke || args.TrilogyGameplaySmoke) && _ports.FusionPreparing())
        {
            int generation = _intentGeneration;
            _ports.Scenes().PrepareLater(
                token => EnterAfterSharedPreparationAsync(destination, generation, token),
                _ports.ReportFailure);
            return;
        }
        Choose(destination);
    }

    private async Task EnterAfterSharedPreparationAsync(ObpDestination destination, int generation,
        CancellationToken cancellationToken)
    {
        long firstFrame = _ports.Frame();
        while (_ports.FusionPreparing())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await GodotAsyncWait.SignalAsync(_owner.GetTree(), SceneTree.SignalName.ProcessFrame, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (generation != _intentGeneration || !_owner.IsInsideTree()) return;
        GD.Print($"[fusion] source setup remained responsive for {_ports.Frame() - firstFrame} frames before fixture/recording entry");
        if (_ports.FusionFailure() is { } failure)
        {
            GD.PrintErr("[playtest] " + failure);
            ApplicationLifecycle.RequestQuit(_owner, "shared-equipment-preparation-failed", 2);
            return;
        }
        Choose(destination);
    }

    internal Rac1PlanetTravelStartResult TravelRac1CampaignTo(int nativeLevelId)
    {
        _ports.Sources().Initialize();
        var campaign = _ports.Campaign();
        if (_ports.World() is not { Game.SourceGame: ObpSourceGame.Rac1 } current ||
            current.LevelId != campaign.Campaign.CurrentLevel)
            return Rac1PlanetTravelStartResult.DestinationUnavailable;

        var destination = TrilogyWorldProviders.Resolve($"rac1:LEVEL{nativeLevelId}");
        var provider = destination is null ? null : TrilogyWorldProviders.Find(destination.Game);
        var source = _ports.Sources().Setup.Library.Get(ObpSourceGame.Rac1);
        if (destination is null || destination.Game != ObpSourceGame.Rac1 ||
            provider is null || source is null || !provider.CanLoad(destination))
            return Rac1PlanetTravelStartResult.DestinationUnavailable;

        var start = campaign.BeginTravel(nativeLevelId);
        if (start == Rac1PlanetTravelStartResult.Started && !Choose(destination))
            return Rac1PlanetTravelStartResult.DestinationUnavailable;
        return start;
    }

    internal bool TryOpenPlanetMap()
    {
        if (_planetMap.IsOpen) return true;
        var campaign = _ports.Campaign();
        if (!_ports.IsWorld() || _ports.PauseMenuOpen() ||
            _ports.ControllerSettingsOpen() || _ports.QuickSelectOpen() ||
            _owner.GetTree().Paused || _ports.Args().CaptureFrame is not null ||
            _ports.World() is not { Game.SourceGame: ObpSourceGame.Rac1 } current ||
            current.LevelId != campaign.Campaign.CurrentLevel || campaign.Travel.TransitionActive)
            return false;
        return _planetMap.Open(campaign.Travel.OpenPlanetMap(), TravelRac1CampaignTo);
    }

    internal bool TryClosePlanetMap() => _planetMap.Close();

    internal bool Choose(ObpDestination destination)
    {
        // A rejected world must leave the current browser usable for retry.
        // Successful activation will retire its page generation and controls.
        _ports.CancelPreparation();
        if (!_ports.Sources().RequireOrdinary("destination entry")) return false;
        var provider = TrilogyWorldProviders.Find(destination.Game);
        var source = _ports.Sources().Setup.Library.Get(destination.Game);
        if (provider is null)
        {
            Error($"No production world provider is registered for {destination.Game} yet.");
            _ports.AbandonFailedTravel();
            return false;
        }
        if (source is null)
        {
            Error("That game source is no longer attached. Return to Game Sources and select it again.");
            _ports.AbandonFailedTravel();
            return false;
        }
        if (!provider.CanLoad(destination))
        {
            Error($"Provider {provider.BuildId} rejected destination {destination.DestinationId}.");
            _ports.AbandonFailedTravel();
            return false;
        }
        return EnterProviderWorld(source.Path, destination);
    }

    internal bool EnterProviderWorld(string path, ObpDestination destination) =>
        _ports.EnterWorld(destination.DestinationId,
            () => TrilogyWorldProviders.Registry.Load(path, destination), destination, path, null);

    internal bool EnterLab()
    {
        // Keep the browser live if loading the Lab definition fails.
        _ports.CancelPreparation();
        try
        {
            var definition = FusionLabDefinition.Parse(
                File.ReadAllText(ProjectSettings.GlobalizePath("res://data/fusion_lab.json")));
            return _ports.EnterWorld(definition.Id,
                () => FusionLabWorldFactory.Build(definition), null, null, definition);
        }
        catch (Exception error)
        {
            Error("Fusion Lab layout is unavailable: " + error.Message);
            return false;
        }
    }

    internal void DecorateLab(FusionLabDefinition definition, bool nativeAvatarAvailable) =>
        _lab.Decorate(definition, _ports.SceneResult(), _ports.Ui(),
            _ports.Sources().Setup.Access == SourceAccessMode.ExplicitDiagnostic, nativeAvatarAvailable);

    internal void ClearLab() => _lab.Clear();

    internal void UpdateLabStatus(int active, int total, IReadOnlyDictionary<string, string> problems) =>
        _lab.UpdateStatus(active, total, problems);

    internal void Error(string message)
    {
        GD.PrintErr($"[destinations] {message}");
        if (_selector is not null && GodotObject.IsInstanceValid(_selector))
            _selector.SetHint(_ports.DebugOutput() ? message :
                "That world could not be opened. Please choose another world.", error: true);
        else if (_ports.Sources().HasView)
            _ports.Sources().SetStatus(message, error: true);
    }

    internal void SetSelectorHint(string message) => _selector?.SetHint(message, error: true);

    internal bool TryReturnWorldToDestinations()
    {
        if (!_worldCanReturnToBrowser || !_ports.IsWorld() ||
            _ports.Args().CaptureFrame is not null)
            return false;
        ShowSelector();
        return true;
    }
}
