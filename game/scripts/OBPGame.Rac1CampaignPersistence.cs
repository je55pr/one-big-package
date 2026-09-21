using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;

namespace OneBigPackage;

/// <summary>
/// OBP host persistence glue for recovered R&C1 campaign fields. File format and
/// save timing are host policy, not claims about the retail memory-card writer.
/// </summary>
public partial class OBPGame
{
    private bool _rac1CampaignPersistenceInitialized;
    private string _rac1CampaignSavePath = string.Empty;
    private Rac1CampaignRestoreKind _rac1CampaignRestoreKind =
        Rac1CampaignRestoreKind.DefaultedMissingState;

    private void EnsureRac1CampaignPersistenceInitialized()
    {
        if (_rac1CampaignPersistenceInitialized)
            return;

        _rac1CampaignSavePath = string.IsNullOrWhiteSpace(_args.Rac1CampaignSavePath)
            ? ProjectSettings.GlobalizePath("user://rac1-campaign.json")
            : Path.GetFullPath(_args.Rac1CampaignSavePath);

        Rac1CampaignRestoreResult restored =
            Rac1CampaignSaveFile.LoadOrDefault(_rac1CampaignSavePath);

        _rac1CampaignSession = new Rac1CampaignRuntimeSession(
            restored.Campaign,
            restored.Weapons);
        _rac1CampaignRestoreKind = restored.Kind;
        _rac1CampaignPersistenceInitialized = true;

        GD.Print(
            $"[rac1-campaign] host state {restored.Kind}: current={restored.Campaign.CurrentLevel}, " +
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

    private void PersistRac1CampaignState(string reason)
    {
        if (!_rac1CampaignPersistenceInitialized)
            EnsureRac1CampaignPersistenceInitialized();

        Rac1CampaignSaveFile.Save(
            _rac1CampaignSavePath,
            _rac1CampaignSession.Campaign,
            _rac1CampaignSession.Weapons);
        GD.Print(
            $"[rac1-campaign] persisted {reason}: current={_rac1CampaignSession.Campaign.CurrentLevel}, " +
            $"admitted={_rac1CampaignSession.Campaign.AdmittedDestinationCount}, " +
            $"item10-owned={_rac1CampaignSession.Weapons.OwnsFirstRanged}, " +
            $"ammo={_rac1CampaignSession.Weapons.FirstRangedAmmo}");
    }
}
