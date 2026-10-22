using Godot;
using OBP.Godot;

namespace OneBigPackage;

public partial class OBPGame
{
    private PauseMenuUi? _pauseMenu;
    private ControllerSettingsUi? _controllerSettings;
    private bool _controllerSettingsFromPause;
    private bool _pauseOwnsTreePause;
    private Input.MouseModeEnum _prePauseMouseMode;
    private Control? _settingsReturnFocus;
    private int _prePauseHeldButtons;

    private bool PauseMenuOpen =>
        _pauseMenu is not null && IsInstanceValid(_pauseMenu);

    private bool ControllerSettingsOpen =>
        _controllerSettings is not null && IsInstanceValid(_controllerSettings);

    public void ShowControllerSettingsFromMenu()
    {
        if (ControllerSettingsOpen)
            return;

        OpenControllerSettings(fromPause: false);
    }

    public bool TryOpenPauseMenu()
    {
        if (_mode != Mode.World || _args.CaptureFrame is not null)
            return false;
        if (PauseMenuOpen)
            return true;

        _prePauseMouseMode = Input.MouseMode;
        _prePauseHeldButtons = _player?.MenuHeldButtons ?? 0;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _pauseMenu = new PauseMenuUi
        {
            ProcessMode = ProcessModeEnum.Always,
            MovementStatesAvailable = _player?.UseRac1Gameplay == true,
            WeaponPreviewEnabled = _rac1WeaponPreviewInventory is not null,
            EquipmentStatus = _player is { } activePlayer
                ? $"Weapons: {(_rac1WeaponPreviewInventory is null ? "campaign inventory" : "full arsenal preview")}\nPack: {activePlayer.Rac1EquipmentLabel}\nSwingshot preview: {(activePlayer.Rac1DevelopmentSwingshotPreview ? "on" : "off")}"
                : string.Empty,
        };
        _pauseMenu.ResumeRequested += () => ClosePauseMenu();
        _pauseMenu.ControllerSettingsRequested += () => OpenControllerSettings(fromPause: true);
        _pauseMenu.SaveMovementStateRequested += SaveMovementState;
        _pauseMenu.LoadMovementStateRequested += () => _ = LoadMovementStateAsync(MovementStatePath);
        _pauseMenu.WeaponPreviewRequested += () => { ClosePauseMenu(); SetRac1WeaponPreview(_rac1WeaponPreviewInventory is not null); };
        _pauseMenu.RefillPreviewRequested += () => { ClosePauseMenu(); if (_rac1WeaponPreviewInventory is not null) SetRac1WeaponPreview(false); };
        _pauseMenu.PackPreviewRequested += () => { _player?.CycleRac1DevelopmentPack(); ClosePauseMenu(); };
        _pauseMenu.SwingshotPreviewRequested += () => { _player?.ToggleRac1DevelopmentSwingshot(); ClosePauseMenu(); };
        _pauseMenu.WorldsRequested += ReturnToWorldsFromPause;
        _pauseMenu.QuitRequested += () =>
            ApplicationLifecycle.RequestQuit(this, "pause-menu-quit");
        AddChild(_pauseMenu);

        GetTree().Paused = true;
        _pauseOwnsTreePause = true;
        GD.Print("[pause] menu opened");
        return true;
    }
    public bool TryClosePauseMenu()
    {
        if (!PauseMenuOpen || ControllerSettingsOpen)
            return false;

        ClosePauseMenu();
        return true;
    }

    public bool TryCloseControllerSettings()
    {
        if (!ControllerSettingsOpen)
            return false;

        CloseControllerSettings();
        return true;
    }

    private void OpenControllerSettings(bool fromPause)
    {
        if (ControllerSettingsOpen)
            return;

        _controllerSettingsFromPause = fromPause && PauseMenuOpen;
        _settingsReturnFocus = GetViewport().GuiGetFocusOwner();
        if (_controllerSettingsFromPause && _pauseMenu is not null)
        {
            _pauseMenu.Visible = false;
            _pauseMenu.SetProcessUnhandledInput(false);
        }
        else
        {
            if (_sourceManager is not null) _sourceManager.Visible = false;
            if (_destinationSelector is not null) _destinationSelector.Visible = false;
        }

        _controllerSettings = new ControllerSettingsUi
        {
            ProcessMode = ProcessModeEnum.Always,
        };
        _controllerSettings.BackRequested += CloseControllerSettings;
        AddChild(_controllerSettings);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GD.Print($"[controllers] settings opened from {(fromPause ? "pause" : "menu")}");
    }

    private void CloseControllerSettings()
    {
        if (!ControllerSettingsOpen)
            return;

        _controllerSettings!.ProcessMode = ProcessModeEnum.Disabled;
        _controllerSettings.QueueFree();
        _controllerSettings = null;

        if (_controllerSettingsFromPause && PauseMenuOpen && _pauseMenu is not null)
        {
            _pauseMenu.Visible = true;
            _pauseMenu.SetProcessUnhandledInput(true);
            _pauseMenu.RestoreFocus();
        }
        else if (!PauseMenuOpen)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            if (_sourceManager is not null) { _sourceManager.Visible = true; _sourceManager.RestoreFocus(); }
            if (_destinationSelector is not null) _destinationSelector.Visible = true;
        }

        MenuFocus.Restore(_settingsReturnFocus, []);
        _settingsReturnFocus = null;
        _player?.SuppressButtonsUntilRelease(_prePauseHeldButtons);

        _controllerSettingsFromPause = false;
        GD.Print("[controllers] settings closed");
    }
    private void ClosePauseMenu()
    {
        if (ControllerSettingsOpen)
        {
            _controllerSettings!.QueueFree();
            _controllerSettings = null;
            _controllerSettingsFromPause = false;
        }

        if (PauseMenuOpen)
        {
            _pauseMenu!.ProcessMode = ProcessModeEnum.Disabled;
            _pauseMenu!.QueueFree();
            _pauseMenu = null;
        }

        if (_pauseOwnsTreePause)
        {
            GetTree().Paused = false;
            _pauseOwnsTreePause = false;
        }

        Input.MouseMode = _prePauseMouseMode;
        _player?.SuppressButtonsUntilRelease(_prePauseHeldButtons);
        GD.Print("[pause] menu closed");
    }

    private void ReturnToWorldsFromPause()
    {
        ClosePauseMenu();
        ShowDestinationSelector();
    }

    private void TearDownInteractiveOverlays()
    {
        if (ControllerSettingsOpen)
        {
            _controllerSettings!.QueueFree();
            _controllerSettings = null;
        }

        if (PauseMenuOpen)
        {
            _pauseMenu!.QueueFree();
            _pauseMenu = null;
        }

        if (_pauseOwnsTreePause)
        {
            GetTree().Paused = false;
            _pauseOwnsTreePause = false;
        }

        _controllerSettingsFromPause = false;
    }
}
