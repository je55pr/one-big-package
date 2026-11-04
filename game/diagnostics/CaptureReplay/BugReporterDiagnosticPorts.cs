using System.Collections.Immutable;
using System.Numerics;

namespace OBP.Game.Diagnostics;

public sealed record BugReporterUiDiagnostic(bool ModalOpen, bool FormOpen, bool InboxOpen, bool Busy, string Status, string? FocusName, string? FocusText, string Description);
public sealed record BugReporterWorldDiagnostic(Vector3 Position, int Bolts, string InventoryJson, bool QuickSelectOpen, bool PauseOpen, bool TreePaused);
public sealed record BugReporterReportDiagnostic(string Id, string Description);
public sealed record BugReporterArchiveDiagnostic(ImmutableArray<BugReporterReportDiagnostic> Reports, int Recordings, int Exports,
    bool FormKeysExcluded, bool ReplaysMatchedSameBuild);
public interface IBugReporterDiagnosticFixture
{
    BugReporterUiDiagnostic ReadUi();
    BugReporterWorldDiagnostic ReadWorld();
    BugReporterArchiveDiagnostic ReadArchive();
    void FocusDescription();
    void FocusButton(string label);
    Vector2 ReportButtonCentre { get; }
    Task CaptureAsync(string name, DiagnosticRunContext run);
}
