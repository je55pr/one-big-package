using Godot;
using OBP.Runtime.Diagnostics;

namespace OneBigPackage;

public partial class OBPGame
{
    private sealed partial class PlaytestDriver
    {
        private bool _bugOverlayRequested, _bugRequestIsReport, _bugSuspended, _bugResumePending;
        private bool _bugPreviousPause, _bugPauseVisible, _bugPreviousNavigationSuspended, _bugQuitPending;
        private readonly List<(Node Node, ProcessModeEnum Mode)> _bugPausedNodes = [];
        private Input.MouseModeEnum _bugPreviousMouse;
        private Control? _bugReturnFocus;
        private ulong _bugSuspendStart, _bugExcludedMilliseconds;
        private int _bugNoticeUntil;
        private string? _bugNotice;
        private Button? _bugReportButton, _bugInboxButton;
        private bool _bugMouseControl;
        internal PlaytestRunWriter BugWriter => _writer ?? throw new InvalidOperationException("Recording is not ready yet.");
        internal bool BugInputSuspended => _bugSuspended;

        private ulong RecordingMilliseconds => Time.GetTicksMsec() - _epoch - _bugExcludedMilliseconds;

        internal void RequestBugOverlay(bool report)
        {
            if (!Game._args.BugCapture || _finished || _bugSuspended || _bugOverlayRequested || _writer is null) return;
            _bugOverlayRequested = true;
            _bugRequestIsReport = report;
        }

        private bool HandleBugControlClick(InputEvent input)
        {
            if (!Game._args.BugCapture || input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
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
                    Game._currentDestinationId!, "Bug on " + (Game._activeDestination?.DisplayName ?? "Fusion Lab"), "");
                _writer!.Mark(new(completedStep, draft.Marker));
                _marks++;
            }
            _bugSuspended = true;
            _bugSuspendStart = Time.GetTicksMsec();
            _bugPreviousPause = GetTree().Paused;
            _bugPreviousMouse = ApplicationPointer.MouseMode;
            _bugReturnFocus = GetViewport().GuiGetFocusOwner();
            _bugPausedNodes.Clear();
            // Pause affects ordinary nodes, but source menus explicitly process
            // Always. Freeze those too, so typing cannot navigate/equip behind us.
            foreach (var node in Game.FindChildren("*", "", true, false))
                if (node.ProcessMode is not (ProcessModeEnum.Inherit or ProcessModeEnum.Disabled))
                {
                    _bugPausedNodes.Add((node, node.ProcessMode));
                    node.ProcessMode = ProcessModeEnum.Disabled;
                }
            _bugPreviousNavigationSuspended = ControllerBindings.MenuNavigationSuspended;
            ControllerBindings.MenuNavigationSuspended = false;
            if (Game._pauseMenu is { } pause)
            {
                _bugPauseVisible = pause.Visible;
                pause.Visible = false;
            }
            GetTree().Paused = true;
            ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;
            PlaytestClock.Active = false;
            if (draft is not null) Game.OpenBugReportForm(draft); else Game.OpenBugReportInbox();
        }

        internal void ResumeAfterBugOverlay() => _bugResumePending = true;

        private bool ResumeBugCaptureAtBoundary(PlaytestStepKind kind)
        {
            if (_bugQuitPending) return false;
            if (!_bugSuspended) return true;
            if (!_bugResumePending || kind != PlaytestStepKind.Physics) return false;
            _bugExcludedMilliseconds += Time.GetTicksMsec() - _bugSuspendStart;
            // Form text and its accept/back buttons must not become held game
            // actions. The next real controls and events are recorded normally.
            foreach (var action in InputMap.GetActions()) Input.ActionRelease(action);
            foreach (var navigation in GetTree().Root.FindChildren("*", "", true, false).OfType<ControllerMenuNavigation>())
                navigation.ResetRepeat();
            foreach (var (node, mode) in _bugPausedNodes)
                if (IsInstanceValid(node)) node.ProcessMode = mode;
            _bugPausedNodes.Clear();
            ControllerBindings.MenuNavigationSuspended = _bugPreviousNavigationSuspended;
            if (Game._pauseMenu is { } pause)
            {
                pause.Visible = _bugPauseVisible;
            }
            if (MenuFocus.Available(_bugReturnFocus)) _bugReturnFocus!.GrabFocus();
            GetTree().Paused = _bugPreviousPause;
            ApplicationPointer.MouseMode = _bugPreviousMouse;
            _bugResumePending = _bugSuspended = false;
            PlaytestClock.RecordedMilliseconds = RecordingMilliseconds;
            PlaytestClock.Active = true;
            return true;
        }

        internal void ShowBugNotice(string notice)
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

        internal async Task PrepareBugCaptureQuitAsync()
        {
            if (!Game._args.BugCapture || _writer is null || _finished) return;
            _finished = true;
            _step = null;
            PlaytestClock.Active = false;
            _flush ??= _writer.FinishAsync(_recordingError is null ? "closed" : "error");
            await _flush;
        }

        internal void SuspendBugCaptureForQuit()
        {
            if (!Game._args.BugCapture) return;
            _bugQuitPending = true;
            _step = null;
        }
    }
}
