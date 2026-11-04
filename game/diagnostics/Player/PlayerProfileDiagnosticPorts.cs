using System.Collections.Immutable;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

public sealed record DiagnosticSharedProfileState(bool Ordinary, bool PathAvailable, bool WriteBlocked, bool EquipmentPreparing,
    string? SavedDestination, string? Destination, int SavedSources, int LancerAmmo, int CannonAmmo, int NativeNanotech);
public interface IDiagnosticSharedProfileFixture
{
    DiagnosticSharedProfileState Read();
    void StageSavedPlayer();
}
public interface IDiagnosticPlayerEquipmentFixture
{
    ImmutableArray<EquipmentCatalogEntry> Read();
    EquipmentChangeResult Select(string catalogId);
    EquipmentChangeResult Assign(int slot, string? catalogId);
    EquipmentChangeResult Unequip();
    EquipmentChangeResult EnableGadget(string catalogId, bool enabled);
    bool HeliPackEnabled { get; }
}
public sealed record DiagnosticPlayerInterfaceSnapshot(bool HudVisible, HudHealth? Health, HudCurrency? Bolts,
    ImmutableArray<SourceWeaponKey?> WheelEntries, bool MetalDetectorEquipped);
public interface IDiagnosticPlayerInterfaceFixture
{
    DiagnosticPlayerInterfaceSnapshot Read();
    bool OpenQuickSelect();
    void CloseQuickSelect();
    bool ToggleMetalDetector();
}
public interface IDiagnosticAcceptanceReporter
{
    void Assert(bool condition, string id, string detail);
    void Write(string phase);
    void Fail(string phase, Exception error, string reason);
}
