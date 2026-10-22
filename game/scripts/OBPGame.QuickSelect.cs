using Godot;
using OBP.RAC1.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private WeaponQuickSelectUi? _quickSelect;
    private Input.MouseModeEnum _preQuickSelectMouse;
    private int _preQuickSelectHeldButtons;
    private ulong _lastQuickSelectTap;
    private Rac1WeaponId? _previousSelectedWeapon;
    private bool QuickSelectOpen => _quickSelect is not null && IsInstanceValid(_quickSelect);

    public bool TryOpenWeaponQuickSelect(InputEvent @event)
    {
        if (QuickSelectOpen) return true;
        if (_mode != Mode.World || _player is not { UseRac1Gameplay: true, Rac1GameplayAlive: true } player ||
            player.Scripted || player.DevelopmentControls.FlyEnabled || player.Rac1RemoteControlActive || PauseMenuOpen || ControllerSettingsOpen ||
            Rac1PlanetMapOpen || GetTree().Paused) return false;
        if (TryHandleRac1MetropolisTaxiInput(@event)) return true;
        // Host input gesture timing. Native inventory ownership/equip admission
        // remains authoritative; this is not the recovered menu state machine.
        if (_lastQuickSelectTap != 0 && Time.GetTicksMsec() - _lastQuickSelectTap < 350 &&
            _previousSelectedWeapon is { } previous && _rac1Weapons.Owns(previous))
        {
            _lastQuickSelectTap = 0;
            OnRac1WeaponSelectionRequested(previous);
            return true;
        }
        var available = Rac1WeaponProfiles.Additional.Select(p => p.Item).Prepend(Rac1WeaponId.FirstRanged)
            .Where(_rac1Weapons.Owns).ToArray();
        // Respect native ring order, then expose owned implemented items not yet
        // inserted by the incomplete acquisition adapter. Extra pages are OBP UI.
        var order = _rac1Weapons.QuickSelect.Where(id => available.Contains((Rac1WeaponId)id))
            .Select(id => (Rac1WeaponId)id).Concat(available).Distinct().ToArray();
        var entries = order.Select(weapon => new WeaponQuickSelectUi.Entry(weapon,
            weapon == Rac1WeaponId.FirstRanged ? "Bomb Glove" : Rac1WeaponProfiles.Get(weapon).Name,
            (int)weapon == 9 ? $"{_rac1AdditionalWeapons?.Cargo.Count ?? 0} captured" :
            Rac1ItemAmmoDescriptors.Get((int)weapon).MaxAmmo > 0 ? $"{_rac1Weapons.GetAmmo((int)weapon)} ammo" : "")).ToArray();
        _quickSelect = new WeaponQuickSelectUi { Entries = entries, CurrentWeapon = _rac1Weapons.Equipped };
        _quickSelect.Completed += CloseWeaponQuickSelect;
        _preQuickSelectMouse = Input.MouseMode;
        _preQuickSelectHeldButtons = player.MenuHeldButtons;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        AddChild(_quickSelect);
        // R&C1 Quick Select is real-time. Do not freeze the world while it is held.
        return true;
    }

    private void CloseWeaponQuickSelect(Rac1WeaponId? weapon = null)
    {
        if (!QuickSelectOpen) return;
        _lastQuickSelectTap = _quickSelect!.NeutralTapCompleted ? Time.GetTicksMsec() : 0;
        _quickSelect!.ProcessMode = ProcessModeEnum.Disabled;
        _quickSelect.QueueFree(); _quickSelect = null;
        if (weapon is { } selected && selected != _rac1Weapons.Equipped) OnRac1WeaponSelectionRequested(selected);
        _player?.SuppressButtonsUntilRelease(_preQuickSelectHeldButtons);
        Input.MouseMode = _preQuickSelectMouse;
    }
}
