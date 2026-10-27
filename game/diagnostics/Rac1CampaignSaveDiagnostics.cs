using OBP.RAC1.Progression;

namespace OBP.Game.Diagnostics;

/// <summary>Read-only campaign-file witnesses; paths and file bytes stay inside this fixture.</summary>
public sealed class Rac1CampaignSaveDiagnostics(Func<Rac1CampaignSaveProvenance> provenance,
    Func<string?> configuredPath, Func<string?> persistencePath) : IRac1CampaignDiagnosticSave
{
    public Rac1CampaignSaveProvenance ReadProvenance() => provenance();
    public Rac1CampaignNativeSaveDiagnostic ReadPersisted()
    {
        string path = persistencePath() ?? throw new DiagnosticUnavailableException("Campaign smoke persistence path was not initialized.");
        return Freeze(Rac1CampaignSaveFile.LoadOrDefault(path));
    }
    public IRac1CampaignFileWitness CaptureFile()
    {
        if (!provenance().ExplicitPath) throw new DiagnosticUnavailableException("An explicit isolated campaign save is required.");
        string path = configuredPath() ?? throw new DiagnosticUnavailableException("The isolated campaign save is unavailable.");
        return new FileWitness(Path.GetFullPath(path));
    }
    public static Rac1CampaignNativeSaveDiagnostic Freeze(Rac1CampaignRestoreResult restored)
    {
        var campaign = restored.Campaign.CapturePersistentState(); var weapons = restored.Weapons.CapturePersistentState();
        return new(restored.Kind, campaign.CurrentLevel, restored.Campaign.AdmittedDestinationCount, [.. campaign.VisitedPlanets], [.. campaign.GalacticMap], [.. campaign.LevelStates],
            [.. weapons.Ammo], [.. weapons.Items], [.. weapons.UnlockFlags], [.. weapons.QuickSelect], weapons.LastEquippedGadget,
            [.. weapons.EquippedGadgets], restored.Bolts, [.. restored.NovalisGoldBoltInstances], [.. restored.BuriedBoltHistory]);
    }
    private sealed class FileWitness(string path) : IRac1CampaignFileWitness
    {
        private readonly byte[] _original = File.ReadAllBytes(path);
        public bool Unchanged => File.Exists(path) && _original.AsSpan().SequenceEqual(File.ReadAllBytes(path));
        public bool TemporaryFileExists => File.Exists(path + ".tmp");
    }
}
