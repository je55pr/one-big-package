using OBP.Core;
using OBP.Core.Math;
using OBP.Fusion;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OBP.Game;

namespace OBP.Tests;

public sealed class PlayerSessionLifetimeTests
{
    internal static PlayerSessionSnapshot Snapshot() => new("player", 0,
        new(new(ObpSourceGame.Rac1, "test", "ratchet", "real-fixture"),
            new(ObpSourceGame.Rac1, "test", "movement"), new(ObpSourceGame.Rac1, "test", "animation"),
            new("input", "attack", "health", "camera", "hud", "menu", "wheel", "audio"), 60),
        new(new(ObpSourceGame.Rac2, 30), null, [new(ObpSourceGame.Rac1, 10), new(ObpSourceGame.Rac2, 10), new(ObpSourceGame.Rac3, 10)]),
        new(3, 4, true), new Dictionary<string, long> { ["bolts"] = 42 });

    [Fact]
    public void CapturesAreFrozenAndRestoreIsExplicitAndVersioned()
    {
        SourceWeaponKey?[] slots = [new(ObpSourceGame.Rac1, 10), new(ObpSourceGame.Rac2, 10), new(ObpSourceGame.Rac3, 10)];
        var balances = new Dictionary<string, long> { ["bolts"] = 42 };
        var initial = Snapshot() with { Loadout = Snapshot().Loadout with { QuickSelect = slots }, Currencies = balances };
        var session = new ObpPlayerSession(initial);
        var before = session.Capture();
        slots[0] = null; balances["bolts"] = 0;
        Assert.Equal(new SourceWeaponKey(ObpSourceGame.Rac1, 10), session.Capture().Loadout.QuickSelect[0]);
        Assert.Equal(42, session.Capture().Currencies["bolts"]);
        Assert.Equal(3, session.Capture().Loadout.QuickSelect.Distinct().Count());
        session.SetPreference("invert-y", "true");
        Assert.Empty(before.Preferences);
        Assert.Equal("true", session.Capture().Preferences["invert-y"]);
        session.Restore(before);
        Assert.Equal(1, session.Capture().Generation);
        Assert.Empty(session.Capture().Preferences);
        Assert.Throws<ArgumentException>(() => session.Restore(before with { PlayerId = "another" }));
        Assert.Throws<ArgumentException>(() => session.Restore(before with { Version = 99 }));
        Assert.Equal(1, session.Capture().Generation);
    }

    [Fact]
    public void MissingUnsupportedAndUnownedRemainDistinctWithoutChangingSelection()
    {
        var session = new ObpPlayerSession(Snapshot());
        Assert.Equal(PlayerCapabilityStatus.MissingSource, session.Availability(ObpSourceGame.Rac2, "test", true, true).Status);
        Assert.Equal(PlayerCapabilityStatus.Unimplemented, session.Availability(ObpSourceGame.Rac2, "test", false, true).Status);
        session.SetAttachedSources([new(ObpSourceGame.Rac2, "test")]);
        Assert.Equal(PlayerCapabilityStatus.Unowned, session.Availability(ObpSourceGame.Rac2, "test", true, false).Status);
        Assert.Equal(PlayerCapabilityStatus.ContextBlocked, session.Availability(ObpSourceGame.Rac2, "test", true, true, false).Status);
        Assert.True(session.Availability(ObpSourceGame.Rac2, "test", true, true).Usable);
        session.SetAttachedSources([new(ObpSourceGame.Rac2, "changed")]);
        Assert.Equal(PlayerCapabilityStatus.UnsupportedBuild, session.Availability(ObpSourceGame.Rac2, "test", true, true).Status);
        session.SetAttachedSources([]);
        Assert.Equal(new SourceWeaponKey(ObpSourceGame.Rac2, 30), session.Capture().Loadout.SelectedWeapon);
    }

