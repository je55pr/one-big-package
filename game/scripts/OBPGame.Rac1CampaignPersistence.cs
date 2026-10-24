using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;

namespace OneBigPackage;

/// <summary>
/// OBP host persistence glue for recovered R&C1 campaign fields. File format and
/// save timing are host policy, not claims about the retail memory-card writer.
/// </summary>
public partial class OBPGame
{
    private readonly HashSet<int> _rac1CollectedNovalisGoldBolts = [];
    private Rac1CampaignSaveBatcher? _rac1SaveBatcher;
    private string? _rac1SaveFailureReported;
    private string? _rac1CampaignRestoreWarning;
    private bool _rac1CampaignPersistenceInitialized;
    private Rac1CampaignSessionPersistence _rac1CampaignPersistence =
        Rac1CampaignSessionPersistence.Ephemeral();
    private Rac1CampaignRestoreKind _rac1CampaignRestoreKind =
        Rac1CampaignRestoreKind.DefaultedMissingState;

    private void EnsureRac1CampaignPersistenceInitialized()
    {
        if (_rac1CampaignPersistenceInitialized)
            return;

        if (_args.Rac1CampaignPersistenceRequested)
        {
            string savePath = string.IsNullOrWhiteSpace(_args.Rac1CampaignSavePath)
                ? ProjectSettings.GlobalizePath("user://rac1-campaign.json")
                : Path.GetFullPath(_args.Rac1CampaignSavePath);
            _rac1CampaignPersistence = Rac1CampaignSessionPersistence.Persistent(savePath);
        }
        else
        {
            _rac1CampaignPersistence = Rac1CampaignSessionPersistence.Ephemeral();
        }

        Rac1CampaignRestoreAttempt attempt = _rac1CampaignPersistence.RestoreWithRecovery();
        _rac1CampaignPersistence = attempt.Persistence;
        // A corrupt or unreadable save switches to an unsaved session. Never
        // construct a writer for a path recovery has declared unsafe.
        _rac1SaveBatcher = Rac1CampaignSaveWriterFactory.CreateIfEnabled(_rac1CampaignPersistence);
        _rac1CampaignRestoreWarning = attempt.Warning;
        if (_rac1CampaignRestoreWarning is not null)
            GD.PushWarning($"[rac1-campaign] {_rac1CampaignRestoreWarning}");
        Rac1CampaignRestoreResult restored = attempt.Restored;

        _rac1CampaignSession = new Rac1CampaignRuntimeSession(
            restored.Campaign,
            restored.Weapons,
            Rac1BuriedBoltProgress.Restore(restored.BuriedBoltHistory));
        _rac1CampaignRestoreKind = restored.Kind;
        _rac1BoltWallet = new Rac1BoltWallet(restored.Bolts);
        _rac1CollectedNovalisGoldBolts.UnionWith(restored.NovalisGoldBoltInstances);
        _rac1CampaignPersistenceInitialized = true;

        string persistenceMode = _rac1CampaignPersistence.Enabled
            ? $"persistent:{_rac1CampaignPersistence.SavePath}"
            : "ephemeral";
        GD.Print(
            $"[rac1-campaign] host state {restored.Kind} ({persistenceMode}): current={restored.Campaign.CurrentLevel}, " +
            $"admitted={restored.Campaign.AdmittedDestinationCount}, " +
            $"item10-owned={restored.Weapons.OwnsFirstRanged}, ammo={restored.Weapons.FirstRangedAmmo}");
    }

    /// <summary>
    /// Feed a recovered progression-dispatch value into the destination admission
    /// branch. No host location, objective, or completion trigger is inferred.
    /// </summary>
    public Rac1DestinationDiscoveryResult ApplyRac1CampaignProgressionEvent(
        int progressionEventId)
    {
        EnsureSourceLibraryInitialized();
        Rac1DestinationDiscoveryResult result =
            _rac1CampaignSession.Campaign.ApplyProgressionEvent(progressionEventId);

        if (result == Rac1DestinationDiscoveryResult.Discovered)
            PersistRac1CampaignState($"discovery event 0x{progressionEventId:x}");

        return result;
    }

