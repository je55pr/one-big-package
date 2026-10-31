using OBP.Game;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;

namespace OBP.Tests;

/// <summary>
/// Exercises the owner used by the Godot campaign composition, rather than
/// only the underlying persistence writer or recovery policy in isolation.
/// </summary>
public sealed class Rac1CampaignPersistenceHostTests
{
    [Fact]
    public void OrdinaryEphemeralModeNeverReadsOrWritesAnExplicitParitySave()
    {
        string path = TemporaryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] original = "{ outdated campaign }"u8.ToArray();
        try
        {
            File.WriteAllBytes(path, original);
            var owner = new Rac1CampaignHost();
            owner.Persistence.Initialize(requested: false, requestedPath: path, defaultPath: path);

            Assert.True(owner.Persistence.Initialized);
            Assert.False(owner.Persistence.Enabled);
            Assert.False(owner.Persistence.HasWriter);
            Assert.Equal(Rac1CampaignRestoreKind.DefaultedMissingState, owner.Persistence.RestoreKind);
            Assert.Equal(0, owner.Session.Campaign.CurrentLevel);
            Assert.False(owner.Persistence.MarkDirty());
            Assert.Null(owner.Persistence.Tick(5, () => owner.Wallet, fusionTestActive: false));
            Assert.Null(owner.Persistence.Flush(() => owner.Wallet, previewActive: false,
                fusionTestActive: false, out _));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void CorruptedParitySaveNeverAcquiresAWriterOrOverwritesOriginalBytes()
    {
        string path = TemporaryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] damaged = "{ invalid campaign"u8.ToArray();
        try
        {
            File.WriteAllBytes(path, damaged);
            var owner = new Rac1CampaignHost();
            owner.Persistence.Initialize(requested: true, requestedPath: path, defaultPath: path);

            Assert.False(owner.Persistence.Enabled);
            Assert.NotNull(owner.Persistence.RestoreWarning);
            Assert.Equal(Rac1CampaignRestoreKind.DefaultedMissingState, owner.Persistence.RestoreKind);
            Assert.False(owner.Persistence.HasWriter);
            owner.Session.Campaign.ApplyProgressionEvent(0x25);
            Assert.False(owner.Persistence.MarkDirty());
            Assert.Null(owner.Persistence.Tick(10, () => owner.Wallet, fusionTestActive: false));
            Assert.Null(owner.Persistence.Flush(() => owner.Wallet, previewActive: false,
                fusionTestActive: false, out _));
            Assert.Equal(damaged, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void ParityOwnerRestoresNativeSessionAndWritesItsCampaignAndBoltWallet()
    {
        string path = TemporaryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var campaign = new Rac1CampaignState();
            Assert.Equal(Rac1DestinationDiscoveryResult.Discovered, campaign.ApplyProgressionEvent(0x25));
            Assert.True(campaign.BeginTravel(1));
            Rac1CampaignSaveFile.Save(path, campaign, Rac1WeaponInventory.CreateOpeningVeldinWitness());

            var owner = new Rac1CampaignHost();
            owner.Persistence.Initialize(requested: true, requestedPath: path, defaultPath: path);
            Assert.True(owner.Persistence.Enabled);
            Assert.True(owner.Persistence.HasWriter);
            Assert.Equal(Rac1CampaignRestoreKind.RestoredCurrentSchema, owner.Persistence.RestoreKind);
            Assert.Equal(1, owner.Session.Campaign.CurrentLevel);
            owner.Wallet.Credit(73);
            owner.CollectedNovalisGoldBolts.Add(19);
            Assert.True(owner.Persistence.MarkDirty());
            Assert.False(owner.Persistence.MarkDirty());
            Assert.True(owner.Persistence.Flush(() => owner.Wallet, previewActive: false,
                fusionTestActive: false, out Exception? error));
            Assert.Null(error);
            var restored = Rac1CampaignSaveFile.LoadOrDefault(path);
            Assert.Equal(73, restored.Bolts);
            Assert.Contains(19, restored.NovalisGoldBoltInstances);
            Assert.Equal(1, restored.Campaign.CurrentLevel);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void PreviewAndFusionFlushSuppressionPreservesDirtyPendingSave()
    {
        string path = TemporaryPath();
        try
        {
            var owner = new Rac1CampaignHost();
            owner.Persistence.Initialize(requested: true, requestedPath: path, defaultPath: path);
            Assert.True(owner.Persistence.HasWriter);
            Assert.True(owner.Persistence.MarkDirty());
            Assert.Null(owner.Persistence.Flush(() => owner.Wallet, previewActive: true,
                fusionTestActive: false, out _));
            Assert.Null(owner.Persistence.Flush(() => owner.Wallet, previewActive: false,
                fusionTestActive: true, out _));
            Assert.Null(owner.Persistence.Tick(10, () => owner.Wallet, fusionTestActive: true));
            Assert.False(File.Exists(path));
            Assert.True(owner.Persistence.Flush(() => owner.Wallet, previewActive: false,
                fusionTestActive: false, out _));
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)!))
                Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void DurabilityBoundaryCapturesTheCurrentlySelectedWallet()
    {
        string path = TemporaryPath();
        try
        {
            var owner = new Rac1CampaignHost();
            owner.Persistence.Initialize(requested: true, requestedPath: path, defaultPath: path);
            var original = new Rac1BoltWallet(3);
            var replacement = new Rac1BoltWallet(91);
            Rac1BoltWallet selected = original;
            Assert.True(owner.Persistence.MarkDirty());

            // The selection can change before a queued writer's durability
            // boundary. Capturing a wallet value too early would save 3.
            selected = replacement;
            Assert.True(owner.Persistence.Flush(() => selected, previewActive: false,
                fusionTestActive: false, out _));
            Assert.Equal(91, Rac1CampaignSaveFile.LoadOrDefault(path).Bolts);
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)!))
                Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void RestoringAnotherProfileClearsPriorSourceGoldBolts()
    {
        var owner = new Rac1CampaignHost();
        owner.CollectedNovalisGoldBolts.Add(19);
        owner.Wallet.Credit(77);
        owner.Restore(Rac1CampaignSessionPersistence.Ephemeral().Restore());
        Assert.Empty(owner.CollectedNovalisGoldBolts);
        Assert.Equal(0, owner.Wallet.Balance);
        Assert.Equal(0, owner.Session.Campaign.CurrentLevel);
    }

    [Fact]
    public void TickQueuesDirtyParityStateWithoutFusionAndFlushPersistsIt()
    {
        string path = TemporaryPath();
        try
        {
            var owner = new Rac1CampaignHost();
            owner.Persistence.Initialize(requested: true, requestedPath: path, defaultPath: path);
            owner.Wallet.Credit(47);
            Assert.True(owner.Persistence.MarkDirty());
            Assert.Null(owner.Persistence.Tick(Rac1CampaignSaveBatcher.QuietSeconds,
                () => owner.Wallet, fusionTestActive: false));
            Assert.True(owner.Persistence.Flush(() => owner.Wallet,
                previewActive: false, fusionTestActive: false, out _));
            Assert.Equal(47, Rac1CampaignSaveFile.LoadOrDefault(path).Bolts);
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)!))
                Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void TickReportsRepeatedSaveFailureOnlyAfterAnObservedRecovery()
    {
        string path = TemporaryPath();
        var parent = Path.GetDirectoryName(path)!;
        try
        {
            var owner = new Rac1CampaignHost();
            owner.Persistence.Initialize(requested: true, requestedPath: path, defaultPath: path);
            Directory.CreateDirectory(path); // A directory cannot be replaced by a save file.
            owner.Persistence.MarkDirty();
            owner.Persistence.Tick(Rac1CampaignSaveBatcher.QuietSeconds,
                () => owner.Wallet, fusionTestActive: false);
            string? first = null;
            Assert.True(SpinWait.SpinUntil(() =>
            {
                first = owner.Persistence.Tick(0, () => owner.Wallet, fusionTestActive: false);
                return first is not null;
            }, TimeSpan.FromSeconds(8)), "Expected a deterministic save-path collision.");
            Assert.Null(owner.Persistence.Tick(0, () => owner.Wallet, fusionTestActive: false));

            Directory.Delete(path);
            Assert.True(owner.Persistence.Flush(() => owner.Wallet,
                previewActive: false, fusionTestActive: false, out Exception? error));
            Assert.Null(error);
            Assert.Null(owner.Persistence.Tick(0, () => owner.Wallet, fusionTestActive: false));

            File.Delete(path);
            Directory.CreateDirectory(path);
            owner.Persistence.MarkDirty();
            owner.Persistence.Tick(Rac1CampaignSaveBatcher.QuietSeconds,
                () => owner.Wallet, fusionTestActive: false);
            string? repeated = null;
            Assert.True(SpinWait.SpinUntil(() =>
            {
                repeated = owner.Persistence.Tick(0, () => owner.Wallet, fusionTestActive: false);
                return repeated is not null;
            }, TimeSpan.FromSeconds(8)), "A recovered failure should be reported if it recurs.");
            Assert.Equal(first, repeated);
            Assert.Null(owner.Persistence.Tick(0, () => owner.Wallet, fusionTestActive: false));
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void SecondInitializeDoesNotRecreateWriterOrReplaceLiveCampaignSession()
    {
        var owner = new Rac1CampaignHost();
        string path = TemporaryPath();
        owner.Persistence.Initialize(requested: false, requestedPath: path, defaultPath: path);
        var session = owner.Session;
        owner.Persistence.Initialize(requested: true, requestedPath: path, defaultPath: path);
        Assert.Same(session, owner.Session);
        Assert.False(owner.Persistence.HasWriter);
        Assert.False(owner.Persistence.Enabled);
        Assert.False(File.Exists(path));
    }

    private static string TemporaryPath() =>
        Path.Combine(Path.GetTempPath(), "obp-rac1-campaign-host-tests",
            Guid.NewGuid().ToString("N"), "campaign.json");
}
