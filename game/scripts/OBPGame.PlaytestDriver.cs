using System.Text.Json;
using Godot;
using OBP.Godot;
using OBP.RAC1.Camera;
using OBP.Runtime.Diagnostics;

namespace OneBigPackage;

public partial class OBPGame
{
    private sealed partial class PlaytestDriver : Node
    {
        internal OBPGame Game { get; init; } = null!;
        internal PlaytestRunReader? Reader { get; init; }
        internal Rac1CameraOptions OriginalCameraOptions { get; set; } = null!;
        internal bool WaitingForEntry { get; private set; } = true;
        internal bool PreparingWorld { get; private set; }
        internal bool HasEntered => _entered;
        private readonly List<PlaytestCommand> _pendingCommands = [], _commands = [], _observedCommands = [];
        private readonly List<PlaytestCommand> _stagedCommands = [];
        private bool _stagingCommands;
        private int _matchedCommands;

        internal void RecordSessionCommand(PlaytestCommand command)
        {
            if (!_entered || _finished) return;
            command.Validate();
            if (Reader is not null) (_stagingCommands ? _stagedCommands : _observedCommands).Add(command);
            else if (_step is null) _pendingCommands.Add(command with { After = false });
            else _commands.Add(command with { After = true });
        }

        private void ReconcileCommands(bool after)
        {
            foreach (var command in _step!.Commands.Where(command => command.After == after))
            {
                if (_observedCommands.Count <= _matchedCommands) Game.ApplyCaptureCommand(command);
                if (_observedCommands.Count <= _matchedCommands ||
                    (_observedCommands[_matchedCommands] with { After = false }) != (command with { After = false }))
                    throw new InvalidDataException("Player-session command order or outcome differs from the recording.");
                _matchedCommands++;
            }
            if (after && _matchedCommands != _observedCommands.Count)
                throw new InvalidDataException("Replay emitted an unrecorded player-session operation.");
        }
        internal bool Finished => _finished;
        private PlaytestRunWriter? _writer;
        private readonly List<PlaytestInputEvent> _pending = [];
        private readonly List<PlaytestPlayerInput> _players = [];
        private PlaytestStep? _step;
        private PlaytestStep? _bufferedStep;
        private readonly List<PlaytestGameplayFact> _gameplay = [];
        internal void RecordGameplayFact(PlaytestGameplayFact fact)
        {
            if (!_finished && _step is not null) { fact.Validate(); _gameplay.Add(fact); }
        }
        private IReadOnlyDictionary<string, float> _previousActions = new Dictionary<string, float>();
        private PlaytestRunHeader? _header;
        private PlayerHost? _attachedPlayer;
        private bool _entered, _finished, _injecting, _saving, _stopRequested, _markRequested;
        private int _nextStep, _playerReads, _processSteps, _marks, _checks;
        private ulong _epoch;
        private Task? _flush;
        private ulong _lastProcessUsec;
        private Label? _hud;
        private PlaytestDifference? _firstDifference;
        private PlaytestCheckpoint? _lastObserved;
        private string? _recordingError;

        public override void _Ready()
        {
            Name = "PlaytestRecorder";
            ProcessMode = ProcessModeEnum.Always;
            ProcessPriority = ProcessPhysicsPriority = -10000;
            AddChild(new PlaytestEndDriver { Driver = this });
            var layer = new CanvasLayer { Layer = 280, ProcessMode = ProcessModeEnum.Always };
            _hud = new Label { Position = new(24, 72), MouseFilter = Control.MouseFilterEnum.Ignore };
            layer.AddChild(_hud);
            AddChild(layer);
            if (Game._args.BugCapture) InstallBugCaptureControls(layer);
            SetNotice(Reader is null ? "Preparing recording…" : "Preparing replay…");
        }

