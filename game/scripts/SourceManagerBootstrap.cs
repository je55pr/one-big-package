using Godot;
using OBP.Core;

namespace OBP.Game;

/// <summary>
/// Scene-level startup/input adapter for the neutral trilogy navigation stack.
/// It checks resolved source readiness, selects the requested startup surface,
/// and intercepts navigation input before player/world handlers consume it.
/// </summary>
public partial class SourceManagerBootstrap : Node
{
    private OBPGame? _game;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _game = GetParentOrNull<OBPGame>();
        CallDeferred(nameof(Activate));
    }

    public override void _Input(InputEvent @event)
    {
        if (_game is null || _game.BugReportModalOpen)
        {
            return;
        }

        if (@event is InputEventJoypadButton && @event.IsActionPressed(ControllerBindings.Pause))
        {
            if (_game.TryHandleInteractiveBack()) GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(ControllerBindings.QuickSelect) && _game.TryOpenWeaponQuickSelect(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("obp_planet_map"))
        {
            if (_game.TryCloseRac1PlanetMap() || _game.TryOpenRac1PlanetMap())
                GetViewport().SetInputAsHandled();
            return;
        }

    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_game is null) return;
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
        if (args.ShotsPath is not null)
        {
            // These modes own their own navigation/lifecycle and do not use the
            // trilogy source-manager stack.
            _game = null;
            return;
        }

        string? rac2Path = args.Rac2Iso;
        string? destinationId = args.Destination;
        string? testScene = args.TestSceneExplicit ? args.TestScene : null;

        if (args.ControllerSmoke) return;
        if (!_game.RequireStartupSetup()) return;

        if (args.LoadMovementState is not null)
        {
            _game.LoadMovementStateFromBootstrap();
            return;
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
            _game.RunRac2StressSwitchFromBootstrap(stressSequence);
            return;
        }

        if (rac2Path is not null && (args.DirectLoad || testScene == "player"))
        {
            _game.OpenLegacyRac2DestinationFromBootstrap();
            return;
        }

        if (!args.TestSceneExplicit && _game.TryResumeSharedProfileFromBootstrap()) return;
        if (_game.ProfileRecoveryRequired) { _game.ShowTitleFromBootstrap(); return; }

        if (testScene == "worlds")
        {
            _game.ShowDestinationSelectorFromBootstrap();
            return;
        }

        // Explicit test scenes retain their diagnostic startup surfaces.
        // --test-scene picker is the deterministic source-screen path.
        if (testScene == "picker")
        {
            _game.ShowSourceManagerFromBootstrap();
            return;
        }

        if (testScene is null && rac2Path is not null)
        {
            // Historical --gc-iso startup used to land on the GC-only planet
            // selector. Preserve the convenience through the neutral Worlds UI.
            _game.ShowDestinationSelectorFromBootstrap();
            return;
        }

        if (testScene is null)
        {
            // A plain double-click / "obp" launch: show the gamey title first,
            // then continue to World Select. --skip-title bypasses it.
            if (args.SkipTitle)
            {
                _game.ShowDestinationSelectorFromBootstrap();
                return;
            }

            _game.ShowTitleFromBootstrap();
        }
    }

}
