using OBP.Core;
using OBP.Fusion;
using OBP.Game;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

/// <summary>
/// #367: source-qualified selection must remain valid when the player removes
/// all quick slots or changes the equipped source after a world transition.
/// </summary>
public sealed class Rac1WeaponCycleSelectionTests
{
    [Fact]
    public void ClearedSlotsWithNoAvailableWeaponsHaveNoSelectionTarget()
    {
        var assignments = new PlayerEquipmentAssignments([]);
        var wheel = assignments.WithOverflow([]);
        Assert.Equal(PlayerEquipmentAssignments.SlotCount, wheel.Count);
        Assert.All(wheel, slot => Assert.Null(slot));

        var order = wheel.OfType<SourceWeaponKey>().ToArray();
        Assert.Empty(order);
        Assert.Null(PlayerEquipmentFacade.CycleTarget(order, FusionWeaponSession.Lancer, 1));
        Assert.Null(PlayerEquipmentFacade.CycleTarget(order, FusionWeaponSession.Lancer, -1));
    }

    [Fact]
    public void ZeroDirectionCannotReequipOrMutateTheCurrentSelection()
    {
        Assert.Null(PlayerEquipmentFacade.CycleTarget(
            FusionWeaponSession.Weapons, FusionWeaponSession.Lancer, 0));
    }

    [Fact]
    public void CyclingPreservesSourceIdentityAndWrapsBothDirections()
    {
        var order = FusionWeaponSession.Weapons;
        Assert.Equal(FusionWeaponSession.ShockCannon,
            PlayerEquipmentFacade.CycleTarget(order, FusionWeaponSession.Lancer, 1));
        Assert.Equal(FusionWeaponSession.BombGlove,
            PlayerEquipmentFacade.CycleTarget(order, FusionWeaponSession.Lancer, -1));
        Assert.Equal(FusionWeaponSession.BombGlove,
            PlayerEquipmentFacade.CycleTarget(order, FusionWeaponSession.ShockCannon, 1));
        Assert.Equal(FusionWeaponSession.ShockCannon,
            PlayerEquipmentFacade.CycleTarget(order, FusionWeaponSession.BombGlove, -1));
    }

    [Fact]
    public void RetiredSelectionFallsBackToFirstOrLastRatherThanSkippingAWeapon()
    {
        var retired = new SourceWeaponKey(ObpSourceGame.Rac1, (int)Rac1WeaponId.Wrench);
        Assert.Equal(FusionWeaponSession.BombGlove,
            PlayerEquipmentFacade.CycleTarget(FusionWeaponSession.Weapons, retired, 1));
        Assert.Equal(FusionWeaponSession.ShockCannon,
            PlayerEquipmentFacade.CycleTarget(FusionWeaponSession.Weapons, retired, -1));
        Assert.Equal(FusionWeaponSession.ShockCannon,
            PlayerEquipmentFacade.CycleTarget(FusionWeaponSession.Weapons, retired, int.MinValue));
    }
}
