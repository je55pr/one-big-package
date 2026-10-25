using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;

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
    private bool _debugOutputEnabled;

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
        UiPointerModality.MenuOpened();

        _pauseMenu = new PauseMenuUi
        {
            ProcessMode = ProcessModeEnum.Always,
            RetailEnabled = _player?.HasRetailPresentation == true && Rac1RetailUiAssets.IsLoaded,
            Weapons = Rac1WeaponProfiles.Additional.Select(profile => profile.Item)
                .Prepend(Rac1WeaponId.FirstRanged)
                .OrderBy(weapon => (int)weapon)
                .Select(weapon => new PauseMenuUi.RetailItem(
                    (int)weapon, Rac1WeaponProfiles.Name(weapon),
                    _rac1Weapons.Owns(weapon), _rac1Weapons.Equipped == weapon,
                    Rac1WeaponIconCatalog.Ordinary(weapon)?.AnimationId))
                .ToArray(),
            Gadgets = (Rac1RetailUiAssets.NativeMenuPages?.NativeGadgetsGrid.Groups
                .SelectMany(group => group.Cells).Select(cell => cell.NativeId) ??
                Enum.GetValues<Rac1TraversalEquipmentId>().Select(gadget => (int)gadget))
                .Select(id =>
                {
                    var gadget = (Rac1TraversalEquipmentId)id;
                    string name = Enum.IsDefined(gadget) ? gadget.ToString() switch
                    {
                        "HeliPack" => "Heli-Pack",
                        "ThrusterPack" => "Thruster-Pack",
                        "HydroPack" => "Hydro-Pack",
                        "OxygenMask" => "O2 Mask",
                        "Magneboots" => "Magneboots",
                        "Grindboots" => "Grindboots",
                        _ => gadget.ToString(),
                    } : $"Native item {id}";
                    return new PauseMenuUi.RetailItem(id, name,
                        _rac1Weapons.OwnsNativeItem(id), false);
                }).ToArray(),
            QuickSlots = (_playerNative?.Assignments.Capture().QuickSelect ?? _rac1Weapons.QuickSelect
                .Select(id => id == 0 ? (OBP.Runtime.Gameplay.SourceWeaponKey?)null : new OBP.Runtime.Gameplay.SourceWeaponKey("rac1", id)).ToArray()).Select((key, slot) =>
            {
                // The retained native menu only admits native icon/item IDs.
                // Foreign slots remain real in the OBP read model and wheel;
                // #259 owns their full pause-menu presentation.
                int id = key is { SourceGame: "rac1" } native ? native.NativeItemId : 0;
                Rac1WeaponId weapon = (Rac1WeaponId)id;
                var icon = Rac1WeaponIconCatalog.Ordinary(weapon);
                return new PauseMenuUi.RetailItem(id,
                    key is { SourceGame: not "rac1" } foreign ? $"{FusionWeaponName(foreign)} [{foreign.SourceGame}]" :
                    id == 0 ? $"Slot {slot + 1}: Empty" :
                    Enum.IsDefined(weapon) ? Rac1WeaponProfiles.Name(weapon) : $"Native item {id}",
                    id != 0 && _rac1Weapons.OwnsNativeItem(id),
                    id != 0 && _rac1Weapons.Equipped == weapon,
                    icon?.AnimationId);
            }).ToArray(),
            // Retail Items kind=0 reads item_available[]. Kind=1 reads
            // independent alternate_item_available[] progression flags,
            // which this campaign host does not yet model or invent.
            NativePrimaryItemAvailability = _rac1Weapons.Items,
            Items = Enumerable.Range(0, Rac1NativeInventoryLayout.ItemCount)
                .Where(id => _rac1Weapons.OwnsNativeItem(id) &&
                    !Enum.IsDefined((Rac1WeaponId)id) &&
                    !Enum.IsDefined((Rac1TraversalEquipmentId)id) &&
                    !(Rac1RetailUiAssets.NativeMenuPages?.NativeGadgetsGrid.Groups
                        .Any(group => group.Cells.Any(cell => cell.NativeId == id)) ?? false))
                .Select(id => new PauseMenuUi.RetailItem(id, $"Native item {id}", true, false))
                .ToArray(),
            DebugOutputEnabled = _debugOutputEnabled,
            MovementStatesAvailable = _player?.CanSaveMovementState == true,
            WeaponPreviewEnabled = _rac1WeaponPreviewInventory is not null,
            FusionTestAvailable = CanRunNativeFusionFixture,
            FusionTestEnabled = FusionTestActive,
            EquipmentStatus = _player is { } activePlayer
                ? FusionTestActive ? "Three-game Veldin test · temporary inventory\nOriginal damage · OBP hand mounts / spread feedback\nHold Triangle / Q to select; Circle / E to fire."
                : $"Weapons: {(_rac1WeaponPreviewInventory is null ? "campaign inventory" : "full arsenal preview")}\nPack: {activePlayer.Rac1EquipmentLabel}\nSwingshot preview: {(activePlayer.Rac1DevelopmentSwingshotPreview ? "on" : "off")}"
                : string.Empty,
        };
        _pauseMenu.EquipRequested += OnRac1WeaponSelectionRequested;
        _pauseMenu.FusionTestRequested += ToggleFusionTest;
        _pauseMenu.QuickSelectAssignRequested = (slot, nativeId) =>
            _playerNative is not null ? AssignPlayerQuickSelect(slot, $"rac1/item/{nativeId}").Accepted : _rac1Weapons.TryAssignQuickSelect(slot, nativeId);
        _pauseMenu.ResumeRequested += () => ClosePauseMenu();
        _pauseMenu.ControllerSettingsRequested += () => OpenControllerSettings(fromPause: true);
        _pauseMenu.DebugOutputChanged += SetDebugOutputEnabled;
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

    private void SetDebugOutputEnabled(bool enabled)
    {
        _debugOutputEnabled = enabled;
        _player?.SetDebugOutputEnabled(enabled);
        if (_playerHud is not null && IsInstanceValid(_playerHud))
            _playerHud.DebugOutputEnabled = enabled;
        if (!enabled)
        {
            if (_inspector is { IsOpen: true }) _inspector.Toggle();
            _overlay?.Clear();
            if (_movementStateNotice is not null && IsInstanceValid(_movementStateNotice))
                _movementStateNotice.Visible = false;
            ++_movementStateNoticeGeneration;
        }
        UpdateWorldHud();
        UpdatePlayerHud();
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
        UiPointerModality.MenuOpened();
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

        UiPointerModality.MenuClosed(_prePauseMouseMode);
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
