using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;

namespace OneBigPackage;

/// <summary>Real Godot GUI/input regression. Sends logical joypad events, never desktop input.</summary>
public partial class ControllerSmoke : Node
{
    private OBPGame _game = null!;
    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        try
        {
            Engine.MaxFps = 120;
            Input.SetIgnoreJoypadOnUnfocusedApplication(false);
            GetTree().Root.Size = new Vector2I(960, 720);
            GetTree().CreateTimer(90).Timeout += () => ApplicationLifecycle.RequestQuit(this, "controller-smoke-timeout", 1);
            var args = CommandLineArgs.Parse(OS.GetCmdlineUserArgs());
            Rac1RetailUiAssets.Clear();
            Require(Rac1RetailUiAssets.Font() is null && Rac1RetailUiAssets.Sprite(0x7544) is null,
                "Unattached retail UI did not use the fallback");
            _game = GD.Load<PackedScene>("res://scenes/OBPGame.tscn").Instantiate<OBPGame>();
            _game.ProcessMode = ProcessModeEnum.Pausable;
            AddChild(_game); await Frames(10);
            bool titleDismissed = false;
            var title = new TitleScreen(); title.Dismissed += () => titleDismissed = true;
            _game.AddChild(title); await Frames(3);
            await Press(JoyButton.Start); await Frames(55);
            Require(titleDismissed, "Start did not dismiss the title screen");
            _game.ShowSourceManagerFromBootstrap(); await Frames(5);
            if (args.Rac1Iso is not null) CheckRetailUiAssets(args.Rac1Iso);
            await Capture("sources");
            Require(Focus() is Button, "Source menu has no controller focus");
            var initial = Focus(); await Press(JoyButton.DpadDown);
            Require(Focus() != initial, "D-pad does not navigate source controls");
            await FocusButton("CONTROLLER SETTINGS"); await Press(JoyButton.A);
            Require(Find<ControllerSettingsUi>() is { Visible: true }, "Cross does not open controller settings");
            var settings = Find<ControllerSettingsUi>()!;
            var settingsScroll = Descendants(settings).OfType<ScrollContainer>().Single();
            Require(settingsScroll.GetHScrollBar().MaxValue <= settingsScroll.Size.X + .1f,
                "Bitmap font widths pushed controller settings offscreen");
            await Capture("controller-settings");
            Require(Focus() is not null && settings.IsAncestorOf(Focus()), "Settings did not take focus");
            if (Focus() is OptionButton)
            {
                await Press(JoyButton.A); await Press(JoyButton.B);
                Require(IsInstanceValid(settings), "Closing device popup also closed settings");
            }
            await Press(JoyButton.Y);
            Require(Find<ControllerSettingsUi>() is null && Focus() is Button { Text: "CONTROLLER SETTINGS" }, "Settings Back lost source-menu focus");
            GD.Print("[controller-smoke] Cross / Triangle / D-pad, settings modal and restored focus PASS");

            await FocusButton("SOURCE", contains: true); await Press(JoyButton.A);
            var dialog = Find<FileDialog>();
            Require(dialog is { Visible: true, UseNativeDialog: false }, "Source attachment escaped into a desktop-only dialog");
            string fixture = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "captures", "controller-smoke", "browser"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(fixture, "child"));
            dialog!.CurrentDir = fixture; await Frames(5);
            string originalDirectory = dialog.CurrentDir;
            await Press(JoyButton.DpadDown); await Press(JoyButton.A);
            Require(dialog.CurrentDir != originalDirectory, "Controller cannot enter directories in the disc picker");
            await Press(JoyButton.B);
            Require(Find<FileDialog>() is null && Focus() is Button, $"Circle did not cancel the disc picker and restore focus: visible={Find<FileDialog>()?.Visible}, focus={Focus()?.Name}");
            GD.Print("[controller-smoke] controller source-file dialog and cancel PASS");
            await CheckControllerWidgetsAsync();

