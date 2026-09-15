using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1WeaponInventoryTests
{
    [Fact]
    public void PromotedWeaponIdsMatchRetailSelectionWitnesses()
    {
        Assert.Equal(8, (int)Rac1WeaponId.Wrench);
        Assert.Equal(10, (int)Rac1WeaponId.FirstRanged);
    }

    [Fact]
    public void UnownedRangedWeaponCannotBeEquipped()
    {
        var inventory = new Rac1WeaponInventory(
            ownsFirstRanged: false,
            equipped: Rac1WeaponId.Wrench,
            firstRangedAmmo: 6);

        Assert.True(inventory.Owns(Rac1WeaponId.Wrench));
        Assert.False(inventory.Owns(Rac1WeaponId.FirstRanged));
        Assert.False(inventory.TryEquip(Rac1WeaponId.FirstRanged));
        Assert.Equal(Rac1WeaponId.Wrench, inventory.Equipped);
        Assert.Equal(6, inventory.FirstRangedAmmo);
    }

    [Fact]
    public void OwnedRangedWeaponCanBeSelectedWithoutSpendingAmmo()
    {
        var inventory = new Rac1WeaponInventory(true, firstRangedAmmo: 6);

        Assert.True(inventory.TryEquip(Rac1WeaponId.FirstRanged));
        Assert.Equal(Rac1WeaponId.FirstRanged, inventory.Equipped);
        Assert.Equal(6, inventory.FirstRangedAmmo);

        Assert.True(inventory.TryEquip(Rac1WeaponId.Wrench));
        Assert.Equal(Rac1WeaponId.Wrench, inventory.Equipped);
        Assert.Equal(6, inventory.FirstRangedAmmo);
    }

    [Fact]
    public void RangedUseConsumesExactlyOneRoundAndRejectsAtZero()
    {
        var inventory = new Rac1WeaponInventory(
            ownsFirstRanged: true,
            equipped: Rac1WeaponId.FirstRanged,
            firstRangedAmmo: 2);

        Assert.True(inventory.TryUseEquipped());
        Assert.Equal(1, inventory.FirstRangedAmmo);
        Assert.True(inventory.TryUseEquipped());
        Assert.Equal(0, inventory.FirstRangedAmmo);
        Assert.False(inventory.TryUseEquipped());
        Assert.Equal(0, inventory.FirstRangedAmmo);
    }

    [Fact]
    public void WrenchUseDoesNotConsumeRangedAmmo()
    {
        var inventory = new Rac1WeaponInventory(true, firstRangedAmmo: 6);

        Assert.True(inventory.TryUseEquipped());
        Assert.Equal(6, inventory.FirstRangedAmmo);
    }

    [Fact]
    public void EquippedRangedWeaponMustBeOwned()
    {
        Assert.Throws<ArgumentException>(() => new Rac1WeaponInventory(
            ownsFirstRanged: false,
            equipped: Rac1WeaponId.FirstRanged,
            firstRangedAmmo: 0));
    }

    [Fact]
    public void NegativeAmmoIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rac1WeaponInventory(
            ownsFirstRanged: true,
            firstRangedAmmo: -1));
    }
}
