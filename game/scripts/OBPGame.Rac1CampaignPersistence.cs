using Godot;
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
            _rac1CampaignSession.Weapons);
        _rac1CampaignRestoreKind = restored.Kind;
        _rac1CampaignPersistenceInitialized = true;

        GD.Print(
            $"[rac1-campaign] host state {restored.Kind}: current={restored.Campaign.CurrentLevel}, " +
            $"admitted={restored.Campaign.AdmittedDestinationCount}");
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

    private void PersistRac1CampaignState(string reason)
    {
        if (!_rac1CampaignPersistenceInitialized)
            EnsureRac1CampaignPersistenceInitialized();

        Rac1CampaignSaveFile.Save(
            _rac1CampaignSavePath,
            _rac1CampaignSession.Campaign);
        GD.Print(
            $"[rac1-campaign] persisted {reason}: current={_rac1CampaignSession.Campaign.CurrentLevel}, " +
            $"admitted={_rac1CampaignSession.Campaign.AdmittedDestinationCount}");
    }
}