            if (args.Rac1Iso is not null || args.GcIso is not null || args.UyaIso is not null)
            {
                await FocusButton("ENTER WORLDS", contains: true); await Press(JoyButton.A);
                var selector = Find<DestinationSelectorUi>();
                Require(selector is not null, "World list did not open with Cross");
                var beforeStick = Focus(); await Axis(JoyAxis.LeftY, 1); await Axis(JoyAxis.LeftY, 0);
                Require(Focus() != beforeStick, "Left stick does not navigate worlds");
                var visited = new HashSet<Control>();
                await Button(JoyButton.DpadDown, true);
                for (int i = 0; i < 100; i++) { if (Focus() is { } control) visited.Add(control); await Frames(1); }
                await Button(JoyButton.DpadDown, false);
                Require(visited.Count >= 4, "Holding a menu direction has no controlled repeat");
                for (int i = 0; i < 18; i++) await Press(JoyButton.DpadDown);
                var scroll = Descendants(selector!).OfType<ScrollContainer>().Single();
                Require(scroll.ScrollVertical > 0, "World list did not scroll to its focused row");
                await Capture("world-list-scrolled");
                Require(Focus() is Button { Disabled: false }, "World list selected an unavailable destination");
                await Press(JoyButton.Y);
                Require(Find<SourceManagerUi>() is not null, "Triangle did not return from worlds to sources");
                GD.Print("[controller-smoke] worlds stick/D-pad navigation, disabled rows and focus scrolling PASS");
            }

