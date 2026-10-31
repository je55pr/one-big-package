using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>Player-lived inputs and current-world observations, never a second native inventory.</summary>
internal sealed record Rac1CombatStatePorts(
    Func<PlayerHost?> Player,
    Func<Rac1WeaponInventory> Weapons,
    Func<Rac1HeldItemSession> Held,
    Func<Rac1WorldModule?> World,
    Func<bool> Alive,
    Func<SourceWeaponKey> CurrentWeapon,
    Action<SourceWeaponKey> RememberPrevious);

/// <summary>Source availability, native registration and menu facts borrowed at use time.</summary>
internal sealed record Rac1CombatSelectionPorts(
    Func<FusionWeaponHost> Fusion,
    Func<bool> NativeSelectedUsable,
    Func<Rac1WeaponId, (bool Selectable, string? Reason)> ValidateSelection,
    Func<bool> QuickSelectOpen,
    Action<string> Report);

/// <summary>Presentation callbacks that do not own R&C1 damage or source-native rules.</summary>
internal sealed record Rac1CombatViewPorts(
    Action<int?> RefreshHeld,
    Action HoldSelected,
    Action RefreshHud,
    Action ResetAudio,
    Action<Rac1WeaponId> PlaySelection);

/// <summary>
/// Player-session R&C1 equipment input and selection. Native hand/inventory
/// sessions remain the sole authorities. World access always borrows the active
/// lease so stale menu callbacks cannot mutate a retired scene.
/// </summary>
internal sealed class Rac1WeaponPlayerInputHost(
    PlayerSessionHost session, Rac1CombatStatePorts state,
    Rac1CombatSelectionPorts selection, Rac1CombatViewPorts views) : IRac1WeaponPlayerInput
{
    public void PrimaryAttack()
    {
        // Circle uses equipment; Square retains its independent wrench path.
        var fusion = selection.Fusion();
        if (!state.Alive() ||
            fusion.Session?.Selected.SourceGame is not (ObpSourceGame.Rac2 or ObpSourceGame.Rac3) &&
            state.Weapons().Equipped == Rac1WeaponId.Wrench) return;
        if (state.Player()?.Rac1WrenchFrame.Active == true || state.World()?.Wrench.ThrowActive == true) return;
        if (state.World() is { } world) world.Context.EquipmentUsePressed = true;
    }

    public void StepInput()
    {
        if (state.World() is not { } world) return;
        bool pressed = world.Context.EquipmentUsePressed;
        world.Context.EquipmentUsePressed = false;
        if (!selection.NativeSelectedUsable()) return;
        var fusion = selection.Fusion();
        if (fusion.Session is not null &&
            fusion.Sources.Assets?.Available(fusion.Session.Selected) == false) return;
        var player = state.Player();
        bool admitted = player is not null && state.Alive() &&
            !player.Rac1WrenchFrame.Active && !world.Wrench.ThrowActive &&
            !player.Rac1SwingshotActive && !player.Rac1Hanging;
        if (fusion.Session is { Selected.SourceGame: not ObpSourceGame.Rac1 })
        {
            if (pressed && admitted && !selection.QuickSelectOpen() &&
                !player!.Scripted && !player.Rac1WaterHandItemHidden)
                fusion.Session.Hand.HoldEquipment(fusion.Session.Selected);
            return;
        }

        int? previous = state.Held().NativeItemId;
        var frame = state.Held().StepCircle(state.Weapons(), pressed,
            player?.Rac1EquippedItemHeld == true, admitted);
        if (frame.HandChanged) views.RefreshHeld(previous);
        if (!frame.UseRequested) return;
        if (state.Weapons().Equipped == Rac1WeaponId.FirstRanged)
            world.Context.BombFireRequested = true;
        else if (Rac1WeaponProfiles.IsAdditional(state.Weapons().Equipped))
            session.Rac1Use.RequestFire();
    }

    public void Select(Rac1WeaponId weapon)
    {
        var admission = selection.ValidateSelection(weapon);
        if (!admission.Selectable)
        {
            selection.Report(admission.Reason ?? "No admitted weapon registration.");
            return;
        }
        var fusion = selection.Fusion();
        if (fusion.TestActive)
        {
            fusion.Select(new(ObpSourceGame.Rac1, (int)weapon));
            return;
        }
        var previousKey = state.CurrentWeapon();
        int previous = state.Weapons().CurrentItemId;
        if (state.Weapons().TryEquip(weapon))
        {
            if (previousKey != new SourceWeaponKey(ObpSourceGame.Rac1, (int)weapon))
                views.ResetAudio();
            fusion.SynchronizeNativeSelection();
            if (state.Player()?.Rac1DevelopmentSwingshotPreview == true)
                state.Player()!.ToggleRac1DevelopmentSwingshot();
            if (previous != (int)weapon && previous != (int)Rac1WeaponId.Wrench &&
                Enum.IsDefined(typeof(Rac1WeaponId), previous))
                state.RememberPrevious(new(ObpSourceGame.Rac1, previous));
            if (previousKey.SourceGame != ObpSourceGame.Rac1)
                state.RememberPrevious(previousKey);
            var world = state.World();
            if (world is not null)
                world.Context.CombatStatus = $"equipped {Rac1WeaponProfiles.Name(weapon)}";
            views.RefreshHud();
            views.PlaySelection(weapon);
            views.HoldSelected();
            session.Equipment.RefreshAvailability();
            if (world is not null) GD.Print($"[rac1-gameplay] {world.Context.CombatStatus}");
        }
        else selection.Report("Enable weapon preview from Pause → Testing Equipment (or press 0), then hold Triangle to select.");
    }

    public void Cycle(int direction)
    {
        var fusion = selection.Fusion();
        if (fusion.Session is not null)
        {
            if (PlayerEquipmentFacade.CycleTarget(session.Equipment.WeaponOrder(), state.CurrentWeapon(), direction) is { } next)
                fusion.Select(next);
            return;
        }
        var weapons = new[] { Rac1WeaponId.FirstRanged }
            .Concat(Rac1WeaponProfiles.Additional.Select(profile => profile.Item))
            .Where(state.Weapons().Owns).ToArray();
        if (weapons.Length == 0) return;
        int index = Array.IndexOf(weapons, state.Weapons().Equipped);
        if (index < 0) index = direction < 0 ? 0 : -1;
        Select(weapons[(index + direction + weapons.Length) % weapons.Length]);
    }
}
