using System.Text.Json;
using OBP.Core;
using OBP.Core.Math;
using OBP.Fusion;
using OBP.Game;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Progression;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class PersistentPlayerOwnerTests
{
    [Fact]
    public void WorldContextsCannotReimportCampaignOrResetTheOrdinaryPlayerControllers()
    {
        var owner = new PlayerSessionHost();
        owner.Ensure(WorldEntryMode.OrdinaryFusion, new Rac1WeaponInventory(true, firstRangedAmmo: 5), 42, 1440, new Preferences());
        var native = owner.Native!;
        var campaignHost = new Rac1CampaignHost();
        var campaign = campaignHost.Session;
        var health = owner.Rac1Health;
        var inventory = owner.Rac1Weapons(null, false, campaignHost.Session.Weapons);
        var wallet = owner.Rac1Wallet(campaignHost.Wallet);
        wallet.Credit(7);
        Assert.True(inventory.TryConsumeAmmo(10, 1));
        int ammo = inventory.FirstRangedAmmo;

        foreach (int level in new[] { 0, 1, 3, 0, 0 })
        {
            _ = new Rac1WorldContext(level, Rac1PickupRandom.FromSeed(1440));
            owner.BeginParityWorld();
            Assert.Same(campaign, campaignHost.Session);
            Assert.Same(native.Inventory, owner.Rac1Weapons(null, false, campaignHost.Session.Weapons));
            Assert.Same(inventory, owner.Rac1Weapons(null, false, campaignHost.Session.Weapons));
            Assert.Same(health, owner.Rac1Health);
            Assert.Same(wallet, owner.Rac1Wallet(campaignHost.Wallet));
            Assert.Equal(49, owner.Rac1Wallet(campaignHost.Wallet).Balance);
            Assert.Equal(ammo, owner.Rac1Weapons(null, false, campaignHost.Session.Weapons).FirstRangedAmmo);
        }
    }

    [Fact]
    public void PlayerOwnerReusesNativeSessionAndWorldEntriesWithoutReimportingCampaign()
    {
        var owner = new PlayerSessionHost();
        Assert.True(owner.Ensure(WorldEntryMode.OrdinaryFusion, Rac1WeaponInventory.CreateOpeningLoadout(), 42, 1440, new Preferences()));
        var session = owner.Session!;
        var native = owner.Native!;
        var entries = owner.WorldEntries!;
        var provider = new PlayerAvatarProviderRegistry([Rac1PlayerAvatarProvider.Instance]);
        var worldHost = new SessionWorldHost();
        var (profile, _, _) = CreateProfile(null, existingPlayer: session, existingNative: native);
        native.Wallet.Credit(7);
        long lastGeneration = 0;

        foreach (string stop in new[] { "rac1", "rac2", "rac3", "fusion", "rac1" })
        {
            Assert.False(owner.Ensure(WorldEntryMode.OrdinaryFusion, Rac1WeaponInventory.CreateOpeningLoadout(), 0, 999, new Preferences()));
            var request = entries.Request(stop + ":entry", WorldEntryMode.OrdinaryFusion);
            var plan = entries.Prepare(request, new SessionWorldSource(SourceIdentityWire.ParseWorld(stop)), provider);
            Assert.True(request.Generation > lastGeneration);
            lastGeneration = request.Generation;
            entries.Commit(plan, worldHost);
            profile.LastCommittedDestination = request.DestinationId;

            Assert.Same(session, owner.Session);
            Assert.Same(native, owner.Native);
            Assert.Same(entries, owner.WorldEntries);
            Assert.Same(session, entries.Player);
            Assert.Equal(49, native.Wallet.Balance);
            Assert.Equal(49, session.Capture().Currencies["obp/bolts"]);
            Assert.Equal(request.DestinationId, profile.Capture().Destination);
            Assert.Same(plan, entries.Current);

            if (stop == "rac3")
            {
                entries.Unload();
                Assert.Null(entries.Current);
                Assert.Same(session, owner.Session);
                Assert.Same(native, owner.Native);
                Assert.Equal(49, native.Wallet.Balance);
                Assert.Equal(request.DestinationId, profile.Capture().Destination);
            }
        }

        var committed = entries.Current;
        var destination = profile.Capture().Destination;
        Assert.Throws<InvalidDataException>(() => entries.Prepare(
            entries.Request("failed:prepare", WorldEntryMode.OrdinaryFusion),
            new SessionWorldSource(WorldContentIdentity.FusionLab, failPreparation: true), provider));
        var rejectedPlan = entries.Prepare(entries.Request("failed:activation", WorldEntryMode.OrdinaryFusion),
            new SessionWorldSource(WorldContentIdentity.FusionLab), provider);
        worldHost.RejectActivation = true;
        Assert.Throws<InvalidOperationException>(() => entries.Commit(rejectedPlan, worldHost));

        Assert.Same(committed, entries.Current);
        Assert.Same(session, owner.Session);
        Assert.Same(native, owner.Native);
        Assert.Same(entries, owner.WorldEntries);
        Assert.Equal(49, native.Wallet.Balance);
        Assert.Equal(destination, profile.Capture().Destination);
        Assert.Equal(WorldEntryMode.OrdinaryFusion, owner.EntryMode);

        entries.Unload();
        Assert.All(worldHost.Leases, lease => Assert.Equal(1, lease.Disposals));
        Assert.Same(session, owner.Session);
        Assert.Same(native, owner.Native);
        Assert.Equal(49, native.Wallet.Balance);
    }

    [Fact]
    public void ProfileVersionMismatchBlocksBothAutosaveAndExplicitWrites()
    {
        WithProfilePath(path =>
        {
            File.WriteAllText(path, "{\"Version\":99,\"NewShape\":true}");
            byte[] original = File.ReadAllBytes(path);
            var (profile, _, _) = CreateProfile(path);
            profile.Initialize();
            Assert.True(profile.RecoveryRequired);
            Assert.NotNull(profile.SaveNotice);
            profile.LastCommittedDestination = "rac2:LEVEL1";
            profile.Tick(10.5);
            Assert.False(profile.Save("manual"));
            Assert.Equal(original, File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void ProfileBuildMismatchDoesNotMutateNativePlayerOrReplaceFile()
    {
        WithProfilePath(path =>
        {
            var bad = ObpProfileTests.Profile() with
            {
                Sources = [new(ObpSourceGame.Rac1, "incompatible-build", new string('A', 64))]
            };
            File.WriteAllBytes(path, bad.Encode());
            byte[] before = File.ReadAllBytes(path);
            var (coordinator, session, native) = CreateProfile(path,
                [new(ObpSourceGame.Rac1, "different-build", new string('B', 64))]);
            long balance = native.Wallet.Balance;
            long generation = session.Capture().Generation;
            coordinator.Initialize();
            Assert.True(coordinator.RecoveryRequired);
            Assert.Equal(balance, native.Wallet.Balance);
            Assert.Equal(generation, session.Capture().Generation);
            Assert.False(coordinator.Save("world transition"));
            Assert.Equal(before, File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void AutosaveOnlyRunsInWorldAndRetainsOpaqueCampaignAfterRecovery()
    {
        WithProfilePath(path =>
        {
            File.WriteAllBytes(path, ObpProfileTests.Profile().Encode());
            bool inWorld = false;
            var (coordinator, _, _) = CreateProfile(path, world: () => inWorld);
            coordinator.Initialize();
            Assert.False(coordinator.RecoveryRequired);
            Assert.Contains(coordinator.Capture().Campaigns, c => c.Owner == "future-source/progress");
            byte[] previous = File.ReadAllBytes(path);
            coordinator.LastCommittedDestination = "rac2:LEVEL1";
            coordinator.Tick(15);
            Assert.Equal(previous, File.ReadAllBytes(path));
            inWorld = true;
            coordinator.Tick(9.9);
            Assert.Equal(previous, File.ReadAllBytes(path));
            coordinator.Tick(0.2);
            Assert.Equal("rac2:LEVEL1", ObpProfile.Decode(File.ReadAllBytes(path)).Destination);
            Assert.Contains(ObpProfile.Decode(File.ReadAllBytes(path)).Campaigns, c => c.Owner == "future-source/progress");
        });
    }

    [Fact]
    public void SavedDestinationAndNativePlayerSurviveProcessRestart()
    {
        WithProfilePath(path =>
        {
            var (writer, _, native) = CreateProfile(path);
            writer.Initialize();
            native.Wallet.Credit(11);
            writer.LastCommittedDestination = "rac2:LEVEL1";
            Assert.True(writer.Save("manual"));
            var (reader, _, restoredNative) = CreateProfile(path);
            reader.Initialize();
            Assert.False(reader.RecoveryRequired);
            Assert.Equal("rac2:LEVEL1", reader.Document?.Destination);
            Assert.Equal(53, restoredNative.Wallet.Balance);
        });
    }

    [Fact]
    public void MissingRememberedSourceStaysRememberedAndCanBeReattached()
    {
        WithProfilePath(path =>
        {
            var remembered = new ProfileSource(ObpSourceGame.Rac1, "fixture-build", new string('A', 64));
            File.WriteAllBytes(path, (ObpProfileTests.Profile() with { Sources = [remembered] }).Encode());
            var attached = new List<ProfileSource>();
            var (coordinator, _, _) = CreateProfile(path, attached);
            coordinator.Initialize();
            Assert.False(coordinator.RecoveryRequired);
            Assert.Equal(remembered, Assert.Single(coordinator.CaptureProfileSources()));
            attached.Add(remembered);
            Assert.Equal(remembered, Assert.Single(coordinator.CaptureProfileSources()));
            Assert.True(coordinator.Save("source recovery"));
            Assert.Equal(remembered, Assert.Single(ObpProfile.Decode(File.ReadAllBytes(path)).Sources));
        });
    }

    [Fact]
    public void NoProfileAndSuspendedSourceWritesDoNotProduceFiles()
    {
        WithProfilePath(path =>
        {
            var (coordinator, _, _) = CreateProfile(path, suspended: () => true);
            coordinator.Initialize();
            Assert.False(coordinator.RecoveryRequired);
            coordinator.Tick(20);
            Assert.False(coordinator.Save("manual"));
            Assert.False(File.Exists(path));
            var (noProfile, _, _) = CreateProfile(null);
            noProfile.Initialize();
            noProfile.Tick(20);
            Assert.False(noProfile.Save("manual"));
        });
    }

    private sealed class SessionWorldSource(WorldContentIdentity game, bool failPreparation = false) : IWorldEntrySource
    {
        public PreparedWorldEntry Prepare(WorldEntryRequest request)
        {
            if (failPreparation) throw new InvalidDataException("Rejected staged world");
            return new(new RuntimeWorld(game, "test", 0, null, null, [], [], 0, [],
                new ObpBounds(Vec3.Zero, new(10, 10, 10)), null, null), new(1, 2, 3, 0), []);
        }
    }

    private sealed class SessionWorldHost : IWorldEntryHost
    {
        public bool RejectActivation;
        public List<SessionWorldLease> Leases { get; } = [];

        public IDisposable Activate(WorldEntryPlan plan)
        {
            if (RejectActivation) throw new InvalidOperationException("Rejected world activation");
            var lease = new SessionWorldLease();
            Leases.Add(lease);
            return lease;
        }
    }

    private sealed class SessionWorldLease : IDisposable
    {
        public int Disposals;
        public void Dispose() => Disposals++;
    }

    private sealed class Preferences : IPlayerSessionPreferencesAdapter
    {
        public IReadOnlyDictionary<string, string> Capture() => new Dictionary<string, string>();
        public void ValidateRestore(IReadOnlyDictionary<string, string> preferences) { }
        public void Restore(IReadOnlyDictionary<string, string> preferences) { }
    }

    private static (ProfileCoordinator Coordinator, ObpPlayerSession Player, PlayerSessionNativeState Native)
        CreateProfile(string? path, IReadOnlyList<ProfileSource>? attached = null,
            Func<bool>? world = null, Func<bool>? suspended = null,
            ObpPlayerSession? existingPlayer = null, PlayerSessionNativeState? existingNative = null)
    {
        var native = existingNative ?? new PlayerSessionNativeState(Rac1WeaponInventory.CreateOpeningLoadout(), 42, 1440);
        var player = existingPlayer ?? new ObpPlayerSession(PlayerSessionLifetimeTests.Snapshot(), native);
        var coordinator = new ProfileCoordinator(new(
            ProfilePath: () => path,
            OrdinaryPlayer: () => true,
            Player: () => player,
            Native: () => native,
            AttachedSources: () => attached ?? [],
            LegacyCampaignExists: () => false,
            CaptureCampaign: previous => new ProfilePayload("rac1/campaign", 1,
                JsonSerializer.SerializeToElement(Rac1CampaignSavePolicy.Capture(new Rac1CampaignState(),
                    Rac1WeaponInventory.CreateOpeningLoadout()))).RetainUnknownFields(previous),
            RestoreCampaign: _ => { },
            RefreshPlayerSources: () => { },
            WritesSuspended: suspended ?? (() => false),
            PlaytestActive: () => false,
            WorldActive: world ?? (() => true),
            Log: _ => { }));
        return (coordinator, player, native);
    }

    private static void WithProfilePath(Action<string> test)
    {
        string dir = Path.Combine(Path.GetTempPath(), "obp349-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { test(Path.Combine(dir, "profile.json")); }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
