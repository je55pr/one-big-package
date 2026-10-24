namespace OBP.RAC1.Progression;

/// <summary>
/// Bind a writer only to a path that survived campaign-save recovery.
/// A damaged or inaccessible save must remain untouched for the entire session.
/// </summary>
public static class Rac1CampaignSaveWriterFactory
{
    public static Rac1CampaignSaveBatcher? CreateIfEnabled(Rac1CampaignSessionPersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        if (!persistence.Enabled)
            return null;

        string path = persistence.SavePath!;
        return new Rac1CampaignSaveBatcher(
            snapshot => Rac1CampaignSaveFile.SaveEnvelope(path, snapshot));
    }
}
