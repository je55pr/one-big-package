using Godot;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Single owner of pause, controller settings, and real-time Quick Select. The
/// underlying retail and universal menu presenters remain independent; only
/// modal focus, input, pointer, and SceneTree pause leases are shared here.
/// </summary>
internal enum ModalBackResult { None, WheelClosed, SettingsClosed, PauseClosed }

internal sealed class UiModalCoordinator(UiModalPorts ports)
{
    private readonly UiModalPorts _ports = ports;
    private PauseMenuUi? _pause;
    private ControllerSettingsUi? _settings;
    private WeaponQuickSelectUi? _wheel;
    private bool _settingsFromPause;
    private bool _ownsPause;
    private bool _audioPaused;
    private Input.MouseModeEnum _beforePauseMouse;
    private Input.MouseModeEnum _beforeSettingsMouse;
    private Input.MouseModeEnum _beforeWheelMouse;
    private int _beforePauseButtons;
    private int _beforeSettingsButtons;
    private int _beforeWheelButtons;
    private Control? _settingsReturnFocus;
    private ulong _lastWheelTap;
    private int _generation;

    internal bool DebugOutputEnabled { get; set; }
    internal ModalEquipmentPorts Equipment => _ports.Equipment;
    internal SourceWeaponKey? PreviousSelectedWeapon { get; set; }
    internal PauseMenuUi? PauseMenu => Alive(_pause) ? _pause : null;
    internal ControllerSettingsUi? Settings => Alive(_settings) ? _settings : null;
    internal WeaponQuickSelectUi? QuickSelect => Alive(_wheel) ? _wheel : null;
    internal bool PauseOpen => PauseMenu is not null;
    internal bool SettingsOpen => Settings is not null;
    internal bool QuickSelectOpen => QuickSelect is not null;
    private static bool Alive(GodotObject? node) => node is not null && GodotObject.IsInstanceValid(node);

    // The stack has a single owner: wheel > settings > pause. World and
    // planet-map routing remain with application navigation.
    internal ModalBackResult HandleBack()
    {
        if (QuickSelectOpen)
        {
            CloseQuickSelect();
            OpenPause();
            return ModalBackResult.WheelClosed;
        }
        if (SettingsOpen)
        {
            CloseSettings();
            return ModalBackResult.SettingsClosed;
        }
        if (PauseOpen)
        {
            ClosePause();
            return ModalBackResult.PauseClosed;
        }
        return ModalBackResult.None;
    }

    internal void ResetWheelGesture()
    {
        _lastWheelTap = 0;
        PreviousSelectedWeapon = null;
    }

    internal bool OpenPause()
    {
        if (PauseOpen) return true;
        if (_pause is not null)
        {
            // A freed page must not strand its old pause/audio lease.
            DiscardPause();
            ReleasePauseLease();
        }
        if (!_ports.CanPause() || SettingsOpen || QuickSelectOpen || _ports.Host.GetTree().Paused)
            return false;

        _beforePauseMouse = ApplicationPointer.MouseMode;
        _beforePauseButtons = _ports.Player()?.MenuHeldButtons ?? 0;
        PauseMenuUi? view = null;
        try
        {
            // Build and attach before acquiring the pause lease. A failed
            // presenter must not strand the world paused or player audio muted.
            view = _ports.BuildPauseMenu();
            _ports.Host.AddChild(view);
            _pause = view;
            UiPointerModality.MenuOpened();
            _ports.SetPlayerAudioPaused(true);
            _audioPaused = true;
            _ports.Host.GetTree().Paused = true;
            _ownsPause = true;
            GD.Print("[pause] menu opened");
            return true;
        }
        catch (Exception error)
        {
            GD.PrintErr($"[pause] could not open menu: {error}");
            if (Alive(view)) { view!.ProcessMode = Node.ProcessModeEnum.Disabled; view.QueueFree(); }
            _pause = null;
            ReleasePauseLease();
            return false;
        }
    }

    internal bool TryClosePause()
    {
        if (!PauseOpen || SettingsOpen) return false;
        ClosePause();
        return true;
    }

