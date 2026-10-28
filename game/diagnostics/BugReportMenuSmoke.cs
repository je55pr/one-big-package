using Godot;

namespace OBP.Game;

/// <summary>Asset-free mixed-input regression for the real reporting inbox.</summary>
public partial class BugReportMenuSmoke : Node
{
    private readonly List<string> _failures = [];
    private async Task Frames(int count = 3)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void Check(bool condition, string message) { if (!condition) _failures.Add(message); }
    private async Task Pad(JoyButton button)
    {
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = true });
        await Frames();
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = false });
        await Frames();
    }
    private async Task Click(Vector2 position, MouseButton button = MouseButton.Left, bool twice = false)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = position });
        Input.ParseInputEvent(new InputEventMouseButton { Position = position, ButtonIndex = button, Pressed = true, DoubleClick = twice });
        await Frames();
        Input.ParseInputEvent(new InputEventMouseButton { Position = position, ButtonIndex = button, Pressed = false });
        await Frames();
    }
    private async Task Stick(float direction)
    {
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = direction });
        await Frames();
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 0 });
        await Frames();
    }
    public override void _Ready() => Callable.From(() => { _ = RunAsync(); }).CallDeferred();

    private async Task RunAsync()
    {
        try
        {
            GetWindow().Size = new(960, 540);
            var inbox = new BugReportUi { InboxMode = true };
            AddChild(inbox);
            int selected = -1, replays = 0, closes = 0;
            inbox.SelectionChanged += value => selected = value;
            inbox.ReplayRequested += () => replays++;
            inbox.CloseRequested += () => closes++;
            inbox.SetReports(["First report", "Second report"]);
            await Frames();
            var list = (ItemList)inbox.FindChild("ReportList", true, false);
            Button Button(string text) => inbox.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == text);
            var replay = Button("REPLAY SELECTED");
            var export = Button("EXPORT SELECTED ZIP");
            Check(GetViewport().GuiGetFocusOwner() == list, "Opening inbox did not focus its report list.");
            list.GrabFocus();
            await Pad(JoyButton.DpadRight);
            Check(GetViewport().GuiGetFocusOwner() == replay, "Right did not move from reports to actions.");
            await Pad(JoyButton.DpadUp);
            Check(GetViewport().GuiGetFocusOwner() == list, "Up did not return from Replay to reports.");
            await Pad(JoyButton.DpadDown);
            Check(selected == 1, "D-pad did not select the next report.");
            await Pad(JoyButton.DpadDown);
            Check(GetViewport().GuiGetFocusOwner() == replay, "D-pad was trapped at the end of the report list.");
            replay.GrabFocus();
            await Pad(JoyButton.DpadRight);
            Check(GetViewport().GuiGetFocusOwner() == export, "Horizontal action focus does not match the visible row.");
            await Pad(JoyButton.DpadUp);
            Check(GetViewport().GuiGetFocusOwner() == list && selected == 1, "Up from actions did not return to the selected report.");
            list.GrabFocus();
            await Pad(JoyButton.A);
            Check(replays == 1, "Cross on a report did not replay it.");
            await Click(list.GlobalPosition + list.GetItemRect(0).GetCenter());
            Check(selected == 0, "Mouse click did not select the clicked report.");
            await Click(list.GlobalPosition + list.GetItemRect(0).GetCenter(), twice: true);
            Check(replays == 2, "Mouse double-click did not replay the clicked report.");
            await Stick(1);
            Check(selected == 1, "Left stick did not select the next report after mouse use.");
            await Stick(-1);
            Check(selected == 0, "Left stick did not return to the previous report.");
            export.GrabFocus();
            inbox.SetBusy(true, "Working");
            await Click(list.GlobalPosition + list.GetItemRect(1).GetCenter());
            Check(selected == 0, "Busy inbox still accepted mouse selection.");
            Check(!Button("BACK").Disabled, "Busy inbox cannot close its view.");
            await Pad(JoyButton.B);
            Check(closes == 1, "Busy inbox did not forward Back for cancellation.");
            inbox.SetBusy(false, "Done");
            await Frames();
            Check(GetViewport().GuiGetFocusOwner() == export, "Finishing an action unexpectedly moved focus.");

            inbox.SetReports(Enumerable.Range(0, 30).Select(i => $"Report {i:00} · " + new string('W', 110)));
            list.GrabFocus();
            await Frames();
            for (int i = 0; i < 29; i++) await Pad(JoyButton.DpadDown);
            Check(selected == 29 && list.GetVScrollBar().Value > 0, "Long list did not scroll with controller selection.");
            await Pad(JoyButton.DpadDown);
            Check(GetViewport().GuiGetFocusOwner() == replay, "Cannot leave a scrolled report list.");
            double oldScroll = list.GetVScrollBar().Value;
            await Click(list.GetGlobalRect().GetCenter(), MouseButton.WheelUp);
            Check(list.GetVScrollBar().Value < oldScroll, "Mouse wheel did not scroll the report list.");
            Check(!list.GetHScrollBar().IsVisibleInTree(), "Long report titles created horizontal scrolling.");
            Check(list.GetGlobalRect().End.X <= GetViewport().GetVisibleRect().End.X,
                $"Report menu lies outside the window: list={list.GetGlobalRect()}, viewport={GetViewport().GetVisibleRect()}.");
            if (DisplayServer.GetName() != "headless" && System.Environment.GetEnvironmentVariable("OBP_BUG_MENU_CAPTURE") is { } capture)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                image.SavePng(capture);
            }
            inbox.SetReports([]);
            Check(replay.Disabled && export.Disabled && Button("DELETE SESSION…").Disabled, "Empty inbox exposes report-only actions.");
            if (_failures.Count > 0) throw new InvalidOperationException(string.Join("\n", _failures));
            // Included by the existing asset-free CI startup smoke as well as
            // available through its standalone diagnostic scene.
            await AutomationDesktopSmoke.VerifyAsync(this);
            inbox.QueueFree(); await Frames();
            var form = new BugReportUi();
            AddChild(form); await Frames();
            form.SetBusy(true, "Saving replayable report…");
            var close = form.FindChildren("*", "Button", true, false).OfType<Button>()
                .Single(button => button.Text == "CLOSE · saving continues");
            Check(!close.Disabled, "Committed-save close must explain that saving continues and remain available.");
            form.SetBusy(false, "Retry");
            Check(close.Text == "CANCEL", "Failed-save retry did not restore the ordinary cancel action.");
            form.QueueFree(); await Frames();
            await PauseLoadoutSmoke.VerifyAsync(this);
            GD.Print("[bug-menu-smoke] PASS: mouse, D-pad, activation, focus return, busy/empty states and scrolling");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr("[bug-menu-smoke] FAIL " + error);
            GetTree().Quit(1);
        }
    }
}
