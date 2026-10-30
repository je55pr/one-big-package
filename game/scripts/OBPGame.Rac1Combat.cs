using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game;

public partial class OBPGame
{
    private void OnRac1PrimaryAttackRequested()
    {
        // Circle uses equipment only. Square has its own wrench input path,
        // including when a legacy repro or keyboard shortcut selects item 8.
        if (!PlayerLife.Alive ||
            FusionWeapons.Session?.Selected.SourceGame is not (ObpSourceGame.Rac2 or ObpSourceGame.Rac3) && _rac1Weapons.Equipped == Rac1WeaponId.Wrench) return;
        if (_player?.Rac1WrenchFrame.Active == true || (_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true)) return;
        Rac1World.Context.EquipmentUsePressed = true;
    }

    private void StepRac1EquippedUseInput()
    {
        bool pressed = Rac1World.Context.EquipmentUsePressed;
        Rac1World.Context.EquipmentUsePressed = false;
        if (_playerNative is not null && ReadPlayerEquipment().Single(entry => entry.Definition.Item == _playerNative.Weapons.Selected).Usable == false) return;
        if (FusionWeapons.Session is not null && FusionWeapons.Sources.Assets?.Available(FusionWeapons.Session.Selected) == false) return;
        bool admitted = _player is not null && PlayerLife.Alive &&
            !_player.Rac1WrenchFrame.Active && !(_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true) && !_player.Rac1SwingshotActive && !_player.Rac1Hanging;
        if (FusionWeapons.Session is { Selected.SourceGame: not ObpSourceGame.Rac1 })
        {
            if (pressed && admitted && !QuickSelectOpen && !_player!.Scripted && !_player.Rac1WaterHandItemHidden)
                FusionWeapons.Session.Hand.HoldEquipment(FusionWeapons.Session.Selected);
            return;
        }
        int? previous = _rac1HeldItem.NativeItemId;
        var frame = _rac1HeldItem.StepCircle(_rac1Weapons, pressed,
            _player?.Rac1EquippedItemHeld == true, admitted);
        if (frame.HandChanged) RefreshRac1HeldItem(previous);
        if (!frame.UseRequested) return;
        if (_rac1Weapons.Equipped == Rac1WeaponId.FirstRanged) Rac1World.Context.BombFireRequested = true;
        else if (Rac1WeaponProfiles.IsAdditional(_rac1Weapons.Equipped)) _rac1AdditionalFireRequested = true;
    }

    private void OnRac1WeaponSelectionRequested(Rac1WeaponId weapon)
    {
        if (_playerNative is not null)
        {
            var entry = ReadPlayerEquipment().SingleOrDefault(entry => entry.Definition.Item == new SourceWeaponKey(ObpSourceGame.Rac1, (int)weapon));
            if (entry?.Selectable != true) { MovementReproduction.Report(entry?.Reason ?? "No admitted weapon registration."); return; }
        }
        if (FusionWeapons.TestActive)
        {
            FusionWeapons.Select(new(ObpSourceGame.Rac1, (int)weapon));
            return;
        }
        var previousKey = CurrentWeaponKey;
        int previous = _rac1Weapons.CurrentItemId;
        if (_rac1Weapons.TryEquip(weapon))
        {
            if (previousKey != new SourceWeaponKey(ObpSourceGame.Rac1, (int)weapon)) ResetRac1WeaponAudio();
            FusionWeapons.SynchronizeNativeSelection();
            if (_player?.Rac1DevelopmentSwingshotPreview == true) _player.ToggleRac1DevelopmentSwingshot();
            if (previous != (int)weapon && previous != (int)Rac1WeaponId.Wrench &&
                Enum.IsDefined(typeof(Rac1WeaponId), previous))
                _previousSelectedWeapon = new(ObpSourceGame.Rac1, previous);
            if (previousKey.SourceGame != ObpSourceGame.Rac1) _previousSelectedWeapon = previousKey;
            Rac1World.Context.CombatStatus = $"equipped {Rac1WeaponProfiles.Name(weapon)}";
            RefreshRac1HudState();
            if (_rac1EquipmentViews.TryGetValue(weapon, out var view))
                view.Play(0, false, ++_rac1WeaponPoseGeneration, 1);
            HoldSelectedRac1HandItem();
            _persistentPlayer.Equipment.RefreshAvailability();
            GD.Print($"[rac1-gameplay] {Rac1World.Context.CombatStatus}");
        }
        else MovementReproduction.Report("Enable weapon preview from Pause → Testing Equipment (or press 0), then hold Triangle to select.");
    }

    private void OnRac1WeaponCycleRequested(int direction)
    {
        if (FusionWeapons.Session is not null)
        {
            var order = _persistentPlayer.Equipment.WeaponOrder();
            int current = order.ToList().IndexOf(CurrentWeaponKey);
            FusionWeapons.Select(order[(current + Math.Sign(direction) + order.Count) % order.Count]);
            return;
        }
        var weapons = new[] { Rac1WeaponId.FirstRanged }
            .Concat(Rac1WeaponProfiles.Additional.Select(profile => profile.Item)).Where(_rac1Weapons.Owns).ToArray();
        if (weapons.Length == 0) return;
        int index = Array.IndexOf(weapons, _rac1Weapons.Equipped);
        if (index < 0) index = direction < 0 ? 0 : -1;
        OnRac1WeaponSelectionRequested(weapons[(index + direction + weapons.Length) % weapons.Length]);
    }

    private void TickRac1Swing(double delta)
    {
        // Wrench motion, authored hit gates and presentation share the player's
        // fixed 60-Hz callback. Render updates cannot apply additional hits.
        _ = delta;
    }

    private int SelectRac1CrateReward(int centre)
    {
        var range = Rac1BoltCrate.RewardRange(centre);
        return range.Maximum == 0 ? 0 : _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Inclusive(range.Minimum, range.Maximum);
    }

    private static bool Rac1WrenchHostPolicyAdmits(
        Vector3 root,
        Vector3 forward,
        Vector3 targetCenter) =>
        Rac1WrenchHostContactPolicy.Admits(
            new Rac1WrenchHostPoint(root.X, root.Y, root.Z),
            new Rac1WrenchHostDirection(forward.X, forward.Y, forward.Z),
            new Rac1WrenchHostPoint(targetCenter.X, targetCenter.Y, targetCenter.Z));
}
