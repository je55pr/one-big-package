using Godot;
using OBP.Runtime.Diagnostics;

namespace OBP.Game;

internal sealed record BugReportUiPorts(Node Host, Func<Node> OverlayParent, Func<PlaytestRunWriter> Writer,
    Action Resume, Action<string> Notice, Func<PlaytestRunHeader, bool> BuildMatches,
    Func<string> PhysicsEngine, Func<CancellationToken, Task> Frame, Action<string, int> Quit);
internal sealed record BugReportUiSnapshot(bool FormOpen, bool InboxOpen, bool Busy, string Status,
    string? FocusName, string? FocusText, string Description);
internal sealed record BugReportReplayReply(bool ConfirmationNeeded, string? Outcome = null);

internal interface IBugReportUiDiagnostics
{
    BugReportUiSnapshot ReadUi();
    BugReportCaptureSnapshot ReadReports();
    string StorageRoot { get; }
    void FocusDescription();
    void FocusButton(string label);
}

/// <summary>Owns report forms, inbox selection/dialogs and their cancellable work, borrowing the existing store and writer.</summary>
internal sealed class BugReportUiCoordinator(BugReportCapture capture, HostOperationScope operations, BugReportUiPorts ports) : IDisposable, IBugReportUiDiagnostics
{
    private readonly BugReportUiWork _work = new(operations);
    private BugReportUi? _view;
    private BugReportEntry? _draft;
    private readonly List<(Guid Session, BugReportEntry Report)> _rows = [];
    private int _selection = -1;
    private Guid? _deleteConfirmation, _changedBuildConfirmation;
    private bool _disposed;

    internal bool IsOpen => Current(_view);
    public string StorageRoot => capture.Inbox.Root;
    internal BugReportEntry? ReplayReport => capture.ReplayReport;
    public BugReportCaptureSnapshot ReadReports() => capture.ReadSnapshot();

    public BugReportUiSnapshot ReadUi()
    {
        var focus = ports.Host.GetViewport().GuiGetFocusOwner();
        bool valid = Current(_view);
        return new(valid && !_view!.InboxMode, valid && _view!.InboxMode, valid && _view!.Busy,
            valid ? _view!.Status : "", focus?.Name.ToString(), (focus as Button)?.Text,
            valid && _view!.FindChild("ReportDescription", true, false) is LineEdit description ? description.Text : "");
    }

    public void FocusDescription() => ((LineEdit)RequireView().FindChild("ReportDescription", true, false)).GrabFocus();
    public void FocusButton(string label) => RequireView().FindChildren("*", "Button", true, false)
        .OfType<Button>().Single(button => button.Text == label).GrabFocus();
    private BugReportUi RequireView() => IsOpen ? _view! : throw new InvalidOperationException("The report UI is unavailable.");

    internal void OpenForm(BugReportEntry draft)
    {
        if (_disposed || operations.Stopping) return;
        BeginView();
        _draft = draft;
        var ui = _view = new BugReportUi { DefaultTitle = draft.Title };
        ui.SubmitRequested += (title, description) =>
        {
            if (!Current(ui) || ui.Busy || _draft is not { } savedDraft) return;
            var report = savedDraft with { Title = title, Description = description };
            _draft = report;
            try { capture.Inbox.RetainPendingDraft(capture.SessionId, report); }
            catch (Exception error)
            {
                Failure(ui, error, "Could not retain your draft. Your text remains in this form. ");
                return;
            }
            ui.SetBusy(true, "Saving replayable report…");
            _work.Start("bug-report-save", _ => capture.Inbox.SaveAsync(capture.SessionId, ports.Writer(), report), _ =>
            {
                GD.Print($"[bug-report] SAVED id={report.Id:N} session={capture.SessionId:N} step={report.Step}");
                Close();
                ports.Notice("Report saved locally · recording continues · F7 opens reports");
            }, error => Failure(ui, error, "Could not save. Your recording is retained. "), committedSave: true);
        };
        ui.CloseRequested += Close;
        Attach(ui);
    }