    [Fact]
    public void HostPreferencesAreCapturedAndValidatedBeforeRestoringGameplay()
    {
        var preferences = new Preferences();
        var native = new PlayerSessionNativeState(OBP.RAC1.Gameplay.Rac1WeaponInventory.CreateOpeningLoadout(), 42, 1440);
        var session = new ObpPlayerSession(Snapshot(), native, preferences);
        var before = session.Capture();
        session.SetPreference("camera", "inverted");
        Assert.Equal("inverted", preferences.Value);
        session.SetPreference("ui/hud", "compact");
        Assert.Equal("compact", session.Capture().Preferences["ui/hud"]);
        native.Wallet.Credit(7);
        Assert.Throws<ArgumentException>(() => session.Restore(before with { Preferences = new Dictionary<string, string> { ["camera"] = "invalid" } }));
        Assert.Equal(49, native.Wallet.Balance);
        Assert.Equal(0, session.Capture().Generation);
        session.Restore(before);
        Assert.Equal("normal", preferences.Value);
        Assert.Equal(42, native.Wallet.Balance);
        Assert.Equal(1, session.Capture().Generation);
        Assert.Equal("normal", before.Preferences["camera"]);
    }

    [Fact]
    public void OnePlayerSurvivesNativeCustomRoundTripsAndEachLeaseRetiresExactlyOnce()
    {
        var session = new ObpPlayerSession(Snapshot());
        var entries = new WorldEntrySession(session);
        var host = new TestHost();
        var providers = new PlayerAvatarProviderRegistry([new AvatarProvider()]);
        long last = 0;
        foreach (string origin in new[] { "rac1", "rac2", "rac3", "fusion", "rac1" })
        {
            var request = entries.Request(origin + ":entry", WorldEntryMode.OrdinaryFusion);
            var plan = entries.Prepare(request, new Source(SourceIdentityWire.ParseWorld(origin)), providers);
            Assert.True(request.Generation > last); last = request.Generation;
            entries.Commit(plan, host);
            Assert.Same(session, entries.Player);
            Assert.Equal(Snapshot().Configuration, entries.Current!.Player.Configuration);
            Assert.Equal(new SourceWeaponKey(ObpSourceGame.Rac2, 30), session.Capture().Loadout.SelectedWeapon);
            Assert.Equal(3, session.Capture().Health.Current);
            Assert.Equal(0, session.Capture().Generation);
            Assert.Single(host.Leases, lease => lease.Disposals == 0);
        }
        var live = entries.Current;
        Assert.Throws<InvalidOperationException>(() => entries.Commit(live!, host));
        Assert.Throws<InvalidDataException>(() => entries.Prepare(entries.Request("bad", WorldEntryMode.OrdinaryFusion), new Source(ObpSourceGame.Synthetic) { Fail = true }, providers));
        Assert.Same(live, entries.Current);
        host.Fail = true;
        var staged = entries.Prepare(entries.Request("next", WorldEntryMode.OrdinaryFusion), new Source(WorldContentIdentity.FusionLab), providers);
        Assert.Throws<InvalidOperationException>(() => entries.Commit(staged, host));
        Assert.Same(live, entries.Current);
        Assert.Single(host.Leases, lease => lease.Disposals == 0);
        entries.Unload(); entries.Unload();
        Assert.All(host.Leases, lease => Assert.Equal(1, lease.Disposals));
        Assert.All(host.Leases, lease => Assert.False(lease.HasAudioOrEventRegistration));
    }

