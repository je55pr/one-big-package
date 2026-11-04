using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OBP.Game.Diagnostics;

public sealed record Rac1CampaignDiagnosticState(int CurrentLevel, int SelectedDestination,
    ImmutableArray<int> Admissions, ImmutableArray<Rac1LevelVisitState> LevelStates, bool TransitionActive)
{
    public bool NovalisAvailable { get; init; }
    public bool AridiaAvailable { get; init; }
}
public sealed record Rac1CampaignCheckpointDiagnostic(int NativeLevel, RuntimeSpawn AuthoredClass0,
    RuntimeSpawn? WorldAuthoredClass0, bool Active, Rac1RestartPlacementKind RestartKind);
public sealed record Rac1CampaignDeathDiagnostic(int Nanotech, bool Dead, bool RecoveredEnvironmentalRespawn, long EnvironmentalDeathGeneration);
public sealed record Rac1CampaignSaveProvenance(ObpSourceGame Source, string? Build, bool ExplicitPath,
    bool SharedProfileActive, bool PersistenceEnabled, bool WriterActive, bool RecoveryWarning, Rac1CampaignRestoreKind RestoreKind);
public sealed record Rac1CampaignNativeSaveDiagnostic(Rac1CampaignRestoreKind Kind, int CurrentLevel, int AdmittedDestinationCount,
    ImmutableArray<byte> Visited, ImmutableArray<int> GalacticMap, ImmutableArray<Rac1LevelVisitState> LevelStates,
    ImmutableArray<int> Ammo, ImmutableArray<byte> Items, ImmutableArray<byte> UnlockFlags,
    ImmutableArray<int> QuickSelect, int LastEquippedGadget, ImmutableArray<int> EquippedGadgets,
    int Bolts, ImmutableArray<int> NovalisGoldBolts, ImmutableArray<byte> BuriedBoltHistory)
{
    public ImmutableArray<int> Admissions => [.. GalacticMap.Take(AdmittedDestinationCount)];
}
public interface IRac1CampaignDiagnosticFacts
{
    Rac1CampaignDiagnosticState ReadCampaign();
    Rac1CampaignDeathDiagnostic ReadDeath();
    bool PurchasedPlumberRetired { get; }
}
public interface IRac1CampaignDiagnosticActions
{
    Rac1DestinationDiscoveryResult Discover(int nativeEvent);
    Rac1PlanetTravelStartResult Travel(int nativeDestination);
    void OpenPlanetMap();
    void ReloadLevel(int nativeLevel);
    void TickSaves(double delta);
    void FlushSaves(string boundary);
}
public interface IRac1CampaignCheckpointFixture
{
    Rac1CampaignCheckpointDiagnostic Read();
    Vector3 Project(RuntimeSpawn placement);
    bool Activate(Rac1CheckpointActivation activation);
    Rac1CampaignDeathDiagnostic InjectEnvironmentalDeath();
    bool CompleteEnvironmentalRestart();
}
public interface IRac1CampaignFileWitness
{
    bool Unchanged { get; }
    bool TemporaryFileExists { get; }
}
public interface IRac1CampaignDiagnosticSave
{
    Rac1CampaignSaveProvenance ReadProvenance();
    Rac1CampaignNativeSaveDiagnostic ReadPersisted();
    IRac1CampaignFileWitness CaptureFile();
}
public interface IRac1MissionDiagnosticHint : IDisposable { void SetText(string instruction); }
public interface IRac1MissionDiagnosticUi
{
    bool Headless { get; }
    IRac1MissionDiagnosticHint ShowHint();
}
public sealed record Rac1CampaignScenarioPorts(IRac1CampaignDiagnosticFacts Facts, IRac1CampaignDiagnosticActions Actions,
    IRac1CampaignCheckpointFixture Checkpoints, IRac1CampaignDiagnosticSave Save, IRac1MissionDiagnosticUi Ui);
