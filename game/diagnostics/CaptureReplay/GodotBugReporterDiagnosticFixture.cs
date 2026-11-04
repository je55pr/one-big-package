using System.Text.Json;
using Godot;
using OBP.Runtime.Diagnostics;

namespace OBP.Game.Diagnostics;

internal sealed class GodotBugReporterDiagnosticFixture(Node owner, Func<IBugReportUiDiagnostics?> coordinator, Func<bool> modal,
    Func<BugReporterWorldDiagnostic> world,
    Func<Node?> recorder) : IBugReporterDiagnosticFixture
{
    private IBugReportUiDiagnostics Reports => coordinator() ?? throw new DiagnosticUnavailableException("The report capture is unavailable.");
    public BugReporterUiDiagnostic ReadUi()
    {
        var view = Reports.ReadUi();
        return new(modal(), view.FormOpen, view.InboxOpen, view.Busy, view.Status, view.FocusName, view.FocusText, view.Description);
    }
    public BugReporterWorldDiagnostic ReadWorld() => world();
    public void FocusDescription() => Reports.FocusDescription();
    public void FocusButton(string label) => Reports.FocusButton(label);
    public System.Numerics.Vector2 ReportButtonCentre
    {
        get
        {
            var capture = recorder() ?? throw new DiagnosticUnavailableException("The capture control is unavailable.");
            var point = ((Button)capture.FindChild("BugReportControl", true, false)).GetGlobalRect().GetCenter(); return new(point.X, point.Y);
        }
    }
    public BugReporterArchiveDiagnostic ReadArchive()
    {
        var snapshot = Reports.ReadReports(); bool excluded = true;
        if (snapshot.Reports.Length > 0)
        {
            using var reader = new PlaytestRunReader(snapshot.RecordingPath);
            while (reader.ReadNextStep() is { } step)
                if (step.Events.Any(item => item.Kind == PlaytestInputKind.Key && item.Code is (int)Key.W or (int)Key.Q)) excluded = false;
        }
        var files = snapshot.ReplayResults;
        bool matched = files.Length == 2;
        foreach (string file in files)
        {
            using var result = JsonDocument.Parse(File.ReadAllText(file));
            matched &= result.RootElement.GetProperty("Result").GetString() == "matched" && !result.RootElement.GetProperty("BuildChanged").GetBoolean();
        }
        return new([.. snapshot.Reports.Select(report => new BugReporterReportDiagnostic(report.Id.ToString("N"), report.Description))],
            snapshot.Recordings, snapshot.Exports, excluded, matched);
    }
    public async Task CaptureAsync(string name, DiagnosticRunContext run)
    {
        if (DisplayServer.GetName() == "headless") return;
        await run.WaitAsync(DiagnosticFramePhase.PostDraw);
        using var image = owner.GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(Reports.StorageRoot, name + ".png")) != Error.Ok) throw new IOException("Report UI capture failed.");
    }
}
