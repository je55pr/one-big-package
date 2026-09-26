using Godot;
using OBP.Godot.Controls;

namespace OneBigPackage;

public sealed partial class ControllerSettingsUi : CanvasLayer
{
    public event Action? BackRequested;

    private OptionButton _devices = null!;
    private Label _identity = null!;
    private Label _raw = null!;
    private Label _calibrated = null!;
    private Label _savedRange = null!;
    private Label _status = null!;
    private SpinBox _leftDeadzone = null!;
    private SpinBox _rightDeadzone = null!;
    private Button _calibrate = null!;
    private Button _save = null!;
    private Button _reset = null!;
    private readonly List<int> _deviceIds = new();
    private ControllerCalibrationProfile _profile = ControllerCalibrationProfile.Default;
    private Capture _capture;
    private bool _capturing;

    public override void _Ready()
    {
        Name = "ControllerSettings";
        Layer = 300;
        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        RefreshDevices(force: true);
    }

    public override void _Process(double delta)
    {
        RefreshDevices(force: false);
        if (SelectedDevice() is not { } device)
        {
            _identity.Text = "No controller connected.";
            _raw.Text = "RAW     L (—, —)    R (—, —)";
            _calibrated.Text = "HOST    L (—, —)    R (—, —)";
            return;
        }

        var rawLeft = new Vector2(
            Input.GetJoyAxis(device, JoyAxis.LeftX),
            Input.GetJoyAxis(device, JoyAxis.LeftY));
        var rawRight = new Vector2(
            Input.GetJoyAxis(device, JoyAxis.RightX),
            Input.GetJoyAxis(device, JoyAxis.RightY));

        if (_capturing)
        {
            _capture.Observe(rawLeft, rawRight);
            UpdateCaptureStatus();
        }

        Vector2 left = Apply(_profile.Left, rawLeft);
        Vector2 right = Apply(_profile.Right, rawRight);
        _raw.Text = $"RAW     L ({rawLeft.X,7:0.000}, {rawLeft.Y,7:0.000})    R ({rawRight.X,7:0.000}, {rawRight.Y,7:0.000})";
        _calibrated.Text = $"HOST    L ({left.X,7:0.000}, {left.Y,7:0.000})    R ({right.X,7:0.000}, {right.Y,7:0.000})";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            BackRequested?.Invoke();
        }
    }

    private void BuildUi()
    {
        AddChild(new ColorRect
        {
            Color = new Color(UiTheme.Backdrop, 0.98f),
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        });

        var margin = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
        margin.AddThemeConstantOverride("margin_left", 54);
        margin.AddThemeConstantOverride("margin_top", 34);
        margin.AddThemeConstantOverride("margin_right", 54);
        margin.AddThemeConstantOverride("margin_bottom", 34);
        AddChild(margin);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 12);
        margin.AddChild(outer);
        outer.AddChild(new Label { Text = "SETTINGS  //  CONTROLLERS", ThemeTypeVariation = "HeaderSmall" });
        outer.AddChild(new Label { Text = "CONTROLLER CALIBRATION", ThemeTypeVariation = "HeaderLarge" });
        outer.AddChild(new Label
        {
            Text = "Host calibration happens before any game-specific PS2 input model. It fixes imperfect hardware range without changing recovered in-game deadzones or movement laws.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = UiTheme.TextDim,
        });

        var deviceRow = new HBoxContainer();
        deviceRow.AddThemeConstantOverride("separation", 12);
        outer.AddChild(deviceRow);
        deviceRow.AddChild(new Label { Text = "Controller", CustomMinimumSize = new Vector2(150, 0), VerticalAlignment = VerticalAlignment.Center });
        _devices = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 44) };
        _devices.ItemSelected += _ => LoadSelectedProfile();
        deviceRow.AddChild(_devices);

        _identity = new Label { Modulate = UiTheme.TextDim, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        outer.AddChild(_identity);

        var live = new PanelContainer { ThemeTypeVariation = "MenuCard" };
        outer.AddChild(live);
        var liveColumn = new VBoxContainer();
        liveColumn.AddThemeConstantOverride("separation", 7);
        live.AddChild(liveColumn);
        liveColumn.AddChild(new Label { Text = "LIVE STICKS", ThemeTypeVariation = "HeaderMedium" });
        _raw = new Label();
        _calibrated = new Label { Modulate = UiTheme.Ready };
        liveColumn.AddChild(_raw);
        liveColumn.AddChild(_calibrated);
        liveColumn.AddChild(new Label
        {
            Text = "RAW is SDL/Godot input. HOST is what OBP sends into the selected game's own input conditioner.",
            Modulate = UiTheme.TextDim,
        });
        var settings = new PanelContainer { ThemeTypeVariation = "MenuCard" };
        outer.AddChild(settings);
        var settingsColumn = new VBoxContainer();
        settingsColumn.AddThemeConstantOverride("separation", 9);
        settings.AddChild(settingsColumn);
        settingsColumn.AddChild(new Label { Text = "BASIC CALIBRATION", ThemeTypeVariation = "HeaderMedium" });
        _savedRange = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        settingsColumn.AddChild(_savedRange);

        var dzGrid = new GridContainer { Columns = 2 };
        dzGrid.AddThemeConstantOverride("h_separation", 18);
        dzGrid.AddThemeConstantOverride("v_separation", 8);
        settingsColumn.AddChild(dzGrid);
        dzGrid.AddChild(new Label { Text = "Left stick host deadzone" });
        _leftDeadzone = DeadzoneSpinBox();
        dzGrid.AddChild(_leftDeadzone);
        dzGrid.AddChild(new Label { Text = "Right stick host deadzone" });
        _rightDeadzone = DeadzoneSpinBox();
        dzGrid.AddChild(_rightDeadzone);

        settingsColumn.AddChild(new Label
        {
            Text = "Leave host deadzone at 0 unless the hardware drifts. Game-specific deadzones are applied afterwards.",
            Modulate = UiTheme.TextDim,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        settingsColumn.AddChild(actions);
        _calibrate = new Button { Text = "START RANGE CALIBRATION", CustomMinimumSize = new Vector2(230, 44) };
        _save = new Button { Text = "SAVE SETTINGS", CustomMinimumSize = new Vector2(180, 44) };
        _reset = new Button { Text = "RESET THIS CONTROLLER", CustomMinimumSize = new Vector2(210, 44) };
        _calibrate.Pressed += ToggleCalibration;
        _save.Pressed += SaveCurrent;
        _reset.Pressed += ResetCurrent;
        actions.AddChild(_calibrate);
        actions.AddChild(_save);
        actions.AddChild(_reset);
        _status = new Label
        {
            Text = "Tip: calibrate using normal thumb pressure. Do not force the stick beyond the way you naturally play.",
            Modulate = UiTheme.TextDim,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 48),
        };
        settingsColumn.AddChild(_status);

        var spacer = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        outer.AddChild(spacer);
        var back = new Button { Text = "‹  BACK", CustomMinimumSize = new Vector2(160, 46) };
        back.Pressed += () => BackRequested?.Invoke();
        outer.AddChild(back);
        Callable.From(() => _devices.GrabFocus()).CallDeferred();
    }

    private static SpinBox DeadzoneSpinBox()
    {
        return new SpinBox
        {
            MinValue = 0,
            MaxValue = 0.30,
            Step = 0.01,
            CustomArrowStep = 0.01,
            Suffix = "",
            CustomMinimumSize = new Vector2(130, 40),
        };
    }

    private void RefreshDevices(bool force)
    {
        var connected = Input.GetConnectedJoypads();
        var ids = connected.Select(value => (int)value).OrderBy(value => value).ToList();
        if (!force && ids.SequenceEqual(_deviceIds))
            return;

        int? previous = SelectedDevice();
        _deviceIds.Clear();
        _deviceIds.AddRange(ids);
        _devices.Clear();

        foreach (int id in _deviceIds)
            _devices.AddItem($"{Input.GetJoyName(id)}  [#{id}]");
        int selected = 0;
        if (previous is { } old && _deviceIds.IndexOf(old) is var found && found >= 0)
            selected = found;
        if (_deviceIds.Count > 0)
            _devices.Select(selected);

        LoadSelectedProfile();
    }

    private int? SelectedDevice()
    {
        int index = _devices?.Selected ?? -1;
        return index >= 0 && index < _deviceIds.Count ? _deviceIds[index] : null;
    }

    private void LoadSelectedProfile()
    {
        _capturing = false;
        _calibrate.Text = "START RANGE CALIBRATION";
        if (SelectedDevice() is not { } device)
        {
            _profile = ControllerCalibrationProfile.Default;
            SetControlsEnabled(false);
            return;
        }

        SetControlsEnabled(true);
        _profile = ControllerCalibrationStore.GetProfile(device);
        _leftDeadzone.Value = _profile.Left.InnerDeadzone;
        _rightDeadzone.Value = _profile.Right.InnerDeadzone;
        _identity.Text = $"name: {Input.GetJoyName(device)}\nGUID: {Input.GetJoyGuid(device)}    mapping known: {Input.IsJoyKnown(device)}";
        UpdateSavedRange();
        _status.Text = "Tip: calibrate using normal thumb pressure. Do not force the stick beyond the way you naturally play.";
        _status.Modulate = UiTheme.TextDim;
    }

    private void SetControlsEnabled(bool enabled)
    {
        _calibrate.Disabled = !enabled;
        _save.Disabled = !enabled;
        _reset.Disabled = !enabled;
        _leftDeadzone.Editable = enabled;
        _rightDeadzone.Editable = enabled;
    }
    private void ToggleCalibration()
    {
        if (SelectedDevice() is not { } device)
            return;

        if (!_capturing)
        {
            _capture = new Capture();
            _capturing = true;
            _calibrate.Text = "FINISH + SAVE RANGE";
            _status.Text = "Rotate BOTH sticks naturally around their full gates a few times, then press FINISH. Normal pressure only.";
            _status.Modulate = UiTheme.Accent;
            return;
        }

        _capturing = false;
        _profile = new ControllerCalibrationProfile(
            _capture.BuildLeft(_profile.Left, (float)_leftDeadzone.Value),
            _capture.BuildRight(_profile.Right, (float)_rightDeadzone.Value));
        ControllerCalibrationStore.SaveProfile(device, _profile);
        _calibrate.Text = "START RANGE CALIBRATION";
        UpdateSavedRange();
        _status.Text = "✓ Range calibration saved. RAW should now reach HOST ±1.000 at your normal physical stick edge.";
        _status.Modulate = UiTheme.Ready;
    }

    private void SaveCurrent()
    {
        if (SelectedDevice() is not { } device)
            return;

        _profile = new ControllerCalibrationProfile(
            _profile.Left with { InnerDeadzone = (float)_leftDeadzone.Value },
            _profile.Right with { InnerDeadzone = (float)_rightDeadzone.Value }).WithSafeLimits();
        ControllerCalibrationStore.SaveProfile(device, _profile);
        UpdateSavedRange();
        _status.Text = "✓ Controller settings saved.";
        _status.Modulate = UiTheme.Ready;
    }

    private void ResetCurrent()
    {
        if (SelectedDevice() is not { } device)
            return;
        ControllerCalibrationStore.ResetProfile(device);
        _profile = ControllerCalibrationProfile.Default;
        _leftDeadzone.Value = 0;
        _rightDeadzone.Value = 0;
        UpdateSavedRange();
        _status.Text = "Controller calibration reset to 1:1 raw input.";
        _status.Modulate = UiTheme.TextDim;
    }
    private void UpdateSavedRange()
    {
        _savedRange.Text =
            $"Saved outer range\n" +
            $"LEFT   X -{_profile.Left.NegativeX:0.000} / +{_profile.Left.PositiveX:0.000}    Y -{_profile.Left.NegativeY:0.000} / +{_profile.Left.PositiveY:0.000}\n" +
            $"RIGHT  X -{_profile.Right.NegativeX:0.000} / +{_profile.Right.PositiveX:0.000}    Y -{_profile.Right.NegativeY:0.000} / +{_profile.Right.PositiveY:0.000}";
    }

    private void UpdateCaptureStatus()
    {
        _status.Text =
            $"CAPTURING  L X -{_capture.LeftNegX:0.000}/+{_capture.LeftPosX:0.000} Y -{_capture.LeftNegY:0.000}/+{_capture.LeftPosY:0.000}    " +
            $"R X -{_capture.RightNegX:0.000}/+{_capture.RightPosX:0.000} Y -{_capture.RightNegY:0.000}/+{_capture.RightPosY:0.000}";
    }

    private static Vector2 Apply(StickCalibration calibration, Vector2 raw)
    {
        return calibration.Apply(raw);
    }

    private struct Capture
    {
        public float LeftNegX;
        public float LeftPosX;
        public float LeftNegY;
        public float LeftPosY;
        public float RightNegX;
        public float RightPosX;
        public float RightNegY;
        public float RightPosY;

        public void Observe(Vector2 left, Vector2 right)
        {
            ObserveAxis(left.X, ref LeftNegX, ref LeftPosX);
            ObserveAxis(left.Y, ref LeftNegY, ref LeftPosY);
            ObserveAxis(right.X, ref RightNegX, ref RightPosX);
            ObserveAxis(right.Y, ref RightNegY, ref RightPosY);
        }
        public StickCalibration BuildLeft(StickCalibration previous, float deadzone) => new StickCalibration(
            KeepOrCapture(LeftNegX, previous.NegativeX),
            KeepOrCapture(LeftPosX, previous.PositiveX),
            KeepOrCapture(LeftNegY, previous.NegativeY),
            KeepOrCapture(LeftPosY, previous.PositiveY),
            deadzone).WithSafeLimits();

        public StickCalibration BuildRight(StickCalibration previous, float deadzone) => new StickCalibration(
            KeepOrCapture(RightNegX, previous.NegativeX),
            KeepOrCapture(RightPosX, previous.PositiveX),
            KeepOrCapture(RightNegY, previous.NegativeY),
            KeepOrCapture(RightPosY, previous.PositiveY),
            deadzone).WithSafeLimits();

        private static void ObserveAxis(float value, ref float negative, ref float positive)
        {
            if (value < 0f)
                negative = Math.Max(negative, -value);
            else
                positive = Math.Max(positive, value);
        }

        private static float KeepOrCapture(float captured, float previous) =>
            captured >= 0.20f ? captured : previous;
    }
}
