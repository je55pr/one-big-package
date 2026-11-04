using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OBP.Game;

/// <summary>Selected player actions are bound independently of world origin.</summary>
internal sealed class Rac1PlayerGameplayBinding(Rac1PlayerGameplayPorts ports) : IDisposable
{
    private PlayerHost? _player;

    internal void Arm(PlayerHost player)
    {
        if (_player is not null) throw new InvalidOperationException("Player gameplay is already armed.");
        _player = player;
        player.Rac1PrimaryAttackRequested += ports.PrimaryAttack;
        player.Rac1WeaponSelectionRequested += ports.SelectWeapon;
        player.Rac1WeaponCycleRequested += ports.CycleWeapon;
        player.Rac1WeaponPreviewRequested += ports.PreviewWeapon;
        player.Rac1WrenchPhysicsUpdated += ports.WrenchPhysics;
        player.Rac1GameplayState = ports.Health().Probe();
        player.Rac1MovementEquipment = Rac1MovementCapabilities.FromInventory(ports.Weapons());
        player.Rac1ActiveBackpack = player.Rac1MovementEquipment.RememberedBackpack(ports.Weapons());
        ports.RefreshMovement();
    }

    internal bool TickParityLifecycle()
    {
        var health = ports.Health();
        health.Actions.Update();
        _player!.Rac1GameplayState = health.Probe();
        if (health.HasPendingRecoveredEnvironmentalRestart && !ports.EnvironmentalRestart())
        {
            GD.PrintErr("[rac1-gameplay] automatic environmental restart pending without a valid recovered checkpoint session");
            return false;
        }
        ports.EnvironmentalDeath();
        if (ports.Alive()) return true;
        ports.CombatDeath();
        return false;
    }

    public void Dispose()
    {
        var player = _player;
        _player = null;
        if (!GodotObject.IsInstanceValid(player)) return;
        player!.Rac1PrimaryAttackRequested -= ports.PrimaryAttack;
        player.Rac1WeaponSelectionRequested -= ports.SelectWeapon;
        player.Rac1WeaponCycleRequested -= ports.CycleWeapon;
        player.Rac1WeaponPreviewRequested -= ports.PreviewWeapon;
        player.Rac1WrenchPhysicsUpdated -= ports.WrenchPhysics;
    }
}
