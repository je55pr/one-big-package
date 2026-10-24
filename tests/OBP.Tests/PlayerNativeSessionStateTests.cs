using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OneBigPackage;

namespace OBP.Tests;

public sealed class PlayerNativeSessionStateTests
{
    [Fact]
    public void NativeControllersPersistWhileWorldTransientsRetireAndCampaignInventoryStaysSeparate()
    {
        var campaign = Rac1WeaponInventory.CreateOpeningLoadout();
        var native = new PlayerSessionNativeState(campaign, 42, 1440);
        var session = new ObpPlayerSession(PlayerSessionLifetimeTests.Snapshot(), native);
        native.Weapons.Select(FusionWeaponSession.Lancer);
        native.Weapons.LancerSession.ResetWorld(18);
        native.Held.HoldWrench();
        native.Health.ApplyHostAridiaContactDamage();
        var before = session.Capture();
        native.Inventory.GrantAmmoClamped(10, 5);
        Assert.NotEqual(native.Inventory.FirstRangedAmmo, campaign.FirstRangedAmmo);
        for (int i = 0; i < 5; i++) session.EndWorld();
        Assert.Equal(FusionWeaponSession.Lancer, native.Weapons.Selected);
        Assert.True(native.Held.Hand.WrenchHeld);
        Assert.Equal(18, native.Weapons.LancerSession.Ammo);
        Assert.Equal(3, native.Health.Probe().Nanotech);
        Assert.Empty(native.Weapons.LancerSession.ActiveShots);
        Assert.Equal(0, session.Capture().Generation);
        native.Weapons.Select(FusionWeaponSession.ShockCannon);
        native.Wallet.Credit(7);
        session.Restore(before);
        Assert.Equal(1, session.Capture().Generation);
        Assert.Equal(FusionWeaponSession.Lancer, native.Weapons.Selected);
        Assert.Equal(18, native.Weapons.LancerSession.Ammo);
        Assert.True(native.Held.Hand.WrenchHeld);
        Assert.Equal(42, native.Wallet.Balance);
        Assert.Equal(3, native.Health.Probe().Nanotech);
        Assert.Equal(campaign.FirstRangedAmmo, native.Inventory.FirstRangedAmmo);
    }

    [Fact]
    public void InvalidNativeRestoreCannotPartiallyReplacePlayerState()
    {
        var native = new PlayerSessionNativeState(Rac1WeaponInventory.CreateOpeningLoadout(), 42, 1440);
        var session = new ObpPlayerSession(PlayerSessionLifetimeTests.Snapshot(), native);
        var before = session.Capture();
        var component = (PlayerSessionNativeState.State)before.Components.Single();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Restore(before with { Components = [component with { LancerAmmo = -1 }] }));
        Assert.Throws<ArgumentException>(() => session.Restore(before with { Components = [component, new UnknownComponent()] }));
        Assert.Equal(before.Generation, session.Capture().Generation);
        Assert.Equal(before.Health, session.Capture().Health);
        Assert.Equal(before.Loadout.SelectedWeapon, session.Capture().Loadout.SelectedWeapon);
        Assert.Equal(42, native.Wallet.Balance);
    }

    [Fact]
    public void TemporaryArsenalPreviewRoundTripsWithoutGrantingThePersistentLoadout()
    {
        var campaign = Rac1WeaponInventory.CreateOpeningLoadout();
        var native = new PlayerSessionNativeState(campaign, 42, 1440);
        var session = new ObpPlayerSession(PlayerSessionLifetimeTests.Snapshot(), native);
        native.SetEquipmentPreview(true);
        native.Inventory.AcquireNativeItem((int)Rac1WeaponId.Blaster);
        native.Weapons.Select(new("rac1", (int)Rac1WeaponId.Blaster));
        var preview = session.Capture();
        session.EndWorld();
        Assert.True(native.Inventory.Owns(Rac1WeaponId.Blaster));
        native.SetEquipmentPreview(false);
        Assert.False(native.Inventory.Owns(Rac1WeaponId.Blaster));
        session.Restore(preview);
        Assert.True(native.Inventory.Owns(Rac1WeaponId.Blaster));
        native.SetEquipmentPreview(false);
        Assert.False(native.Inventory.Owns(Rac1WeaponId.Blaster));
        Assert.False(campaign.Owns(Rac1WeaponId.Blaster));
        Assert.Same(native.Inventory, native.Weapons.Rac1Inventory);
    }

    private sealed record UnknownComponent : IPlayerSessionComponentSnapshot
    {
        public string ComponentId => "unregistered-player-state";
        public int Version => 99;
    }
}
