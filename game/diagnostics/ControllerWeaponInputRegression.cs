using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public sealed class ControllerWeaponInputRegression(MovementDiagnosticContext context, IDiagnosticControllerEquipment equipment) : MovementScenarioSupport(context)
{
    public async Task RunAsync()
    {
        var player = Player!;
        Require(equipment.Read().Selected == Rac1WeaponId.FirstRanged && equipment.Read().BombAmmo == 20,
            "ordinary opening did not equip Bomb Glove with 20 ammo");
        await WaitForGroundedAsync(player, 360);
        Require(equipment.Read().HeldNativeId == (int)Rac1WeaponId.Wrench &&
            equipment.Read().WrenchVisible && !equipment.Read().BombVisible,
            "level entry did not hold the wrench independently of selected Bomb Glove");
        await Driver.TapButtonAsync(DiagnosticPhysicalButton.Wrench);
        Require(equipment.Read().WrenchActive && equipment.Read().Selected == Rac1WeaponId.FirstRanged &&
            equipment.Read().BombAmmo == 20, "Square did not swing the wrench independently of selected equipment");
        await Rac1SmokeWaitAsync(() => !equipment.Read().WrenchActive, 180, "controller wrench recovery");
        Require(equipment.Read().WrenchVisible && !equipment.Read().BombVisible,
            "wrench recovery restored the unused selected equipment");
        await Driver.TapButtonAsync(DiagnosticPhysicalButton.EquippedUse);
        Require(equipment.Read().BombAmmo == 20 && equipment.Read().BombVisible && !equipment.Read().WrenchVisible,
            "Circle switch tap fired a bomb or failed to draw the selected glove");
        await PhysicsFramesAsync(20);
        Require(equipment.Read().BombAmmo == 20, "released Circle switch fired a delayed bomb");
        await Driver.TapButtonAsync(DiagnosticPhysicalButton.EquippedUse);
        Require(!equipment.Read().WrenchActive && equipment.Read().BombAmmo == 19,
            "Circle did not fire exactly one Bomb Glove round");
        await Rac1SmokeWaitAsync(() => CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.StandingSequenceId &&
            PlayerAvatarPresentationIsSynchronized(), 120, "controller Bomb Glove recovery");
        await PhysicsFramesAsync(60);
        Require(equipment.Read().BombVisible && !equipment.Read().WrenchVisible &&
            equipment.Read().HeldNativeId == (int)Rac1WeaponId.FirstRanged,
            "Bomb Glove did not stay in hand after firing and idling");
        await Driver.TapButtonAsync(DiagnosticPhysicalButton.Wrench);
        Require(equipment.Read().WrenchActive,
            $"second Square did not enter an attack: alive={player.Read().Rac1GameplayAlive}, " +
            $"ground={player.Read().Rac1RecoveredSupportGrounded}, action={equipment.Read().AudioMovementAction}, " +
            $"phase={player.Read().Rac1MovementPhase}, position={player.Read().GlobalPosition}, sequence={CurrentPlayerAvatarSourceSequence()}");
        await Rac1SmokeWaitAsync(() => !equipment.Read().WrenchActive, 180, "controller second wrench recovery");
        await PhysicsFramesAsync(30);
        Require(equipment.Read().WrenchVisible && !equipment.Read().BombVisible &&
            equipment.Read().Selected == Rac1WeaponId.FirstRanged && equipment.Read().BombAmmo == 19,
            "Square did not leave the wrench in hand while retaining Circle equipment and ammo");
        await Driver.SetButtonAsync(DiagnosticPhysicalButton.EquippedUse, true);
        await PhysicsFramesAsync(6);
        Require(equipment.Read().BombAmmo == 19 && equipment.Read().BombVisible,
            "held Circle fired before the retail switch/hold gate");
        await PhysicsFramesAsync(8);
        Require(equipment.Read().BombAmmo == 18, "continuing the switch hold did not throw a bomb");
        await Driver.SetButtonAsync(DiagnosticPhysicalButton.EquippedUse, false);
        await Rac1SmokeWaitAsync(() => CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.StandingSequenceId &&
            PlayerAvatarPresentationIsSynchronized(), 120, "held-switch Bomb Glove recovery");
        equipment.Select(Rac1WeaponId.Wrench);
        try
        {
            await Driver.TapButtonAsync(DiagnosticPhysicalButton.EquippedUse);
            Require(!equipment.Read().WrenchActive && equipment.Read().BombAmmo == 18,
                "Circle fell back to wrench with legacy item 8 selected");
            equipment.Cycle(1);
            Require(equipment.Read().Selected == Rac1WeaponId.FirstRanged, "weapon cycle did not leave the legacy wrench selection");
            equipment.Cycle(-1);
            Require(equipment.Read().Current == equipment.Read().Order[^1] && equipment.Read().Current != PlayerEquipmentRegistrations.Wrench,
                "weapon cycle did not follow the development loadout or exposed the wrench as Circle equipment");
        }
        finally { CleanupStep(() => equipment.Select(Rac1WeaponId.FirstRanged)); }
        Log("[controller-smoke] wrench on entry, Circle tap draws, second press fires, continuous switch hold fires after delay, last-used item PASS");

    }
}