    internal void ClosePause()
    {
        // Pause owns settings opened from it; close the nested page first.
        if (_settingsFromPause && _settings is not null)
            DiscardSettings(restoreParent: false);
        bool hadPause = _pause is not null || _ownsPause || _audioPaused;
        DiscardPause();
        if (hadPause) ReleasePauseLease();
        if (hadPause) GD.Print("[pause] menu closed");
    }

    private void DiscardPause()
    {
        if (Alive(_pause))
        {
            _pause!.ProcessMode = Node.ProcessModeEnum.Disabled;
            _pause.QueueFree();
        }
        _pause = null;
    }

    private void ReleasePauseLease()
    {
        if (_ownsPause)
        {
            _ports.Host.GetTree().Paused = false;
            _ownsPause = false;
        }
        if (_audioPaused)
        {
            _ports.SetPlayerAudioPaused(false);
            _audioPaused = false;
        }
        if (_pause is null)
        {
            UiPointerModality.MenuClosed(_beforePauseMouse);
            _ports.Player()?.SuppressButtonsUntilRelease(_beforePauseButtons);
        }
    }

    internal void OpenSettings(bool fromPause)
    {
        if (SettingsOpen || QuickSelectOpen || fromPause && !PauseOpen) return;
        if (_settings is not null) DiscardSettings(restoreParent: true);
        _settingsFromPause = fromPause && PauseOpen;
        _beforeSettingsMouse = ApplicationPointer.MouseMode;
        _beforeSettingsButtons = _ports.Player()?.MenuHeldButtons ?? 0;
        _settingsReturnFocus = _ports.Host.GetViewport().GuiGetFocusOwner();
        ControllerSettingsUi? page = null;
        try
        {
            if (_settingsFromPause && PauseMenu is { } pause)
            {
                pause.Visible = false;
                pause.SetProcessUnhandledInput(false);
            }
            else
            {
                if (_ports.HasSourceView()) _ports.SetSourceVisible(false);
                _ports.SetSelectorVisible(false);
            }
            page = new ControllerSettingsUi { ProcessMode = Node.ProcessModeEnum.Always };
            int generation = ++_generation;
            page.BackRequested += () =>
            {
                if (generation == _generation) CloseSettings();
            };
            _ports.Host.AddChild(page);
            _settings = page;
            UiPointerModality.MenuOpened();
            GD.Print($"[controllers] settings opened from {(_settingsFromPause ? "pause" : "menu")}");
        }
        catch (Exception error)
        {
            GD.PrintErr($"[controllers] could not open settings: {error}");
            if (Alive(page)) { page!.ProcessMode = Node.ProcessModeEnum.Disabled; page.QueueFree(); }
            _settings = null;
            RestoreSettingsParent();
            _settingsReturnFocus = null;
            _settingsFromPause = false;
        }
    }

    internal bool TryCloseSettings()
    {
        if (!SettingsOpen) return false;
        CloseSettings();
        return true;
    }

    internal void CloseSettings() => DiscardSettings(restoreParent: true);

    private void DiscardSettings(bool restoreParent)
    {
        if (_settings is null) return;
        ++_generation;
        if (Alive(_settings))
        {
            _settings!.ProcessMode = Node.ProcessModeEnum.Disabled;
            _settings.QueueFree();
        }
        _settings = null;
        if (restoreParent)
        {
            RestoreSettingsParent();
            MenuFocus.Restore(_settingsReturnFocus, []);
            _ports.Player()?.SuppressButtonsUntilRelease(_beforeSettingsButtons);
        }
        _settingsReturnFocus = null;
        _settingsFromPause = false;
        GD.Print("[controllers] settings closed");
    }

    private void RestoreSettingsParent()
    {
        if (_settingsFromPause && PauseMenu is { } pause)
        {
            pause.Visible = true;
            pause.SetProcessUnhandledInput(true);
            pause.RestoreFocus();
        }
        else if (!PauseOpen)
        {
            ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;
            if (_ports.HasSourceView()) _ports.SetSourceVisible(true);
            _ports.SetSelectorVisible(true);
        }
    }

