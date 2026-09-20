namespace OBP.RAC1.Progression;

public enum Rac1CampaignRestoreKind
{
    DefaultedMissingState,
    RestoredCurrentSchema,
    MigratedLegacyUnversioned,
}

/// <summary>
/// Versioned host persistence envelope for the recovered R&C1 campaign blocks.
/// The payload remains the single <see cref="Rac1CampaignPersistentState"/> source
/// of truth; the envelope only establishes an explicit migration boundary.
/// </summary>
public sealed record Rac1CampaignSaveEnvelope(
    int SchemaVersion,
    Rac1CampaignPersistentState Campaign);

public sealed record Rac1CampaignRestoreResult(
    Rac1CampaignState Campaign,
    Rac1CampaignRestoreKind Kind);

/// <summary>
/// Persistence policy for engine-neutral R&C1 campaign state.
/// </summary>
public static class Rac1CampaignSavePolicy
{
    public const int CurrentSchemaVersion = 1;
    public static Rac1CampaignSaveEnvelope Capture(Rac1CampaignState campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        return new Rac1CampaignSaveEnvelope(
            CurrentSchemaVersion,
            campaign.CapturePersistentState());
    }

    /// <summary>
    /// Missing campaign data means a save predates this contract. Default only
    /// that missing field to the recovered retail opening state.
    /// </summary>
    public static Rac1CampaignRestoreResult RestoreOrDefault(Rac1CampaignSaveEnvelope? save)
    {
        if (save is null)
            return new Rac1CampaignRestoreResult(
                new Rac1CampaignState(),
                Rac1CampaignRestoreKind.DefaultedMissingState);

        if (save.SchemaVersion != CurrentSchemaVersion)
            throw new NotSupportedException(
                $"Unsupported R&C1 campaign save schema {save.SchemaVersion}; expected {CurrentSchemaVersion}.");

        ArgumentNullException.ThrowIfNull(save.Campaign);
        return new Rac1CampaignRestoreResult(
            Rac1CampaignState.RestorePersistentState(save.Campaign),
            Rac1CampaignRestoreKind.RestoredCurrentSchema);
    }

    /// <summary>
    /// Migrates snapshots captured before the envelope existed. The native-shaped
    /// payload is already schema v1, so migration adds version metadata only.
    /// </summary>
    public static Rac1CampaignSaveEnvelope MigrateLegacyUnversioned(
        Rac1CampaignPersistentState legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        return new Rac1CampaignSaveEnvelope(CurrentSchemaVersion, legacy);
    }

    public static Rac1CampaignRestoreResult RestoreLegacyUnversioned(
        Rac1CampaignPersistentState legacy)
    {
        Rac1CampaignSaveEnvelope migrated = MigrateLegacyUnversioned(legacy);
        return new Rac1CampaignRestoreResult(
            Rac1CampaignState.RestorePersistentState(migrated.Campaign),
            Rac1CampaignRestoreKind.MigratedLegacyUnversioned);
    }
}