        internal void WorldEntered()
        {
            if (_finished) return;
            if (!_entered)
            {
                _epoch = Time.GetTicksMsec();
                _entered = true;
                if (Reader is { } reader) Game.GetWindow().Size = new(reader.Header.Width, reader.Header.Height);
            }
            AttachPlayer();
            if (Game.OrdinaryPlayerSession)
            {
                PreparingWorld = true;
                Game.SetPhysicsProcess(false); Game._player?.SetPhysicsProcess(false);
            }
            if (Game._args.PlaytestSmoke && Reader is null && WaitingForEntry)
                Callable.From(() => { _ = Game._args.FusionTest || Game._args.DefaultWeaponsSmoke ? Game.RunFusionRecordingSmokeAsync() : Game.RunPlaytestRecordingSmokeAsync(); }).CallDeferred();
            if (Game._args.BugReportSmoke && Reader is null && WaitingForEntry)
                Callable.From(() => { _ = Game.RunBugReporterSmokeAsync(); }).CallDeferred();
            if (Game._args.ReconciliationSmoke && Reader is null && WaitingForEntry)
                Callable.From(() => { _ = Game.RunReconciliationRecordingSmokeAsync(); }).CallDeferred();
        }

        public override void _PhysicsProcess(double delta) => Begin(PlaytestStepKind.Physics, delta);
        public override void _Process(double delta)
        {
            if (_bugSuspended || _finished && (Reader is null || Game._args.PlaytestInspectMarker || Game._bookmarkReplayStep.HasValue)) PaceHumanRecording();
            Begin(PlaytestStepKind.Process, delta);
        }