    internal void OpenInbox()
    {
        if (_disposed || operations.Stopping) return;
        BeginView();
        var ui = _view = new BugReportUi { InboxMode = true };
        ui.CloseRequested += Close;
        ui.SelectionChanged += index => Select(ui, index);
        ui.OpenFolderRequested += () =>
        {
            if (!Current(ui)) return;
            try { Directory.CreateDirectory(StorageRoot); OS.ShellOpen(StorageRoot); }
            catch (Exception error) { Failure(ui, error); }
        };
        ui.ReplayRequested += () => StartReplay(ui);
        ui.ExportRequested += () =>
        {
            if (!Current(ui) || ui.Busy) return;
            try
            {
                var selected = Selected();
                ui.SetBusy(true, "Preparing portable ZIP…");
                _work.Start("bug-export", token => ExportSelectedBugAsync(selected, token),
                    archive => ui.SetBusy(false, $"ZIP saved in exports/{Path.GetFileName(archive)}. Review it before sharing."),
                    error => Failure(ui, error));
            }
            catch (Exception error) { Failure(ui, error); }
        };
        ui.ImportRequested += () => ImportArchive(ui);
        ui.DeleteRequested += () => DeleteSession(ui);
        Attach(ui);
        try { RefreshInbox(); }
        catch (Exception error) { Failure(ui, error); }
    }

    private void BeginView()
    {
        RetireView();
        _work.Open();
        _selection = -1;
        _deleteConfirmation = _changedBuildConfirmation = null;
    }

    private void Attach(BugReportUi ui)
    {
        try { ports.OverlayParent().AddChild(ui); }
        catch { Close(); throw; }
    }

    private void Select(BugReportUi ui, int index)
    {
        if (!Current(ui)) return;
        _selection = index;
        _deleteConfirmation = _changedBuildConfirmation = null;
        try
        {
            var selected = Selected();
            string planet = TrilogyWorldProviders.Resolve(selected.Report.Destination)?.DisplayName ?? selected.Report.Destination;
            var prerequisites = capture.Inbox.Read(selected.Session).Recording;
            string sources = string.Join(", ", prerequisites.Sources.Select(source => $"{source.Game}/{source.BuildId}/{source.Sha256?[..12]}"));
            ui.SetDetails($"{planet} · {selected.Report.CreatedUtc.LocalDateTime:g}\n{selected.Report.Description}\nRequired sources: {sources}\n{prerequisites.Adapters.Count} native/session adapters; exact physics/backend and build required.");
        }
        catch (Exception error) { Failure(ui, error); }
    }

    private void RefreshInbox()
    {
        _rows.Clear();
        _selection = -1;
        _deleteConfirmation = _changedBuildConfirmation = null;
        var sessions = capture.Inbox.List();
        foreach (var session in sessions.Where(s => s.Session is not null).OrderByDescending(s => s.Session!.StartedUtc))
            foreach (var report in session.Session!.Reports) _rows.Add((session.Id, report));
        _view!.SetReports(_rows.Select(row => $"{row.Report.CreatedUtc.LocalDateTime:HH:mm:ss} · {row.Report.Title}"));
        int unfinished = sessions.Count(s => s.Session is null) + capture.Inbox.InterruptedCaptureCount(capture.SessionId);
        int pending = capture.Inbox.PendingDraftCount();
        var notices = new List<string>();
        if (unfinished > 0) notices.Add($"{unfinished} unfinished/unreadable session(s) are not offered for replay.");
        if (pending > 0) notices.Add($"{pending} report draft(s) retained while saving or awaiting recovery. Pending recovery is retried at Bug Capture startup.");
        _view.SetStatus(notices.Count == 0 ? "Files stay local. Exporting a ZIP does not upload it." :
            string.Join(' ', notices) + " Open Folder to inspect them.");
    }

