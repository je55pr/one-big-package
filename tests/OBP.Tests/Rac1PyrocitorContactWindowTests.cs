using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1PyrocitorContactWindowTests
{
    [Fact]
    public void SameNativeIdentityInAnotherSourceHasIndependentRepeatWindow()
    {
        var window = new Rac1PyrocitorContactWindow();
        var rac1 = new WeaponTargetKey(ObpSourceGame.Rac1, 500, 3);
        var rac2 = new WeaponTargetKey(ObpSourceGame.Rac2, 500, 3);
        var rac3 = new WeaponTargetKey(ObpSourceGame.Rac3, 500, 3);

        window.ConfirmHit(rac1, 10);
        Assert.False(window.CanHit(rac1, 11));
        Assert.True(window.CanHit(rac2, 11));
        window.ConfirmHit(rac2, 11);
        Assert.True(window.CanHit(rac3, 11));
        Assert.True(window.CanHit(rac1, 10 + Rac1WeaponReconstructionPolicy.FlameVictimRepeatTicks));
        Assert.False(window.CanHit(rac2, 10 + Rac1WeaponReconstructionPolicy.FlameVictimRepeatTicks));
    }

    [Fact]
    public void FailedHitsCanRetryAndExpiredContactsArePruned()
    {
        var window = new Rac1PyrocitorContactWindow();
        var key = new WeaponTargetKey(ObpSourceGame.Rac3, 1882, 5);
        Assert.True(window.CanHit(key, 1)); // A rejected native immunity query does not call ConfirmHit.
        Assert.True(window.CanHit(key, 2));
        window.ConfirmHit(key, 2);
        Assert.False(window.CanHit(key, 3));
        window.Prune(2 + Rac1WeaponReconstructionPolicy.FlameVictimRepeatTicks);
        Assert.True(window.CanHit(key, 2 + Rac1WeaponReconstructionPolicy.FlameVictimRepeatTicks));
        window.ConfirmHit(key, 20);
        window.Clear(); // World unload never carries contact identity into the next lease.
        Assert.True(window.CanHit(key, 20));
    }

    [Fact]
    public void DistinctInstancesWithSameClassRemainIndependent()
    {
        var window = new Rac1PyrocitorContactWindow();
        var a = new WeaponTargetKey(ObpSourceGame.Rac3, 1882, 1);
        var b = a with { InstanceIndex = 2 };
        window.ConfirmHit(a, 100);
        Assert.False(window.CanHit(a, 100));
        Assert.True(window.CanHit(b, 100));
    }
}
