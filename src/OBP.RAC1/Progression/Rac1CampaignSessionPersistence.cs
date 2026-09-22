using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Progression;

/// <summary>
/// OBP host-session policy for optional campaign persistence. Ordinary local
/// sessions are intentionally ephemeral; callers must explicitly construct a
/// persistent session to read or write host save state.
/// </summary>
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

    public bool PersistIfEnabled(
        Rac1CampaignState campaign,
        Rac1WeaponInventory weapons)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(weapons);

        if (!Enabled)
            return false;

        Rac1CampaignSaveFile.Save(_savePath!, campaign, weapons);
        return true;
    }
}
