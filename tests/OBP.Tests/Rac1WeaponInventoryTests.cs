using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1WeaponInventoryTests
{
    [Fact]
    public void PromotedWeaponIdsAndNativeLayoutMatchRetailWitnesses()
    {
        Assert.Equal(8, (int)Rac1WeaponId.Wrench);
        Assert.Equal(10, (int)Rac1WeaponId.FirstRanged);
        Assert.Equal(37, Rac1NativeInventoryLayout.ItemCount);
        Assert.Equal(8, Rac1NativeInventoryLayout.QuickSelectCapacity);
        Assert.Equal(7, Rac1NativeInventoryLayout.EquippedGadgetCapacity);
        Assert.Equal(9, Rac1NativeInventoryLayout.AmmoSaveBlockId);
        Assert.Equal(10, Rac1NativeInventoryLayout.ItemsSaveBlockId);
        Assert.Equal(11, Rac1NativeInventoryLayout.UnlockFlagsSaveBlockId);
        Assert.Equal(13, Rac1NativeInventoryLayout.QuickSelectSaveBlockId);
        Assert.Equal(21, Rac1NativeInventoryLayout.LastEquippedGadgetSaveBlockId);
        Assert.Equal(32, Rac1NativeInventoryLayout.EquippedGadgetsSaveBlockId);
    }

    [Fact]
    public void RetailDescriptorAmmoFieldsCoverAllThirtySevenSlots()
    {
        var expected = new Dictionary<int, (int Gate, int Initial, int Max)>
        {
            [10] = (5, 10, 40),
            [11] = (50, 10, 20),
            [13] = (100, 10, 20),
            [15] = (1, 100, 200),
            [16] = (1, 120, 240),
            [17] = (5, 25, 50),
            [19] = (1, 120, 240),
            [20] = (40, 3, 10),
            [23] = (20, 25, 50),
            [24] = (40, 3, 10),
            [25] = (10, 10, 20),
        };

        for (int itemId = 0; itemId < Rac1NativeInventoryLayout.ItemCount; itemId++)
        {
            var descriptor = Rac1ItemAmmoDescriptors.Get(itemId);
            if (expected.TryGetValue(itemId, out var values))
            {
                Assert.True(descriptor.UsesAmmo);
                Assert.True(descriptor.AppliesFirstAcquisitionAmmoFloor);
                Assert.Equal(values.Gate, descriptor.FirstAcquisitionAmmoGate);
                Assert.Equal(values.Initial, descriptor.FirstAcquisitionAmmoFloor);
                Assert.Equal(values.Max, descriptor.MaxAmmo);
            }
            else
            {
                Assert.False(descriptor.UsesAmmo);
                Assert.False(descriptor.AppliesFirstAcquisitionAmmoFloor);
                Assert.Equal(0, descriptor.FirstAcquisitionAmmoGate);
                Assert.Equal(0, descriptor.FirstAcquisitionAmmoFloor);
                Assert.Equal(0, descriptor.MaxAmmo);
            }
        }
    }

    [Fact]
    public void EarlySliceCarriesRetailBackedOwnershipUnlockAndRememberedGadgetState()
    {
        var inventory = new Rac1WeaponInventory(
            ownsFirstRanged: true,
            equipped: Rac1WeaponId.Wrench,
            firstRangedAmmo: 6);

        Assert.True(inventory.OwnsNativeItem(8));
        Assert.True(inventory.OwnsNativeItem(10));
        Assert.True(inventory.HasUnlockFlag(10));
        Assert.Equal(6, inventory.GetAmmo(10));
        Assert.Equal(10, inventory.QuickSelect[0]);
        Assert.Equal(10, inventory.EquippedGadgets[0]);
        Assert.Equal(8, inventory.CurrentItemId);
    }

    [Fact]
    public void PersistentGadgetSelectionAndCurrentHeldItemRemainSeparate()
    {
        var persistent = CreatePersistent(
            owned: [10],
            unlocked: [10],
            quickSelect: [10],
            equippedGadgets: [10]);

        var inventory = new Rac1WeaponInventory(persistent, currentItemId: 8);

        Assert.Equal(8, inventory.CurrentItemId);
        Assert.Equal(10, inventory.EquippedGadgets[0]);

        var captured = inventory.CapturePersistentState();
        Assert.Equal(10, captured.EquippedGadgets[0]);
        Assert.Equal(10, captured.QuickSelect[0]);
    }

    [Fact]
    public void GenericResumeShapeCanRetainUnimplementedEquippedItem()
    {
        var persistent = CreatePersistent(
            ammo: new Dictionary<int, int> { [10] = 40, [15] = 200, [16] = 190 },
            owned: [2, 10, 12, 15, 16],
            unlocked: [2, 10, 12, 15, 16],
            quickSelect: [10, 16, 15, 12],
            lastEquippedGadget: 16,
            equippedGadgets: [12, 0, 0, 2, 0, 0, 0]);

        var inventory = new Rac1WeaponInventory(persistent, currentItemId: 12);

        Assert.Equal(12, inventory.CurrentItemId);
        Assert.Equal(16, inventory.LastEquippedGadget);
        Assert.Equal(new[] { 10, 16, 15, 12, 0, 0, 0, 0 }, inventory.QuickSelect);
        Assert.Equal(new[] { 12, 0, 0, 2, 0, 0, 0 }, inventory.EquippedGadgets);
        Assert.Throws<InvalidOperationException>(() => _ = inventory.Equipped);
        Assert.False(inventory.TryUseEquipped());
    }

    [Fact]
    public void UnownedRangedWeaponCannotBeEquipped()
    {
        var inventory = new Rac1WeaponInventory(
            ownsFirstRanged: false,
            equipped: Rac1WeaponId.Wrench,
            firstRangedAmmo: 6);

        Assert.False(inventory.TryEquip(Rac1WeaponId.FirstRanged));
        Assert.Equal(8, inventory.CurrentItemId);
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
    public void FirstAcquisitionSetsPersistentFlagsAndDescriptorAmmoFloor()
    {
        var inventory = new Rac1WeaponInventory(
            CreatePersistent(),
            currentItemId: (int)Rac1WeaponId.Wrench);

        var acquired = inventory.AcquireNativeItem((int)Rac1WeaponId.FirstRanged);

        Assert.True(acquired.IsFirstAcquisition);
        Assert.False(acquired.WasPersistentlyOwned);
        Assert.Equal(0, acquired.AmmoBefore);
        Assert.Equal(10, acquired.AmmoAfter);
        Assert.Equal(10, acquired.AmmoGranted);
        Assert.True(inventory.OwnsNativeItem((int)Rac1WeaponId.FirstRanged));
        Assert.True(inventory.HasUnlockFlag((int)Rac1WeaponId.FirstRanged));
        Assert.Equal(10, inventory.FirstRangedAmmo);
        Assert.All(inventory.QuickSelect, itemId => Assert.Equal(0, itemId));
        Assert.Equal((int)Rac1WeaponId.Wrench, inventory.CurrentItemId);
    }

    [Fact]
    public void ReacquisitionSetsUnlockFlagWithoutReapplyingFirstAcquisitionFloor()
    {
        var inventory = new Rac1WeaponInventory(
            CreatePersistent(
                ammo: new Dictionary<int, int> { [10] = 2 },
                owned: [10]),
            currentItemId: (int)Rac1WeaponId.Wrench);

        var acquired = inventory.AcquireNativeItem(10);

        Assert.False(acquired.IsFirstAcquisition);
        Assert.True(acquired.WasPersistentlyOwned);
        Assert.Equal(2, acquired.AmmoBefore);
        Assert.Equal(2, acquired.AmmoAfter);
        Assert.Equal(0, acquired.AmmoGranted);
        Assert.True(inventory.HasUnlockFlag(10));
        Assert.Equal(2, inventory.GetAmmo(10));
    }

    [Fact]
    public void GenericAmmoAdditionClampsToRetailDescriptorCapacity()
    {
        var inventory = new Rac1WeaponInventory(
            CreatePersistent(ammo: new Dictionary<int, int> { [16] = 190 }),
            currentItemId: 8);

        Assert.Equal(240, inventory.AddAmmoClamped(16, 100));
        Assert.Equal(240, inventory.GetAmmo(16));
        Assert.Equal(100, inventory.AddAmmoClamped(8, 100));
    }

    [Fact]
    public void WrenchUseDoesNotConsumeRangedAmmo()
    {
        var inventory = new Rac1WeaponInventory(true, firstRangedAmmo: 6);

        Assert.True(inventory.TryUseEquipped());
        Assert.Equal(6, inventory.FirstRangedAmmo);
    }

    [Fact]
    public void InvalidNativeShapesAndAmmoAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rac1WeaponInventory(
            ownsFirstRanged: true,
            firstRangedAmmo: 41));

        var tooMuchAmmo = CreatePersistent(
            ammo: new Dictionary<int, int> { [10] = 41 },
            owned: [10],
            unlocked: [10]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rac1WeaponInventory(tooMuchAmmo));

        var wrongAmmoCount = new Rac1WeaponPersistentState(
            new int[36],
            new byte[37],
            new byte[37],
            new int[8],
            0,
            new int[7]);
        Assert.Throws<ArgumentException>(() => new Rac1WeaponInventory(wrongAmmoCount));
    }

    private static Rac1WeaponPersistentState CreatePersistent(
        IReadOnlyDictionary<int, int>? ammo = null,
        IReadOnlyList<int>? owned = null,
        IReadOnlyList<int>? unlocked = null,
        IReadOnlyList<int>? quickSelect = null,
        int lastEquippedGadget = 0,
        IReadOnlyList<int>? equippedGadgets = null)
    {
        var ammoValues = new int[Rac1NativeInventoryLayout.ItemCount];
        var items = new byte[Rac1NativeInventoryLayout.ItemCount];
        var unlocks = new byte[Rac1NativeInventoryLayout.ItemCount];
        var quick = new int[Rac1NativeInventoryLayout.QuickSelectCapacity];
        var equipped = new int[Rac1NativeInventoryLayout.EquippedGadgetCapacity];

        if (ammo is not null)
            foreach (var pair in ammo)
                ammoValues[pair.Key] = pair.Value;
        if (owned is not null)
            foreach (int itemId in owned)
                items[itemId] = 1;
        if (unlocked is not null)
            foreach (int itemId in unlocked)
                unlocks[itemId] = 1;
        if (quickSelect is not null)
            for (int index = 0; index < quickSelect.Count && index < quick.Length; index++)
                quick[index] = quickSelect[index];
        if (equippedGadgets is not null)
            for (int index = 0; index < equippedGadgets.Count && index < equipped.Length; index++)
                equipped[index] = equippedGadgets[index];

        return new Rac1WeaponPersistentState(
            ammoValues,
            items,
            unlocks,
            quick,
            lastEquippedGadget,
            equipped);
    }
}