            await CheckRawGameplayAsync();
            if (args.Rac1Iso is not null) await CheckWorldAsync();
            GD.Print("[controller-smoke] PASS");
            ApplicationLifecycle.RequestQuit(this, "controller-smoke-pass");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[controller-smoke] FAIL: {ex}");
            GetTree().Paused = false;
            ApplicationLifecycle.RequestQuit(this, "controller-smoke-fail", 1);
        }
    }

    private void CheckRetailUiAssets(string path)
    {
        Require(Rac1RetailUiAssets.IsLoaded, "Attaching the ISO did not generate retail UI resources");
        foreach (string style in new[] { "regular", "small", "large" })
        {
            var font = Rac1RetailUiAssets.Font(style);
            Require(font is not null && font.HasChar('A') && font.GetTextureCount(0, new(16, 0)) == 1,
                $"Retail {style} bitmap font did not load from the disc");
        }
        var regular = Rac1RetailUiAssets.Font()!;
        Require(Math.Abs(regular.GetStringSize("A A", fontSize: 16).X - 33) < .01f,
            "Font shaping lost native advances / space width");
        Require(regular.GetGlyphSize(0, new(16, 0), ' ') == Vector2.Zero,
            "Space incorrectly draws the A cell at its native UVs");
        Require(GetTree().Root.Theme.DefaultFont == regular, "Source attachment left the bootstrap fallback theme active");
        var menuLabel = Descendants(Find<SourceManagerUi>()!).OfType<Label>().First(label => label.Text == "GAME SOURCES");
        Require(menuLabel.GetThemeFont("font") == Rac1RetailUiAssets.Font("large"),
            "The source menu behind CanvasLayer is still using the Godot fallback font");
        Require(Rac1RetailUiAssets.Text(20196, "missing") == "Quick Select", "Native menu text did not load");
        Require(Rac1RetailUiAssets.Sprite(0x7544) is not null, "Cached sprite miss survived source attachment");
        foreach (var weapon in Rac1WeaponProfiles.Additional.Select(p => p.Item).Prepend(Rac1WeaponId.FirstRanged))
        {
            var icon = Rac1WeaponIconCatalog.Ordinary(weapon);
            Require(icon is { } mapped && Rac1RetailUiAssets.Sprite(mapped.AnimationId, mapped.Frame) is not null,
                $"Missing retail icon for {weapon}");
        }
        Rac1RetailUiAssets.Clear();
        Require(!Rac1RetailUiAssets.IsLoaded && Rac1RetailUiAssets.Font() is null &&
            Rac1RetailUiAssets.Text(20196, "fallback") == "fallback" && Rac1RetailUiAssets.Sprite(0x7544) is null,
            "Clearing the source retained retail UI resources");
        _game.RememberCommandLineSource(OBP.Core.ObpSourceGame.Rac1, path, persist: false);
        Require(Rac1RetailUiAssets.Font() is { } reloaded && reloaded != regular && GetTree().Root.Theme.DefaultFont == reloaded &&
            Rac1RetailUiAssets.Sprite(0x7544) is not null, "Reattaching the ISO did not refresh the UI resource cache and theme");
        Require(menuLabel.GetThemeFont("font") == Rac1RetailUiAssets.Font("large"), "An existing control retained a font from the previous source");
        GD.Print("[controller-smoke] automatic ISO UI extraction, native glyph spacing, icons, text and source refresh PASS");
    }

    private async Task CheckRawGameplayAsync()
    {
        _game.ProcessMode = ProcessModeEnum.Disabled;
        // Independent input boundary: no gameplay queries or invented source state.
        var input = new RawGamepadInput(); input.Read(true);
        await Button(JoyButton.A, true);
        Require(input.Read(true).JumpHeld, "Cross is not Jump");
        input.SuppressButtonsUntilRelease();
        Require(!input.Read(true).JumpHeld, "Menu dismissal leaked a held Cross into a jump");
        await Button(JoyButton.A, false); input.Read(true);
        await Button(JoyButton.RightShoulder, true);
        int preserved = input.HeldButtons;
        await Button(JoyButton.B, true); input.SuppressButtonsUntilRelease(preserved);
        Require(input.Read(true).CrouchHeld && !input.Read(true).TraversalHeld, "Menu masks lost an existing crouch or leaked Circle");
        await Button(JoyButton.A, true);
        Require(input.Read(true).JumpHeld, "A held menu-dismissal button suppressed an unrelated new jump");
        await Button(JoyButton.A, false); await Button(JoyButton.B, false); await Button(JoyButton.RightShoulder, false); input.Read(true);
        await Button(JoyButton.X, true);
        Require(input.Read(true).ActionHeld, "Square is not Wrench");
        await Button(JoyButton.X, false); await Button(JoyButton.B, true);
        Require(input.Read(true).TraversalHeld, "Circle is not equipped item");
        await Button(JoyButton.B, false); await Button(JoyButton.RightShoulder, true);
        Require(input.Read(true).CrouchHeld, "R1 is not Crouch");
        await Button(JoyButton.RightShoulder, false); await Axis(JoyAxis.TriggerRight, 1);
        Require(input.Read(true).CrouchHeld && !input.Read(false).CrouchHeld, "R2 crouch alias is not restricted to R&C1");
        await Axis(JoyAxis.TriggerRight, 0); await Button(JoyButton.DpadRight, true);
        Require(input.Read(true).Move.X == 1, "D-pad is not movement outside a menu");
        await Button(JoyButton.DpadRight, false); await Axis(JoyAxis.LeftX, .05f);
        Require(Math.Abs(input.Read(true).Move.X - .05f) < .00001f, "UI deadzone contaminated raw gameplay axes");
        await Axis(JoyAxis.LeftX, 0);
        _game.ProcessMode = ProcessModeEnum.Pausable;
        GD.Print("[controller-smoke] gameplay face/shoulder bindings, D-pad movement and raw analogue range PASS");
    }

    private async Task CheckWorldAsync()
    {
        _game.OpenDestinationFromBootstrap("rac1:LEVEL0");
        for (int i = 0; Find<PlayerHost>() is null && i < 600; i++) await Frames(1);
        var player = Find<PlayerHost>(); Require(player is not null, "Controller world test did not load Veldin");
        Require(Find<SourceManagerUi>() is null, "World entry retained the source menu over gameplay");
        await Frames(50);
        await _game.CheckControllerWeaponInputAsync();
        await Press(JoyButton.Start);
        Require(GetTree().Paused && Find<PauseMenuUi>() is not null, "Start did not pause gameplay");
        var pausedPosition = player!.GlobalPosition;
        await Frames(20);
        Require(player.GlobalPosition == pausedPosition, "World simulation continued through the pause menu");
        await Capture("pause-menu");
        await FocusButton("CONTROLLER SETTINGS"); await Press(JoyButton.A); await Press(JoyButton.B);
        Require(GetTree().Paused && Find<PauseMenuUi>() is { Visible: true } && Focus() is Button, "Settings Back resumed gameplay or lost pause focus");
        await Press(JoyButton.Start);
        Require(!GetTree().Paused && Find<PauseMenuUi>() is null, "Start did not resume");
        await Press(JoyButton.Back);
        Require(Find<Rac1PlanetTravelUi>() is not null && GetTree().Paused, "Select did not open the map / suspend world simulation");
        await Press(JoyButton.RightStick);
        Require(Find<Rac1PlanetTravelUi>() is null && !GetTree().Paused, "R3 did not close the map");
        await Press(JoyButton.Start);
        await FocusButton("TOGGLE WEAPON PREVIEW"); await Press(JoyButton.A);
        Require(!GetTree().Paused, "Preview selection left gameplay paused");
        await Button(JoyButton.Y, true);
        var quick = Find<WeaponQuickSelectUi>();
        Require(quick is not null && quick.Entries.Count == 15, "Triangle did not expose the preview arsenal");
        Require(!GetTree().Paused, "Triangle Quick Select paused the real-time world");
        await Press(JoyButton.RightShoulder); await Axis(JoyAxis.LeftX, 1); await Axis(JoyAxis.LeftX, 0);
        Require(quick!.SelectedIndex >= 8, "R1 cannot reach the second quick-select page");
        await Capture("quick-select");
        await Button(JoyButton.Y, false);
        Require(!GetTree().Paused && Find<WeaponQuickSelectUi>() is null, "Triangle release did not equip / restore gameplay");
        // This gesture has a wall-clock tap window. Long per-button frame
        // waits can miss it on a rendered run even though input is correct.
        await Press(JoyButton.Y, frames: 1); await Press(JoyButton.Y, frames: 1);
        Require(Find<WeaponQuickSelectUi>() is null && !GetTree().Paused, "Double Triangle did not quick-swap the previous item");
        Require(Descendants(_game).OfType<Label>().Any(label => label.Text == "Bomb Glove"), "Quick swap did not actually equip the previous weapon");
        await Button(JoyButton.Y, true); await Press(JoyButton.Start); await Button(JoyButton.Y, false);
        Require(Find<WeaponQuickSelectUi>() is null && Find<PauseMenuUi>() is not null && GetTree().Paused, "Start nested an invalid pause over quick select");
        await FocusButton("RETURN TO WORLDS"); await Press(JoyButton.A);
        Require(Find<DestinationSelectorUi>() is not null && !GetTree().Paused, "Controller could not leave gameplay for world selection");
        GD.Print("[controller-smoke] Start pause/settings, Select/R3 map, preview, paged Triangle selection and world return PASS");
    }

    private async Task CheckControllerWidgetsAsync()
    {
        var layer = new CanvasLayer { Layer = 400 }; _game.AddChild(layer);
        var column = new VBoxContainer(); layer.AddChild(column);
        var options = new OptionButton(); options.AddItem("First"); options.AddItem("Second"); options.AddItem("Third");
        column.AddChild(options);
        var slider = new HSlider { MinValue = 0, MaxValue = .3, Step = .01, CustomMinimumSize = new(200, 40) };
        column.AddChild(slider); MenuFocus.WireVertical([options, slider]);
        options.GrabFocus(); await Frames(3);
        await Press(JoyButton.A); await Press(JoyButton.DpadDown); await Press(JoyButton.A);
        Require(options.Selected == 1, $"Controller dropdown navigation skipped or ignored an item: {options.Selected}");
        await Press(JoyButton.DpadDown);
        Require(Focus() == slider, "Cannot focus a controller-adjustable slider");
        await Press(JoyButton.DpadRight);
        Require(Math.Abs(slider.Value - .01) < .00001, "Controller slider did not change by one step");
        ControllerBindings.MenuNavigationSuspended = true;
        await Axis(JoyAxis.LeftY, 1); await Frames(50); await Axis(JoyAxis.LeftY, 0);
        Require(Focus() == slider, "Calibration stick motion changed menu focus");
        ControllerBindings.MenuNavigationSuspended = false;
        layer.QueueFree(); await Frames(3);
        Find<SourceManagerUi>()!.RestoreFocus(); await Frames(3);
        GD.Print("[controller-smoke] dropdown, slider and calibration navigation isolation PASS");
    }

    private async Task Capture(string name)
    {
        string? directory = System.Environment.GetEnvironmentVariable("OBP_CONTROLLER_CAPTURE_DIR");
        if (directory is null || DisplayServer.GetName() == "headless") return;
        var capture = await CaptureHarness.CaptureAsync(this, System.IO.Path.Combine(directory, name + ".png"), 2,
            new Dictionary<string, object?> { ["fixture"] = "controller-navigation", ["stage"] = name });
        Require(capture.Ok, "Controller UI capture failed");
    }

    private async Task FocusButton(string text, bool contains = false)
    {
        for (int i = 0; i < 160; i++)
        {
            if (Focus() is Button button && (contains ? button.Text.Contains(text, StringComparison.Ordinal) : button.Text == text)) return;
            await Press(JoyButton.DpadDown);
        }
        throw new InvalidOperationException($"Cannot reach controller button: {text}; focus={Focus()?.Name}");
    }
    private Control? Focus() => GetViewport().GuiGetFocusOwner();
    private T? Find<T>() where T : Node => Descendants(_game).OfType<T>().FirstOrDefault(node => !node.IsQueuedForDeletion());
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren()) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private async Task Press(JoyButton button, int frames = 3) { await Button(button, true, frames); await Button(button, false, frames); }
    private async Task Button(JoyButton button, bool pressed, int frames = 3)
    {
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = pressed, Device = 0 }); await Frames(frames);
    }
    private async Task Axis(JoyAxis axis, float value)
    {
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = axis, AxisValue = value, Device = 0 }); await Frames(3);
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