    /// <summary>
    /// Apply an already-identified native item-acquisition event. This is the
    /// recovered common acquisition prefix only; vendor price/payment and the
    /// unresolved quick-select insertion predicate stay outside this boundary.
    /// </summary>
    public Rac1ItemAcquisitionResult ApplyRac1ItemAcquisition(int nativeItemId)
    {
        EnsureSourceLibraryInitialized();
        EnsureRac1CampaignPersistenceInitialized();

        Rac1ItemAcquisitionResult result =
            _rac1CampaignSession.Weapons.AcquireNativeItem(nativeItemId);
        if (_player is { UseRac1Gameplay: true } player)
            player.Rac1MovementEquipment = Rac1MovementCapabilities.FromInventory(_rac1Weapons);
        PersistRac1CampaignState($"item acquisition {nativeItemId}");

        if (nativeItemId == (int)Rac1WeaponId.FirstRanged && result.IsFirstAcquisition)
        {
            RefreshRac1HudState(Rac1HudProjection.BombGloveAcquiredFeedback(result.AmmoGranted));
        }

        return result;
    }

    /// <summary>
    /// Apply an already-recovered native ammo amount through the generic retail
    /// clamp helper. Selection of which pickup refills which item, and by how
    /// much, remains a separate class-511 recovery problem.
    /// </summary>
    public Rac1AmmoGrantResult ApplyRac1AmmoGrant(int nativeItemId, int amount)
    {
        EnsureSourceLibraryInitialized();
        EnsureRac1CampaignPersistenceInitialized();

        Rac1AmmoGrantResult result =
            _rac1CampaignSession.Weapons.GrantAmmoClamped(nativeItemId, amount);
        if (result.Changed)
        {
            PersistRac1CampaignState($"ammo grant item {nativeItemId}");
            if (nativeItemId == (int)Rac1WeaponId.FirstRanged)
                RefreshRac1HudState(Rac1HudProjection.BombGloveAmmoPickupFeedback(result.AmmoGranted));
        }

        return result;
    }

    private Rac1CampaignSaveEnvelope CaptureRac1CampaignSnapshot() =>
        Rac1CampaignSavePolicy.Capture(
            _rac1CampaignSession.Campaign, _rac1CampaignSession.Weapons, _rac1BoltWallet,
            _rac1CollectedNovalisGoldBolts, _rac1CampaignSession.BuriedBolts);

    private void TickRac1CampaignSaves(double delta)
    {
        if (_rac1SaveBatcher is null || FusionTestActive) return;
        _rac1SaveBatcher.Tick(delta, CaptureRac1CampaignSnapshot);
        string? failure = _rac1SaveBatcher.Failure?.Message;
        if (failure is not null && failure != _rac1SaveFailureReported)
            GD.PushWarning($"[rac1-campaign] save failed; latest progress retained for retry: {failure}");
        _rac1SaveFailureReported = failure;
    }

    private void FlushRac1CampaignSaves(string boundary)
    {
        if (_rac1SaveBatcher is null || _rac1WeaponPreviewInventory is not null || FusionTestActive) return;
        if (!_rac1SaveBatcher.FlushNow(CaptureRac1CampaignSnapshot, out Exception? error))
            GD.PushWarning($"[rac1-campaign] {boundary} save failed; progress remains unsaved: {error!.Message}");
        else
            GD.Print($"[rac1-campaign] {boundary} save flush complete");
    }

    private void PersistRac1CampaignState(string reason)
    {
        // Preview equipment and its ammo are session-only. Never export this
        // development loadout through ordinary campaign persistence.
        if (_rac1WeaponPreviewInventory is not null || FusionTestActive) return;
        if (!_rac1CampaignPersistenceInitialized)
            EnsureRac1CampaignPersistenceInitialized();

        if (_rac1SaveBatcher is null) return;
        bool wasDirty = _rac1SaveBatcher.Dirty;
        _rac1SaveBatcher.MarkDirty();
        if (reason == "completed campaign travel")
            FlushRac1CampaignSaves("campaign travel");
        else if (!wasDirty)
            GD.Print($"[rac1-campaign] save queued ({reason})");
    }
}