    private (Guid Session, BugReportEntry Report) Selected() => _selection >= 0 && _selection < _rows.Count
        ? _rows[_selection] : throw new InvalidDataException("Select a saved report first.");

    private Task<string> ExportSelectedBugAsync((Guid Session, BugReportEntry Report) selected, CancellationToken token) =>
        Task.Run(() => capture.Inbox.Export(selected.Session, selected.Report.Id, token), token);

    private void ImportArchive(BugReportUi ui)
    {
        if (!Current(ui) || ui.Busy) return;
        var picker = new FileDialog
        {
            Title = "Open OBP bug report",
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*.zip ; OBP bug report"],
        };
        ui.AddChild(picker);
        picker.FileSelected += path =>
        {
            if (!Current(ui)) return;
            picker.QueueFree();
            ui.SetBusy(true, "Importing portable report…");
            _work.Start("bug-import", token => Task.Run(() => capture.Inbox.Import(path, token), token), _ =>
            {
                ui.SetBusy(false, "");
                RefreshInbox();
            }, error => Failure(ui, error, "Import failed: "));
        };
        picker.Canceled += () => { if (GodotObject.IsInstanceValid(picker)) picker.QueueFree(); };
        picker.PopupCenteredRatio(.75f);
    }

    private void DeleteSession(BugReportUi ui)
    {
        if (!Current(ui) || ui.Busy) return;
        try
        {
            var selected = Selected();
            if (selected.Session == capture.SessionId)
                throw new InvalidDataException("Close this capture before deleting its session. Recording is still active.");
            if (_deleteConfirmation != selected.Session)
            {
                _deleteConfirmation = selected.Session;
                ui.SetStatus("Delete every report and the shared recording in this session? Press DELETE SESSION again to confirm.");
                return;
            }
            capture.Inbox.Delete(selected.Session);
            RefreshInbox();
        }
        catch (Exception error) { Failure(ui, error); }
    }

    private void StartReplay(BugReportUi ui)
    {
        if (!Current(ui) || ui.Busy) return;
        try
        {
            var selected = Selected();
            bool confirmed = _changedBuildConfirmation == selected.Report.Id;
            ui.SetBusy(true, "Checking the completed recording…");
            _work.Start("bug-replay", token => ReplaySelectedBugAsync(selected, confirmed, token), reply =>
            {
                if (reply.ConfirmationNeeded) { ConfirmChangedBuild(ui, selected); return; }
                ui.SetBusy(false, reply.Outcome == "matched" ? "Replay reached this report; sampled state checks matched." :
                    reply.Outcome == "diverged" ? "Replay reached this report with a state divergence. Its result is saved in replay-results." :
                    "Replay could not complete. Inspect its result in replay-results.");
                GD.Print($"[bug-report] replay {reply.Outcome}; id={selected.Report.Id:N}");
            }, error => Failure(ui, error));
        }
        catch (Exception error) { Failure(ui, error); }
    }

    private void ConfirmChangedBuild(BugReportUi ui, (Guid Session, BugReportEntry Report) selected)
    {
        ui.SetBusy(false, "This report uses another build.");
        var confirmation = new ConfirmationDialog
        {
            Title = "Replay with a different build?",
            Exclusive = true,
            DialogText = "This is not the recorded build. Replaying may show differences unrelated to the reported bug.",
            OkButtonText = "Replay current build",
            CancelButtonText = "Cancel",
        };
        ui.AddChild(confirmation);
        confirmation.Confirmed += () =>
        {
            if (!Current(ui)) return;
            confirmation.QueueFree();
            // A delayed confirmation may not authorize a different selected report.
            if (Selected() != selected) return;
            _changedBuildConfirmation = selected.Report.Id;
            StartReplay(ui);
        };
        confirmation.Canceled += () => { if (GodotObject.IsInstanceValid(confirmation)) confirmation.QueueFree(); };
        confirmation.PopupCentered(new(520, 180));
    }

