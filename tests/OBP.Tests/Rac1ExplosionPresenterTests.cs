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
    public void NativeEquipmentTintUsesOriginalHalfRangeScaling()
    {
        var color = Rac1ExplosionPresenter.EquipmentColor(0x80402010);
        Assert.Equal(.125f, color.R);
        Assert.Equal(.25f, color.G);
        Assert.Equal(.5f, color.B);
        Assert.Equal(1f, color.A);
    }

    [Fact]
    public void EquipmentMotesUseTheExistingWorldParticlePresenterRatherThanRootMeshConstruction()
    {
        var rootType = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        Assert.Empty(rootType.GetMembers("EquipmentParticle"));
        Assert.Empty(rootType.GetMembers("GetRac1ParticleMesh"));
        Assert.Empty(rootType.GetMembers("EquipmentColor"));

        string world = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1WeaponWorld.cs"));
        string factory = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        string presenter = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/Rac1ExplosionPresenter.cs"));
        Assert.Contains("Explosions.CreateEquipmentParticle(type, color, size", world, StringComparison.Ordinal);
        Assert.Contains("Explosions.CreateEquipmentParticle(27, new(.3f, .7f, 1, .6f), .6f", factory, StringComparison.Ordinal);
        Assert.Contains("Mesh = GetParticleMesh(poolType, assets?.ParticleTextures, gloveTextures)", presenter, StringComparison.Ordinal);
        Assert.Contains("Scale = Vector3.One * size", presenter, StringComparison.Ordinal);
        Assert.Contains("CastShadow = GeometryInstance3D.ShadowCastingSetting.Off", presenter, StringComparison.Ordinal);
        Assert.Contains("node.SetInstanceShaderParameter(\"tint\", color)", presenter, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Projectiles.AddFlash(node, ticks, size)", world, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Projectiles.AddMote(ring, forward * .25f)", factory, StringComparison.Ordinal);
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
