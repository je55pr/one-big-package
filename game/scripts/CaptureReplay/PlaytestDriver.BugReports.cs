using Godot;
using OBP.Runtime.Diagnostics;

namespace OBP.Game;

internal sealed partial class PlaytestDriver
{
    private bool _bugOverlayRequested, _bugRequestIsReport, _bugSuspended, _bugResumePending;
    private bool _bugQuitPending;
    private IDisposable? _bugUiSuspension;
    private ulong _bugSuspendStart, _bugExcludedMilliseconds;
    private int _bugNoticeUntil;
    private string? _bugNotice;
    private Button? _bugReportButton, _bugInboxButton;
    private bool _bugMouseControl;
    public PlaytestRunWriter BugWriter => _writer ?? throw new InvalidOperationException("Recording is not ready yet.");
    public bool BugInputSuspended => _bugSuspended;

    private ulong RecordingMilliseconds => Time.GetTicksMsec() - _epoch - _bugExcludedMilliseconds;

    public void RequestBugOverlay(bool report)
    {
        if (!Session.Arguments.BugCapture || _finished || _bugSuspended || _bugOverlayRequested || _writer is null) return;
        _bugOverlayRequested = true;
        _bugRequestIsReport = report;
    }

    private bool HandleBugControlClick(InputEvent input)
    {
        if (!Session.Arguments.BugCapture || input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
            return false;
        bool report = _bugReportButton?.GetGlobalRect().HasPoint(click.Position) == true;
        bool inbox = _bugInboxButton?.GetGlobalRect().HasPoint(click.Position) == true;
        if (!report && !inbox) return false;
        // Input's action state is independent of GUI event consumption.
        // Mask button actions before recording what the player consumes.
        _bugMouseControl = true;
        RequestBugOverlay(report);
        GetViewport().SetInputAsHandled();
        return true;
    }

    private void EnterBugOverlay(int completedStep)
    {
        _bugOverlayRequested = false;
        _bugMouseControl = false;
        BugReportEntry? draft = null;
        if (_bugRequestIsReport)
        {
            Guid id = Guid.NewGuid();
            draft = new(id, DateTimeOffset.UtcNow, completedStep, "report-" + id.ToString("N"),
                Ports.Destination().Id, "Bug on " + Ports.Destination().Label, "");
            _writer!.Mark(new(completedStep, draft.Marker));
            _marks++;
        }
        _bugSuspended = true;
        _bugSuspendStart = Time.GetTicksMsec();
        _bugUiSuspension = Ports.Host.SuspendUi();
        PlaytestClock.Active = false;
        if (draft is not null) Ports.Reports.OpenForm(draft); else Ports.Reports.OpenInbox();
    }

    public void ResumeAfterBugOverlay() { if (!_finished && !_finalized) _bugResumePending = true; }

    private bool ResumeBugCaptureAtBoundary(PlaytestStepKind kind)
    {
        if (_bugQuitPending) return false;
        if (!_bugSuspended) return true;
        if (!_bugResumePending || kind != PlaytestStepKind.Physics) return false;
        _bugExcludedMilliseconds += Time.GetTicksMsec() - _bugSuspendStart;
        // Form text and its accept/back buttons must not become held game
        // actions. The next real controls and events are recorded normally.
        _bugUiSuspension?.Dispose();
        _bugUiSuspension = null;
        _bugResumePending = _bugSuspended = false;
        PlaytestClock.RecordedMilliseconds = RecordingMilliseconds;
        PlaytestClock.Active = true;
        return true;
    }

    public void ShowBugNotice(string notice)
    {
        _bugNotice = notice;
        _bugNoticeUntil = _processSteps + 240;
    }

    private void InstallBugCaptureControls(CanvasLayer layer)
    {
        var buttons = new HBoxContainer { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -340, OffsetRight = -20, OffsetTop = 18 };
        var report = new Button { Name = "BugReportControl", Text = "Report bug · F8", FocusMode = Control.FocusModeEnum.None };
        var inbox = new Button { Name = "BugInboxControl", Text = "Reports · F7", FocusMode = Control.FocusModeEnum.None };
        _bugReportButton = report;
        _bugInboxButton = inbox;
        report.Pressed += () => RequestBugOverlay(true);
        inbox.Pressed += () => RequestBugOverlay(false);
        buttons.AddChild(report); buttons.AddChild(inbox);
        layer.AddChild(buttons);
    }

    public async Task PrepareBugCaptureQuitAsync()
    {
        if (!Session.Arguments.BugCapture || _writer is null || _finished) return;
        _finished = true;
        _step = null;
        ReleaseSceneBindings();
        PlaytestClock.Active = false;
        _flush ??= _writer.FinishAsync(_recordingError is null ? "closed" : "error");
        await _flush;
    }

    public void SuspendBugCaptureForQuit()
    {
        if (!Session.Arguments.BugCapture) return;
        _bugQuitPending = true;
        _step = null;
    }
}
