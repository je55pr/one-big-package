using Godot;
using OBP.Core;

namespace OneBigPackage;

/// <summary>
/// Scene-level startup/input adapter for the neutral trilogy navigation stack.
/// It attaches explicit source arguments, selects the requested startup surface,
/// and intercepts navigation input before player/world handlers consume it.
/// </summary>
public partial class SourceManagerBootstrap : Node
{
    private OBPGame? _game;

    public override void _Ready()
    {
        _game = GetParentOrNull<OBPGame>();
        CallDeferred(nameof(Activate));
    }

    public override void _Input(InputEvent @event)
    {
        if (_game is null)
        {
            return;
        }

        if (@event.IsActionPressed("obp_planet_map"))
        {
            if (_game.TryCloseRac1PlanetMap() || _game.TryOpenRac1PlanetMap())
                GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("ui_cancel") && _game.TryCloseRac1PlanetMap())
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            return;
        }

        if (_game.TryHandleInteractiveBack())
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private void Activate()
    {
        if (_game is null)
        {
            return;
        }

        CommandLineArgs args = _game.StartupArgs;
        if (args.Compose || args.ShotsPath is not null)
        {
            // These modes own their own navigation/lifecycle and do not use the
            // trilogy source-manager stack.
            _game = null;
            return;
        }

        string? rac1Path = args.Rac1Iso;
        string? gcPath = args.GcIso;
        string? uyaPath = args.UyaIso;
        string? destinationId = args.Destination;
        string? testScene = args.TestSceneExplicit ? args.TestScene : null;

        if (rac1Path is not null)
        {
            _game.RememberCommandLineSource(ObpSourceGame.Rac1, rac1Path);
        }
        if (gcPath is not null)
        {
            _game.RememberCommandLineSource(ObpSourceGame.Rac2, gcPath);
        }
        if (uyaPath is not null)
        {
            _game.RememberCommandLineSource(ObpSourceGame.Rac3, uyaPath);
        }

        if (args.Rac1CampaignSmoke)
        {
            _game.OpenRac1CampaignCurrentFromBootstrap();
            return;
        }

        if (args.Rac1StartupVisibilitySmoke)
        {
            _game.RunRac1StartupVisibilitySmokeFromBootstrap();
            return;
        }

        if (destinationId is not null)
        {
            _game.OpenDestinationFromBootstrap(destinationId);
            return;
        }

        if (args.StressSwitch is { } stressSequence)
        {
            _game.RunGcStressSwitchFromBootstrap(stressSequence);
            return;
        }

        if (gcPath is not null && (args.DirectLoad || testScene == "player"))
        {
            _game.OpenLegacyGcDestinationFromBootstrap();
            return;
        }

        if (testScene == "worlds")
        {
            _game.ShowDestinationSelectorFromBootstrap();
            return;
        }

        // Interactive executable/editor launch with no explicit test scene now
        // lands on the trilogy source manager. Deterministic harnesses always
        // pass --test-scene and therefore retain their existing smoke/player
        // behaviour. --test-scene picker is the deterministic source-screen path.
        if (testScene == "picker")
        {
            _game.ShowSourceManagerFromBootstrap();
            return;
        }

        if (testScene is null && gcPath is not null)
        {
            // Historical --gc-iso startup used to land on the GC-only planet
            // selector. Preserve the convenience through the neutral Worlds UI.
            _game.ShowDestinationSelectorFromBootstrap();
            return;
        }

        if (testScene is null)
        {
            // A plain double-click / "obp" launch: show the gamey title first,
            // then fall through to Game Sources. --skip-title bypasses it.
            if (args.SkipTitle)
            {
                _game.ShowSourceManagerFromBootstrap();
                return;
            }

            var title = new TitleScreen();
            title.Dismissed += () => _game?.ShowSourceManagerFromBootstrap();
            _game.AddChild(title);
        }
    }

}
