using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using OBP.Core;
using OBP.Core.Math;
using OBP.Godot;
using OBP.RAC1.Player;
using OBP.RAC1.Progression;

namespace OBP.Game;

/// <summary>Application ports needed by the legacy movement reproduction workflow.
/// Source verification, scene entry and world mutations belong to their existing owners.</summary>
internal sealed record MovementReproductionPorts(
    Node3D Owner,
    HostOperationScope HostOperations,
    Func<CommandLineArgs> Args,
    Func<PlayerHost?> Player,
    Func<string?> WorldBuildId,
    Func<string?> DestinationId,
    Func<bool> OrdinaryPlayerSession,
    Func<bool> FusionBlocked,
    Func<bool> HasRecorder,
    Action<string> RequestBookmark,
    Func<bool> RejectMovementLoad,
    Action<string, MovementReproState> OpenBookmark,
    Func<MovementReproWorld> CaptureWorld,
    Action<MovementReproWorld> PrepareWorld,
    Action<MovementReproWorld> RestoreWorld,
    Action<MovementReproWorld> RestoreHeld,
    Action InitializeSources,
    Func<ObpDestination, string, bool> VerifySource,
    Action<string> OpenDestination,
    Func<bool> PauseMenuOpen,
    Action ClosePauseMenu,
    Action ClearSmokeInput,
    Action<float, float> SetAnalogueSmokeInput,
    Action RefreshWrench,
    Action RefreshHud,
    Func<CancellationToken> WorldLifetime,
    Func<long> Frame,
    Action<string, int> Quit,
    Action<string> Report);

/// <summary>
/// Owns the movement reproduction operation, file IO and a single world-scoped
/// forward sampler. No scene lifecycle or source library is reimplemented here.
/// </summary>
internal sealed class MovementReproductionService(MovementReproductionPorts ports)
{
    private readonly MovementReproductionPorts _ports = ports;
    private HostOperationScope _hostOperations => _ports.HostOperations;
    private CommandLineArgs _args => _ports.Args();
    private PlayerHost? _player => _ports.Player();

