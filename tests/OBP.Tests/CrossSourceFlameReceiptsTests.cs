using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class CrossSourceFlameReceiptsTests
{
    private static CrossSourceFlameReceipts NewSession() =>
        new(new(ObpSourceGame.Rac1, (int)Rac1WeaponId.Pyrocitor), 12);

    private static WeaponTargetKey Tyhrranoid(int id) => new(ObpSourceGame.Rac3, 5821, id);

    [Fact]
    public void EachNativeParticleKeepsOneShotIdentityAndThrottleIsSourceQualified()
    {
        var receipts = NewSession();
        object flameA = new(), flameB = new();
        var first = Tyhrranoid(7);
        var second = Tyhrranoid(8);

        Assert.True(receipts.CanHit(flameA, first, 100, out var shotA));
        Assert.True(receipts.CanHit(flameA, second, 100, out var sameShot));
        Assert.Equal(shotA, sameShot);
        receipts.Accept(flameA, first, 100);
        Assert.False(receipts.CanHit(flameA, first, 112, out _)); // Same particle.
        Assert.True(receipts.CanHit(flameA, second, 100, out _));
        Assert.False(receipts.CanHit(flameB, first, 111, out var shotB));
        Assert.NotEqual(shotA, shotB);
        Assert.True(receipts.CanHit(flameB, first, 112, out _)); // 12 native ticks elapsed.
        receipts.Accept(flameB, first, 112);
        Assert.False(receipts.CanHit(flameA, first, 124, out _)); // Particle receipt still retained.
        Assert.True(receipts.CanHit(flameA, second, 124, out _));
        Assert.Equal(2, receipts.TrackedParticles);
    }

    [Fact]
    public void ResetDoesNotReuseOldShotKeyAndOldParticleEntriesAreReleased()
    {
        var receipts = NewSession();
        object oldParticle = new();
        var victim = Tyhrranoid(3);
        Assert.True(receipts.CanHit(oldParticle, victim, 300, out var before));
        receipts.Accept(oldParticle, victim, 300);
        receipts.Forget(oldParticle);
        Assert.Equal(0, receipts.TrackedParticles);

        receipts.Reset();
        object freshParticle = new();
        Assert.True(receipts.CanHit(freshParticle, victim, 0, out var after));
        Assert.NotEqual(before, after);
        Assert.Equal(before.Weapon, after.Weapon);
        Assert.Equal(1, receipts.TrackedParticles);
        Assert.True(receipts.CanHit(freshParticle, Tyhrranoid(4), 0, out var multi));
        Assert.Equal(after, multi);
    }

    [Fact]
    public void ReceiptIdentityUsesReferencesAndRejectsInvalidTicks()
    {
        var receipts = NewSession();
        object first = new string('p', 2), second = new string('p', 2);
        Assert.True(first.Equals(second));
        var victim = Tyhrranoid(1);
        Assert.True(receipts.CanHit(first, victim, 1, out var a));
        Assert.True(receipts.CanHit(second, victim, 1, out var b));
        Assert.NotEqual(a, b);
        Assert.Throws<ArgumentOutOfRangeException>(() => receipts.CanHit(first, victim, -1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CrossSourceFlameReceipts(new(ObpSourceGame.Rac1, (int)Rac1WeaponId.Pyrocitor), 0));
    }
}
