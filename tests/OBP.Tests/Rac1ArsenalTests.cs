using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1ArsenalTests
{
    private static Rac1WeaponInventory Inventory(Rac1WeaponId weapon)
    {
        var inventory = Rac1WeaponInventory.CreateOpeningVeldinWitness();
        inventory.AcquireNativeItem((int)weapon);
        if (Rac1ItemAmmoDescriptors.Get((int)weapon).UsesAmmo) inventory.GrantAmmoClamped((int)weapon, 999);
        Assert.True(inventory.TryEquip(weapon)); return inventory;
    }

    [Theory]
    [InlineData(Rac1WeaponId.Devastator, 35)]
    [InlineData(Rac1WeaponId.Ryno, 114)]
    [InlineData(Rac1WeaponId.DecoyGlove, 20)]
    public void UseCooldownCannotBeBypassedByTappingOrSwitching(Rac1WeaponId weapon, int ticks)
    {
        var inventory = Inventory(weapon); var session = new Rac1AdditionalWeaponSession(inventory);
        int before = inventory.GetAmmo((int)weapon);
        Assert.NotNull(session.Step(true));
        for (int tick = 1; tick < ticks; tick++)
        {
            inventory.TryEquip(tick % 2 == 0 ? weapon : Rac1WeaponId.Wrench);
            Assert.Null(session.Step(tick % 2 == 0));
        }
        inventory.TryEquip(weapon); Assert.NotNull(session.Step(true));
        Assert.Equal(before - 2, inventory.GetAmmo((int)weapon));
    }

    [Theory]
    [InlineData(Rac1WeaponId.DroneDevice)]
    [InlineData(Rac1WeaponId.VisibombGun)]
    public void AFullDeploymentOrActiveMissileNeverSpendsAmmo(Rac1WeaponId weapon)
    {
        var inventory = Inventory(weapon); var session = new Rac1AdditionalWeaponSession(inventory);
        int before = inventory.GetAmmo((int)weapon);
        for (int tick = 0; tick < 100; tick++) Assert.Null(session.Step(tick % 2 == 0, false));
        Assert.Equal(before, inventory.GetAmmo((int)weapon));
        session.Step(false); Assert.NotNull(session.Step(true));
        for (int tick = 0; tick < 50; tick++) Assert.Null(session.Step(true));
        Assert.Equal(before - 1, inventory.GetAmmo((int)weapon));
    }

    [Theory]
    [InlineData(Rac1WeaponId.Walloper)]
    [InlineData(Rac1WeaponId.Taunter)]
    [InlineData(Rac1WeaponId.MorphORay)]
    public void NonAmmoWeaponsFireAndHideTheAmmoCounter(Rac1WeaponId weapon)
    {
        var inventory = Inventory(weapon); var session = new Rac1AdditionalWeaponSession(inventory);
        Assert.NotNull(session.Step(true));
        Assert.Equal(0, inventory.GetAmmo((int)weapon));
        Assert.Null(Rac1HudProjection.Capture(new(), inventory).CurrentWeapon!.Ammo);
    }

    [Fact]
    public void MovingPyrocitorUsesForwardFireArmLayerWithoutChangingOtherWeaponSlots()
    {
        Assert.Equal(51, Rac1WeaponProfiles.Get(Rac1WeaponId.Pyrocitor).BodySequence);
        Assert.Equal(57, Rac1ArsenalRules.MovingBodySequence(Rac1WeaponId.Pyrocitor));
        Assert.Equal(57, Rac1ArsenalRules.MovingBodySequence(Rac1WeaponId.Blaster));
        Assert.Equal(73, Rac1ArsenalRules.MovingBodySequence(Rac1WeaponId.Devastator));
        Assert.Equal(-1, Rac1ArsenalRules.MovingBodySequence(Rac1WeaponId.SuckCannon));
    }

    [Fact]
    public void FlameAmmoUsesGlobalFramePhaseAcrossWeaponSwitches()
    {
        var inventory = Inventory(Rac1WeaponId.Pyrocitor); var session = new Rac1AdditionalWeaponSession(inventory);
        int before = inventory.GetAmmo(16);
        for (int tick = 0; tick < 7; tick++) session.Step(false);
        for (int tick = 7; tick < 10; tick++) Assert.Null(session.Step(true));
        Assert.NotNull(session.Step(true));
        Assert.Equal(before - 1, inventory.GetAmmo(16));
    }

    [Fact]
    public void SalvoRotatesNineBarrelsAndCannotRestartOrEmitAfterCancellation()
    {
        var salvo = new Rac1RynoSalvoSession();
        Assert.True(salvo.Begin()); Assert.False(salvo.Begin());
        var emissions = new List<(int Tick, int Barrel)>();
        for (int tick = 0; tick < 90; tick++)
            if (salvo.Step() is { } barrel) emissions.Add((tick, barrel));
        Assert.Equal(new[] { 0, 9, 18, 27, 36, 45, 54 }, emissions.Select(item => item.Tick));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, emissions.Select(item => item.Barrel));
        Assert.True(salvo.Begin()); Assert.Equal(8, salvo.Step());
        for (int tick = 0; tick < 8; tick++) Assert.Null(salvo.Step());
        Assert.Equal(0, salvo.Step()); Assert.Null(salvo.Step(false)); Assert.Equal(0, salvo.Remaining);
        Assert.Null(salvo.Step());
    }

    [Fact]
    public void ProjectileSteeringIsBoundedAndExpiryDoesNotDependOnRendering()
    {
        var missile = new Rac1ArsenalProjectile(Vector3.Zero, Vector3.UnitX, 20, 300);
        missile.Step(new(0, 100, 0), 3, 30, 70);
        Assert.InRange(MathF.Acos(Vector3.Dot(Vector3.UnitX, missile.Direction)), .0499f, .0501f);
        Assert.InRange(missile.Position.Length(), .3416f, .3417f);
        for (int tick = 1; tick < 299; tick++) Assert.False(missile.Step(null, 0, 30, 70));
        Assert.Equal(70, missile.Speed); Assert.True(missile.Step());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rac1ArsenalProjectile(Vector3.Zero, Vector3.Zero, 20, 300));
    }

    [SkippableFact]
    public void DiscPinsAllWeaponIdentitiesAndTheNewNativeCadenceAndPopulationConstants()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay();
        uint Word(int address) => BinaryPrimitives.ReadUInt32LittleEndian(read(address, 4));
        Assert.Equal(14, Rac1WeaponProfiles.Additional.Count);
        Assert.Equal(14, Rac1WeaponProfiles.Additional.Select(profile => profile.ModelClass).Distinct().Count());
        foreach (var profile in Rac1WeaponProfiles.Additional)
        {
            int row = 0x179ac0 + (int)profile.Item * 0x4c;
            Assert.Equal((uint)profile.ModelClass, Word(row + 0x10));
            Assert.Equal((uint)profile.HandSemantic, Word(row + 0xc));
            Assert.Equal((uint)profile.BodySequence, Word(row + 0x24));
        }
        Assert.Equal(0x24040023u, Word(0x2b2d48)); // Devastator gate 35.
        Assert.Equal(0x24040014u, Word(0x2d29c4)); // Decoy recovery 20.
        Assert.Equal(0x24040009u, Word(0x2ccb90)); // RYNO 9 ticks.
        Assert.Equal(0x2c630007u, Word(0x2cccf0)); // Seven missiles per use.
        Assert.Equal(0x2404003cu, Word(0x2ccd20)); // Recovery after the salvo.
        Assert.Equal(0x24040099u, Word(0x2afcbc)); // Devastator's rocket class153.
        Assert.Equal(0x240400acu, Word(0x2b5904)); // Visibomb class172.
        Assert.Equal(0x240401c9u, Word(0x2ccf1c)); // RYNO rocket class457.
        Assert.Equal(0x24040bb8u, Word(0x2b59c0)); // Controlled missile lifetime.
        Assert.Equal(0x3c060021u, Word(0x2b99c4)); // Tesla flags 0x210000.
        Assert.Equal(0x3c014000u, Word(0x2b99bc)); // Tesla damage upgrade+2.
        Assert.Equal(0x3c013f80u, Word(0x2ceb04)); // Drone damage1.
        Assert.Equal(0x24040258u, Word(0x2c4a78)); // Decoy's grounded lifetime600.
        Assert.Equal(0x3f800000u, Word(0x160080));
        Assert.Equal(0x3f2ac083u, Word(0x160084));
        Assert.Equal(0x3f000000u, Word(0x160088));
        Assert.Equal(0x3f000000u, Word(0x16008c));
        Assert.Equal(0xa2830028u, Word(0x2e0cb8)); // Robot health-state kind1.
        Assert.Equal(0x3c013e80u, Word(0x2ccf88)); // RYNO missile scale .25, not a full-size class457.
    }

    [SkippableFact]
    public void EveryHeldWeaponAndItsDeployedModelsDecodeWithRetailAnimations()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        var catalogue = Rac1EquipmentAssets.Load(path!);
        foreach (var profile in Rac1WeaponProfiles.Additional)
        {
            var model = catalogue.Models[profile.ModelClass];
            Assert.Equal(profile.CompressedSha256, model.SourceSha256);
            Assert.True(model.VertexCount > 500); Assert.True(model.TriangleCount > 500);
        }
        foreach (int id in new[] { 153, 172, 203, 270, 457, 479 })
            Assert.NotEmpty(catalogue.Models[id].Clips);
        var sockets = catalogue.Models[454].Clips[1].SocketOrigins!;
        Assert.Equal(9, sockets.Count);
        Assert.Equal(9, sockets.Values.Select(points => points[0]).Distinct().Count());
        var avatar = Rac1PlayerAvatarProvider.Instance.Load(path!, "ratchet");
        Assert.All(avatar.AnimationClips, clip => Assert.True(clip.DurationSeconds > 0));
    }
}