    public bool IsLoading { get; private set; }
    public string? PathOverride { get; set; }
    public string MovementStatePath => PathOverride ?? _args.MovementStatePath ??
        Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "captures", "repros", "quick-state.json"));

    public void Report(string message) => _ports.Report(message);

    public MovementReproState CaptureMovementState()
    {
        var player = _player;
        string? build = _ports.WorldBuildId(), destination = _ports.DestinationId();
        if (build is null || player is null || destination is null || IsLoading)
            throw new InvalidOperationException("Enter an R&C1 world before saving a movement state.");
        return new MovementReproState(2, destination, build, DateTimeOffset.UtcNow,
            typeof(MovementReproductionService).Module.ModuleVersionId, player.CaptureMovementRepro(), _ports.CaptureWorld()).Validate();
    }

    public void SaveMovementState()
    {
        if (_ports.OrdinaryPlayerSession())
        {
            if (_ports.HasRecorder()) _ports.RequestBookmark(MovementStatePath);
            else Report("Universal bookmarks need an input history. Start Bug Capture or --record-playtest, then press F12; the completed prefix preserves native actors too.");
            return;
        }
        if (_ports.FusionBlocked()) { Report("Cross-game weapons use input recordings; use --record-playtest to retain all three sources."); return; }
        try
        {
            MovementReproState state = CaptureMovementState();
            MovementReproStateFile.Save(MovementStatePath, state);
            Report($"Movement state saved. Shift+F12 loads it.");
            GD.Print($"[movement-state] saved {MovementStatePath}; position={state.Player.Position}; " +
                $"facing={state.Player.FacingYaw:R}; control={state.Player.CameraController.ControlHeading:R}; " +
                $"assembly={state.HostAssemblyId}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException or InvalidOperationException)
        {
            Report($"Movement state not saved: {ex.Message}");
        }
    }

    public void LoadMovementStateFromBootstrap() => StartMovementStateLoad(_args.LoadMovementState!, startup: true);

    public void StartMovementStateLoad(string path, bool startup = false) =>
        _hostOperations.Start("movement-state-load", token => LoadMovementStateAsync(path, startup, cancellationToken: token), lifetime: CancellationToken.None);

    public async Task<List<MovementReplaySample>?> LoadMovementStateAsync(string path, bool startup = false, int? recordFrames = null,
        CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.CanBeCanceled) cancellationToken = _hostOperations.Token;
        cancellationToken.ThrowIfCancellationRequested();
        if (_ports.RejectMovementLoad()) return null;
        if (IsLoading) return null;
        PlayerHost? restoringPlayer = null;
        try
        {
            MovementReproState state = MovementReproStateFile.Load(path);
            if (state.Version == MovementReproState.CurrentVersion)
            {
                _ports.OpenBookmark(path, state);
                return null;
            }
            if (_ports.OrdinaryPlayerSession()) throw new InvalidOperationException("Legacy native R&C1 bookmarks require an explicit parity session. Open them with --load-movement-state at launch.");
            if (_ports.FusionBlocked()) throw new InvalidOperationException("Legacy R&C1 bookmarks require an R&C1 weapon with no active foreign shots.");
            if (_args.ReplayForwardFrames is { } frames && (frames < 1 || frames > 3600))
                throw new InvalidDataException("Forward replay must contain 1 through 3600 physics updates.");
            var destination = TrilogyWorldProviders.Resolve(state.Destination)
                ?? throw new InvalidDataException("The saved destination is unavailable.");
            _ports.InitializeSources();
            if (!_ports.VerifySource(destination, state.SourceBuildId))
                throw new InvalidDataException("Attach the saved state's supported R&C1 source before loading.");
            IsLoading = true;
            if (_ports.PauseMenuOpen()) _ports.ClosePauseMenu();
            _ports.ClearSmokeInput();
            // Rebuild transient controllers, then restore saved removals without
            // replaying damage, rewards or death effects. Version1 had no world
            // snapshot and retains its original fresh-level behaviour.
            if (state.World is { } savedWorld) _ports.PrepareWorld(savedWorld);
            _ports.OpenDestination(state.Destination);
            if (_ports.WorldBuildId() != state.SourceBuildId || _player is null || !_player.CanSaveMovementState)
                throw new InvalidDataException("The movement state does not match the loaded retail source build.");
            if (state.World is { } world) _ports.RestoreWorld(world);

            restoringPlayer = _player;
            using var restoreWorld = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _ports.WorldLifetime());
            restoringPlayer.SetPhysicsProcess(false);
            // A newly imported collision mesh needs registration before the
            // floor-contact cache can be primed. Never use spawn ground snapping.
            for (int frame = 0; frame < 2; frame++)
                await GodotAsyncWait.SignalAsync(_ports.Owner.GetTree(), SceneTree.SignalName.PhysicsFrame, restoreWorld.Token);
            restoreWorld.Token.ThrowIfCancellationRequested();
            if (_player != restoringPlayer || !GodotObject.IsInstanceValid(restoringPlayer))
                throw new InvalidOperationException("The world changed while loading the movement state.");
            restoringPlayer.RestoreMovementRepro(state.Player);
            if (state.World is { } handWorld)
                _ports.RestoreHeld(handWorld);
            _ports.RefreshWrench();
            Report(state.World is null
                ? "Movement state loaded (legacy pose only; enemies reset). F12 saves world removals too."
                : "Movement state loaded, including defeated enemies and broken crates.");
            _ports.RefreshHud();
            GD.Print($"[movement-state] loaded {path}; position={restoringPlayer.GlobalPosition}; " +
                $"saved-assembly={state.HostAssemblyId}; current-assembly={typeof(MovementReproductionService).Module.ModuleVersionId}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return null; }
        catch (OperationCanceledException)
        {
            Report("Movement state not loaded: the world changed during restoration.");
            if (startup) _ports.Quit("movement-state-load-fail", 2);
            return null;
        }
        catch (Exception ex)
        {
            Report($"Movement state not loaded: {ex.Message}");
            GD.PrintErr($"[movement-state] load failed: {ex.Message}");
            if (startup) _ports.Quit("movement-state-load-fail", 2);
            return null;
        }
        finally
        {
            if (restoringPlayer is not null && GodotObject.IsInstanceValid(restoringPlayer))
                restoringPlayer.SetPhysicsProcess(true);
            IsLoading = false;
        }

        if ((recordFrames ?? (startup ? _args.ReplayForwardFrames : null)) is { } replayFrames)
        {
            try
            {
                Engine.MaxFps = 60;
                var samples = await RecordForwardMovementReplayAsync(replayFrames, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!startup) return samples;
                string output = _args.ReplayOut ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "forward-replay.json");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                File.WriteAllText(output, JsonSerializer.Serialize(new
                {
                    StateFile = Path.GetFileName(path),
                    HostAssemblyId = typeof(MovementReproductionService).Module.ModuleVersionId,
                    PhysicsHz = Engine.PhysicsTicksPerSecond,
                    Samples = samples,
                }, new JsonSerializerOptions { WriteIndented = true }));
                GD.Print($"[movement-replay] PASS; frames={replayFrames}, output={output}, " +
                    $"start={samples[0].Position}, end={samples[^1].Position}");
                _ports.Quit("movement-replay-pass", 0);
            }
            catch (OperationCanceledException error) when (error.CancellationToken == cancellationToken && cancellationToken.IsCancellationRequested)
            { return null; }
            catch (Exception ex)
            {
                GD.PrintErr($"[movement-replay] FAIL: {ex.Message}");
                _ports.Quit("movement-replay-fail", 3);
            }
        }
        else
        {
            // An empty sample list confirms a successful restore without a requested forward replay.
            return [];
        }
        return null;
    }

    internal sealed record MovementReplaySample(int Tick, Vec3 Position, Vec3 Velocity, double FacingYaw,
        double ControlHeading, string Phase, bool Floor, bool Support, string SmallStep, Vec3[] ContactNormals,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MovementReplayProfile? Profile);

    internal sealed record MovementReplayProfile(double ElapsedMs, long RenderFrame, double ControllerMs,
        double ContactMs, double SlideMs, double SmallStepMs, double CrestMs, double PresentationMs,
        double CameraMs, double EngineProcessMs, double EnginePhysicsMs, double RenderCpuMs, double RenderGpuMs,
        double Fps);

    private MovementReplaySample CaptureMovementReplaySample(int tick, System.Diagnostics.Stopwatch? timer = null)
    {
        PlayerHost player = _player ?? throw new InvalidOperationException("Replay player is missing.");
        static Vec3 V(Vector3 value) => new(value.X, value.Y, value.Z);
        var normals = new List<Vec3>();
        for (int i = 0; i < player.GetSlideCollisionCount(); i++)
        {
            var collision = player.GetSlideCollision(i);
            for (int j = 0; j < collision.GetCollisionCount(); j++) normals.Add(V(collision.GetNormal(j)));
        }
        return new(tick, V(player.GlobalPosition), V(player.Velocity), player.Rac1CurrentYaw,
            player.MovementReproControlHeading,
            player.Rac1MovementPhase.ToString(), player.IsOnFloor(), player.Rac1RecoveredSupportGrounded,
            player.Rac1SmallStepDecision, normals.ToArray(), timer is null ? null :
                new(timer.Elapsed.TotalMilliseconds, _ports.Frame(), player.DevelopmentProfile.Controller,
                    player.DevelopmentProfile.Contact, player.DevelopmentContactProfile.Slide,
                    player.DevelopmentContactProfile.SmallStep, player.DevelopmentContactProfile.Crest,
                    player.DevelopmentProfile.Presentation, player.DevelopmentProfile.Camera,
                    Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000,
                    Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000,
                    RenderingServer.ViewportGetMeasuredRenderTimeCpu(_ports.Owner.GetViewport().GetViewportRid()) +
                        RenderingServer.GetFrameSetupTimeCpu(),
                    RenderingServer.ViewportGetMeasuredRenderTimeGpu(_ports.Owner.GetViewport().GetViewportRid()),
                    Engine.GetFramesPerSecond()));
    }

    public async Task<List<MovementReplaySample>> RecordForwardMovementReplayAsync(int frames, CancellationToken cancellationToken)
    {
        if (frames is < 1 or > 3600) throw new ArgumentOutOfRangeException(nameof(frames), "Forward replay must contain 1 through 3600 physics updates.");
        PlayerHost player = _player ?? throw new InvalidOperationException("Replay player is missing.");
        using var worldWork = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            _ports.WorldLifetime());
        player.DevelopmentProfileEnabled = _args.ReplayProfile;
        System.Diagnostics.Stopwatch? timer = _args.ReplayProfile ? System.Diagnostics.Stopwatch.StartNew() : null;
        if (_args.ReplayProfile)
            RenderingServer.ViewportSetMeasureRenderTime(_ports.Owner.GetViewport().GetViewportRid(), true);
        var samples = new List<MovementReplaySample> { CaptureMovementReplaySample(0, timer) };
        var complete = new TaskCompletionSource<List<MovementReplaySample>>(TaskCreationOptions.RunContinuationsAsynchronously);
        void CompletedPhysics()
        {
            try
            {
                if (worldWork.IsCancellationRequested) return;
                if (_player != player || !GodotObject.IsInstanceValid(player) || !player.IsInsideTree())
                    throw new InvalidOperationException("The replay world changed.");
                samples.Add(CaptureMovementReplaySample(samples.Count, timer));
                if (samples.Count == frames + 1)
                {
                    _ports.ClearSmokeInput();
                    player.DevelopmentPhysicsCompleted -= CompletedPhysics;
                    complete.TrySetResult(samples);
                }
            }
            catch (Exception ex) { complete.TrySetException(ex); }
        }
        // Inject only the ordinary forward action. The saved control/camera
        // heading determines the route; this never steers toward a guessed point.
        try
        {
            player.DevelopmentPhysicsCompleted += CompletedPhysics;
            _ports.SetAnalogueSmokeInput(0f, 1f);
            return await complete.Task.WaitAsync(TimeSpan.FromSeconds(frames / 60d + 30d), worldWork.Token);
        }
        catch (OperationCanceledException error) when (error.CancellationToken == worldWork.Token && worldWork.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The replay world changed.", error);
        }
        finally
        {
            player.DevelopmentPhysicsCompleted -= CompletedPhysics;
            if (GodotObject.IsInstanceValid(player)) player.DevelopmentProfileEnabled = false;
            if (_args.ReplayProfile)
                RenderingServer.ViewportSetMeasureRenderTime(_ports.Owner.GetViewport().GetViewportRid(), false);
            _ports.ClearSmokeInput();
        }
    }

}
