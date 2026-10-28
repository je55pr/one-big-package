using System.Text.Json;
using OBP.Core;
using OBP.Fusion;
using OBP.Game;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class PersistentPlayerOwnerTests
{
    [Fact]
    public void PlayerOwnerReusesNativeSessionAndWorldEntriesWithoutReimportingCampaign()
    {
        var owner = new PlayerSessionHost();
        Assert.True(owner.Ensure(WorldEntryMode.OrdinaryFusion, Rac1WeaponInventory.CreateOpeningLoadout(), 42, 1440, new Preferences()));
        var session = owner.Session;
        var native = owner.Native;
        var entries = owner.WorldEntries;
        native!.Wallet.Credit(7);
        foreach (var _ in new[] { "rac1", "rac2", "rac3", "fusion", "rac1" })
        {
            Assert.False(owner.Ensure(WorldEntryMode.OrdinaryFusion, Rac1WeaponInventory.CreateOpeningLoadout(), 0, 999, new Preferences()));
            Assert.Same(session, owner.Session);
            Assert.Same(native, owner.Native);
            Assert.Same(entries, owner.WorldEntries);
            Assert.Equal(49, native.Wallet.Balance);
        }
        Assert.Equal(WorldEntryMode.OrdinaryFusion, owner.EntryMode);
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

    private sealed class Preferences : IPlayerSessionPreferencesAdapter
    {
        public IReadOnlyDictionary<string, string> Capture() => new Dictionary<string, string>();
        public void ValidateRestore(IReadOnlyDictionary<string, string> preferences) { }
        public void Restore(IReadOnlyDictionary<string, string> preferences) { }
    }

    private static (ProfileCoordinator Coordinator, ObpPlayerSession Player, PlayerSessionNativeState Native)
        CreateProfile(string? path, IReadOnlyList<ProfileSource>? attached = null,
            Func<bool>? world = null, Func<bool>? suspended = null)
    {
        var native = new PlayerSessionNativeState(Rac1WeaponInventory.CreateOpeningLoadout(), 42, 1440);
        var player = new ObpPlayerSession(PlayerSessionLifetimeTests.Snapshot(), native);
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