    private async Task<BugReportReplayReply> ReplaySelectedBugAsync((Guid Session, BugReportEntry Report) selected,
        bool confirmed, CancellationToken token)
    {
        var resolved = await Task.Run(() => capture.Inbox.Resolve(selected.Session, selected.Report.Id, token), token);
        token.ThrowIfCancellationRequested();
        using var reader = new PlaytestRunReader(resolved.RecordingPath);
        if (reader.Header.EngineVersion != Engine.GetVersionInfo()["string"].AsString() ||
            reader.Header.Platform != OS.GetName() || reader.Header.PhysicsEngine != ports.PhysicsEngine())
            throw new InvalidDataException("Replay requires the recorded Godot version, platform and physics backend.");
        bool changed = !ports.BuildMatches(reader.Header);
        if (changed && !confirmed) return new(true);
        string results = Path.Combine(StorageRoot, "replay-results");
        Directory.CreateDirectory(results);
        string result = Path.Combine(results, Guid.NewGuid().ToString("N") + ".json");
        bool automated = capture.Args.BugReportSmoke || DisplayServer.GetName() == "headless";
        var arguments = new List<string> { "--path", ProjectSettings.GlobalizePath("res://"), "--fixed-fps", "60" };
        if (automated) arguments.Add("--headless");
        arguments.AddRange(["--", "--replay-bug-report", Path.Combine(capture.Inbox.SessionDirectory(selected.Session), "session.json"),
            "--bug-report-id", selected.Report.Id.ToString(), "--playtest-report", result]);
        if (!automated) arguments.Add("--playtest-inspect-marker");
        if (capture.Args.Rac1Iso is { } iso) arguments.AddRange(["--rac1-iso", iso]);
        if (capture.Args.Rac2Iso is { } rac2Iso) arguments.AddRange(["--rac2-iso", rac2Iso]);
        if (capture.Args.Rac3Iso is { } rac3Iso) arguments.AddRange(["--rac3-iso", rac3Iso]);
        if (changed) arguments.Add("--playtest-allow-build-change");
        if (Current(_view)) _view!.SetBusy(true, "Replaying in a separate game process…");
        try
        {
            var reply = await BugReportReplayProcess.RunAsync(new(result, arguments), new(
                args => OS.CreateProcess(OS.GetExecutablePath(), args.ToArray()), OS.IsProcessRunning,
                id =>
                {
                    if (OS.Kill(id) != Error.Ok && OS.IsProcessRunning(id))
                        throw new BugReportChildCleanupException("Could not stop the owned replay process.");
                },
                ports.Frame, Time.GetTicksMsec), token);
            return new(false, reply.Outcome);
        }
        catch (BugReportChildCleanupException)
        {
            ports.Quit("bug-replay-child-stop-failed", 2);
            throw;
        }
    }

    private bool Current(BugReportUi? ui) => !_disposed && !operations.Stopping && ui is not null &&
        ReferenceEquals(_view, ui) && GodotObject.IsInstanceValid(ui) && !ui.IsQueuedForDeletion();

    private void Failure(BugReportUi ui, Exception error, string prefix = "")
    {
        GD.PrintErr("[bug-report] operation failed: " + error);
        if (Current(ui)) ui.SetBusy(false, prefix + error.Message);
    }

    private void RetireView()
    {
        _work.Close();
        if (GodotObject.IsInstanceValid(_view))
        {
            _view!.ProcessMode = Node.ProcessModeEnum.Disabled;
            _view.QueueFree();
        }
        _view = null;
        _draft = null;
        _rows.Clear();
    }

    internal void Close()
    {
        bool hadView = _view is not null;
        RetireView();
        if (hadView && !_disposed && !operations.Stopping) ports.Resume();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RetireView();
        _work.Dispose();
    }
}