public partial class OBPGame
{
    internal async Task CheckControllerWeaponInputAsync()
    {
        var player = _player!;
        Require(_rac1Weapons.Equipped == Rac1WeaponId.FirstRanged && _rac1Weapons.FirstRangedAmmo == 20,
            "ordinary opening did not equip Bomb Glove with 20 ammo");
        await WaitForGroundedAsync(player, 360);
        Require(_rac1HeldItem.NativeItemId == (int)Rac1WeaponId.Wrench &&
            _rac1WrenchView is { Visible: true } && _rac1BombGloveView is { Visible: false },
            "level entry did not hold the wrench independently of selected Bomb Glove");
        await TapAsync(JoyButton.X);
        Require(player.Rac1WrenchFrame.Active && _rac1Weapons.Equipped == Rac1WeaponId.FirstRanged &&
            _rac1Weapons.FirstRangedAmmo == 20, "Square did not swing the wrench independently of selected equipment");
        await Rac1SmokeWaitAsync(() => !player.Rac1WrenchFrame.Active, 180, "controller wrench recovery");
        Require(_rac1WrenchView!.Visible && !_rac1BombGloveView!.Visible,
            "wrench recovery restored the unused selected equipment");
        await TapAsync(JoyButton.B);
        Require(_rac1Weapons.FirstRangedAmmo == 20 && _rac1BombGloveView!.Visible && !_rac1WrenchView.Visible,
            "Circle switch tap fired a bomb or failed to draw the selected glove");
        await PhysicsFramesAsync(20);
        Require(_rac1Weapons.FirstRangedAmmo == 20, "released Circle switch fired a delayed bomb");
        await TapAsync(JoyButton.B);
        Require(!player.Rac1WrenchFrame.Active && _rac1Weapons.FirstRangedAmmo == 19,
            "Circle did not fire exactly one Bomb Glove round");
        await Rac1SmokeWaitAsync(() => CurrentPlayerAvatarSourceSequence() == 0, 120, "controller Bomb Glove recovery");
        await PhysicsFramesAsync(60);
        Require(_rac1BombGloveView!.Visible && !_rac1WrenchView.Visible &&
            _rac1HeldItem.NativeItemId == (int)Rac1WeaponId.FirstRanged,
            "Bomb Glove did not stay in hand after firing and idling");
        await TapAsync(JoyButton.X);
        Require(player.Rac1WrenchFrame.Active,
            $"second Square did not enter an attack: alive={player.Rac1GameplayAlive}, " +
            $"ground={player.Rac1RecoveredSupportGrounded}, action={player.Rac1AudioMovementAction}, " +
            $"phase={player.Rac1MovementPhase}, position={player.GlobalPosition}, sequence={CurrentPlayerAvatarSourceSequence()}");
        await Rac1SmokeWaitAsync(() => !player.Rac1WrenchFrame.Active, 180, "controller second wrench recovery");
        await PhysicsFramesAsync(30);
        Require(_rac1WrenchView.Visible && !_rac1BombGloveView.Visible &&
            _rac1Weapons.Equipped == Rac1WeaponId.FirstRanged && _rac1Weapons.FirstRangedAmmo == 19,
            "Square did not leave the wrench in hand while retaining Circle equipment and ammo");
        await SetButtonAsync(JoyButton.B, true);
        await PhysicsFramesAsync(6);
        Require(_rac1Weapons.FirstRangedAmmo == 19 && _rac1BombGloveView.Visible,
            "held Circle fired before the retail switch/hold gate");
        await PhysicsFramesAsync(8);
        Require(_rac1Weapons.FirstRangedAmmo == 18, "continuing the switch hold did not throw a bomb");
        await SetButtonAsync(JoyButton.B, false);
        await Rac1SmokeWaitAsync(() => CurrentPlayerAvatarSourceSequence() == 0, 120, "held-switch Bomb Glove recovery");
        OnRac1WeaponSelectionRequested(Rac1WeaponId.Wrench);
        await TapAsync(JoyButton.B);
        Require(!player.Rac1WrenchFrame.Active && _rac1Weapons.FirstRangedAmmo == 18,
            "Circle fell back to wrench with legacy item 8 selected");
        OnRac1WeaponCycleRequested(1);
        Require(_rac1Weapons.Equipped == Rac1WeaponId.FirstRanged, "weapon cycle did not leave the legacy wrench selection");
        OnRac1WeaponCycleRequested(-1);
        Require(_rac1Weapons.Equipped == Rac1WeaponId.FirstRanged, "weapon cycle exposed the wrench as Circle equipment");
        GD.Print("[controller-smoke] wrench on entry, Circle tap draws, second press fires, continuous switch hold fires after delay, last-used item PASS");

        async Task TapAsync(JoyButton button)
        {
            await SetButtonAsync(button, true);
            await PhysicsFramesAsync(3);
            await SetButtonAsync(button, false);
            await PhysicsFramesAsync(3);
        }

        async Task SetButtonAsync(JoyButton button, bool pressed)
        {
            // Inject between physics updates, as hardware events arrive, and
            // keep the synthetic device separate from any connected controller.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventJoypadButton { Device = 15, ButtonIndex = button, Pressed = pressed });
        }
    }
}
