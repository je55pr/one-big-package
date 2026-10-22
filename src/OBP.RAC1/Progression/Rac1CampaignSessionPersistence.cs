using System.Text.Json;
using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Progression;

/// <summary>
/// OBP host-session policy for optional campaign persistence. Ordinary local
/// sessions are intentionally ephemeral; callers must explicitly construct a
/// persistent session to read or write host save state.
/// </summary>
public sealed record Rac1CampaignRestoreAttempt(
    Rac1CampaignSessionPersistence Persistence,
    Rac1CampaignRestoreResult Restored,
    string? Warning);

public sealed class Rac1CampaignSessionPersistence
{
    private readonly string? _savePath;

    private Rac1CampaignSessionPersistence(string? savePath)
    {
        _savePath = savePath;
    }

    public bool Enabled => _savePath is not null;
    public string? SavePath => _savePath;

    public static Rac1CampaignSessionPersistence Ephemeral() => new(null);

    public static Rac1CampaignSessionPersistence Persistent(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Campaign save path is required.", nameof(path));

        return new Rac1CampaignSessionPersistence(Path.GetFullPath(path));
    }
    public Rac1CampaignRestoreResult Restore()
    {
        return Enabled
            ? Rac1CampaignSaveFile.LoadOrDefault(_savePath!)
            : Rac1CampaignSavePolicy.RestoreOrDefault(null);
    }

    /// <summary>
    /// Recover from host-save corruption without replacing the original file.
    /// The fallback cannot write to the damaged path during this session.
    /// </summary>
    public Rac1CampaignRestoreAttempt RestoreWithRecovery()
    {
        try
        {
            return new Rac1CampaignRestoreAttempt(this, Restore(), null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException
            or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new Rac1CampaignRestoreAttempt(
                Ephemeral(),
                Rac1CampaignSavePolicy.RestoreOrDefault(null),
                $"R&C1 campaign save could not be restored ({ex.GetType().Name}: {ex.Message}). " +
                "The original file is unchanged. This session uses fresh, unsaved progress.");
        }
    }

    public bool PersistIfEnabled(
        Rac1CampaignState campaign,
        Rac1WeaponInventory weapons,
        Rac1BoltWallet? wallet = null,
        IReadOnlyCollection<int>? novalisGoldBoltInstances = null,
        Rac1BuriedBoltProgress? buriedBoltHistory = null)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(weapons);

        if (!Enabled)
            return false;

        Rac1CampaignSaveFile.Save(_savePath!, campaign, weapons, wallet, novalisGoldBoltInstances, buriedBoltHistory);
        return true;
    }
}
