using OBP.Core;
using OBP.Fusion;
using OBP.Game;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class PlayerLifecycleBridgeTests
{
    private sealed class Preferences : IPlayerSessionPreferencesAdapter
    {
        public IReadOnlyDictionary<string, string> Capture() => new Dictionary<string, string>();
        public void ValidateRestore(IReadOnlyDictionary<string, string> preferences)
        {
            // No preference state belongs to this fixture.
        }
        public void Restore(IReadOnlyDictionary<string, string> preferences)
        {
            // No preference state to restore; the native adapter restores gameplay state.
        }
    }
    private sealed class Fixture
    {
        internal readonly PlayerSessionHost Player = new();
        internal readonly List<PlayerLifecycleEvent> Events = [];
        internal readonly PlayerLifecycleBridge Bridge;
        internal Func<bool> Enter = () => false;
        internal Fixture()
        {
            Player.Ensure(WorldEntryMode.OrdinaryFusion, Rac1WeaponInventory.CreateOpeningLoadout(), 123, 1440, new Preferences());
            Bridge = new(new(() => Player.Native, () => Player.Session, () => true, () => Enter(), Events.Add));
        }
        internal PlayerIntakeResult Hit(ActorInstanceKey key, Func<bool>? alive = null, bool scene = true)
        {
            bool rac1 = key.Target.SourceGame == ObpSourceGame.Rac1;
            string build = rac1 ? OBP.RAC1.Rac1Authority.Primary.BuildId : OBP.RAC2.Rac2Authority.Primary.BuildId;
            string adapter = rac1 ? PlayerSessionNativeState.Rac1DamageAdapter : PlayerSessionNativeState.Rac2DamageAdapter;
            string descriptor = rac1 ? PlayerSessionNativeState.Rac1DamageDescriptor : PlayerSessionNativeState.Rac2DamageDescriptor;
            return Bridge.Apply(new(key, new(key.Target.SourceGame, build, "moby:" + key.Target.NativeClassId), adapter,
                "native-contact", [new(descriptor, CapabilityAdmission.Ready)]), new(key, build, adapter, descriptor, 1, 1, 0), alive ?? (() => true), scene);
        }
    }

    [Fact]
    public void RetiredWorldAndDelayedTokenCannotDamageOrRetireTheReplacementPlayer()
    {
        var f = new Fixture();
        var old = f.Bridge.BeginWorld("first", 1);
        var key = new ActorInstanceKey("first", 1, new(ObpSourceGame.Rac1, 1440, 7));
        Assert.True(f.Hit(key).Applied);
        var health = f.Player.Native!.Life.Capture();
        using var next = f.Bridge.BeginWorld("next", 2);
        old.Dispose();
        int calls = 0;
        Assert.Equal(PlayerIntakeStatus.StaleLease, f.Hit(key, () => { calls++; return true; }).Status);
        Assert.Equal(0, calls);
        Assert.Equal(health, f.Player.Native.Life.Capture());
        var current = key with { WorldInstanceId = "next", Generation = 2 };
        Assert.NotEqual(PlayerIntakeStatus.StaleLease, f.Hit(current).Status);
        Assert.True(f.Bridge.Contains(current));
        old.Dispose();
        Assert.True(f.Bridge.Contains(current));
    }

    [Theory]
    [InlineData(ObpSourceGame.Rac1)]
    [InlineData(ObpSourceGame.Rac2)]
    public void SourceDamageUsesTheExistingAdmissionAndPresentationBoundary(ObpSourceGame game)
    {
        var f = new Fixture();
        using var world = f.Bridge.BeginWorld("world", 1);
        var key = new ActorInstanceKey("world", 1, new(game, 1440, 7));
        Assert.Equal(PlayerIntakeStatus.StaleLease, f.Hit(key, scene: false).Status);
        Assert.False(f.Bridge.Contains(key));
        Assert.True(f.Hit(key).Applied);
        Assert.Equal(3, f.Player.Native!.Life.Capture().Current);
        Assert.Single(f.Events, change => change.Kind == PlayerLifecycleEventKind.Hurt);
        Assert.Equal(PlayerIntakeStatus.Duplicate, f.Hit(key).Status);
        Assert.Single(f.Events, change => change.Kind == PlayerLifecycleEventKind.Hurt);
        f.Bridge.RetireActor(key);
        Assert.False(f.Bridge.Contains(key));
    }

    [Fact]
    public void MixedSourceDamageSharesOneWorldLeaseAndRetiresOneActor()
    {
        var f = new Fixture();
        using var world = f.Bridge.BeginWorld("world", 1);
        var rac1 = new ActorInstanceKey("world", 1, new(ObpSourceGame.Rac1, 1440, 7));
        var rac2 = new ActorInstanceKey("world", 1, new(ObpSourceGame.Rac2, 1440, 7));
        Assert.True(f.Hit(rac1).Applied);
        Assert.Equal(3, f.Player.Native!.Life.Capture().Current);
        // Respect the recovered RAC1 hit-protection window before the second source hits.
        int protectionTicks = f.Player.Native.Health.Probe().EnemyHitProtectionTicks;
        for (int tick = 0; tick < protectionTicks; tick++) f.Player.Native.Life.TickNative();
        Assert.True(f.Hit(rac2).Applied);
        Assert.Equal(2, f.Player.Native.Life.Capture().Current);
        Assert.Equal(2, f.Events.Count(change => change.Kind == PlayerLifecycleEventKind.Hurt));
        Assert.True(f.Bridge.Contains(rac1));
        Assert.True(f.Bridge.Contains(rac2));
        f.Bridge.RetireActor(rac1);
        Assert.False(f.Bridge.Contains(rac1));
        Assert.True(f.Bridge.Contains(rac2));
    }

    [Fact]
    public void HazardDeathIsPresentedOnceAndRetirementStopsTheServiceClockWithoutHealing()
    {
        var f = new Fixture();
        using var world = f.Bridge.BeginWorld("world", 1);
        f.Bridge.Tick(true, -2, -1);
        f.Bridge.Tick(true, -2, -1);
        Assert.False(f.Bridge.Alive);
        Assert.Single(f.Events, change => change.Kind == PlayerLifecycleEventKind.Defeated);
        long tick = f.Bridge.ServiceTick;
        f.Bridge.RetireWorld();
        f.Bridge.Tick(true, 0, -1);
        Assert.Equal(tick, f.Bridge.ServiceTick);
        Assert.False(f.Bridge.Alive);
        using var replacement = f.Bridge.BeginWorld("replacement", 2);
        Assert.False(f.Bridge.Alive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedRespawnRestoresHealthAmmoWalletAndLifeGeneration(bool throws)
    {
        var f = new Fixture();
        using var world = f.Bridge.BeginWorld("world", 1);
        f.Player.Native!.Weapons.Select(FusionWeaponSession.Lancer);
        f.Player.Native.Life.DefeatFromWorldHazard();
        string before = System.Text.Json.JsonSerializer.Serialize(f.Player.Native.Capture());
        f.Enter = () => throws ? throw new InvalidOperationException("stage rejected") : false;
        if (throws) Assert.Throws<InvalidOperationException>(() => f.Bridge.Respawn());
        else Assert.False(f.Bridge.Respawn());
        Assert.False(f.Bridge.Alive);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(f.Player.Native.Capture()));
        Assert.Equal("world", f.Bridge.WorldId);
        Assert.Single(f.Events, change => change.Kind == PlayerLifecycleEventKind.RespawnRejected);
    }

    [Fact]
    public void SuccessfulRespawnUsesExistingWorldEntryAndRetainsThePersistentLoadout()
    {
        var f = new Fixture();
        using var world = f.Bridge.BeginWorld("world", 1);
        f.Player.Native!.Weapons.Select(FusionWeaponSession.Lancer);
        f.Player.Native.Life.DefeatFromWorldHazard();
        f.Enter = () => { f.Bridge.BeginWorld("next", 2); return true; };
        Assert.True(f.Bridge.Respawn());
        Assert.True(f.Bridge.Alive);
        Assert.Equal(123, f.Player.Native.Wallet.Balance);
        Assert.Equal(FusionWeaponSession.Lancer, f.Player.Native.Weapons.Selected);
        Assert.Equal(40, f.Player.Native.Weapons.LancerSession.Ammo);
        Assert.Equal(1, f.Player.Native.Life.LifeGeneration);
        Assert.Single(f.Events, change => change.Kind == PlayerLifecycleEventKind.Respawned);
        world.Dispose();
        Assert.Equal("next", f.Bridge.WorldId);
    }
}
