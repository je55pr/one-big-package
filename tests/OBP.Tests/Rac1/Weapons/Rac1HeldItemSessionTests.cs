using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1HeldItemSessionTests
{
    [Fact]
    public void LevelEntryHoldsWrenchWhileBombGloveRemainsSelectedWithTwentyRounds()
    {
        var inventory = Rac1WeaponInventory.CreateOpeningLoadout();
        var hand = new Rac1HeldItemSession();

        Assert.Equal((int)Rac1WeaponId.Wrench, hand.NativeItemId);
        Assert.Equal(Rac1WeaponId.FirstRanged, inventory.Equipped);
        Assert.Equal(20, inventory.FirstRangedAmmo);
    }

    [Fact]
    public void WrenchAndEquipmentUsesRememberTheirHandItemWithoutChangingCircleSelectionOrAmmo()
    {
        var inventory = Rac1WeaponInventory.CreateOpeningLoadout();
        var hand = new Rac1HeldItemSession();
        foreach (var item in Rac1WeaponProfiles.Additional.Select(profile => profile.Item).Prepend(Rac1WeaponId.FirstRanged))
        {
            inventory.AcquireNativeItem((int)item);
            Assert.True(inventory.TryEquip(item));
            int ammo = inventory.GetAmmo((int)item);
            hand.HoldSelected(inventory);
            Assert.Equal((int)item, hand.NativeItemId);

            hand.HoldWrench();
            hand.HoldWrench(); // Combo continuation and catch leave the same hand item.
            Assert.Equal((int)Rac1WeaponId.Wrench, hand.NativeItemId);
            Assert.Equal(item, inventory.Equipped);
            Assert.Equal(ammo, inventory.GetAmmo((int)item));

            hand.HoldSelected(inventory);
            Assert.Equal((int)item, hand.NativeItemId);
            Assert.Equal(ammo, inventory.GetAmmo((int)item));
        }
    }

    [Fact]
    public void ReenteringLevelResetsHandWithoutRefillingOrReplacingSelectedEquipment()
    {
        var inventory = Rac1WeaponInventory.CreateOpeningLoadout();
        Assert.True(inventory.TryConsumeAmmo((int)Rac1WeaponId.FirstRanged, 7));
        var hand = new Rac1HeldItemSession();
        hand.HoldSelected(inventory);

        hand.ResetForLevelEntry();

        Assert.Equal((int)Rac1WeaponId.Wrench, hand.NativeItemId);
        Assert.Equal(Rac1WeaponId.FirstRanged, inventory.Equipped);
        Assert.Equal(13, inventory.FirstRangedAmmo);
    }

    [Fact]
    public void BookmarkRestoresHandIndependentlyOfSelectionAndRejectsUnownedOrInvalidItems()
    {
        var inventory = Rac1WeaponInventory.CreateOpeningLoadout();
        var hand = new Rac1HeldItemSession();
        hand.Restore((int)Rac1WeaponId.FirstRanged, inventory);
        Assert.Equal((int)Rac1WeaponId.FirstRanged, hand.NativeItemId);
        hand.Restore((int)Rac1WeaponId.Wrench, inventory);
        Assert.Equal(Rac1WeaponId.FirstRanged, inventory.Equipped);
        Assert.Equal((int)Rac1WeaponId.Wrench, hand.NativeItemId);

        Assert.Throws<ArgumentException>(() => hand.Restore((int)Rac1WeaponId.Blaster, inventory));
        Assert.Throws<ArgumentException>(() => hand.Restore(-1, inventory));
        Assert.Throws<ArgumentException>(() => hand.Restore(37, inventory));
        Assert.Equal((int)Rac1WeaponId.Wrench, hand.NativeItemId);
    }

    [Fact]
    public void ShortCircleTapDrawsBombGloveWithoutFiringAndNextPressFires()
    {
        var inventory = Rac1WeaponInventory.CreateOpeningLoadout();
        var hand = new Rac1HeldItemSession();
        var bomb = new Rac1BombGloveSession(inventory);
        for (int tick = 0; tick < 60; tick++)
        {
            var input = hand.StepCircle(inventory, pressed: tick == 0, held: tick < 3);
            Assert.False(input.UseRequested);
            Assert.Null(bomb.Step(input.UseRequested).Shot);
        }
        Assert.Equal((int)Rac1WeaponId.FirstRanged, hand.NativeItemId);
        Assert.Equal(20, inventory.FirstRangedAmmo);

        var second = hand.StepCircle(inventory, pressed: true, held: true);
        Assert.True(second.UseRequested);
        Assert.NotNull(bomb.Step(second.UseRequested).Shot);
        Assert.Equal(19, inventory.FirstRangedAmmo);
    }

    [Fact]
    public void ContinuousCircleHoldDrawsThenThrowsWithoutNeedingAnotherPress()
    {
        var inventory = Rac1WeaponInventory.CreateOpeningLoadout();
        var hand = new Rac1HeldItemSession();
        var bomb = new Rac1BombGloveSession(inventory);
        var shots = new List<int>();
        for (int tick = 0; tick <= 35; tick++)
        {
            var input = hand.StepCircle(inventory, pressed: tick == 0, held: true);
            Assert.Equal(tick == 4, input.HandChanged);
            if (bomb.Step(input.UseRequested).Shot is not null) shots.Add(tick);
        }
        Assert.Equal(new[] { 10, 30 }, shots);
        Assert.Equal(18, inventory.FirstRangedAmmo);
    }

    [Fact]
    public void ASecondPressDuringTheSwapIsBufferedButTheOriginalSwitchPressIsExcluded()
    {
        var inventory = Rac1WeaponInventory.CreateOpeningLoadout();
        var hand = new Rac1HeldItemSession();
        for (int tick = 0; tick <= 5; tick++)
        {
            var input = hand.StepCircle(inventory, pressed: tick is 0 or 2, held: tick is 0 or 2);
            Assert.Equal(tick == 5, input.UseRequested);
        }
    }

    [Fact]
    public void ThrowHeldWindowIsBoundedAndSquareCancelsAnUnfinishedDraw()
    {
        var inventory = Rac1WeaponInventory.CreateOpeningLoadout();
        var hand = new Rac1HeldItemSession();
        hand.StepCircle(inventory, true, true);
        Assert.True(hand.Switching);
        hand.HoldWrench();
        for (int tick = 0; tick < 30; tick++)
            Assert.False(hand.StepCircle(inventory, false, true).UseRequested);
        Assert.Equal((int)Rac1WeaponId.Wrench, hand.NativeItemId);

        hand.StepCircle(inventory, true, true);
        for (int tick = 1; tick < 30; tick++)
            Assert.False(hand.StepCircle(inventory, false, false).UseRequested);
        Assert.False(hand.StepCircle(inventory, false, true).UseRequested); // Too late for 8<age<17.
        Assert.True(hand.StepCircle(inventory, true, true).UseRequested);
    }

    [Theory]
    [InlineData(Rac1WeaponId.VisibombGun)]
    [InlineData(Rac1WeaponId.Walloper)]
    public void EdgeDrivenWeaponsNeedAFreshPressAfterTheWrenchSwitch(Rac1WeaponId weapon)
    {
        var inventory = Rac1WeaponInventory.CreateOpeningLoadout();
        inventory.AcquireNativeItem((int)weapon);
        Assert.True(inventory.TryEquip(weapon));
        var hand = new Rac1HeldItemSession();
        for (int tick = 0; tick < 60; tick++)
            Assert.False(hand.StepCircle(inventory, tick == 0, true).UseRequested);
        Assert.Equal((int)weapon, hand.NativeItemId);
        Assert.True(hand.StepCircle(inventory, true, true).UseRequested);
    }

    [Fact]
    public void BlasterCanContinueTheSwitchHoldOnceItsNativeAgeGateOpens()
    {
        var inventory = Rac1WeaponInventory.CreateOpeningLoadout();
        inventory.AcquireNativeItem((int)Rac1WeaponId.Blaster);
        inventory.TryEquip(Rac1WeaponId.Blaster);
        var hand = new Rac1HeldItemSession();
        for (int tick = 0; tick <= 7; tick++)
            Assert.Equal(tick == 7, hand.StepCircle(inventory, tick == 0, true).UseRequested);
    }

    [SkippableFact]
    public void CanonicalRetailCodePinsSwitchEdgesSlotDelayAndPerWeaponHoldGates()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay();
        foreach (var (start, end, hash) in new[]
        {
            (0x210024, 0x210074, "0a503ba9ea46a20b3ebd3f2f6a1b166d0491e797df7e388be5425199bf23a658"),
            (0x210584, 0x2105f0, "70939c7a9dae1656d93102bfeeda6674f61ce040e0b3279ffbe80747ed4f69ff"),
            (0x210854, 0x21089c, "7e4ae1256393cc3bde911ee11a2738dbf4235779709c5b45a6582c7336ddc308"),
            (0x20ea80, 0x20ebd0, "9fbcb481ecec3a136c247158b2d417bd1ef8aa03f71221a6396666bc050aa62e"),
            (0x228860, 0x2288d4, "62dfdbf5d453237e808300983ca48ef8e035d5563450506ad132cf92b6ae1104"),
            (0x228710, 0x22875c, "66c8cc6931e857603114d27b3dff289c7b1fdd95ebfd5d70525949994dbaf616"),
            (0x228984, 0x2289b0, "898ddb2d5eecaddeda0483f395616f428af085fccec09ea2e221d59de2178db5"),
            (0x2b309c, 0x2b30bc, "a7e2140a34415ec1f1206614897df08693e09cb44c023515e7eb4e2782dc8472"),
            (0x266d60, 0x266e00, "83270ec3c911713f9193ebe8c1cf5fe80aae9b083190fc77c4c5c868627c2a54"),
            (0x2671b0, 0x2677b0, "b41f67e45bd6d146a08939d04b50cb90a9dc4ecb79ada8100075233d294fb730"),
        }) Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(read(start, end - start))).ToLowerInvariant());

        int Word(int at) => BinaryPrimitives.ReadInt32LittleEndian(read(at, 4));
        Assert.Equal(0x30420020, Word(0x210034)); // Circle edge, not held mask.
        Assert.Equal(0x24030000 | Rac1HeldItemSession.NativeSwitchItemAge, Word(0x2105e0));
        Assert.Equal(0x24040000 | Rac1HeldItemSession.ThrowHeldAgeExclusiveMinimum, Word(0x2288b0));
        Assert.Equal(0x24040000 | Rac1HeldItemSession.ThrowHeldAgeExclusiveMaximum, Word(0x2288c4));
        Assert.Equal(0x24040000 | Rac1HeldItemSession.BlasterHeldAgeExclusiveMinimum, Word(0x228738));
        Assert.Equal(unchecked((int)0x8e8201a4), Word(0x2b30a8)); // Visibomb reads pressed edges.
    }
}
