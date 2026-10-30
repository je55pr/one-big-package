using OBP.Game;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

/// <summary>
/// Source-free player-use tests: the live native weapon session still determines
/// ammo and shot cadence, while the persistent player owns request/use clocks.
/// </summary>
public sealed class Rac1AdditionalUseHostTests
{
    private static Rac1WeaponInventory Inventory(Rac1WeaponId weapon, int ammo = 40)
    {
        var inventory = Rac1WeaponInventory.CreateOpeningVeldinWitness();
        inventory.AcquireNativeItem((int)weapon);
        if (Rac1ItemAmmoDescriptors.Get((int)weapon).UsesAmmo)
            inventory.GrantAmmoClamped((int)weapon, ammo);
        Assert.True(inventory.TryEquip(weapon));
        return inventory;
    }

    [Fact]
    public void BlasterRequestUsesExistingNativeSessionAndAdvancesPlayerUseClock()
    {
        var inventory = Inventory(Rac1WeaponId.Blaster);
        var use = new Rac1AdditionalUseHost();
        use.ResetSession(inventory);
        use.ResetInputState();
        int ammo = inventory.GetAmmo((int)Rac1WeaponId.Blaster);
        var events = new List<string>();

        use.RequestFire();
        var first = use.BeginFrame(true, () => false, inventory,
            held => events.Add("held:" + held), () =>
            {
                events.Add("admit");
                return true;
            });

        Assert.Equal(new[] { "held:True", "admit" }, events);
        Assert.True(first.Held);
        Assert.False(first.Pyro);
        Assert.False(first.PyroChanged);
        var shot = Assert.IsType<Rac1AdditionalWeaponSession.Shot>(first.Shot);
        Assert.Equal(Rac1WeaponId.Blaster, shot.Weapon);
        Assert.Equal(1, shot.Generation);
        Assert.Equal(ammo - 1, inventory.GetAmmo((int)Rac1WeaponId.Blaster));
        Assert.Equal(0, use.ShotsFired);

        use.RecordShot(shot);
        use.CompleteFrame(first, inventory);
        Assert.Equal(6, use.UseTicks);
        Assert.Equal(0, use.UseAge);
        Assert.Equal(1, use.ShotsFired);

        var second = use.BeginFrame(true, () => false, inventory, _ => { }, () => true);
        Assert.False(second.Held);
        Assert.Null(second.Shot);
        Assert.Equal(5, use.UseTicks);
        Assert.Equal(1, use.UseAge);
        Assert.Equal(ammo - 1, inventory.GetAmmo((int)Rac1WeaponId.Blaster));
    }

    [Fact]
    public void DetectorOrIneligibleInputIsConsumedWithoutNativeShot()
    {
        var inventory = Inventory(Rac1WeaponId.Blaster);
        var use = new Rac1AdditionalUseHost();
        use.ResetSession(inventory);
        use.ResetInputState();

        bool detectorChecked = false;
        use.RequestFire();
        var blocked = use.BeginFrame(true, () => { detectorChecked = true; return true; },
            inventory, held => Assert.False(held), () => true);
        Assert.True(detectorChecked);
        Assert.Null(blocked.Shot);
        Assert.False(blocked.Held);
        Assert.False(use.FireRequested);

        use.RequestFire();
        var ineligible = use.BeginFrame(false,
            () => throw new InvalidOperationException("Ineligible input must not probe the detector."),
            inventory, held => Assert.False(held), () => true);
        Assert.Null(ineligible.Shot);
        Assert.False(use.FireRequested);
        Assert.Equal(0, use.ShotsFired);
        Assert.Equal(0, use.Session!.Generation);
    }

    [Fact]
    public void PyrocitorContinuousUseKeepsNativeAmmoCadenceAndPresentationTransition()
    {
        var inventory = Inventory(Rac1WeaponId.Pyrocitor, 40);
        var use = new Rac1AdditionalUseHost();
        use.ResetSession(inventory);
        use.ResetInputState();

        use.RequestFire();
        var first = use.BeginFrame(true, () => false, inventory, _ => { }, () => true);
        Assert.True(first.Pyro);
        Assert.True(first.PyroChanged);
        if (first.Shot is { } shot) use.RecordShot(shot);
        use.CompleteFrame(first, inventory);
        Assert.True(use.PyroEmitting);
        Assert.True(use.UseTicks >= 2);

        use.RequestFire();
        var continuous = use.BeginFrame(true, () => false, inventory, _ => { }, () => true);
        Assert.True(continuous.Pyro);
        Assert.False(continuous.PyroChanged);
        use.CompleteFrame(continuous, inventory);
        Assert.True(use.UseTicks >= 2);

        var released = use.BeginFrame(true, () => false, inventory, _ => { }, () => true);
        Assert.False(released.Pyro);
        Assert.True(released.PyroChanged);
        Assert.False(use.PyroEmitting);
        use.CompleteFrame(released, inventory);
    }

    [Fact]
    public void PreviewRebindReplacesOnlyNativeUseSessionAndResetClearsPlayerClocks()
    {
        var original = Inventory(Rac1WeaponId.Blaster);
        var replacement = Inventory(Rac1WeaponId.Devastator);
        var use = new Rac1AdditionalUseHost();
        use.ResetSession(original);
        var previousSession = use.Session;

        use.RequestFire();
        var first = use.BeginFrame(true, () => false, original, _ => { }, () => true);
        if (first.Shot is { } shot) use.RecordShot(shot);
        Assert.True(use.ShotsFired > 0);

        use.ResetSession(replacement);
        Assert.NotSame(previousSession, use.Session);
        Assert.Same(replacement, use.BoundInventory);
        Assert.True(use.ShotsFired > 0);

        use.ResetInputState();
        Assert.Equal(0, use.UseTicks);
        Assert.Equal(0, use.UseAge);
        Assert.Equal(0, use.ShotsFired);
        Assert.False(use.FireRequested);
        Assert.False(use.PyroEmitting);

        var replacementSession = use.Session;
        use.BindInventory(original);
        Assert.Same(original, use.BoundInventory);
        Assert.Same(replacementSession, use.Session);
        use.ClearUseTicks();
        use.StopPyro();
    }
}