    [Fact]
    public void RestoreInvalidatesAStagedPlanWithoutDisposingTheCurrentWorld()
    {
        var session = new ObpPlayerSession(Snapshot());
        var entries = new WorldEntrySession(session);
        var host = new TestHost();
        var providers = new PlayerAvatarProviderRegistry([new AvatarProvider()]);
        var plan = entries.Prepare(entries.Request("first", WorldEntryMode.OrdinaryFusion), new Source(ObpSourceGame.Rac1), providers);
        entries.Commit(plan, host);
        var next = entries.Prepare(entries.Request("next", WorldEntryMode.OrdinaryFusion), new Source(ObpSourceGame.Rac2), providers);
        session.Restore(session.Capture());
        Assert.Throws<InvalidOperationException>(() => entries.Commit(next, host));
        Assert.Equal(0, host.Leases[0].Disposals);
        next = entries.Prepare(entries.Request("next", WorldEntryMode.OrdinaryFusion), new Source(ObpSourceGame.Rac2), providers);
        session.Configure(session.Capture().Configuration with { NativeTicksPerSecond = 30 });
        Assert.Throws<InvalidOperationException>(() => entries.Commit(next, host));
        Assert.Equal(0, host.Leases[0].Disposals);
    }

    private sealed class Preferences : IPlayerSessionPreferencesAdapter
    {
        public string Value = "normal";
        public IReadOnlyDictionary<string, string> Capture() => new Dictionary<string, string> { ["camera"] = Value };
        public void ValidateRestore(IReadOnlyDictionary<string, string> preferences)
        {
            if (preferences.GetValueOrDefault("camera") is not ("normal" or "inverted")) throw new ArgumentException("Invalid camera preference.");
        }
        public void Restore(IReadOnlyDictionary<string, string> preferences) { ValidateRestore(preferences); Value = preferences["camera"]; }
    }

    [Fact]
    public void AuthoredStartsKeepTheirHeadingWhileInvalidShipPointsUseBoundsFallback()
    {
        var world = new RuntimeWorld(ObpSourceGame.Synthetic, "test", 0, null, null, [], [], 0, [],
            new ObpBounds(Vec3.Zero, new(10, 10, 10)), null, new(5, 8, 5, .7));
        Assert.Equal(world.Ship, WorldEntryPlanning.ResolveEntryPoint(world));
        var outside = new RuntimeSpawn(500, 800, 500, 1.5);
        Assert.Equal(new RuntimeSpawn(5, 16, 5, 0), WorldEntryPlanning.ResolveEntryPoint(world with { Ship = outside }));
        Assert.Equal(outside, WorldEntryPlanning.ResolveEntryPoint(world with { PlayerStart = outside }));
        Assert.Equal(new RuntimeSpawn(5, 16, 5, 0), WorldEntryPlanning.ResolveEntryPoint(world with { Ship = null }));
    }

    private sealed class Source(WorldContentIdentity game) : IWorldEntrySource
    {
        public bool Fail;
        public PreparedWorldEntry Prepare(WorldEntryRequest request)
        {
            if (Fail) throw new InvalidDataException("Failed staged world.");
            return new(new RuntimeWorld(game, "test", 0, null, null, [], [], 0, [],
                new ObpBounds(Vec3.Zero, new(10, 10, 10)), null, null), new(1, 2, 3, .5), []);
        }
    }

    private sealed class AvatarProvider : IPlayerAvatarProvider
    {
        public ObpSourceGame SourceGame => ObpSourceGame.Rac1;
        public string BuildId => "test";
        public string DefaultAvatarId => "ratchet";
        public bool CanLoad(string id) => id == DefaultAvatarId;
        public PlayerAvatar Load(string path, string id) => throw new NotSupportedException();
    }

    private sealed class TestHost : IWorldEntryHost
    {
        public bool Fail;
        public List<Lease> Leases { get; } = [];
        public IDisposable Activate(WorldEntryPlan plan)
        {
            if (Fail) throw new InvalidOperationException("Activation rejected before commitment.");
            var lease = new Lease(); Leases.Add(lease); return lease;
        }
    }

    private sealed class Lease : IDisposable
    {
        public int Disposals;
        public bool HasAudioOrEventRegistration = true;
        public void Dispose() { Disposals++; HasAudioOrEventRegistration = false; }
    }
}
