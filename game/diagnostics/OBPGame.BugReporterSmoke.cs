using System.Text.Json;
using Godot;
using OBP.Godot;
using OBP.Runtime.Diagnostics;

namespace OBP.Game;

public partial class OBPGame
{
    private async Task BugFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task WaitBugAsync(Func<bool> condition, string failure, int maximum = 5000)
    {
        ulong deadline = Time.GetTicksMsec() + (ulong)maximum;
        while (!condition() && Time.GetTicksMsec() < deadline) await BugFrames(1);
        Require(condition(), failure);
    }

    private async Task BugKey(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = true });
        await BugFrames(2);
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = false });
        await BugFrames(2);
    }

    private async Task BugPad(JoyButton button)
    {
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = true });
        await BugFrames(2);
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = false });
        await BugFrames(2);
    }

    private async Task ClickBugButton(string label)
    {
        var button = _bugReportUi!.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == label);
        button.GrabFocus();
        await BugPad(JoyButton.A);
    }

    private async Task CaptureBugUi(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng(Path.Combine(_bugReports!.Root, name + ".png"));
    }

    private async Task RunBugReporterSmokeAsync()
    {
        try
        {
            await WaitBugAsync(() => _playtest?.WaitingForEntry == false, "Recorder did not enter its ordinary world.");
            await BugFrames(25);
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -.5f });
            await BugFrames(25);
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 0 });
            await BugKey(Key.Space);
            await BugFrames(70);
            await BugKey(Key.F8);
            await WaitBugAsync(() => _bugReportUi is { InboxMode: false }, "F8 did not open the report form.");
            Vector3 position = _player!.GlobalPosition;
            int bolts = _rac1BoltWallet.Balance;
            string inventory = JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState());
            var description = (LineEdit)_bugReportUi!.FindChild("ReportDescription", true, false);
            description.GrabFocus();
            foreach (char letter in "wasd qe")
            {
                var key = letter == ' ' ? Key.Space : (Key)char.ToUpperInvariant(letter);
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = letter, Pressed = true });
                await BugFrames(1);
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
            }
            await BugFrames(4);
            Require(description.Text == "wasd qe", "Description typing did not reach its field.");
            Require(_player.GlobalPosition.DistanceTo(position) < .001f && _rac1BoltWallet.Balance == bolts &&
                JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState()) == inventory && !QuickSelectOpen,
                "Report typing affected the paused world or inventory.");
            await CaptureBugUi("report-form");
            await ClickBugButton("SAVE REPORT");
            await WaitBugAsync(() => !BugReportModalOpen, "First report did not save/resume.");
            var first = _bugReports!.Read(_bugSessionId);
            Require(first.Reports.Count == 1 && first.Reports[0].Description == "wasd qe", "First report metadata changed.");
            var resolved = _bugReports.Resolve(_bugSessionId, first.Reports[0].Id);
            using (var reader = new PlaytestRunReader(resolved.RecordingPath))
            {
                while (reader.ReadNextStep() is { } step)
                    Require(!step.Events.Any(e => e.Kind == PlaytestInputKind.Key && e.Code is (int)Key.W or (int)Key.Q),
                        "Form keystrokes contaminated the gameplay tape.");
            }
            GD.Print("[bug-report-smoke] F8, description isolation, immediate completed report and resume PASS");

            // Always-processing source menus must remain frozen behind the
            // reporter too. Cancel restores the same wheel and its input path.
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = true });
            await BugFrames(8);
            Require(QuickSelectOpen, "Triangle did not open the ordinary weapon wheel.");
            await BugKey(Key.F8);
            await WaitBugAsync(() => _bugReportUi is { InboxMode: false }, "Report did not open over Quick Select.");
            await BugPad(JoyButton.B);
            await WaitBugAsync(() => !BugReportModalOpen, "Report cancel did not resume.");
            Require(QuickSelectOpen, "Reporter changed the underlying weapon wheel.");
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = false });
            await BugFrames(4);
            Require(!QuickSelectOpen, "Restored weapon wheel did not receive normal release input.");
            var reportControl = (Button)_playtest!.FindChild("BugReportControl", true, false);
            Vector2 click = reportControl.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = click, Pressed = true });
            await WaitBugAsync(() => _bugReportUi is { InboxMode: false }, "Mouse report control did not open its form.");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = click, Pressed = false });
            await BugPad(JoyButton.B);
            await WaitBugAsync(() => !BugReportModalOpen, "Mouse report cancel did not restore capture.");
            await BugFrames(20);
            await BugPad(JoyButton.Start);
            await WaitBugAsync(() => PauseMenuOpen, "Start did not pause normally.");
            await BugPad(JoyButton.X);
            await WaitBugAsync(() => _bugReportUi is { InboxMode: false }, "Paused Square did not open the report form.");
            await ClickBugButton("SAVE REPORT");
            await WaitBugAsync(() => !BugReportModalOpen, "Controller report did not save/resume.");
            Require(PauseMenuOpen && GetTree().Paused, "Report did not restore the original pause menu.");
            var second = _bugReports.Read(_bugSessionId);
            Require(second.Reports.Count == 2 && second.Reports[1].Description.Length == 0 &&
                Directory.GetFiles(_bugReports.SessionDirectory(_bugSessionId), "*.gz").Length == 1,
                "Reports did not share one complete recording.");
            await BugPad(JoyButton.Back);
            await WaitBugAsync(() => _bugReportUi is { InboxMode: true }, "Paused Select did not open the inbox.");
            await BugPad(JoyButton.B);
            await WaitBugAsync(() => !BugReportModalOpen, "Inbox back did not restore pause.");
            Require(PauseMenuOpen && GetTree().Paused, "Inbox changed the prior pause state.");
            await BugPad(JoyButton.Start);
            await BugFrames(20);
            await BugKey(Key.F7);
            await WaitBugAsync(() => _bugReportUi is { InboxMode: true }, "Report inbox did not open.");
            await CaptureBugUi("report-inbox");
            Require(GetViewport().GuiGetFocusOwner()?.Name == "ReportList", "Inbox did not start in the report list.");
            await BugPad(JoyButton.DpadRight);
            await BugPad(JoyButton.DpadRight);
            Require(GetViewport().GuiGetFocusOwner() is Button { Text: "EXPORT SELECTED ZIP" }, "Cannot navigate from reports to export.");
            await BugPad(JoyButton.A);
            await WaitBugAsync(() => _bugReportUi is { Busy: false }, "Portable export did not complete.");
            Require(Directory.GetFiles(Path.Combine(_bugReports.Root, "exports"), "*.zip").Length == 1, "No portable ZIP was saved.");
            await BugPad(JoyButton.DpadUp);
            Require(GetViewport().GuiGetFocusOwner()?.Name == "ReportList", "Cannot return from export to the selected report.");
            GD.Print("[bug-report-smoke] paused Square, blank controller submission, multiple reports, inbox and export PASS");

            // Both reports replay from their original level entry in separate
            // processes; the second crosses the excluded first reporting form.
            for (int index = 0; index < 2; index++)
            {
                Require(GetViewport().GuiGetFocusOwner()?.Name == "ReportList", "Replay did not restore list focus.");
                if (index > 0) await BugPad(JoyButton.DpadDown);
                await BugPad(JoyButton.A);
                // A cold Novalis import can exceed 30s while other builds run.
                await WaitBugAsync(() => _bugReportUi is { Busy: false }, "Separate replay did not finish.", 90000);
                Require(_bugReportUi!.Status.Contains("checks matched"), "Separate replay failed: " + _bugReportUi.Status);
            }
            foreach (string file in Directory.GetFiles(Path.Combine(_bugReports.Root, "replay-results"), "*.json"))
            {
                using var result = JsonDocument.Parse(File.ReadAllText(file));
                Require(result.RootElement.GetProperty("Result").GetString() == "matched" &&
                    !result.RootElement.GetProperty("BuildChanged").GetBoolean(), "Replay required a build override or diverged.");
            }
            GD.Print("[bug-report-smoke] two fresh-process same-build replays through saved report markers PASS");
            ApplicationLifecycle.RequestQuit(this, "bug-report-smoke-pass");
        }
        catch (Exception error)
        {
            GD.PrintErr("[bug-report-smoke] FAIL " + error);
            ApplicationLifecycle.RequestQuit(this, "bug-report-smoke-fail", 1);
        }
    }
}
