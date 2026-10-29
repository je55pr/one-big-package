using System.Reflection;
using Godot;
using OBP.Core;
using OBP.Game;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1ExplosionPresenterTests
{
    [Theory]
    [InlineData(Rac1WeaponId.MineGlove, 74)]
    [InlineData(Rac1WeaponId.GloveOfDoom, 230)]
    [InlineData(Rac1WeaponId.Devastator, 157)]
    [InlineData(Rac1WeaponId.VisibombGun, 163)]
    [InlineData(Rac1WeaponId.Ryno, 454)]
    [InlineData(Rac1WeaponId.DroneDevice, 483)]
    public void ExplosionDiagnosticRecordsNativeEmitterClassRatherThanWeaponItemId(Rac1WeaponId weapon, int nativeClass)
    {
        Assert.Equal(nativeClass, Rac1ExplosionPresenter.NativeExplosionClass(weapon));
        Assert.NotEqual((int)weapon, nativeClass);
    }

    [Fact]
    public void GroundedMineWithoutNativeEffectBankDoesNotClaimParticleEmission()
    {
        var presenter = new Rac1ExplosionPresenter();
        presenter.SpawnWeapon(Rac1WeaponId.MineGlove, Vector3.Zero, Vector3.Up, true,
            Rac1PickupRandom.FromSeed(1),
            (_, _) => throw new InvalidOperationException("Grounded mine must not allocate burst nodes"),
            (_, _) => throw new InvalidOperationException("Grounded mine must not rotate fragments"),
            assets: null, seed: 1);
        Assert.Null(presenter.Particles);
        Assert.Empty(presenter.Events);
        presenter.RetireWorld();
        Assert.Empty(presenter.Events);
    }

    [Fact]
    public void ExplosionHistoryIsSourceQualifiedBoundedCopiedAndClearedOnRetirement()
    {
        var presenter = new Rac1ExplosionPresenter();
        var record = typeof(Rac1ExplosionPresenter).GetMethod("Record", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int i = 0; i < 513; i++) record.Invoke(presenter, [74, new Vector3(i, 0, 0), "native-mine-ground-pool"]);
        var firstSnapshot = presenter.Events;
        Assert.Equal(512, firstSnapshot.Length);
        Assert.Equal(1f, firstSnapshot[0].Origin.X);
        Assert.Equal(512f, firstSnapshot[^1].Origin.X);
        Assert.All(firstSnapshot, item =>
        {
            Assert.Equal(ObpSourceGame.Rac1, item.Source);
            Assert.Equal(74, item.NativeClass);
        });
        firstSnapshot[0] = default;
        Assert.Equal(1f, presenter.Events[0].Origin.X);
        presenter.RetireWorld();
        Assert.Empty(presenter.Events);
    }
}
