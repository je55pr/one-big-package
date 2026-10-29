namespace OBP.Game.Diagnostics;

public sealed class BugReporterScenario(CaptureDiagnosticContext context, ICaptureRecorderDiagnosticFixture recorder,
    IBugReporterDiagnosticFixture reports) : CaptureScenarioSupport(context)
{
    private async Task TapKey(CaptureKey key)
    { Key(key, true, CaptureKeyOrigin.Physical); await Frames(2); Key(key, false, CaptureKeyOrigin.Physical); await Frames(2); }
    private async Task TapPad(CaptureButton button)
    { Pad(button, true); await Frames(2); Pad(button, false); await Frames(2); }
    private async Task Click(string label) { reports.FocusButton(label); await TapPad(CaptureButton.A); }
    // A cold Fusion Lab entry asynchronously imports actors from three retail worlds.
    // Require a fully ready ordinary recorder, but do not apply the 5-second UI-action deadline to source preparation.
    internal Task WaitForOrdinaryWorldAsync() => WaitProcess(
        () => recorder.Read() is { RecorderPresent: true, PlayerPresent: true, WaitingForEntry: false, PreparingWorld: false },
        "Recorder did not enter its ordinary world.", 120_000);

    public async Task RunAsync()
    {
        await WaitForOrdinaryWorldAsync();
        await DescribeAndSaveAsync();
        await VerifyUnderlyingControlsAsync();
        await SavePausedReportAsync();
        await ExportAndReplayAsync();
    }
    private async Task DescribeAndSaveAsync()
    {
        await Frames(25); Axis(CaptureAxis.LeftY, -.5f); await Frames(25); Axis(CaptureAxis.LeftY, 0);
        await TapKey(CaptureKey.Space); await Frames(70); await TapKey(CaptureKey.F8);
        await WaitProcess(() => reports.ReadUi().FormOpen, "F8 did not open the report form.");
        var before = reports.ReadWorld(); reports.FocusDescription();
        foreach (char letter in "wasd qe")
        {
            CaptureKey key = letter switch { 'w' => CaptureKey.W, 'a' => CaptureKey.A, 's' => CaptureKey.S, 'd' => CaptureKey.D, 'q' => CaptureKey.Q, 'e' => CaptureKey.E, _ => CaptureKey.Space };
            Context.Input.Key(key, true, unicode: letter); await Frames(); Key(key, false);
        }
        await Frames(4); Require(reports.ReadUi().Description == "wasd qe", "Description typing did not reach its field.");
        var after = reports.ReadWorld();
        Require(System.Numerics.Vector3.Distance(after.Position, before.Position) < .001f && after.Bolts == before.Bolts &&
            after.InventoryJson == before.InventoryJson && !after.QuickSelectOpen, "Report typing affected the paused world or inventory.");
        await reports.CaptureAsync("report-form", Context.Run); await Click("SAVE REPORT");
        await WaitProcess(() => !reports.ReadUi().ModalOpen, "First report did not save/resume.");
        var archive = reports.ReadArchive();
        Require(archive.Reports is { Length: 1 } && archive.Reports[0].Description == "wasd qe", "First report metadata changed.");
        Require(archive.FormKeysExcluded, "Form keystrokes contaminated the gameplay tape.");
        Log("[bug-report-smoke] F8, description isolation, immediate completed report and resume PASS");
    }
    private async Task VerifyUnderlyingControlsAsync()
    {
        Pad(CaptureButton.Y, true); await Frames(8); Require(reports.ReadWorld().QuickSelectOpen, "Triangle did not open the ordinary weapon wheel.");
        await TapKey(CaptureKey.F8); await WaitProcess(() => reports.ReadUi().FormOpen, "Report did not open over Quick Select.");
        await TapPad(CaptureButton.B); await WaitProcess(() => !reports.ReadUi().ModalOpen, "Report cancel did not resume.");
        Require(reports.ReadWorld().QuickSelectOpen, "Reporter changed the underlying weapon wheel.");
        Pad(CaptureButton.Y, false); await Frames(4); Require(!reports.ReadWorld().QuickSelectOpen, "Restored weapon wheel did not receive normal release input.");
        var click = reports.ReportButtonCentre; Context.Input.Mouse(true, click);
        await WaitProcess(() => reports.ReadUi().FormOpen, "Mouse report control did not open its form."); Context.Input.Mouse(false, click);
        await TapPad(CaptureButton.B); await WaitProcess(() => !reports.ReadUi().ModalOpen, "Mouse report cancel did not restore capture.");
    }
    private async Task SavePausedReportAsync()
    {
        await Frames(20); await TapPad(CaptureButton.Start); await WaitProcess(() => reports.ReadWorld().PauseOpen, "Start did not pause normally.");
        await TapPad(CaptureButton.X); await WaitProcess(() => reports.ReadUi().FormOpen, "Paused Square did not open the report form.");
        await Click("SAVE REPORT"); await WaitProcess(() => !reports.ReadUi().ModalOpen, "Controller report did not save/resume.");
        Require(reports.ReadWorld() is { PauseOpen: true, TreePaused: true }, "Report did not restore the original pause menu.");
        var archive = reports.ReadArchive();
        Require(archive.Reports is { Length: 2 } && archive.Reports[1].Description.Length == 0 && archive.Recordings == 1, "Reports did not share one complete recording.");
        await TapPad(CaptureButton.Back); await WaitProcess(() => reports.ReadUi().InboxOpen, "Paused Select did not open the inbox.");
        await TapPad(CaptureButton.B); await WaitProcess(() => !reports.ReadUi().ModalOpen, "Inbox back did not restore pause.");
        Require(reports.ReadWorld() is { PauseOpen: true, TreePaused: true }, "Inbox changed the prior pause state.");
    }
    private async Task ExportAndReplayAsync()
    {
        await TapPad(CaptureButton.Start); await Frames(20); await TapKey(CaptureKey.F7);
        await WaitProcess(() => reports.ReadUi().InboxOpen, "Report inbox did not open."); await reports.CaptureAsync("report-inbox", Context.Run);
        Require(reports.ReadUi().FocusName == "ReportList", "Inbox did not start in the report list.");
        await TapPad(CaptureButton.Right); await TapPad(CaptureButton.Right);
        Require(reports.ReadUi().FocusText == "EXPORT SELECTED ZIP", "Cannot navigate from reports to export.");
        await TapPad(CaptureButton.A); await WaitProcess(() => !reports.ReadUi().Busy, "Portable export did not complete.");
        Require(reports.ReadArchive().Exports == 1, "No portable ZIP was saved.");
        await TapPad(CaptureButton.Up); Require(reports.ReadUi().FocusName == "ReportList", "Cannot return from export to the selected report.");
        Log("[bug-report-smoke] paused Square, blank controller submission, multiple reports, inbox and export PASS");
        await TapPad(CaptureButton.A);
        await WaitProcess(() => reports.ReadUi() is { Busy: true, Status: "Replaying in a separate game process…" }, "Reproduction child did not start.");
        await TapPad(CaptureButton.B);
        await WaitProcess(() => !reports.ReadUi().ModalOpen, "Back did not cancel the active reproduction and close its view.");
        await Frames(8); await TapKey(CaptureKey.F7);
        await WaitProcess(() => reports.ReadUi().InboxOpen, "Inbox did not reopen after canceled reproduction.");
        Require(!reports.ReadUi().Busy, "Canceled reproduction updated the replacement inbox.");
        Log("[bug-report-smoke] active child cancellation, view cleanup and inbox reopen PASS");
        for (int index = 0; index < 2; index++)
        {
            Require(reports.ReadUi().FocusName == "ReportList", "Replay did not restore list focus.");
            if (index > 0) await TapPad(CaptureButton.Down);
            await TapPad(CaptureButton.A); await WaitProcess(() => !reports.ReadUi().Busy, "Separate replay did not finish.", 90_000);
            Require(reports.ReadUi().Status.Contains("checks matched", StringComparison.Ordinal), "Separate replay failed: " + reports.ReadUi().Status);
        }
        Require(reports.ReadArchive().ReplaysMatchedSameBuild, "Replay required a build override or diverged.");
        Log("[bug-report-smoke] two fresh-process same-build replays through saved report markers PASS");
    }
}