        public override void _Input(InputEvent input)
        {
            if (_finished || !_entered || _injecting) return;
            if (_bugSuspended) return;
            if (HandleBugControlClick(input)) return;
            if (Reader is not null)
            {
                // Menu repeat is generated by ControllerMenuNavigation from
                // replayed pad state and the recorded gesture clock.
                if (input is InputEventAction) return;
                // Replayed controls are the sole input source for this process.
                GetViewport().SetInputAsHandled();
                return;
            }
            bool keyMark = input is InputEventKey { Pressed: true, Echo: false } mark &&
                (mark.Keycode == Key.F8 || mark.PhysicalKeycode == Key.F8);
            bool keySave = input is InputEventKey { Pressed: true, Echo: false } save &&
                (save.Keycode == Key.F9 || save.PhysicalKeycode == Key.F9);
            bool padMark = GetTree().Paused && input is InputEventJoypadButton { ButtonIndex: JoyButton.X, Pressed: true };
            bool padSave = GetTree().Paused && input is InputEventJoypadButton { ButtonIndex: JoyButton.Back, Pressed: true };
            bool inbox = input is InputEventKey { Pressed: true, Echo: false } inboxKey &&
                (inboxKey.Keycode == Key.F7 || inboxKey.PhysicalKeycode == Key.F7);
            if (Game._args.BugCapture && (keyMark || padMark || inbox || padSave || keySave))
            {
                RequestBugOverlay(keyMark || padMark);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (keyMark || keySave || padMark || padSave)
            {
                if (keyMark || padMark) Mark(); else Save();
                GetViewport().SetInputAsHandled();
                return;
            }
            // UI navigation generates these itself from the recorded pad state
            // and gesture clock. Recording them again would double the repeat.
            if (input is InputEventAction) return;
            if (input is InputEventKey { Keycode: Key.F12, ShiftPressed: true, Pressed: true })
            {
                SetNotice("Movement-state loading is unavailable during a recorded run.");
                GetViewport().SetInputAsHandled();
                return;
            }
            ulong now = RecordingMilliseconds;
            PlaytestClock.RecordedMilliseconds = now;
            if (PlaytestInputCodec.Capture(input, now) is { } captured) _pending.Add(captured);
        }

        internal void Mark() { if (!_finished && Reader is null && _marks < 4096) _markRequested = true; }
        internal void Save() { if (!_finished && Reader is null) _stopRequested = true; }
        internal bool RejectMovementLoad()
        {
            if (_finished) return false;
            SetNotice("Save this run before loading a movement bookmark. Recorded runs start at level entry.");
            return true;
        }

        private void Begin(PlaytestStepKind kind, double delta)
        {
            if (!_entered || _finished || !ResumeBugCaptureAtBoundary(kind)) return;
            try
            {
                if (Game._movementStateLoading) throw new InvalidOperationException("Movement-state loads cannot be mixed into an input tape.");
                if (Game._bookmarkReplayStep is { } bookmarkStep && _nextStep > bookmarkStep) { CompleteReplay(atBookmark: true); return; }
                if (Game._bugReplayReport is { } report && _nextStep > report.Step) { CompleteReplay(atReportMarker: true); return; }
                if (Reader is not null)
                {
                    _bufferedStep ??= Reader.ReadNextStep();
                    if (_bufferedStep is null) { CompleteReplay(); return; }
                    // An external world-browser command can retire the world
                    // between Physics and Process. Stage that operation before
                    // waiting for its next recorded phase, including any IO gap.
                    if (!PreparingWorld && _bufferedStep.Kind != kind && _bufferedStep.Commands.Any(command => !command.After && command.Kind == "destination"))
                    {
                        _stagingCommands = true;
                        try { foreach (var command in _bufferedStep.Commands.Where(command => !command.After)) Game.ApplyCaptureCommand(command); }
                        finally { _stagingCommands = false; }
                    }
                }
                if (PreparingWorld)
                {
                    if (!Game.CaptureWorldReady() || kind != (_bufferedStep?.Kind ?? PlaytestStepKind.Physics)) return;
                    PreparingWorld = false; Game.SetPhysicsProcess(true); Game._player?.SetPhysicsProcess(true);
                }
                if (Math.Abs(delta - 1d / 60) > .00001)
                    throw new InvalidOperationException("Simulation timing changed; playtest requires fixed 60-Hz updates.");
                AttachPlayer();
                _players.Clear(); _playerReads = 0;
                _gameplay.Clear();
                _observedCommands.Clear();
                _observedCommands.AddRange(_stagedCommands); _stagedCommands.Clear();
                _matchedCommands = 0;
                PlaytestClock.Active = true;
                if (Reader is null)
                {
                    PlaytestClock.RecordedMilliseconds = RecordingMilliseconds;
                    if (WaitingForEntry)
                    {
                        _header = Game.CapturePlaytestHeader();
                        _writer = new(Game._args.RecordPlaytest!, _header);
                        GD.Print($"[playtest] recording: {_writer.Path}; build={_header.AssemblyId}; seed={_header.Seed}");
                    }
                    var actions = InputMap.GetActions().Select(a => (Name: a.ToString(), Strength: Input.GetActionStrength(a)))
                        .Where(a => a.Strength > 0).ToDictionary(a => a.Name, a => a.Strength, StringComparer.Ordinal);
                    _step = new(_nextStep, kind, PlaytestClock.RecordedMilliseconds, delta, _pending.ToArray(), actions, [])
                    {
                        Width = Game.GetWindow().Size.X,
                        Height = Game.GetWindow().Size.Y,
                    };
                    _pending.Clear();
                    _commands.Clear(); _commands.AddRange(_pendingCommands); _pendingCommands.Clear();
                }
                else
                {
                    _step = _bufferedStep ?? Reader.ReadNextStep();
                    _bufferedStep = null;
                    if (_step is null) { CompleteReplay(); return; }
                    if (_step.Kind != kind) throw new InvalidOperationException($"Step {_nextStep}: expected {_step.Kind}, engine called {kind}.");
                    var size = new Vector2I(_step.Width, _step.Height);
                    if (Game.GetWindow().Size != size) Game.GetWindow().Size = size;
                    RestoreActions(_previousActions);
                    _injecting = true;
                    try
                    {
                        foreach (var input in _step.Events)
                        {
                            PlaytestClock.RecordedMilliseconds = input.Milliseconds;
                            using var restored = PlaytestInputCodec.Restore(input);
                            Input.ParseInputEvent(restored);
                            Input.FlushBufferedEvents();
                        }
                    }
                    finally { _injecting = false; }
                    ReconcileCommands(after: false);
                    RestoreActions(_step.Actions);
                    _previousActions = _step.Actions;
                    PlaytestClock.RecordedMilliseconds = _step.Milliseconds;
                    if (WaitingForEntry)
                    {
                        _header = Reader.Header;
                        Game.ValidateCaptureProvenance(_header);
                        if (_header.SourceBuild != Game._world!.BuildId) throw new InvalidDataException("Recorded source build does not match this world.");
                        if (_header.Weapons is { } weapons && (Game.FusionConfiguration is not { } actualWeapons || !weapons.Matches(actualWeapons)))
                        {
                            string changed = Game.FusionConfiguration is not { } configuration ? "missing loadout" : string.Join(", ", weapons.Sources.Keys.Where(k =>
                                !configuration.Sources.TryGetValue(k, out var actual) || actual != weapons.Sources[k]));
                            throw new InvalidDataException($"Recorded cross-game weapon sources do not match ({changed}). Attach the exact original sources used by the recording.");
                        }
                        Compare(_header.InitialState, Game.CapturePlaytestCheckpoint(), -1);
                        GD.Print($"[playtest] replay: build={_header.AssemblyId}; seed={_header.Seed}");
                    }
                }
                WaitingForEntry = false;
            }
            catch (Exception error) { Fail(error); }
        }

        private void AttachPlayer()
        {
            if (Game._player is not { } player || player == _attachedPlayer) return;
            _attachedPlayer = player;
            player.PlaytestInputFilter = input =>
            {
                if (_finished) return input;
                try
                {
                    if (PreparingWorld || WaitingForEntry && _step is null) return default;
                    if (_step?.Kind != PlaytestStepKind.Physics) throw new InvalidOperationException("Player input escaped its recorded physics step.");
                    if (Reader is null)
                    {
                        var controls = PlaytestInputCodec.Capture(input);
                        if (_bugMouseControl)
                        {
                            controls = controls with { Jump = false, Crouch = false, ActionPressed = false, ActionHeld = false, UsePressed = false, UseHeld = false };
                            input = PlaytestInputCodec.Restore(controls);
                        }
                        _players.Add(controls);
                        return input;
                    }
                    if (_playerReads >= _step.Players.Count) throw new InvalidOperationException($"Step {_nextStep}: replay consumed extra player input.");
                    return PlaytestInputCodec.Restore(_step.Players[_playerReads++]);
                }
                catch (Exception error) { Fail(error); return default; }
            };
        }

        internal void End(PlaytestStepKind kind)
        {
            if (_finished || _step is null || !_entered) return;
            try
            {
                if (_step.Kind != kind) throw new InvalidOperationException("Playtest callback phases overlapped.");
                if (kind == PlaytestStepKind.Process) _processSteps++;
                if (Reader is null)
                {
                    bool observe = !PreparingWorld && kind == PlaytestStepKind.Process && (_processSteps % 60 == 0 || _markRequested || _stopRequested || _bugOverlayRequested || _bookmarkPath is not null);
                    _lastObserved = observe ? Game.CapturePlaytestCheckpoint() : null;
                    _writer!.Add(_step with { Players = _players.ToArray(), Checkpoint = _lastObserved, Commands = _commands.ToArray(), Gameplay = _gameplay.ToArray() });
                    if (observe && _markRequested)
                    {
                        _writer.Mark(new(_step.Index, "Bug " + ++_marks));
                        _markRequested = false;
                        GD.Print($"[playtest] marker Bug {_marks}: step {_step.Index}");
                    }
                }
                else
                {
                    ReconcileCommands(after: true);
                    if (Reader.Header.Version == PlaytestRunHeader.CurrentVersion && !PlaytestGameplayFact.Matches(_step.Gameplay, _gameplay))
                        throw new InvalidDataException($"Step {_nextStep}: source-qualified shot/damage order or adapter clock changed.");
                    if (_playerReads != _step.Players.Count) throw new InvalidOperationException("Replay consumed fewer player updates than recorded.");
                    if (_step.Checkpoint is { } expected) Compare(expected, Game.CapturePlaytestCheckpoint(), _step.Index);
                }
                _nextStep++;
                _step = null;
                if (Reader is null && kind == PlaytestStepKind.Process)
                {
                    string focus = Game.GetWindow().HasFocus() ? "" : "\nClick the game window to give F8/F9 keyboard focus.";
                    SetNotice(Game._args.BugCapture
                        ? $"BUG CAPTURE · {_processSteps / 60}s\nF8: Report · F7: Inbox · Paused: Square Report / Select Inbox\n{(_processSteps < _bugNoticeUntil ? _bugNotice : "")}"
                        : $"RECORDING  ·  {_processSteps / 60}s  ·  {_marks} bug markers\nF8: Mark bug   F9: Save run\nPaused: Square marks · Select saves{focus}");
                    if (_bugOverlayRequested) EnterBugOverlay(_nextStep - 1);
                    if (_bookmarkPath is { } bookmark && !PreparingWorld) { _bookmarkPath = null; _ = SaveBookmarkAsync(bookmark, _nextStep - 1); }
                    if (_stopRequested || _nextStep >= 999_990) _ = SaveAsync();
                    PaceHumanRecording();
                }
            }
            catch (Exception error) { Fail(error); }
        }

        private void PaceHumanRecording()
        {
            if (Game._args.PlaytestSmoke || Game._args.BugReportSmoke || Game._args.ReconciliationSmoke) return;
            // --fixed-fps disables Godot's real-time synchronization. Explicitly
            // pace the visible recorder; --max-fps alone does not cap this mode.
            // Replays and input smokes still run as fast as their host allows.
            const ulong period = 1_000_000 / 60;
            ulong now = Time.GetTicksUsec();
            if (_lastProcessUsec != 0 && now - _lastProcessUsec < period)
                OS.DelayUsec((int)(period - (now - _lastProcessUsec)));
            _lastProcessUsec = Time.GetTicksUsec();
        }

        private static void RestoreActions(IReadOnlyDictionary<string, float> actions)
        {
            foreach (var action in InputMap.GetActions()) Input.ActionRelease(action);
            foreach (var (action, strength) in actions)
            {
                if (!InputMap.HasAction(action)) throw new InvalidDataException("Recording contains an unavailable action.");
                Input.ActionPress(action, strength);
            }
        }

        private void Compare(PlaytestCheckpoint expected, PlaytestCheckpoint actual, int step)
        {
            _checks++;
            _lastObserved = actual;
            if (_firstDifference is null && PlaytestCheckpoint.Compare(expected, actual, step) is { } difference)
            {
                _firstDifference = difference;
                GD.PrintErr($"[playtest] first divergence: step {step}, {difference.Field} ({difference.Reason})");
            }
        }

        private async Task SaveAsync()
        {
            if (_finished || _saving) return;
            _saving = _finished = true;
            PlaytestClock.Active = false;
            SetNotice("Saving recorded run…");
            try
            {
                _flush = _writer!.FinishAsync("saved");
                await _flush;
                SetNotice($"Recording saved · {_marks} bug markers\n{Path.GetFileName(_writer.Path)}");
                GD.Print($"[playtest] SAVED {_writer.Path}; steps={_nextStep}; markers={_marks}");
                if (Game._args.ReconciliationSmoke) await Game.VerifyFinishedCaptureContinuationAsync();
                if (Game._args.PlaytestSmoke || Game._args.ReconciliationSmoke) ApplicationLifecycle.RequestQuit(Game, "playtest-record-smoke-pass");
            }
            catch (Exception error)
            {
                _recordingError = error.Message;
                SetNotice("Recording could not be saved: " + error.Message);
                GD.PrintErr("[playtest] save failed: " + error.Message);
                if (Game._args.PlaytestSmoke || Game._args.ReconciliationSmoke) ApplicationLifecycle.RequestQuit(Game, "playtest-save-failed", 2);
            }
        }

        private void CompleteReplay(bool atReportMarker = false, bool atBookmark = false)
        {
            if (!atBookmark && Game._bookmarkReplayStep is { } targetStep && _nextStep <= targetStep)
                throw new InvalidDataException("Bookmark recording ended before its saved step.");
            if (!atReportMarker && !atBookmark && Reader!.End?.Reason is not ("saved" or "closed"))
                throw new InvalidDataException("The recording ended with an error; it cannot establish a matched run.");
            bool matched = _firstDifference is null;
            WriteReport(matched ? "matched" : "diverged", null);
            _finished = true;
            PlaytestClock.Active = false;
            GD.Print($"[playtest] {(matched ? "PASS" : "DIVERGED")}; steps={_nextStep}; checkpoints={_checks}; markers={Reader!.Markers.Count}");
            if (atBookmark && matched && DisplayServer.GetName() != "headless" && !AutomationRunPolicy.IsAutomated(OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs()))
            {
                RestoreActions(new Dictionary<string, float>());
                GetTree().Paused = false;
                SetNotice("Universal bookmark restored. Native encounter history and shared player matched; play can continue.\nThis reproduction session does not write your ordinary profile.");
                return;
            }
            if (atReportMarker && Game._args.PlaytestInspectMarker)
            {
                GetTree().Paused = true;
                SetNotice($"REPLAY · {Game._bugReplayReport!.Title}\nMarked moment reached · {(matched ? "sampled checks matched" : "state divergence recorded")}\nClose this window to return to the report inbox.");
                return;
            }
            ApplicationLifecycle.RequestQuit(Game, "playtest-replay-complete", matched ? 0 : 3);
        }

        private void WriteReport(string result, string? error)
        {
            string output = Game.PlaytestReportPath();
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Result = result,
                Error = error,
                Steps = _nextStep,
                CheckpointsCompared = _checks,
                RecordedBuild = Reader!.Header.AssemblyId,
                ReplayedBuild = typeof(OBPGame).Module.ModuleVersionId,
                RecordedAssemblies = Reader.Header.Assemblies,
                ReplayedAssemblies = PlaytestAssemblyIds(),
                BuildChanged = !PlaytestBuildMatches(Reader.Header),
                FirstDifference = _firstDifference,
                Reader.Markers,
                ReportMarker = Game._bugReplayReport,
                LastObserved = _lastObserved,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void Fail(Exception error)
        {
            if (_finished) return;
            GD.PrintErr("[playtest] FAIL: " + error.Message);
            WaitingForEntry = false;
            if (PreparingWorld)
            {
                PreparingWorld = false; Game.SetPhysicsProcess(true); Game._player?.SetPhysicsProcess(true);
            }
            _recordingError = error.Message;
            _finished = true;
            if (Reader is not null)
            {
                try { WriteReport("invalid", error.Message); }
                catch (Exception reportError) { GD.PrintErr("[playtest] report failed: " + reportError.Message); }
                ApplicationLifecycle.RequestQuit(Game, "playtest-replay-failed", 2);
            }
            else
            {
                SetNotice("Recording stopped: " + error.Message);
                if (Game._args.BugCapture && _bugReportButton is { } reportButton) reportButton.Disabled = true;
                if (Game._args.PlaytestSmoke || Game._args.ReconciliationSmoke || Game._args.BugReportSmoke) ApplicationLifecycle.RequestQuit(Game, "playtest-record-failed", 2);
            }
            PlaytestClock.Active = false;
        }

        private void SetNotice(string message) { if (_hud is not null && IsInstanceValid(_hud)) _hud.Text = message; }

        internal void FinishOnExit()
        {
            try
            {
                if (Reader is not null && !_finished)
                {
                    // A recorded Quit-menu press can close the host immediately
                    // after the final callback, before the next Begin reads EOF.
                    if (_step is null && Reader.ReadNextStep() is null) CompleteReplay();
                    else WriteReport("invalid", "Replay exited before its complete input tape was consumed.");
                }
                if (_writer is not null)
                {
                    _flush ??= _writer.FinishAsync(_recordingError is null ? "closed" : "error");
                    _flush.GetAwaiter().GetResult();
                    _writer.Dispose();
                }
                Reader?.Dispose();
            }
            catch (Exception error)
            {
                GD.PrintErr("[playtest] finalization failed: " + error.Message);
                if (Reader is not null && !_finished)
                    try { WriteReport("invalid", error.Message); }
                    catch (Exception reportError) { GD.PrintErr("[playtest] report failed: " + reportError.Message); }
            }
            finally
            {
                _finished = true;
                PlaytestClock.Active = false;
                if (OriginalCameraOptions is not null) CameraSettingsStore.Set(OriginalCameraOptions, persist: false);
                QueueFree();
            }
        }
    }

    private sealed partial class PlaytestEndDriver : Node
    {
        internal PlaytestDriver Driver { get; init; } = null!;
        public override void _Ready()
        {
            ProcessMode = ProcessModeEnum.Always;
            ProcessPriority = ProcessPhysicsPriority = 10000;
        }
        public override void _PhysicsProcess(double delta) => Driver.End(PlaytestStepKind.Physics);
        public override void _Process(double delta) => Driver.End(PlaytestStepKind.Process);
    }
}
