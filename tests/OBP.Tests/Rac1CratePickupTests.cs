using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1CratePickupTests
{
    private static readonly Vector3 FarRatchet = new(100, 100, 0);
    private static Rac1PickupRandom Midpoint() => new(() => 16384);

    [Fact]
    public void FlightIsZUpGravityAndAttractionRequiresTimeToArrive()
    {
        var bolt = new Rac1BoltMotionSession(13, new(0, 0, 1), new(.02f, -.01f, .1f), Midpoint());
        bolt.Step(FarRatchet, FarRatchet, attractEnabled: false);
        Assert.Equal(.1f - 10.8f / 3600, bolt.Velocity.Z, 6);
        Assert.Equal(new Vector3(.02f, -.01f, 1 + bolt.Velocity.Z), bolt.Position);
        Assert.True(bolt.Attract());
        Assert.False(bolt.Attract());
        bolt.Step(Vector3.Zero, new(0, 0, .7f));
        Assert.Equal(4, bolt.NativeState);
        Assert.False(bolt.Collected);
        Assert.Equal(16f / 3600, bolt.CollectionStep, 6);
        for (int i = 0; i < 240 && !bolt.Collected; i++) bolt.Step(Vector3.Zero, new(0, 0, .7f));
        Assert.True(bolt.Collected);
        Vector3 collected = bolt.Position;
        bolt.Step(FarRatchet, FarRatchet);
        Assert.Equal(collected, bolt.Position);
    }

    [Fact]
    public void ContactUsesUpwardNormalThenOneReboundAndSettles()
    {
        var bolt = new Rac1BoltMotionSession(14, new(0, 0, .3f), new(0, 0, -.03f), Midpoint());
        bolt.Step(FarRatchet, FarRatchet, new(new(0, 0, .23f), Vector3.UnitZ), attractEnabled: false);
        Assert.Equal(2, bolt.NativeState);
        float max = bolt.Position.Z;
        for (int tick = 0; tick < 100; tick++)
        {
            bolt.Step(FarRatchet, FarRatchet, attractEnabled: false);
            max = Math.Max(max, bolt.Position.Z);
        }
        Assert.Equal(3, bolt.NativeState);
        Assert.True(max > .3f);
        Assert.False(bolt.Collected);
        Vector3 settled = bolt.Position;
        for (int tick = 0; tick < 100; tick++) bolt.Step(FarRatchet, FarRatchet, attractEnabled: false);
        Assert.Equal(settled, bolt.Position);
    }

    [Fact]
    public void ThrownWrenchStartsHomingAtDistanceWithoutInstantCredit()
    {
        var bolt = new Rac1BoltMotionSession(16, new(10, 0, 1), Vector3.Zero, Midpoint());
        bolt.Step(Vector3.Zero, new(0, 0, .7f), thrownWrenchContact: true);
        Assert.Equal(4, bolt.NativeState);
        Assert.False(bolt.Collected);
        Assert.InRange(bolt.Position.X, 9.9f, 10f);
    }

    [Fact]
    public void DebrisUsesItsOwnModelsAndExpiresWithoutLeakingPhysicsObjects()
    {
        var fragments = Rac1CrateDebris.Break(500, Matrix4x4.CreateTranslation(3, 5, 7), Midpoint());
        Assert.Equal(8, fragments.Count);
        Assert.Equal(4, fragments.Count(piece => piece.NativeClassId == 357));
        Assert.Equal(4, fragments.Count(piece => piece.NativeClassId == 358));
        Assert.All(fragments, piece => Assert.True(piece.Position.Z >= 7));
        Assert.Single(Rac1CrateDebris.Break(500, Matrix4x4.Identity, Midpoint(), freeMobySlots: 50));
        foreach (var piece in fragments) for (int i = 0; i < 600 && !piece.Expired; i++) piece.Step();
        Assert.All(fragments, piece => Assert.True(piece.Expired));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(199)]
    [InlineData(300)]
    [InlineData(999)]
    public void HigherPayoutPartitionsConserveValueAndUseOnlyNativeDenominations(int value)
    {
        var pieces = Rac1BoltCrate.PartitionTotal(value);
        Assert.Equal(value, pieces.Sum());
        Assert.All(pieces, piece => Assert.Contains(piece, new[] { 1, 5, 20, 50 }));
    }

    [Fact]
    public void MetalCratesRejectWrenchButAcceptTheBombFlagAndUseSharedPersistence()
    {
        var source = Crate(502, 15);
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1BoltCrateSession(runtime, new(0));
        var instance = session.Register(source, RuntimeEntityState.FromAuthored(source));
        var wrench = new Rac1GameplayDamageEvent(Rac1GameplayEntityRef.Player,
            Rac1GameplayEntityRef.Moby(instance.Key), 2, 0x10000);
        Assert.False(runtime.CanDispatchDamage(wrench));
        var bomb = new Rac1GameplayDamageEvent(Rac1GameplayEntityRef.Player,
            Rac1GameplayEntityRef.Moby(instance.Key), 2, Rac1BombGlove.NativeDamageFlags);
        Assert.True(runtime.CanDispatchDamage(bomb));
        var admission = runtime.DispatchDamage<Rac1BoltCrateDamageAdmission>(bomb);
        var broken = session.CompleteDamage(admission, 19);
        Assert.Equal(19, broken.PhysicalValue);
        Assert.Equal(1, session.DestroyedCrateCount);
        Assert.Throws<InvalidOperationException>(() => session.CompleteDamage(admission, 19));
    }

    [Fact]
    public void AmmoPiecesWaitForCapacityAndGrantTheirOwnItemAfterSettling()
    {
        var inventory = new Rac1WeaponInventory(true, firstRangedAmmo: 40);
        var definition = Rac1CrateSupply.AmmoDefinitions.Single(d => d.Item == 10);
        var pickup = new Rac1AmmoPickupSession(definition, new(0, 0, .2f), Midpoint());
        for (int i = 0; i < 150; i++) pickup.Step(Vector3.Zero, inventory, true, new(new(0, 0, .4f), Vector3.UnitZ));
        Assert.False(pickup.Collected);
        Assert.True(inventory.TryConsumeAmmo(10, 2));
        pickup.Step(Vector3.Zero, inventory, true);
        Assert.True(pickup.Collected);
        Assert.Equal(2, pickup.Grant!.Value.AmmoGranted);
        Assert.Equal(40, inventory.FirstRangedAmmo);
        pickup.Step(Vector3.Zero, inventory, true);
        Assert.False(pickup.GrantedThisTick);
    }

    [Fact]
    public void NanotechCloudHasOneRewardAndDoesNotWasteItAtFullHealth()
    {
        var health = new Rac1RatchetNanotechSession();
        var cloud = new Rac1NanotechCloudSession(Vector3.Zero);
        Assert.False(cloud.TryAttract(Vector3.Zero, health.Probe(), 0));
        health.ApplyDamage(Rac1DamageRuntime.FromClass749Attack(new(749, 149), new(34, 1)));
        Assert.True(cloud.TryAttract(Vector3.Zero, health.Probe(), 0));
        Assert.True(cloud.Arrive(health));
        Assert.Equal(4, health.Probe().Nanotech);
        Assert.False(cloud.Arrive(health));
    }

    [Fact]
    public void WalletIsIndependentFromLevelCratesAndSurvivesOptionalHostSave()
    {
        var wallet = new Rac1BoltWallet(); wallet.Credit(17);
        var campaign = new Rac1CampaignState(); var weapons = Rac1WeaponInventory.CreateOpeningVeldinWitness();
        var restored = Rac1CampaignSavePolicy.RestoreOrDefault(Rac1CampaignSavePolicy.Capture(campaign, weapons, wallet));
        Assert.Equal(17, restored.Bolts);
        Assert.Equal(17, Rac1HudProjection.Capture(new(), weapons, wallet).Bolts!.Balance);
        Assert.Throws<InvalidDataException>(() => Rac1CampaignSavePolicy.RestoreOrDefault(
            Rac1CampaignSavePolicy.Capture(campaign, weapons) with { Bolts = -1 }));
    }

    private static RuntimeDynamicObject Crate(int nativeClass, int reward)
    {
        byte[] raw = new byte[0x78];
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0xc), 100);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x10), reward);
        return new("rac1", nativeClass, 17, null, $"moby:{nativeClass}", "moby:17", new(new double[16]), [],
            [new(Rac1BoltCrate.InstancePayloadFormat, raw), new(Rac1BoltCrate.PVarPayloadFormat, new byte[0x100])]);
    }

    [Fact]
    public void ToneRateAccountsForOctavesFineModulationAndSignedRateBasis()
    {
        Assert.Equal(22_050, Rac1CrateAudio.ApproximateToneRate(72, 0, 0));
        Assert.Equal(44_100, Rac1CrateAudio.ApproximateToneRate(72, 0, 12 * 128));
        Assert.Equal(24_000, Rac1CrateAudio.ApproximateToneRate(-72, 0, 0));
    }

    [Fact]
    public void UnsupportedCrateFallsAtNativeGravityAndTerminalVelocityThenRestsOnSupport()
    {
        var crate = new Rac1CrateFallSession(3);
        crate.Step(null);
        Assert.Equal(-Rac1CrateFallSession.GravityPerTick, crate.Velocity);
        for (int i = 0; i < 200; i++) crate.Step(null);
        Assert.Equal(-.3f, crate.Velocity);
        var supported = new Rac1CrateFallSession(1);
        for (int i = 0; i < 90; i++) supported.Step(0);
        Assert.Equal(0, supported.Height);
        Assert.Equal(0, supported.Velocity);
        supported.Step(null);
        Assert.True(supported.Height < 0);
    }

    [Fact]
    public void PickupDimensionsIncludeTheNativeBoltGrabberUpgrade()
    {
        Assert.Equal((3f, 1.75f), Rac1BoltMotionSession.AttractionProfile(0, false));
        Assert.Equal((2.125f, 1.25f), Rac1BoltMotionSession.AttractionProfile(1, false));
        Assert.Equal((15f, 3f), Rac1BoltMotionSession.AttractionProfile(2, false));
        Assert.Equal((12f, 4.5f), Rac1BoltMotionSession.AttractionProfile(0, true));
        var ordinary = new Rac1BoltMotionSession(13, new(8, 0, 0), Vector3.Zero, Midpoint());
        ordinary.Step(Vector3.Zero, Vector3.Zero);
        Assert.Equal(1, ordinary.NativeState);
        ordinary.Step(Vector3.Zero, Vector3.Zero, attractPlanarRadius: 12, attractHeight: 4.5f);
        Assert.Equal(4, ordinary.NativeState);
    }
}
