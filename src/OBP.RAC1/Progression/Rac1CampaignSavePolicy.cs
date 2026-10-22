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
/// transient session state. Level checkpoint state is deliberately absent: its
/// retail serialization/reload lifetime is not recovered.
/// </summary>
public sealed record Rac1CampaignSaveEnvelope(
    int SchemaVersion,
    Rac1CampaignPersistentState Campaign,
    Rac1WeaponPersistentState? Weapons = null,
    int Bolts = 0)
{
    // OBP host-level identity until the original retail Gold Bolt save field is recovered.
    public int[] NovalisGoldBoltInstances { get; init; } = [];
}

public sealed record Rac1CampaignRestoreResult(
    Rac1CampaignState Campaign,
    Rac1WeaponInventory Weapons,
    Rac1CampaignRestoreKind Kind)
{
    public int Bolts { get; init; }
    public int[] NovalisGoldBoltInstances { get; init; } = [];
}

/// <summary>
/// Persistence policy for engine-neutral R&C1 campaign state. Schema 2 remains
/// the current development-save format when checkpoint sessions are introduced
/// because those sessions are explicitly level-local and never serialized.
/// </summary>
public static class Rac1CampaignSavePolicy
{
    public const int CurrentSchemaVersion = 2;

    public static Rac1CampaignSaveEnvelope Capture(
        Rac1CampaignState campaign,
        Rac1WeaponInventory weapons,
        Rac1BoltWallet? wallet = null,
        IReadOnlyCollection<int>? novalisGoldBoltInstances = null)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(weapons);
        return new Rac1CampaignSaveEnvelope(
            CurrentSchemaVersion,
            campaign.CapturePersistentState(),
            weapons.CapturePersistentState(), wallet?.Balance ?? 0)
        {
            NovalisGoldBoltInstances = ValidateNovalisGoldBolts(novalisGoldBoltInstances),
        };
    }

    /// <summary>
    /// Missing host state uses the opening campaign and Bomb Glove loadout.
    /// Schema 1 host saves predate weapon persistence, so their campaign survives
    /// while weapon state receives the same explicit OBP fallback.
    /// </summary>
    public static Rac1CampaignRestoreResult RestoreOrDefault(Rac1CampaignSaveEnvelope? save)
    {
        if (save is null)
            return new Rac1CampaignRestoreResult(
                new Rac1CampaignState(),
                Rac1WeaponInventory.CreateOpeningLoadout(),
                Rac1CampaignRestoreKind.DefaultedMissingState);

        ArgumentNullException.ThrowIfNull(save.Campaign);
        if (save.Bolts < 0) throw new InvalidDataException("R&C1 saved bolt balance is negative.");
        if (save.SchemaVersion == 1)
            return new Rac1CampaignRestoreResult(
                Rac1CampaignState.RestorePersistentState(save.Campaign),
                Rac1WeaponInventory.CreateOpeningLoadout(),
                Rac1CampaignRestoreKind.MigratedCampaignOnlySchema1);

        if (save.SchemaVersion != CurrentSchemaVersion)
            throw new NotSupportedException(
                $"Unsupported R&C1 campaign save schema {save.SchemaVersion}; expected {CurrentSchemaVersion}.");

        if (save.Weapons is null)
            throw new InvalidDataException("R&C1 campaign save is missing its recovered weapon inventory payload.");

        return new Rac1CampaignRestoreResult(
            Rac1CampaignState.RestorePersistentState(save.Campaign),
            RestoreWeapons(save.Weapons),
            Rac1CampaignRestoreKind.RestoredCurrentSchema)
        {
            Bolts = save.Bolts,
            NovalisGoldBoltInstances = ValidateNovalisGoldBolts(save.NovalisGoldBoltInstances),
        };
    }

    private static Rac1WeaponInventory RestoreWeapons(Rac1WeaponPersistentState state)
    {
        var weapons = new Rac1WeaponInventory(state);
        // The held item is transient. Prefer the opening equipment when owned,
        // preserving the saved ammo and ownership bytes without granting a refill.
        weapons.TryEquip(Rac1WeaponId.FirstRanged);
        return weapons;
    }

    private static int[] ValidateNovalisGoldBolts(IEnumerable<int>? instances)
    {
        int[] sorted = (instances ?? []).Order().ToArray();
        if (sorted.Length > Rac1GoldBoltSession.ExpectedNovalisBolts ||
            sorted.Any(id => id < 0) || sorted.Distinct().Count() != sorted.Length)
            throw new InvalidDataException("Invalid persisted Novalis Gold Bolt identities.");
        return sorted;
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
            Rac1WeaponInventory.CreateOpeningLoadout().CapturePersistentState());
    }

    public static Rac1CampaignRestoreResult RestoreLegacyUnversioned(
        Rac1CampaignPersistentState legacy)
    {
        Rac1CampaignSaveEnvelope migrated = MigrateLegacyUnversioned(legacy);
        return new Rac1CampaignRestoreResult(
            Rac1CampaignState.RestorePersistentState(migrated.Campaign),
            RestoreWeapons(migrated.Weapons!),
            Rac1CampaignRestoreKind.MigratedLegacyUnversioned);
    }
}
