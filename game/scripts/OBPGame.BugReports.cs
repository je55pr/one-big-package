using System.Text.Json;
using OBP.Fusion;
using OBP.IO;
using Godot;
using OBP.Godot;
using OBP.RAC1.Progression;
using OBP.Runtime.Diagnostics;

namespace OneBigPackage;

public partial class OBPGame
{
    private BugReportInbox? _bugReports;
    private Guid _bugSessionId;
    private BugReportUi? _bugReportUi;
    private BugReportEntry? _bugDraft;
    private Task? _bugSaveInFlight;
    private readonly List<(Guid Session, BugReportEntry Report)> _bugRows = [];
    private int _bugSelection;
    private Guid? _bugDeleteConfirmation, _bugChangedBuildConfirmation;
    private BugReportEntry? _bugReplayReport;
    internal bool BugReportModalOpen => _bugReportUi is not null && IsInstanceValid(_bugReportUi) || _playtest?.BugInputSuspended == true;

    private bool InitializeBugReporting()
    {
        if (!_args.BugCapture && _args.ReplayBugReport is null) return true;
        try
        {
            if (_args.BugCapture && (_args.PlaytestRequested || _args.ReplayBugReport is not null))
                throw new InvalidDataException("Choose either bug capture or replay.");
            string inbox = _args.BugReportInbox ?? ProjectSettings.GlobalizePath("res://../captures/bug-reports");
            _bugReports = new(inbox);
            if (_args.ReplayBugReport is { } manifest)
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
                if (Path.GetFileName(manifest) != "session.json" ||
                    !Guid.TryParseExact(Path.GetFileName(directory), "N", out var sessionId) ||
                    !Guid.TryParse(_args.BugReportId, out var reportId))
                    throw new InvalidDataException("Choose a saved report and its report identity.");
                _bugReports = new(Path.GetDirectoryName(directory)!);
                var resolved = _bugReports.Resolve(sessionId, reportId);
                _bugReplayReport = resolved.Report;
                _args = _args with { ReplayPlaytest = resolved.RecordingPath };
            }
            else
            {
                _bugSessionId = Guid.NewGuid();
                string live = Path.Combine(_bugReports.Root, "live", _bugSessionId.ToString("N"));
                Directory.CreateDirectory(live);
                if (!_args.NativeParityRequested)
                {
                    string? destination = _args.Destination;
                    string profilePath = _args.ProfilePath ?? ProjectSettings.GlobalizePath("user://obp-profile.json");
                    if (destination is null && !_args.NoProfile && (File.Exists(profilePath) || File.Exists(profilePath + ".bak")))
                    {
                        var saved = AtomicDocumentFile.Load(profilePath, ObpProfile.MaximumBytes, ObpProfile.Decode,
                            error => error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException);
                        if (saved.Value is not { } savedProfile) throw new InvalidDataException("The shared profile cannot be recovered; original files retained.");
                        destination = savedProfile.Destination;
                    }
                    _args = _args with { RecordPlaytest = Path.Combine(live, "session.run.jsonl.gz"), Destination = destination ?? "rac1:LEVEL0" };
                    return true;
                }
                string sourceSave = _args.Rac1CampaignSavePath ?? ProjectSettings.GlobalizePath("user://rac1-campaign.json");
                int current = 0;
                string isolated = Path.Combine(live, "campaign.json");
                if (File.Exists(sourceSave))
                {
                    var envelope = JsonSerializer.Deserialize<Rac1CampaignSaveEnvelope>(File.ReadAllText(sourceSave))
                        ?? throw new InvalidDataException("Campaign could not be read.");
                    current = Rac1CampaignSavePolicy.RestoreOrDefault(envelope).Campaign.CurrentLevel;
                    File.Copy(sourceSave, isolated);
                }
                _args = _args with
                {
                    RecordPlaytest = Path.Combine(live, "session.run.jsonl.gz"),
                    Rac1CampaignSavePath = isolated,
                    Destination = _args.Destination ?? $"rac1:LEVEL{current}",
                };
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException or UnsupportedProfileVersionException)
        {
            GD.PrintErr("[bug-report] startup failed: " + error.Message);
            if (_args.BugCapture && !_args.BugReportSmoke && DisplayServer.GetName() != "headless")
                OS.Alert(error.Message, "OBP Bug Capture could not start");
            GetNodeOrNull<Node>("SourceManagerBootstrap")?.Free();
            if (_args.PlaytestReport is { } output)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                File.WriteAllText(output, JsonSerializer.Serialize(new { Result = "invalid", Error = error.Message }));
            }
            ApplicationLifecycle.RequestQuit(this, "bug-report-invalid", 2);
            return false;
        }
    }

    private void OpenBugReportForm(BugReportEntry draft)
    {
        _bugDraft = draft;
        _bugReportUi = new BugReportUi { DefaultTitle = draft.Title };
        _bugReportUi.SubmitRequested += (title, description) => _bugSaveInFlight = SaveBugReportAsync(title, description);
        _bugReportUi.CloseRequested += CloseBugReportOverlay;
        _playtest!.AddChild(_bugReportUi);
    }

    private async Task SaveBugReportAsync(string title, string description)
    {
        var ui = _bugReportUi!;
        ui.SetBusy(true, "Saving replayable report…");
        try
        {
            var report = _bugDraft! with { Title = title, Description = description };
            await _bugReports!.SaveAsync(_bugSessionId, _playtest!.BugWriter, report);
            GD.Print($"[bug-report] SAVED id={report.Id:N} session={_bugSessionId:N} step={report.Step}");
            CloseBugReportOverlay();
            _playtest.ShowBugNotice("Report saved locally · recording continues · F7 opens reports");
        }
        catch (Exception error)
        {
            ui.SetBusy(false, "Could not save. Your recording is retained. " + error.Message);
        }
    }

    private void OpenBugReportInbox()
    {
        _bugReportUi = new BugReportUi { InboxMode = true };
        _bugReportUi.CloseRequested += CloseBugReportOverlay;
        _bugReportUi.SelectionChanged += index =>
        {
            _bugSelection = index;
            _bugDeleteConfirmation = _bugChangedBuildConfirmation = null;
            if (index >= 0 && index < _bugRows.Count)
            {
                var report = _bugRows[index].Report;
                string planet = TrilogyWorldProviders.Resolve(report.Destination)?.DisplayName ?? report.Destination;
                var prerequisites = _bugReports!.Read(_bugRows[index].Session).Recording;
                string sources = string.Join(", ", prerequisites.Sources.Select(source => $"{source.Game}/{source.BuildId}/{source.Sha256?[..12]}"));
                _bugReportUi.SetDetails($"{planet} · {report.CreatedUtc.LocalDateTime:g}\n{report.Description}\nRequired sources: {sources}\n{prerequisites.Adapters.Count} native/session adapters; exact physics/backend and build required.");
            }
        };
        _bugReportUi.OpenFolderRequested += () =>
        {
            Directory.CreateDirectory(_bugReports!.Root);
            OS.ShellOpen(_bugReports.Root);
        };
        _bugReportUi.ReplayRequested += () => _ = ReplaySelectedBugAsync();
        _bugReportUi.ExportRequested += () => _ = ExportSelectedBugAsync();
        _bugReportUi.ImportRequested += ImportBugArchive;
        _bugReportUi.DeleteRequested += DeleteSelectedBugSession;
        _playtest!.AddChild(_bugReportUi);
        RefreshBugInbox();
    }

    private void RefreshBugInbox()
    {
        _bugRows.Clear();
        var sessions = _bugReports!.List();
        foreach (var session in sessions.Where(s => s.Session is not null).OrderByDescending(s => s.Session!.StartedUtc))
            foreach (var report in session.Session!.Reports) _bugRows.Add((session.Id, report));
        _bugReportUi!.SetReports(_bugRows.Select(r => $"{r.Report.CreatedUtc.LocalDateTime:HH:mm:ss} · {r.Report.Title}"));
        int unfinished = sessions.Count(s => s.Session is null) + _bugReports.InterruptedCaptureCount(_bugSessionId);
        _bugReportUi.SetStatus(unfinished == 0 ? "Files stay local. Exporting a ZIP does not upload it." :
            $"{unfinished} unfinished/unreadable session(s) are not offered for replay. Open Folder to inspect them.");
    }

    private (Guid Session, BugReportEntry Report) SelectedBug() => _bugRows.Count > 0 && _bugSelection < _bugRows.Count
        ? _bugRows[_bugSelection] : throw new InvalidDataException("Select a saved report first.");

    private async Task ExportSelectedBugAsync()
    {
        var ui = _bugReportUi!;
        try
        {
            var selected = SelectedBug();
            ui.SetBusy(true, "Preparing portable ZIP…");
            string archive = await Task.Run(() => _bugReports!.Export(selected.Session, selected.Report.Id));
            ui.SetBusy(false, $"ZIP saved in exports/{Path.GetFileName(archive)}. Review it before sharing.");
        }
        catch (Exception error) { ui.SetBusy(false, error.Message); }
    }

    private void ImportBugArchive()
    {
        var picker = new FileDialog
        {
            Title = "Open OBP bug report",
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*.zip ; OBP bug report"],
        };
        _bugReportUi!.AddChild(picker);
        picker.FileSelected += path =>
        {
            try { _bugReports!.Import(path); RefreshBugInbox(); }
            catch (Exception error) { _bugReportUi!.SetStatus("Import failed: " + error.Message); }
            picker.QueueFree();
        };
        picker.Canceled += () => picker.QueueFree();
        picker.PopupCenteredRatio(.75f);
    }

    private void DeleteSelectedBugSession()
    {
        try
        {
            var selected = SelectedBug();
            if (selected.Session == _bugSessionId)
                throw new InvalidDataException("Close this capture before deleting its session. Recording is still active.");
            if (_bugDeleteConfirmation != selected.Session)
            {
                _bugDeleteConfirmation = selected.Session;
                _bugReportUi!.SetStatus("Delete every report and the shared recording in this session? Press DELETE SESSION again to confirm.");
                return;
            }
            _bugReports!.Delete(selected.Session);
            RefreshBugInbox();
        }
        catch (Exception error) { _bugReportUi!.SetStatus(error.Message); }
    }

    private async Task ReplaySelectedBugAsync()
    {
        var ui = _bugReportUi!;
        try
        {
            var selected = SelectedBug();
            var inbox = _bugReports ?? throw new InvalidOperationException("Report inbox is unavailable.");
            ui.SetBusy(true, "Checking the completed recording…");
            var resolved = await Task.Run(() => inbox.Resolve(selected.Session, selected.Report.Id));
            using var reader = new PlaytestRunReader(resolved.RecordingPath);
            if (reader.Header.EngineVersion != Engine.GetVersionInfo()["string"].AsString() ||
                reader.Header.Platform != OS.GetName() || reader.Header.PhysicsEngine != PlaytestPhysicsEngine())
                throw new InvalidDataException("Replay requires the recorded Godot version, platform and physics backend.");
            bool changed = !PlaytestBuildMatches(reader.Header);
            if (changed && _bugChangedBuildConfirmation != selected.Report.Id)
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
                    _bugChangedBuildConfirmation = selected.Report.Id;
                    confirmation.QueueFree();
                    _ = ReplaySelectedBugAsync();
                };
                confirmation.Canceled += () => confirmation.QueueFree();
                confirmation.PopupCentered(new(520, 180));
                return;
            }
            string results = Path.Combine(inbox.Root, "replay-results");
            Directory.CreateDirectory(results);
            string result = Path.Combine(results, Guid.NewGuid().ToString("N") + ".json");
            var arguments = new List<string> { "--path", ProjectSettings.GlobalizePath("res://"), "--fixed-fps", "60" };
            bool automatedReplay = _args.BugReportSmoke || DisplayServer.GetName() == "headless";
            if (automatedReplay) arguments.Add("--headless");
            arguments.AddRange(["--", "--replay-bug-report", Path.Combine(inbox.SessionDirectory(selected.Session), "session.json"),
                "--bug-report-id", selected.Report.Id.ToString(), "--playtest-report", result]);
            if (!automatedReplay) arguments.Add("--playtest-inspect-marker");
            if (_args.Rac1Iso is { } iso) arguments.AddRange(["--rac1-iso", iso]);
            if (_args.GcIso is { } gcIso) arguments.AddRange(["--gc-iso", gcIso]);
            if (_args.UyaIso is { } uyaIso) arguments.AddRange(["--uya-iso", uyaIso]);
            if (changed) arguments.Add("--playtest-allow-build-change");
            ui.SetBusy(true, "Replaying in a separate game process…");
            int process = OS.CreateProcess(OS.GetExecutablePath(), arguments.ToArray());
            if (process < 0) throw new IOException("Could not start the replay process.");
            ulong deadline = Time.GetTicksMsec() + 30 * 60 * 1000;
            while (OS.IsProcessRunning(process) && Time.GetTicksMsec() < deadline)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (OS.IsProcessRunning(process)) throw new IOException("Replay is still running. Inspect its game window.");
            if (!File.Exists(result)) throw new IOException("Replay exited without a result file.");
            using var json = JsonDocument.Parse(File.ReadAllText(result));
            string? outcome = json.RootElement.GetProperty("Result").GetString();
            ui.SetBusy(false, outcome == "matched" ? "Replay reached this report; sampled state checks matched." :
                outcome == "diverged" ? "Replay reached this report with a state divergence. Its result is saved in replay-results." :
                "Replay could not complete. Inspect its result in replay-results.");
            GD.Print($"[bug-report] replay {outcome}; id={selected.Report.Id:N}");
        }
        catch (Exception error) { ui.SetBusy(false, error.Message); }
    }

    private void CloseBugReportOverlay()
    {
        if (_bugReportUi is not null)
        {
            _bugReportUi.ProcessMode = ProcessModeEnum.Disabled;
            _bugReportUi.QueueFree();
            _bugReportUi = null;
        }
        _bugDraft = null;
        _playtest!.ResumeAfterBugOverlay();
    }
}