    internal bool OpenQuickSelect(InputEvent input)
    {
        if (QuickSelectOpen) return true;
        if (_wheel is not null) CloseQuickSelect(retiring: true);
        if (!_ports.CanQuickSelect() || PauseOpen || SettingsOpen || _ports.Host.GetTree().Paused ||
            _ports.Player() is not { CanQuickSelect: true, Rac1GameplayAlive: true } player ||
            player.Scripted || player.DevelopmentControls.FlyEnabled || player.Rac1RemoteControlActive)
            return false;
        if (_ports.TryMissionInteraction(input) || _ports.TryTaxiInteraction(input)) return true;

        var equipment = _ports.Equipment;
        var previous = PreviousSelectedWeapon;
        if (_lastWheelTap != 0 && PlaytestClock.Milliseconds - _lastWheelTap < 350 &&
            previous is { } weapon && weapon != new SourceWeaponKey(ObpSourceGame.Rac1, (int)Rac1WeaponId.Wrench) &&
            equipment.CanRepeatWeapon(weapon))
        {
            _lastWheelTap = 0;
            equipment.SelectWeapon(weapon);
            return true;
        }

        WeaponQuickSelectUi? view = null;
        try
        {
            view = new WeaponQuickSelectUi
            {
                Entries = equipment.WheelEntries(),
                CurrentWeapon = equipment.CurrentWeapon(),
            };
            int generation = ++_generation;
            view.Completed += selected =>
            {
                if (generation == _generation) CloseQuickSelect(selected);
            };
            _beforeWheelMouse = ApplicationPointer.MouseMode;
            _beforeWheelButtons = player.MenuHeldButtons;
            _ports.Host.AddChild(view);
            _wheel = view;
            player.QuickSelectOwnsGameplayInput = true;
            ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;
            // The R&C1 wheel is live gameplay; it never acquires a pause lease.
            return true;
        }
        catch (Exception error)
        {
            GD.PrintErr($"[quick-select] could not open wheel: {error}");
            if (Alive(view)) { view!.ProcessMode = Node.ProcessModeEnum.Disabled; view.QueueFree(); }
            _wheel = null;
            player.QuickSelectOwnsGameplayInput = false;
            ApplicationPointer.MouseMode = _beforeWheelMouse;
            player.SuppressButtonsUntilRelease(_beforeWheelButtons);
            return false;
        }
    }

    internal void CloseQuickSelect(SourceWeaponKey? selection = null, bool retiring = false)
    {
        if (_wheel is null) return;
        bool tapped = Alive(_wheel) && _wheel!.NeutralTapCompleted;
        ++_generation;
        if (Alive(_wheel))
        {
            _wheel!.ProcessMode = Node.ProcessModeEnum.Disabled;
            _wheel.QueueFree();
        }
        _wheel = null;
        _lastWheelTap = !retiring && tapped ? PlaytestClock.Milliseconds : 0;
        if (_ports.Player() is { } player && Alive(player))
        {
            player.QuickSelectOwnsGameplayInput = false;
            player.SuppressButtonsUntilRelease(_beforeWheelButtons);
        }
        ApplicationPointer.MouseMode = _beforeWheelMouse;
        if (!retiring && selection is { } selected && selected != _ports.Equipment.CurrentWeapon())
            _ports.Equipment.SelectWeapon(selected);
    }

    internal void RetireAll()
    {
        CloseQuickSelect(retiring: true);
        bool hadSettings = _settings is not null;
        if (hadSettings) DiscardSettings(restoreParent: false);
        bool hadPause = _pause is not null || _ownsPause || _audioPaused;
        DiscardPause();
        if (hadPause) ReleasePauseLease();
        else if (hadSettings) ApplicationPointer.MouseMode = _beforeSettingsMouse;
        _settingsReturnFocus = null;
        _settingsFromPause = false;
        ResetWheelGesture();
    }
}
