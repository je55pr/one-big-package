using Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private WeaponQuickSelectUi? _quickSelect;
    private Input.MouseModeEnum _preQuickSelectMouse;
    private int _preQuickSelectHeldButtons;
    private ulong _lastQuickSelectTap;
    private SourceWeaponKey? _previousSelectedWeapon;
    private SourceWeaponKey CurrentWeaponKey => _fusion is { Selected.SourceGame: "rac2" or "rac3" } ? _fusion.Selected : new("rac1", _rac1Weapons.CurrentItemId);
    private bool QuickSelectOpen => _quickSelect is not null && IsInstanceValid(_quickSelect);

    public bool TryOpenWeaponQuickSelect(InputEvent @event)
    {
        if (QuickSelectOpen) return true;
        if (_mode != Mode.World || _player is not { UseRac1Gameplay: true, Rac1GameplayAlive: true } player ||
            player.Scripted || player.DevelopmentControls.FlyEnabled || player.Rac1RemoteControlActive || PauseMenuOpen || ControllerSettingsOpen ||
            Rac1PlanetMapOpen || GetTree().Paused) return false;
        if (TryHandleRac1MissionInteraction(@event)) return true;
        if (TryHandleRac1MetropolisTaxiInput(@event)) return true;
        // Host input gesture timing. Native inventory ownership/equip admission
        // remains authoritative; this is not the recovered menu state machine.
        if (_lastQuickSelectTap != 0 && PlaytestClock.Milliseconds - _lastQuickSelectTap < 350 &&
            _previousSelectedWeapon is { } previous && previous != new SourceWeaponKey("rac1", (int)Rac1WeaponId.Wrench) &&
            (previous.SourceGame == "rac1" ? _rac1Weapons.Owns((Rac1WeaponId)previous.NativeItemId) : _fusion is not null && FusionWeaponSession.Weapons.Contains(previous)))
        {
            _lastQuickSelectTap = 0;
            SelectWeaponKey(previous);
            return true;
        }
        var available = Rac1WeaponProfiles.Additional.Select(p => p.Item).Prepend(Rac1WeaponId.FirstRanged)
            .Where(_rac1Weapons.Owns).ToArray();
        // Respect native ring order, then expose owned implemented items not yet
        // inserted by the incomplete acquisition adapter. Extra pages are OBP UI.
        var order = _rac1Weapons.QuickSelect.Where(id => available.Contains((Rac1WeaponId)id))
            .Select(id => (Rac1WeaponId)id).Concat(available).Distinct().ToArray();
        var entries = order.Select(weapon => new WeaponQuickSelectUi.Entry(new("rac1", (int)weapon),
            weapon == Rac1WeaponId.FirstRanged ? "Bomb Glove" : Rac1WeaponProfiles.Get(weapon).Name,
            (int)weapon == 9 ? $"{_rac1AdditionalWeapons?.Cargo.Count ?? 0} captured" :
            Rac1ItemAmmoDescriptors.Get((int)weapon).MaxAmmo > 0 ? $"{_rac1Weapons.GetAmmo((int)weapon)} ammo" : "")).ToArray();
        if (_fusion is not null)
        {
            var keys = FusionTestActive ? FusionWeaponSession.Weapons : FusionWeaponSession.DefaultQuickSelect(_rac1Weapons);
            entries = keys.Select(key => key.SourceGame == "rac1" && !FusionTestActive ? entries.Single(e => e.Weapon == key) :
                new WeaponQuickSelectUi.Entry(key, FusionWeaponName(key) + $" [{key.SourceGame}]",
                    _fusionAssets?.Available(key) == false ? "Source unavailable" : FusionWeaponDetail(key))).ToArray();
        }
        _quickSelect = new WeaponQuickSelectUi { Entries = entries, CurrentWeapon = CurrentWeaponKey };
        _quickSelect.Completed += CloseWeaponQuickSelect;
        _preQuickSelectMouse = Input.MouseMode;
        _preQuickSelectHeldButtons = player.MenuHeldButtons;
        player.QuickSelectOwnsGameplayInput = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        AddChild(_quickSelect);
        // R&C1 Quick Select is real-time. Do not freeze the world while it is held.
        return true;
    }

    private void CloseWeaponQuickSelect(SourceWeaponKey? weapon = null)
    {
        if (!QuickSelectOpen) return;
        _lastQuickSelectTap = _quickSelect!.NeutralTapCompleted ? PlaytestClock.Milliseconds : 0;
        _quickSelect!.ProcessMode = ProcessModeEnum.Disabled;
        _quickSelect.QueueFree(); _quickSelect = null;
        if (_player is { } player)
        {
            player.QuickSelectOwnsGameplayInput = false;
            player.SuppressButtonsUntilRelease(_preQuickSelectHeldButtons);
        }
        if (weapon is { } selected && selected != CurrentWeaponKey) SelectWeaponKey(selected);
        Input.MouseMode = _preQuickSelectMouse;
    }
}
