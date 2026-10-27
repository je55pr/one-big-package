using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using OBP.Runtime.Diagnostics;

namespace OBP.Game.Diagnostics;

internal sealed class GodotBugReporterDiagnosticFixture(Node owner, Func<BugReportUi?> ui, Func<bool> modal,
    Func<BugReporterWorldDiagnostic> world, Func<BugReportInbox?> inbox, Func<Guid> session,
    Func<Node?> recorder) : IBugReporterDiagnosticFixture
{
    private BugReportUi Ui => ui() is { } value && GodotObject.IsInstanceValid(value) ? value : throw new DiagnosticUnavailableException("The report UI is unavailable.");
    private BugReportInbox Inbox => inbox() ?? throw new DiagnosticUnavailableException("The report inbox is unavailable.");
    public BugReporterUiDiagnostic ReadUi()
    {
        var panel = ui(); bool valid = GodotObject.IsInstanceValid(panel); var focus = owner.GetViewport().GuiGetFocusOwner();
        return new(modal(), valid && !panel!.InboxMode, valid && panel!.InboxMode, valid && panel!.Busy,
            valid ? panel!.Status : "", focus?.Name.ToString(), (focus as Button)?.Text,
            valid && panel!.FindChild("ReportDescription", true, false) is LineEdit description ? description.Text : "");
    }
    public BugReporterWorldDiagnostic ReadWorld() => world();
    public void FocusDescription() => ((LineEdit)Ui.FindChild("ReportDescription", true, false)).GrabFocus();
    public void FocusButton(string label) => Ui.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text == label).GrabFocus();
    public System.Numerics.Vector2 ReportButtonCentre
    {
        get
        {
            var capture = recorder() ?? throw new DiagnosticUnavailableException("The capture control is unavailable.");
            var point = ((Button)capture.FindChild("BugReportControl", true, false)).GetGlobalRect().GetCenter(); return new(point.X, point.Y);
        }
    }
    private static int Count(string path, string pattern) => Directory.Exists(path) ? Directory.GetFiles(path, pattern).Length : 0;
    public BugReporterArchiveDiagnostic ReadArchive()
    {
        var store = Inbox; var id = session(); var manifest = store.Read(id); bool excluded = true;
        if (manifest.Reports.Count > 0)
        {
            var resolved = store.Resolve(id, manifest.Reports[0].Id);
            using var reader = new PlaytestRunReader(resolved.RecordingPath);
            while (reader.ReadNextStep() is { } step)
                if (step.Events.Any(item => item.Kind == PlaytestInputKind.Key && item.Code is (int)Key.W or (int)Key.Q)) excluded = false;
        }
        string results = Path.Combine(store.Root, "replay-results"); var files = Directory.Exists(results) ? Directory.GetFiles(results, "*.json") : [];
        bool matched = files.Length == 2;
        foreach (string file in files)
        {
            using var result = JsonDocument.Parse(File.ReadAllText(file));
            matched &= result.RootElement.GetProperty("Result").GetString() == "matched" && !result.RootElement.GetProperty("BuildChanged").GetBoolean();
        }
        return new([.. manifest.Reports.Select(report => new BugReporterReportDiagnostic(report.Id.ToString("N"), report.Description))],
            Count(store.SessionDirectory(id), "*.gz"), Count(Path.Combine(store.Root, "exports"), "*.zip"), excluded, matched);
    }
    public async Task CaptureAsync(string name, DiagnosticRunContext run)
    {
        if (DisplayServer.GetName() == "headless") return;
        await run.WaitAsync(DiagnosticFramePhase.PostDraw);
        using var image = owner.GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(Inbox.Root, name + ".png")) != Error.Ok) throw new IOException("Report UI capture failed.");
    }
}
