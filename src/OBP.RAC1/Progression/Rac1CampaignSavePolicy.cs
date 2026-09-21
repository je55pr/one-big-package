using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Progression;

public enum Rac1CampaignRestoreKind
{
    DefaultedMissingState,
    RestoredCurrentSchema,
    MigratedCampaignOnlySchema1,
    MigratedLegacyUnversioned,
}

/// <summary>
/// Versioned host persistence envelope for recovered R&C1 campaign and weapon
/// inventory blocks. The envelope is an OBP container only; both payloads keep
/// their native-shaped source-specific layouts and the currently held item stays
/// transient session state.
/// </summary>
public sealed record Rac1CampaignSaveEnvelope(
    int SchemaVersion,
    Rac1CampaignPersistentState Campaign,
    Rac1WeaponPersistentState? Weapons = null);

public sealed record Rac1CampaignRestoreResult(
    Rac1CampaignState Campaign,
    Rac1WeaponInventory Weapons,
    Rac1CampaignRestoreKind Kind);

/// <summary>
/// Persistence policy for engine-neutral R&C1 campaign state.
/// </summary>
public static class Rac1CampaignSavePolicy
{
    public const int CurrentSchemaVersion = 2;

    public static Rac1CampaignSaveEnvelope Capture(
        Rac1CampaignState campaign,
        Rac1WeaponInventory weapons)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(weapons);
        return new Rac1CampaignSaveEnvelope(
            CurrentSchemaVersion,
            campaign.CapturePersistentState(),
            weapons.CapturePersistentState());
    }

    /// <summary>
    /// Missing host state defaults both recovered owners to the retained opening
    /// Veldin witness. Schema 1 host saves predate weapon persistence, so their
    /// campaign survives while weapon state receives the same explicit fallback.
    /// </summary>
    public static Rac1CampaignRestoreResult RestoreOrDefault(Rac1CampaignSaveEnvelope? save)
    {
        if (save is null)
            return new Rac1CampaignRestoreResult(
                new Rac1CampaignState(),
                Rac1WeaponInventory.CreateOpeningVeldinWitness(),
                Rac1CampaignRestoreKind.DefaultedMissingState);

        ArgumentNullException.ThrowIfNull(save.Campaign);
        if (save.SchemaVersion == 1)
            return new Rac1CampaignRestoreResult(
                Rac1CampaignState.RestorePersistentState(save.Campaign),
                Rac1WeaponInventory.CreateOpeningVeldinWitness(),
                Rac1CampaignRestoreKind.MigratedCampaignOnlySchema1);

        if (save.SchemaVersion != CurrentSchemaVersion)
            throw new NotSupportedException(
                $"Unsupported R&C1 campaign save schema {save.SchemaVersion}; expected {CurrentSchemaVersion}.");

        if (save.Weapons is null)
            throw new InvalidDataException("R&C1 campaign save is missing its recovered weapon inventory payload.");

        return new Rac1CampaignRestoreResult(
            Rac1CampaignState.RestorePersistentState(save.Campaign),
            new Rac1WeaponInventory(save.Weapons),
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
        return new Rac1CampaignSaveEnvelope(
            CurrentSchemaVersion,
            legacy,
            Rac1WeaponInventory.CreateOpeningVeldinWitness().CapturePersistentState());
    }

    public static Rac1CampaignRestoreResult RestoreLegacyUnversioned(
        Rac1CampaignPersistentState legacy)
    {
        Rac1CampaignSaveEnvelope migrated = MigrateLegacyUnversioned(legacy);
        return new Rac1CampaignRestoreResult(
            Rac1CampaignState.RestorePersistentState(migrated.Campaign),
            new Rac1WeaponInventory(migrated.Weapons!),
            Rac1CampaignRestoreKind.MigratedLegacyUnversioned);
    }
}
