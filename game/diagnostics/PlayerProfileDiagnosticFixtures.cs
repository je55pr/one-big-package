using System.Collections.Immutable;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

internal sealed record DiagnosticProfileStorageState(bool Ordinary, bool PathAvailable, bool WriteBlocked,
    bool EquipmentPreparing, string? SavedDestination, string? Destination, int Sources);
internal sealed class DiagnosticSharedProfileFixture(Func<DiagnosticProfileStorageState> storage,
    Func<PlayerSessionNativeState?> native) : IDiagnosticSharedProfileFixture
{
    private PlayerSessionNativeState Native => native() ?? throw new DiagnosticUnavailableException("No native player state for the isolated profile fixture.");
    public DiagnosticSharedProfileState Read()
    {
        var profile = storage(); var player = Native;
        return new(profile.Ordinary, profile.PathAvailable, profile.WriteBlocked, profile.EquipmentPreparing, profile.SavedDestination,
            profile.Destination, profile.Sources, player.Weapons.LancerSession.Ammo, player.Weapons.CannonSession.Ammo, player.Health.Probe().Nanotech);
    }
    public void StageSavedPlayer()
    {
        var player = Native;
        player.Weapons.LancerSession.ResetWorld(17); player.Weapons.CannonSession.Reset(9);
        player.Health.ApplyAdmittedDamageUnits(1); player.Wallet.Credit(42);
    }
}

internal sealed class DiagnosticPlayerEquipmentFixture(Func<IReadOnlyList<EquipmentCatalogEntry>> read,
    Func<string, EquipmentChangeResult> select, Func<int, string?, EquipmentChangeResult> assign,
    Func<EquipmentChangeResult> unequip, Func<string, bool, EquipmentChangeResult> enable, Func<bool> heliPack) : IDiagnosticPlayerEquipmentFixture
{
    public ImmutableArray<EquipmentCatalogEntry> Read() => [.. read()];
    public EquipmentChangeResult Select(string catalogId) => select(catalogId);
    public EquipmentChangeResult Assign(int slot, string? catalogId) => assign(slot, catalogId);
    public EquipmentChangeResult Unequip() => unequip();
    public EquipmentChangeResult EnableGadget(string catalogId, bool enabled) => enable(catalogId, enabled);
    public bool HeliPackEnabled => heliPack();
}

internal sealed class DiagnosticPlayerInterfaceFixture(Func<DiagnosticPlayerInterfaceSnapshot> read, Func<bool> open,
    Action close, Func<bool> detector) : IDiagnosticPlayerInterfaceFixture
{
    public DiagnosticPlayerInterfaceSnapshot Read() => read();
    public bool OpenQuickSelect() => open();
    public void CloseQuickSelect() => close();
    public bool ToggleMetalDetector() => detector();
}

/// <summary>Application-owned acceptance evidence; no diagnostic checks or collections live in the production root.</summary>
internal sealed class OwnedDiagnosticAcceptanceReporter(Func<string?> destination, Func<string?> path,
    Func<AcceptanceSnapshot> snapshot, Action<string> error, Action<string, int> quit) : IDiagnosticAcceptanceReporter
{
    private readonly AcceptanceEvidence _evidence = new();
    public void Assert(bool condition, string id, string detail) => _evidence.Assert(condition, id, destination(), detail);
    public void Write(string phase) => Write(phase, null);
    private void Write(string phase, Exception? failure)
    {
        if (path() is { } target) _evidence.Write(target, phase, snapshot(), failure);
    }
    public void Fail(string phase, Exception failure, string reason) => AcceptanceEvidence.FinishFailure(failure, reason, () => Write(phase, failure), error, quit);
}
