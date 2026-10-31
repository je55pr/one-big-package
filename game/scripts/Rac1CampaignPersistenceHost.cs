using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;

namespace OBP.Game;

/// <summary>
/// Persistent R&C1 campaign save/restore owner. Ordinary shared-player profiles
/// remain with PlayerSessionHost; a rejected native-parity save never gains a
/// writer. This owner holds the only parity autosave clock and keeps the native
/// campaign, inventory and wallet identities together through restoration.
/// </summary>
internal sealed class Rac1CampaignPersistenceHost(Rac1CampaignHost campaign)
{
    private Rac1CampaignSaveBatcher? _batcher;
    private string? _reportedFailure;
    private Rac1CampaignSessionPersistence _persistence = Rac1CampaignSessionPersistence.Ephemeral();

    internal bool Initialized { get; private set; }
    internal bool Enabled => _persistence.Enabled;
    internal string? SavePath => _persistence.SavePath;
    internal bool HasWriter => _batcher is not null;
    internal string? RestoreWarning { get; private set; }
    internal Rac1CampaignRestoreKind RestoreKind { get; private set; } =
        Rac1CampaignRestoreKind.DefaultedMissingState;

    internal void Initialize(bool requested, string? requestedPath, string defaultPath)
    {
        if (Initialized) return;

        _persistence = requested
            ? Rac1CampaignSessionPersistence.Persistent(
                string.IsNullOrWhiteSpace(requestedPath) ? defaultPath : Path.GetFullPath(requestedPath))
            : Rac1CampaignSessionPersistence.Ephemeral();

        var attempt = _persistence.RestoreWithRecovery();
        _persistence = attempt.Persistence;
        // Recovery can reject a corrupt file. The returned disabled policy
        // must not create a batcher that could overwrite that original file.
        _batcher = Rac1CampaignSaveWriterFactory.CreateIfEnabled(_persistence);
        RestoreWarning = attempt.Warning;
        var restored = attempt.Restored;
        campaign.Session = new Rac1CampaignRuntimeSession(
            restored.Campaign, restored.Weapons,
            Rac1BuriedBoltProgress.Restore(restored.BuriedBoltHistory));
        campaign.Wallet = new Rac1BoltWallet(restored.Bolts);
        campaign.CollectedNovalisGoldBolts.UnionWith(restored.NovalisGoldBoltInstances);
        RestoreKind = restored.Kind;
        Initialized = true;
    }

    internal Rac1CampaignSaveEnvelope Capture(Rac1BoltWallet wallet) =>
        Rac1CampaignSavePolicy.Capture(
            campaign.Session.Campaign, campaign.Session.Weapons, wallet,
            campaign.CollectedNovalisGoldBolts, campaign.Session.BuriedBolts);

    /// <summary>Advances the single campaign-parity save clock, not the shared profile clock.</summary>
    internal string? Tick(double delta, Func<Rac1BoltWallet> wallet, bool fusionTestActive)
    {
        if (_batcher is null || fusionTestActive) return null;
        _batcher.Tick(delta, () => Capture(wallet()));
        string? failure = _batcher.Failure?.Message;
        string? newlyReported = failure is not null && failure != _reportedFailure ? failure : null;
        _reportedFailure = failure;
        return newlyReported;
    }

    /// <summary>Null means intentionally suppressed; false means the attempted flush failed.</summary>
    internal bool? Flush(Func<Rac1BoltWallet> wallet, bool previewActive, bool fusionTestActive, out Exception? error)
    {
        error = null;
        if (_batcher is null || previewActive || fusionTestActive) return null;
        return _batcher.FlushNow(() => Capture(wallet()), out error);
    }

    /// <summary>Returns true only for the first dirty mutation awaiting a save.</summary>
    internal bool MarkDirty()
    {
        if (_batcher is null) return false;
        bool wasDirty = _batcher.Dirty;
        _batcher.MarkDirty();
        return !wasDirty;
    }
}
