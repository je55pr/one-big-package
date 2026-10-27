using OBP.Core;
using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.IO;
using OBP.RAC1.Progression;
using OBP.RAC2;
using OBP.Runtime;
using OBP.Runtime.Presentation;
using OBP.Runtime.Player;
using OBP.Game.Diagnostics;

namespace OBP.Game;

/// <summary>
/// Application root for the trilogy runtime host.
///
/// Production world entry is source-neutral:
/// <c>Game Sources → ObpDestination → IObpWorldProvider → RuntimeWorld → Godot</c>.
/// Source-game parsing ends at the provider boundary; this host owns presentation,
/// input, navigation, diagnostics and the single live-world lifecycle.
///
/// Small <c>--test-scene</c> fixtures remain available for deterministic host
/// checks. Historical GC command-line aliases are translated to canonical
/// destinations rather than selecting a separate runtime path.
/// </summary>
public partial class OBPGame : Node3D
{
    private enum Mode { Smoke, Picker, Selector, World, Title }

    private Node3D _worldRoot = null!;
    private Node3D _playerRoot = null!;
    private Node3D _cameraRoot = null!;
    private CanvasLayer _ui = null!;
    private Camera3D _camera = null!;

    private CommandLineArgs _args = new();
    internal CommandLineArgs StartupArgs => _args;

    private long _frame;
    private Mode _mode = Mode.Smoke;

    // capture / smoke bookkeeping
    private string _sceneKind = "smoke";
    private readonly HostOperationScope _hostOperations = new();
    private CaptureDiagnosticHarness? _captureDiagnostics;
    private readonly DiagnosticPlayerAudioCollector _playerAudioDiagnosticEvents = new();

    // the live world
    private readonly WorldHost _idleWorldHost = new();
    private WorldHost _worldHost => _worldScenes?.Current?.Resources.Presentation ?? _idleWorldHost;
    private readonly HudStateAdapter _hudState = new();
    private RuntimeWorld? _world => _worldScenes?.Current?.Plan.Prepared.World;
    private RuntimeWorldScene.Result? _sceneResult => _worldScenes?.Current?.Resources.Scene;
    private DebugOverlay? _overlay;
    private WorldInspectorPanel? _inspector;
    private PlayerHost? _player;
    private int _worldSwitches;

    private Camera3D _activeCamera = null!;
    private Label? _worldHud;
    private PlayerHud? _playerHud;
    private SourceVerificationOperation? _verification;
    private string _loadSummary = string.Empty;

    public override void _Ready()
    {
        ApplicationLifecycle.InstallWindowCloseInterception(GetTree());
        ApplicationLifecycle.SetQuitPreparation(PrepareForQuitAsync);
        try { _args = CommandLineArgs.Parse(OS.GetCmdlineUserArgs()); }
        catch (ArgumentException error)
        {
            GD.PrintErr("[cli] " + error.Message);
            GetNodeOrNull<Node>("SourceManagerBootstrap")?.Free();
            ApplicationLifecycle.RequestQuit(this, "cli-invalid", 2);
            return;
        }
        if (!InitializeReplayBookmark()) return;
        if (_args.FusionTest && _args.Destination is null) _args = _args with { Destination = "rac1:LEVEL0" };
        if (!InitializeBugReporting()) return;
        if (!InitializePlaytest()) return;
        if (_args.FusionTest && (_args.Destination != "rac1:LEVEL0" || _args.LoadMovementState is not null))
        {
            GD.PrintErr("[fusion] Choose fresh --destination rac1:LEVEL0 without a movement-state load.");
            GetNodeOrNull<Node>("SourceManagerBootstrap")?.Free();
            ApplicationLifecycle.RequestQuit(this, "fusion-invalid-start", 2);
            return;
        }
        if (_args.ReplayForwardFrames.HasValue && _args.LoadMovementState is null)
        {
            GD.PrintErr("[movement-replay] --replay-forward requires --load-movement-state.");
            ApplicationLifecycle.RequestQuit(this, "movement-replay-missing-state", 2);
            return;
        }
        if (_args.FixedSeed is { } seed)
        {
            GD.Seed((ulong)seed);
        }

        BuildSkeleton();
        if (!InitializeStartupSources()) return;
        if (_args.OrdinaryUiSmoke) CreateUiDiagnosticHarness().Start("ordinary-ui-smoke");

        // Dedicated deterministic fixture through the same playable Lab host
        // used by the ordinary World Select button.
        if (_args.TestScene == "fusion-lab")
        {
            if (EnterFusionLab() && _args.CaptureFrame is { } fusionCapture)
            {
                if (_player is { } player) StartFusionLabTraversalSmoke(player);
                _hostOperations.Start("capture", token => RunCaptureAsync(fusionCapture, token), lifetime: CancellationToken.None);
            }
            return;
        }

        // A --shots run works for any provider and drives the same neutral
        // destination/provider path used by ordinary world entry.
        if (_args.ShotsPath is { } shotList)
        {
            _hostOperations.Start("shots", token => CreateShotListCliService().RunShotsAsync(shotList, token), lifetime: CancellationToken.None);
            return;
        }

        bool bootstrapOwnsScene =
            _args.Destination is not null ||
            _args.Rac2Iso is not null ||
            _args.TestScene is "picker" or "worlds";

        if (_args.TestSceneExplicit && !bootstrapOwnsScene)
        {
            // Keep small deterministic host fixtures independent of retail data.
            // A GC-backed --test-scene player run is handled by the neutral
            // source/destination bootstrap instead.
            BuildScene(_args.TestScene);
        }
        else
        {
            // Production/navigation startup is owned by SourceManagerBootstrap.
            // Keep only a neutral background until its deferred route is ready.
            EnsurePlainEnvironment();
        }

        GD.Print($"[OBPGame] ready — mode={_mode} scene='{_sceneKind}' " +
                 $"capture={_args.CaptureFrame?.ToString() ?? "off"} renderer={CaptureHarness.ActiveRenderer()} " +
                 $"providers={string.Join(",", TrilogyWorldProviders.All.Select(provider => provider.BuildId))}");

        if (_args.CaptureFrame is { } frame)
        {
            Engine.MaxFps = 60;
            _hostOperations.Start("capture", token => RunCaptureAsync(frame, token), lifetime: CancellationToken.None);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_mode != Mode.World || _movementStateLoading || _player is null || _player.DevelopmentControls.FlyEnabled) return;
        StepRac1NovalisMechanisms();
        StepRac1PlayerAudioPhysics();
        StepRegisteredActorsPhysics();
    }

    public override void _Process(double delta)
    {
        ReportHostOperationFailures();
        if (_hostOperations.Stopping) return;
        _frame++;
        TickSourceSetup(delta);
        TickFusionPreparation();
        TickActorPreparation();
        _worldScenes?.Current?.Resources.Effects?.PrepareTick();
        TickSharedProfile(delta);
        TickRac1CampaignSaves(delta);

        if (_mode == Mode.World)
        {
            if (_playtest is { WaitingForEntry: true } or { PreparingWorld: true }) return;
            // Sky-follow, animated mobies and per-region hero light / fog.
            _worldHost.Tick(delta, _activeCamera?.GlobalPosition ?? Vector3.Zero);
            _worldScenes?.Current?.Resources.Effects?.Render();
            TickRac2Gameplay(delta);
            TickRac1Gameplay(delta);
            TickRac3Gameplay(delta);
            UpdatePlayerHud();
            UpdateWorldHud();
        }

        TickSourceVerification();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (TryHandleRac1MetropolisTaxiInput(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (key.Keycode == Key.Escape)
        {
            if (TryHandleInteractiveBack())
            {
                return;
            }

            bool interactiveSurface =
                (_mode == Mode.World || _mode == Mode.Selector || _mode == Mode.Picker) &&
                _args.CaptureFrame is null;
            if (ApplicationLifecycle.ResolveEscapeFallback(interactiveSurface) ==
                ApplicationLifecycle.EscapeFallback.StayAlive)
            {
                ApplicationLifecycle.ReportStayAlive(
                    $"unhandled-escape mode={_mode} scene={_sceneKind}");
                return;
            }

            ApplicationLifecycle.RequestQuit(this, "escape-noninteractive");
            return;
        }

        if (_mode != Mode.World || !_debugOutputEnabled)
        {
            return;
        }

        if (key.Keycode == Key.M && TryToggleRac1MetalDetector())
        {
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.K)
        {
            _worldHost.SetAnimationPlaying(!_worldHost.AnimationPlaying);
            UpdateWorldHud();
        }
        else if (key.Keycode == Key.I)
        {
            _inspector?.Toggle();
            if (_inspector?.IsOpen == true) _rac3TargetAudit.Request();
        }
        else if (_overlay is { } overlay && HandleOverlayKey(key.Keycode, overlay))
        {
            UpdateWorldHud();
        }
    }

    public bool TryHandleInteractiveBack()
    {
        if (QuickSelectOpen)
        {
            CloseWeaponQuickSelect();
            return TryOpenPauseMenu();
        }
        if (TryCloseControllerSettings())
        {
            ApplicationLifecycle.ReportNavigation("controller-settings-close");
            return true;
        }

        if (TryClosePauseMenu())
        {
            ApplicationLifecycle.ReportNavigation("pause-menu-close");
            return true;
        }

        if (TryCloseRac1PlanetMap())
        {
            ApplicationLifecycle.ReportNavigation("rac1-planet-map-close");
            return true;
        }

        if (TryOpenPauseMenu())
        {
            ApplicationLifecycle.ReportNavigation("world-pause-menu-open");
            return true;
        }

        if (TryReturnWorldToDestinations())
        {
            ApplicationLifecycle.ReportNavigation("world-to-destinations");
            return true;
        }

        if (TryReturnSelectorToMenu())
        {
            ApplicationLifecycle.ReportNavigation("selector-to-menu");
            return true;
        }

        return false;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            bool automated = WorldSmokeHarness.IsRequested(_args) ||
                _args.Rac1StartupVisibilitySmoke || _args.CaptureFrame is not null ||
                _args.ShotsPath is not null || _args.StressSwitch is not null;
            ApplicationLifecycle.RequestQuit(this, "desktop-window-close", automated ? 1 : 0);
        }
    }

    private void ReleaseAudioForExit()
    {
        _hostOperations.Dispose();
        RetireBugOverlayLifetime();
        if (_verification is { } verification) CancelSourceVerification(verification.Source.Game);
        SaveSharedProfile("clean exit");
        // An external exit can bypass normal world teardown. Finish campaign
        // persistence before shutting down its owning Godot session.
        FlushRac1CampaignSaves("clean exit");
        ResetRegisteredActors();
        _worldHost.Unload(releaseAudioImmediately: true);
        ResetRac1PlayerAudio(freeImmediately: true);
        ResetRac1WeaponAudio(freeImmediately: true);
        ResetFusionWeapons(freeImmediately: true);
        ResetSharedWeaponAssets();
        _worldScenes?.Dispose();
    }

    private async Task PrepareForQuitAsync()
    {
        // Stop gameplay before releasing its audio, otherwise a final physics
        // update can start another footstep/voice during shutdown.
        ProcessMode = ProcessModeEnum.Disabled;
        _playtest?.SuspendBugCaptureForQuit();
        Task hostStopped = _hostOperations.StopAsync();
        if (_worldScenes is { } scenes)
        {
            scenes.CancelPreparation();
            await scenes.PendingPreparation;
        }
        await hostStopped;
        ReportHostOperationFailures();
        if (_bugSaveInFlight is { } save) await _hostOperations.ObserveQuitWriteAsync("bug-report-save", save);
        if (_playtest is { } playtest)
            await _hostOperations.ObserveQuitWriteAsync("capture-flush", playtest.PrepareBugCaptureQuitAsync());
        ReportHostOperationFailures();
        ReleaseAudioForExit();

        // Godot 4.7.2 retires stopped playbacks on the mixing thread, then
        // releases their references in AudioServer's main-thread update. Tree
        // teardown alone cannot guarantee either has run. Observe two mixes
        // before the final quit; use wall time even with --fixed-fps.
        static double LastMixTime() => Time.GetTicksUsec() / 1_000_000d - AudioServer.GetTimeSinceLastMix();
        double lastMix = LastMixTime();
        ulong deadline = Time.GetTicksMsec() + 1000;
        int mixes = 0;
        while (mixes < 2 && Time.GetTicksMsec() < deadline)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            double mix = LastMixTime();
            if (mix > lastMix + .001d)
            {
                mixes++;
                lastMix = mix;
            }
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"[lifecycle] audio retired before quit (mixes={mixes})");
    }

    public override void _ExitTree()
    {
        // Also cover external/engine exits which bypass RequestQuit.
        ReleaseAudioForExit();
        ApplicationLifecycle.SetQuitPreparation(null);
        ApplicationLifecycle.ReportRootExit(Name);
    }

    /// <summary>F1..F7 + J toggle the <see cref="DebugOverlay"/> inspection layers in a loaded world.</summary>
    private static bool HandleOverlayKey(Key keycode, DebugOverlay overlay)
    {
        switch (keycode)
        {
            case Key.F1: overlay.IsolateNextKind(); return true;
            case Key.F2: overlay.Toggle(DebugOverlay.Layer.KindTint); return true;
            case Key.F3: overlay.Toggle(DebugOverlay.Layer.CollisionWire); return true;
            case Key.F4: overlay.Toggle(DebugOverlay.Layer.WorldBounds); return true;
            case Key.F5: overlay.Toggle(DebugOverlay.Layer.EnvGizmos); return true;
            case Key.F6: overlay.Toggle(DebugOverlay.Layer.HideSky); return true;
            case Key.F7: overlay.Clear(); return true;
            case Key.J: overlay.Toggle(DebugOverlay.Layer.Skeleton); return true;
            default: return false;
        }
    }

    private void BuildSkeleton()
    {
        _worldRoot = new Node3D { Name = "WorldRoot" };
        _playerRoot = new Node3D { Name = "PlayerRoot" };
        _cameraRoot = new Node3D { Name = "CameraRoot" };
        _ui = new CanvasLayer { Name = "UI" };
        AddChild(_worldRoot);
        AddChild(_playerRoot);
        AddChild(_cameraRoot);
        AddChild(_ui);

        _camera = new Camera3D { Name = "Camera3D", Current = true, Far = 12000f };
        _cameraRoot.AddChild(_camera);
        _activeCamera = _camera;
    }

    /// <summary>
    /// Remove deterministic smoke/bootstrap presentation before a production
    /// provider world is adopted. These nodes are not part of RuntimeWorld and
    /// must never leak into a direct destination capture.
    /// </summary>
    private void ClearBootstrapSceneArtifacts()
    {
        GetNodeOrNull<Node>("PlainEnvironment")?.QueueFree();
        GetNodeOrNull<Node>("Sun")?.QueueFree();
        _worldRoot.GetNodeOrNull<Node>("Floor")?.QueueFree();
        _worldRoot.GetNodeOrNull<Node>("SmokeCube")?.QueueFree();
        _ui.GetNodeOrNull<Node>("Banner")?.QueueFree();
    }

    /// <summary>Free the current world sub-tree and everything it owns. Safe to call when nothing is loaded.</summary>
    private void TeardownWorld() => WorldScenes.Retire();

    private void RetireWorldBindings()
    {
        FlushRac1CampaignSaves("world teardown");
        ResetRegisteredActors();
        _playerNative?.Life.EndWorld(); _playerDamageActors.Clear();
        ClearFusionLabStatus();
        CloseWeaponQuickSelect();
        TearDownInteractiveOverlays();
        TryCloseRac1PlanetMap();
        ClearPlayerAvatarView();
        ResetRac1LevelGameplay();
        if (!OrdinaryPlayerSession) _hudState.ResetSession();
        _playerHud?.Render(_hudState.Current);
        if (_player is not null) _player.PlayerServicesPhysicsUpdated -= StepPlayerServicesPhysics;
        // Retire the old collision object before a same-frame replacement can
        // snap its ground ray onto that queued-for-deletion player's capsule.
        if (_player?.GetParent() is { } parent) parent.RemoveChild(_player);
        _player?.QueueFree();
        _player = null;

        _overlay = null; // its nodes live under the world sub-tree that was just freed
        _inspector?.QueueFree();
        _inspector = null;
        ResetRac2GameplayHost();
        ResetRac3Gameplay();
    }

    /// <summary>
    /// Attach the runtime inspection overlay to a freshly-built world and apply
    /// any <c>--overlay</c> layers requested for a deterministic capture.
    /// F1..F7 toggle the layers interactively; see <see cref="DebugOverlay"/>.
    /// </summary>
    private void SetupOverlay(RuntimeWorldScene.Result result, RuntimeWorld world)
    {
        _overlay = new DebugOverlay(result, world);
        ApplyOverlaySpec(_args.Overlay);

        _inspector = new WorldInspectorPanel(result, world, _worldHost);
        _ui.AddChild(_inspector);
        if (_args.Inspect && _debugOutputEnabled)
        {
            CallDeferred(nameof(OpenInspectorDeferred));
        }
    }

    private void OpenInspectorDeferred()
    {
        if (_debugOutputEnabled) _inspector?.Toggle();
    }

    /// <summary>
    /// Reset the overlay and apply a comma-separated layer / <c>isolate:&lt;kind&gt;</c>
    /// spec (the <c>--overlay</c> syntax). Used for one-shot captures and per shot
    /// in a <c>--shots</c> run.
    /// </summary>
    private void ApplyOverlaySpec(string? spec)
    {
        if (_overlay is not { } overlay)
        {
            return;
        }

        overlay.Clear();
        if (spec is not { Length: > 0 })
        {
            return;
        }

        foreach (string token in spec.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith("isolate:", System.StringComparison.OrdinalIgnoreCase))
            {
                string want = token["isolate:".Length..];
                if (!overlay.TryIsolateKind(want))
                {
                    GD.PrintErr($"[OBPGame] unknown --overlay isolate kind '{want}' (valid: {string.Join(", ", DebugOverlay.IsolationCycle[1..])})");
                }
            }
            else if (System.Enum.TryParse<DebugOverlay.Layer>(token, ignoreCase: true, out var layer))
            {
                overlay.Set(layer, true);
            }
            else
            {
                GD.PrintErr($"[OBPGame] unknown --overlay token '{token}'");
            }
        }
    }

    /// <summary>
    /// Per-planet establishing shot for the regression capture set: a fixed
    /// camera at the native ship point, lifted and pulled back, looking at the
    /// trusted-geometry centre. Deterministic — no player, no physics settle.
    /// </summary>
    private void FrameShowcaseCamera(RuntimeWorld world)
    {
        RuntimeWorldScene.FrameCamera(_camera, world.Bounds, azimuthDegrees: 38f, elevationDegrees: 26f);
        // Pull back a touch and aim slightly above centre so the horizon / sky
        // reads instead of just the ground under the camera.
        var b = world.Bounds;
        var centre = RuntimeWorldScene.ToScene(
            (b.Min.X + b.Max.X) * 0.5, (b.Min.Y + b.Max.Y) * 0.5, (b.Min.Z + b.Max.Z) * 0.5);
        _camera.Position = centre + (_camera.Position - centre) * 1.15f;
        _camera.LookAt(centre + Vector3.Up * (float)((b.Max.Y - b.Min.Y) * 0.15), Vector3.Up);
        _camera.Current = true;
        _activeCamera = _camera;
        GD.Print($"[OBPGame] showcase camera at {_camera.Position}");
    }

    /// <summary>
    /// MobySequence showcase (<c>--anim-solo</c>): park a static camera on the
    /// bounds of the animated mobies alone. Deterministic, no world geometry.
    /// </summary>
    private void FrameAnimatedMobies(RuntimeWorld world)
    {
        var anim = world.AnimatedMeshes;
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        if (anim is { Count: > 0 })
        {
            // Frame the first instance only — a tight shot, not the whole spread.
            string firstInstance = anim[0].Name.Split("_t")[0];
            foreach (var am in anim.Where(a => a.Name.StartsWith(firstInstance)))
            {
                foreach (var frame in am.Frames)
                {
                    for (int i = 0; i + 3 <= frame.Length; i += 3)
                    {
                        minX = System.Math.Min(minX, frame[i]);
                        maxX = System.Math.Max(maxX, frame[i]);
                        minY = System.Math.Min(minY, frame[i + 1]);
                        maxY = System.Math.Max(maxY, frame[i + 1]);
                        minZ = System.Math.Min(minZ, frame[i + 2]);
                        maxZ = System.Math.Max(maxZ, frame[i + 2]);
                    }
                }
            }
        }
        else
        {
            minX = minY = minZ = -1;
            maxX = maxY = maxZ = 1;
        }

        var bounds = new OBP.Core.Math.ObpBounds(
            new OBP.Core.Math.Vec3(minX, minY, minZ),
            new OBP.Core.Math.Vec3(maxX, maxY, maxZ));
        RuntimeWorldScene.FrameCamera(_camera, bounds, azimuthDegrees: 35f, elevationDegrees: 18f);
        var centre = RuntimeWorldScene.ToScene((minX + maxX) * 0.5, (minY + maxY) * 0.5, (minZ + maxZ) * 0.5);
        _camera.Position = centre + (_camera.Position - centre) * 0.55f;
        _camera.LookAt(centre, Vector3.Up);
        _camera.Current = true;
        _activeCamera = _camera;
        GD.Print($"[OBPGame] anim-solo camera at {_camera.Position}, {anim?.Count ?? 0} animated mobies");
    }

    private void SpawnPlayer(RuntimeWorld world)
    {
        var b = world.Bounds;
        RuntimeSpawn? stagedStart = _worldPlayerEntry;

        var preferredStart = stagedStart ?? world.PreferredPlayerStart;
        bool explicitPlayerStart = stagedStart is not null || world.PlayerStart is not null;
        bool nativeStartUsable = preferredStart is { } s
            && (explicitPlayerStart
                || (s.X > b.Min.X && s.X < b.Max.X && s.Z > b.Min.Z && s.Z < b.Max.Z
                    && s.Y > b.Min.Y - 4 && s.Y < b.Max.Y + 40));

        Vector3 spawn;
        float yaw = 0f;
        if (nativeStartUsable && preferredStart is { } sp)
        {
            RuntimeSpawnScenePose pose = RuntimeSpawnSceneAdapter.ToScenePose(sp);
            spawn = pose.Position;
            yaw = pose.SceneYaw;
            string source = stagedStart is not null
                ? "staged source/authored entry"
                : explicitPlayerStart ? "explicit player start" : "native ship point";
            GD.Print($"[OBPGame] spawn from {source} ({sp.X:0},{sp.Y:0},{sp.Z:0})");
        }
        else
        {
            // ship-less levels (Aranos, the Aranos return): drop at the top
            // centre of the world bounds and let the capsule fall to collision.
            spawn = RuntimeWorldScene.ToScene(
                (b.Min.X + b.Max.X) * 0.5,
                b.Max.Y + 6.0,
                (b.Min.Z + b.Max.Z) * 0.5);
            GD.Print("[OBPGame] no in-bounds ship point — spawning at world-bounds centre");
        }

        // MobySequence showcase: stand right next to the first animated moby.
        if (_args.AnimFocus && world.AnimatedMeshes is { Count: > 0 } anim)
        {
            var f0 = anim[0].Frames[0];
            double cx = 0, cy = 0, cz = 0;
            int n = f0.Length / 3;
            for (int i = 0; i < f0.Length; i += 3) { cx += f0[i]; cy += f0[i + 1]; cz += f0[i + 2]; }
            var centre = RuntimeWorldScene.ToScene(cx / n, cy / n, cz / n);
            spawn = centre + new Vector3(0f, 2.5f, 9f);
            var look = centre - spawn;
            yaw = Mathf.Atan2(look.X, look.Z) + Mathf.Pi;
            GD.Print($"[OBPGame] anim-focus spawn near {anim[0].Name} at ({cx / n:0},{cy / n:0},{cz / n:0})");
        }

        if (_worldScenes?.Current?.Resources.CrateDiagnostics.TryGetFocusPose(world, out var crateSpawn, out var crateYaw) == true)
        {
            spawn = crateSpawn;
            yaw = crateYaw;
            GD.Print($"[crate-debug] focus spawn at {spawn}");
        }

        bool scripted = _args.CaptureFrame is not null;

        // When no native player/ship start is usable, deterministic captures
        // face the world centre because the bounds fallback has no authored heading.
        if (scripted && !nativeStartUsable)
        {
            var centre = RuntimeWorldScene.ToScene(
                (b.Min.X + b.Max.X) * 0.5, spawn.Y, (b.Min.Z + b.Max.Z) * 0.5);
            var toCentre = centre - spawn;
            if (toCentre.LengthSquared() > 1f)
            {
                yaw = Mathf.Atan2(toCentre.X, toCentre.Z) + Mathf.Pi;
            }
        }

        var player = new PlayerHost
        {
            Name = "PlayerHost",
            Scripted = scripted,
            ScriptedStill = _args.CrateFocus && !_args.CrateAutoStrike,
            ControlProfile = _playerControlProfile ?? PlayerHostProfiles.Diagnostic,
            // Source/authored placement clearance was staged with the world.
            InitialGroundSnapUpwardReach = _worldEntrySnapReach,
            // OBP deliberately reuses the retail-derived R&C1 controller in
            // all supported trilogy worlds; seed its facing from the authored
            // host spawn heading regardless of source game.
            Rac1CurrentYaw = -yaw,
            Position = spawn,
        };
        _playerRoot.AddChild(player);
        player.SetDebugOutputEnabled(_debugOutputEnabled);
        player.RotateY(yaw);
        _camera.Current = false;
        _activeCamera = player.Camera;
        _player = player;
        player.PlayerServicesPhysicsUpdated += StepPlayerServicesPhysics;
        if (_playerNative is not null) player.PlayerHealth = _playerNative.Life.Capture();
        player.DevelopmentControls.SaveMovementStateRequested += SaveMovementState;
        player.DevelopmentControls.LoadMovementStateRequested += () => StartMovementStateLoad(MovementStatePath);
        AttachPlayerAvatarVisual(player);
        ArmRac2GameplayHost(player);
        ArmRac1Gameplay(player);
        ArmRac3Gameplay(player);
    }

    // --- HUD ---------------------------------------------------------------

    private void EnsurePlayerHud()
    {
        if (_playerHud is not null && IsInstanceValid(_playerHud))
        {
            return;
        }

        _playerHud = new PlayerHud { DebugOutputEnabled = _debugOutputEnabled };
        _ui.AddChild(_playerHud);
    }

    private void UpdatePlayerHud()
    {
        if (_playerHud is not null && IsInstanceValid(_playerHud))
        {
            _playerHud.Render(_hudState.Current);
        }
    }

    private void EnsureWorldHud()
    {
        if (_worldHud is not null && IsInstanceValid(_worldHud))
        {
            _worldHud.Visible = _debugOutputEnabled;
            return;
        }

        _worldHud = new Label { Name = "WorldHud", Position = new Vector2(16, 12), Visible = _debugOutputEnabled, MouseFilter = Control.MouseFilterEnum.Ignore };
        _worldHud.AddThemeColorOverride("font_color", new Color(0.85f, 0.92f, 1f));
        _ui.AddChild(_worldHud);
    }

    private void UpdateWorldHud()
    {
        if (_worldHud is null) return;
        _worldHud.Visible = _debugOutputEnabled && _mode == Mode.World && _world is not null;
        if (!_worldHud.Visible)
        {
            _worldHud.Text = string.Empty;
            return;
        }
        if (_world is not { } w || _sceneResult is not { } r)
        {
            return;
        }

        var rac2Entry = w.Game == ObpSourceGame.Rac2 ? Rac2PlanetCatalogue.Find(w.LevelId) : null;
        string gameLabel = w.Game.SourceGame switch
        {
            ObpSourceGame.Rac1 => "Ratchet & Clank",
            ObpSourceGame.Rac2 => "Going Commando",
            ObpSourceGame.Rac3 => "Up Your Arsenal",
            _ => w.Game.ToString(),
        };
        string destinationName = w.PlanetName is { Length: > 0 }
            ? w.DisplayName
            : _activeDestination?.DisplayName ?? "Unknown destination";
        string container = _activeDestination?.NativeContainer ?? rac2Entry?.ContainerFile ?? "(native container unresolved)";
        string pos = "-";
        string ground = "-";
        if (_player is { } p && IsInstanceValid(p))
        {
            var gp = p.GlobalPosition;
            pos = $"{gp.X:0.0} {gp.Y:0.0} {gp.Z:0.0}";
            ground = p.IsOnFloor() ? "grounded" : "air";
        }

        var resources = _worldScenes?.Current?.Resources;
        string crateDebug = resources?.CrateDiagnostics.HudLine(resources.Rac2CrateBolts) ?? string.Empty;
        string rac2Gameplay = GetRac2GameplayHudLine();
        string rac1Gameplay = GetRac1GameplayHudLine();
        string rac3Gameplay = GetRac3GameplayHudLine();

        _worldHud.Text =
            $"Game: {gameLabel}   Build: {w.BuildId}\n" +
            $"Destination: {destinationName}\n" +
            $"Level id: {w.LevelId}   Container: {container}   (switch #{_worldSwitches})\n" +
            $"Player XYZ: {pos}   {ground}\n" +
            $"Render tris: {r.Triangles:N0}   Collision tris: {r.CollisionTriangles:N0}\n" +
            $"TIE meshes: {r.TieInstances}   Shrub meshes: {r.ShrubInstances}   Moby meshes: {r.MobyInstances}" +
            (r.AnimatedMobies > 0 ? $"   Animated mobies: {r.AnimatedMobies}" : "") +
            (r.DynamicObjects > 0 ? $"   Dynamic objects: {r.DynamicObjects}" : "") +
            $"\n{_overlay?.StatusLine() ?? "overlays: off"}   (F1 isolate · F2 tint · F3 collision · F4 bounds · F5 lights · F6 sky · F7 clear)" +
            (_worldHost.AudioDiagnostics.Count > 0 ? $"\n{_worldHost.AudioStatusLine}" : "") +
            (string.IsNullOrEmpty(crateDebug) ? "" : $"\n{crateDebug}") +
            (string.IsNullOrEmpty(rac2Gameplay) ? "" : $"\n{rac2Gameplay}") +
            (string.IsNullOrEmpty(rac1Gameplay) ? "" : $"\n{rac1Gameplay}") +
            (GetRac1MetropolisTaxiHudLine() is { Length: > 0 } taxiHint ? $"\n{taxiHint}" : "") +
            (string.IsNullOrEmpty(rac3Gameplay) ? "" : $"\n{rac3Gameplay}");
    }

    // --- smoke / plain scenes -------------------------------------------

    private void EnsurePlainEnvironment()
    {
        if (GetNodeOrNull("PlainEnvironment") is not null)
        {
            return;
        }

        AddChild(new WorldEnvironment
        {
            Name = "PlainEnvironment",
            Environment = new global::Godot.Environment
            {
                BackgroundMode = global::Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.04f, 0.05f, 0.08f),
                AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(1, 1, 1),
                AmbientLightEnergy = 1.0f,
            },
        });
    }

    // Temporary composition for retained Phase 1 callers. #340/#343 remove the nested pause entrypoints;
    // #346/#347/#348 move these bindings to their source/navigation/pause owners.

    private MovementDiagnosticHarness CreateMovementDiagnosticHarness()
    {
        var clock = new GodotDiagnosticFrameClock(this);
        var ports = new MovementScenarioPorts(new GodotMovementPlayer(() => _player, () => _world),
            new GodotMovementAnimation(new(() => _playerAvatarView, () => _playerAvatarAnimationController, () => _playerAvatarAnimationSink,
                CurrentPlayerAvatarSourceSequence, PlayerAvatarPresentationIsSynchronized, () => _playerAvatar?.AnimationBounds.Height ?? double.NaN)),
            new GodotMovementGeometry(this), new GodotMovementWorld(() => _world,
                () => _sceneResult?.CollisionBodies ?? 0, () => _rac1NovalisWater is not null),
            new GodotDiagnosticInputActions(), new GodotMovementPhysicalInput(), clock, message => GD.Print(message), message => GD.PrintErr(message));
        var water = new GodotMovementWater(() => [.. _rac1Enemies.Values.Select(enemy => ((Node3D)enemy.Node.Root, (Node3D)enemy.Body))],
            position => SampleRac1NovalisWaterHeight(GodotMovementPlayer.Engine(position)),
            () => _playerHud?.FindChild("Air", true, false) is Control air ? air.Visible : null, _rac1Nanotech.Probe);
        var environment = new DiagnosticEnvironmentalDeathFixture(() => new(_rac1Nanotech.EnvironmentalDeathGeneration,
            _rac1Nanotech.EnvironmentalRestartGeneration, _rac1Nanotech.Probe(), _rac1LastEnvironmentalDeathBoundary,
            GodotMovementPlayer.Copy(_rac1LastEnvironmentalDeathPosition), _rac1LastEnvironmentalDeathContactSeparation,
            MeasureRac1ContactSeparation(), _rac1CampaignSession.LevelCheckpoint?.AuthoredClass0.Yaw));
        var equipment = new DiagnosticControllerEquipmentFixture(() =>
        {
            var player = _player ?? throw new DiagnosticUnavailableException("The controller fixture player is unavailable.");
            return new(_rac1Weapons.Equipped, _rac1Weapons.FirstRangedAmmo,
                _rac1HeldItem.NativeItemId, _rac1WrenchView?.Visible == true, _rac1BombGloveView?.Visible == true,
                player.Rac1WrenchFrame.Active, player.Rac1AudioMovementAction, CurrentWeaponKey, [.. PlayerWeaponOrder()]);
        },
            OnRac1WeaponSelectionRequested, OnRac1WeaponCycleRequested);
        return new(ports, water, environment, equipment, _activeDestination,
            [.. _sources.Attached.Values.Select(source => new DiagnosticSourceRequirement(source.Game, source.Identity.BuildId))],
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. _sources.Attached.Values.Select(source =>
                new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]), clock), _hostOperations, _args.MovementCase);
    }
    internal Task CheckControllerWeaponInputAsync() => CreateMovementDiagnosticHarness().RunControllerAsync(_hostOperations.Token);
    private static void SetSyntheticNativeHeading(PlayerHost player, double yaw, double magnitude = 1) => GodotMovementCompatibility.Heading(player, yaw, magnitude);
    private static void AddSyntheticTraversalMesh(Node3D parent, string name, int material, params (Vector3 A, Vector3 B, Vector3 C)[] triangles) =>
        GodotRac1DiagnosticSurface.Add(parent, name, material, triangles);
    private static void SetAnalogueSmokeInput(float right, float forward) => GodotMovementCompatibility.Analogue(right, forward);
    private static void SetRac1SmokeConditionedInput(double right, double forward) => GodotMovementCompatibility.Conditioned(right, forward);
    private static void ClearMovementSmokeInput() => GodotMovementCompatibility.Clear();


    private FusionWeaponDiagnosticHarness CreateFusionWeaponDiagnosticHarness()
    {
        var weapons = CreateWeaponDiagnosticHarness();
        var fixture = new GodotFusionWeaponDiagnosticFixture(this, new()
        {
            Status = () => new(_fusion is not null, _fusionAssets is not null, _fusionAssets?.Problems.Count ?? -1, _fusionPreparation is not null,
                _fusionAssetFailure, _frame, FusionTestActive, FusionConfiguration?.LoadoutId, _playtest?.WaitingForEntry == true, _playtest is not null),
            Session = () => new(_fusion?.Selected, _fusion?.Hand.Equipment, _rac1HeldItem.Hand.WrenchHeld, _fusion?.LancerSession.Ammo ?? 0,
                _fusion?.CannonSession.Ammo ?? 0, _fusionAcceptedHits, _fusionAudioStarted, _fusionBeam?.Visible == true, _fusion?.CannonFrame?.BeamUpdatesRemaining ?? 0,
                _fusionLastDamage is { } damage ? new(damage.Shot.Weapon, damage.SourceDamage, damage.Kind) : null,
                _player?.Rac1WrenchFrame.Active == true, _fusion is not null && ReferenceEquals(_fusion.Rac1Inventory, _rac1CampaignSession.Weapons),
                ReferenceEquals(_rac1Weapons, _rac1CampaignSession.Weapons)),
            Transients = () => new(_fusionProjectiles.Count, _fusion?.LancerSession.ActiveShots.Count ?? 0, _fusionEffects.Count, _fusionPendingAudio.Count, _fusionAudioPlayers.Count),
            Menu = () => new(QuickSelectOpen, _quickSelect is { } menu ? [.. menu.Entries.Select(entry => entry.Weapon)] : [], _quickSelect?.SelectedWeapon),
            NativeSession = () => _fusion,
            Assets = () => _fusionAssets,
            ReplaceAssets = assets => _fusionAssets = assets,
            Preparation = () => _fusionPreparation,
            Sources = () => _sources,
            ReplaceSources = sources => _sources = sources,
            Views = () => [.. _fusionHeldViews],
            Animation = () => _playerAvatarAnimationController,
            Avatar = () => _playerAvatar,
            OriginalInventory = () => System.Text.Json.JsonSerializer.Serialize((_fusionOriginalCampaign ?? throw new DiagnosticUnavailableException("The isolated original campaign is unavailable.")).Weapons.CapturePersistentState()),
            CurrentInventory = () => System.Text.Json.JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState()),
            Prepare = () => PrepareSharedWeaponSources(force: true),
            Reset = () => ResetFusionWeapons(),
            Clear = () => ClearFusionTransients(),
            Reenter = () => { _args = _args with { FusionTest = false }; return OnDestinationChosen(_activeDestination!); },
            ShowDestinations = ShowDestinationSelector,
            Camera = () => _activeCamera,
            Player = () => _player,
            Forward = () => GodotMovementPlayer.Copy(EquipmentForward),
            SourceSequence = CurrentPlayerAvatarSourceSequence,
            Held = () => _rac1HeldItem.Hand.Equipment,
        }, new GodotDiagnosticFrameClock(this));
        return new(weapons, fixture,
            [.. TrilogySourceDefinitions.All.Select(source => new DiagnosticSourceRequirement(source.Game, source.Authority.BuildId))],
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. _sources.Attached.Values.Select(source => new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
                new GodotDiagnosticFrameClock(this)), _hostOperations, (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.PrintErr(message));
    }

    private Rac1CampaignDiagnosticHarness CreateRac1CampaignDiagnosticHarness()
    {
        var movement = CreateMovementDiagnosticHarness();
        var facts = new Rac1CampaignDiagnosticFacts(() =>
        {
            var campaign = _rac1CampaignSession.Campaign; var travel = _rac1CampaignSession.Travel;
            return new(campaign.CurrentLevel, travel.SelectedDestination, [.. campaign.GalacticMap.Take(campaign.AdmittedDestinationCount)], [.. campaign.LevelStates], travel.TransitionActive)
            { NovalisAvailable = campaign.CanSelectDestination(1), AridiaAvailable = campaign.CanSelectDestination(2) };
        }, () =>
        {
            var state = _rac1Nanotech.Probe();
            return new(state.Nanotech, state.IsDead, state.HasRecoveredEnvironmentalRespawn, _rac1Nanotech.EnvironmentalDeathGeneration);
        }, () => _rac1Missions?.Probe().SingleOrDefault(actor => actor.Key.NativeClassId == 774)?.Retired == true);
        var actions = new Rac1CampaignDiagnosticActions(ApplyRac1CampaignProgressionEvent, TravelRac1CampaignTo, () => _rac1CampaignSession.Travel.OpenPlanetMap(),
            level => OpenDestinationFromBootstrap($"rac1:LEVEL{level}"), TickRac1CampaignSaves, FlushRac1CampaignSaves);
        var checkpoints = new GodotRac1CampaignCheckpointFixture(() =>
        {
            var checkpoint = _rac1CampaignSession.LevelCheckpoint ?? throw new DiagnosticUnavailableException("R&C1 smoke checkpoint session is missing.");
            return new(checkpoint.NativeLevelId, checkpoint.AuthoredClass0, _world?.PlayerStart, checkpoint.ActiveCheckpoint is not null, checkpoint.ResolveEnvironmentalRestart().Kind);
        }, activation => (_rac1CampaignSession.LevelCheckpoint ?? throw new DiagnosticUnavailableException("The checkpoint session is unavailable.")).Activate(activation),
        () =>
        {
            var player = _player ?? throw new DiagnosticUnavailableException("The checkpoint fixture player is unavailable.");
            var dead = _rac1Nanotech.ApplyEnvironmentalDeathReset(); player.Rac1GameplayState = dead;
            return new(dead.Nanotech, dead.IsDead, dead.HasRecoveredEnvironmentalRespawn, _rac1Nanotech.EnvironmentalDeathGeneration);
        }, () => TryCompleteRac1EnvironmentalRestart(automatic: false));
        var save = new Rac1CampaignSaveDiagnostics(() => new(ObpSourceGame.Rac1, _sources.Get(ObpSourceGame.Rac1)?.Identity.BuildId,
            !string.IsNullOrWhiteSpace(_args.Rac1CampaignSavePath), OrdinaryPlayerSession, _rac1CampaignPersistence.Enabled, _rac1SaveBatcher is not null,
            _rac1CampaignRestoreWarning is not null, _rac1CampaignRestoreKind), () => _args.Rac1CampaignSavePath, () => _rac1CampaignPersistence.SavePath);
        return new(movement.Ports, new(facts, actions, checkpoints, save, new GodotRac1MissionDiagnosticUi(() => _ui)));
    }

    private Rac1LootDiagnosticHarness CreateRac1LootDiagnosticHarness()
    {
        var movement = CreateMovementDiagnosticHarness(); var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var targets = new GodotRac1WeaponTargetFixture(new(() => [.. _rac1CrateNodes], () => [.. _rac1Enemies.Values], IsRac1GameplayActive,
            (node, position) => GodotRac1CrateStaging.Place(node, position, _rac1FallingCrates),
            id => _rac1Class1440?.Get(id).Health ?? throw new DiagnosticUnavailableException("The robot health source is unavailable.")), worlds);
        var loot = new GodotRac1LootDiagnosticFixture(new()
        {
            Read = () => new(ObpSourceGame.Rac1, _rac1BoltWallet.Balance, Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons, _rac1BoltWallet).Bolts?.Balance,
                _rac1PickupSoundCount, _rac1Nanotech.Probe().Nanotech, _rac1Weapons.FirstRangedAmmo, _rac1BoltCrates.OutstandingPickupCount,
                [.. _rac1Debris.Select(piece => piece.Motion.NativeClassId)],
                [.. _rac1PickupMotion.Select(pair => new Rac1LootMotionDiagnostic(pair.Key, pair.Value.NativeClassId, pair.Value.NativeState, pair.Value.Position,
                    GodotMovementPlayer.Copy(FromEffectNative(pair.Value.Position))))],
                [.. _rac1AmmoPickups.Select(pickup => new Rac1AmmoPickupDiagnostic(new(ObpSourceGame.Rac1, pickup.Motion.Definition.Item), pickup.Motion.Definition.Amount,
                    pickup.Motion.Position, pickup.Motion.Velocity, pickup.Motion.DelayTicks, _rac1Weapons.GetAmmo(pickup.Motion.Definition.Item)))],
                [.. _rac1NanotechClouds.Select(cloud => cloud.Motion.Attracting)]),
            OriginalPickupMeshes = () => _rac1PickupNodes.Values.All(node => GodotWeaponModelFacts.Read(node).OriginalMeshes),
            Pending = () => _rac1PendingPlacedBolts.Count,
            Placed = () => _rac1PlacedBolts,
            PlacedNodes = () => _rac1PlacedBoltNodes,
            Motions = () => _rac1PickupMotion,
            PickupNodes = () => _rac1PickupNodes,
            Random = () => Rac1EffectRandom,
            ToNative = ToEffectNative,
            FromNative = FromEffectNative,
            Orientation = FromEffectOrientation,
            CrateBody = id => _rac1CrateBodies.ContainsKey(id),
            Strike = node => TryStrikeRac1Crate(node),
            Damage = () => _rac1Nanotech.ApplyDamage(Rac1DamageRuntime.FromClass749Attack(new(749, 149), new(34, 1))),
        }, targets, worlds);
        return new(movement.Ports, new(loot, targets, movement.Equipment, new GodotRac1LootDiagnosticCapture(this,
            () => _world?.LevelId ?? throw new DiagnosticUnavailableException("The loot capture world is unavailable."), () => _ui, () => _player, new GodotDiagnosticFrameClock(this))));
    }

    private WeaponDiagnosticHarness CreateWeaponDiagnosticHarness()
    {
        var movement = CreateMovementDiagnosticHarness();
        var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var targets = new GodotRac1WeaponTargetFixture(new(() => [.. _rac1CrateNodes], () => [.. _rac1Enemies.Values],
            IsRac1GameplayActive, (node, position) => GodotRac1CrateStaging.Place(node, position, _rac1FallingCrates),
            id => _rac1Class1440?.Get(id).Health ?? throw new DiagnosticUnavailableException("The robot health source is unavailable.")), worlds);
        var wrench = new GodotRac1WrenchDiagnosticFacts(new(() => _player, () => _rac1WrenchView, () => _playerAvatar,
            () => _playerAvatarAnimationController, () => _rac1WrenchHostGeneration, () => _rac1WrenchThrow.Active,
            WrenchWorldPosition, () => _rac1BombGloveView?.Visible == true));
        var capture = new GodotWeaponDiagnosticCapture(this, () => _player, () => _sceneResult?.Root, () => _ui, new GodotDiagnosticFrameClock(this));
        var bomb = new GodotRac1BombAcceptanceFixture(new(
            () =>
            {
                var glove = _rac1BombGloveView; var staged = _rac1StagedBombView;
                bool glovePresent = glove is not null && IsInstanceValid(glove), stagedPresent = staged is not null && IsInstanceValid(staged);
                return new(glovePresent && glove!.Visible, stagedPresent && staged!.Visible,
                    _rac1WrenchView is { } view && IsInstanceValid(view) && view.Visible,
                    _rac1BombGloveAsset?.VertexCount ?? 0, _rac1BombGloveAsset?.TriangleCount ?? 0,
                    glovePresent ? GodotMovementPlayer.Copy(glove!.BombLocal) : System.Numerics.Vector3.Zero,
                    stagedPresent ? GodotMovementPlayer.Copy(staged!.Position) : System.Numerics.Vector3.Zero,
                    glovePresent && stagedPresent && staged!.Position.IsEqualApprox(glove!.BombLocal));
            },
            () => new(_rac1BombFragments.Count, _rac1BombBursts.Count, _rac1Particles?.Count ?? 0, _rac1BombImpactCount,
                _rac1Particles is { } particles ? [.. particles.Live.Select(particle => particle.Type)] : []),
            () =>
            {
                var shot = _rac1Projectiles.Single();
                bool originalModel = GodotWeaponModelFacts.Read(shot.Value.Node).OriginalMeshes;
                return new GodotRac1BombAcceptanceWitness(new(worlds), () => new(shot.Key, FusionWeaponSession.BombGlove,
                    shot.Value.Motion.Position, shot.Value.Motion.Velocity, shot.Value.Motion.NativeState, shot.Value.Motion.Flying, originalModel));
            }, BombHasLineOfSight, ProbeBombWorldContact,
            amount => _rac1Weapons.TryConsumeAmmo((int)Rac1WeaponId.FirstRanged, amount), () => _player));
        var transients = new GodotWeaponDiagnosticTransientFixture(worlds, () => DiagnosticCleanup.Run("Weapon fixture transient cleanup",
            () => { foreach (var (id, projectile) in _rac1Projectiles.ToArray()) RetireBomb(id, projectile); },
            ResetRac1BombPresentation, ResetRac1AdditionalWeapons, () => ClearFusionTransients()));
        async Task<bool> LoadWeaponStateAsync(string path, CancellationToken token) => await LoadMovementStateAsync(path, cancellationToken: token) is not null;
        var reproduction = new GodotWeaponMovementStateFixture(() => _movementStateLoading, () => CaptureMovementState(), LoadWeaponStateAsync,
            name => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "captures", "repros", name)), transients);
        return new(movement.Ports, wrench, new(movement.Equipment, targets, CreateRac1EncounterDiagnosticHarness().Combat, capture, bomb, reproduction,
            CreateRac1AdditionalWeaponDiagnosticFixture(worlds), CreateRac1ArsenalDiagnosticFixture(worlds, targets)),
            transients);
    }

    private GodotRac1AdditionalWeaponDiagnosticFixture CreateRac1AdditionalWeaponDiagnosticFixture(IDiagnosticWorldObservations worlds) => new(new(
        () => new(_rac1WeaponPreviewInventory is not null, _rac1CampaignSession.Weapons.Owns(Rac1WeaponId.Blaster), _rac1AdditionalWeapons?.Cargo.Count ?? 0,
            _rac1EquipmentUseTicks, _rac1WeaponShotsFired, _rac1PyroEmitting, _rac1WeaponLoop, _hudState.Current.CurrentWeapon,
            GodotMovementPlayer.Copy(EquipmentForward), _player?.Rac1RemoteControlActive == true, _rac1MissileCamera is not null,
            _rac1MissileCamera?.Current == true, _player?.Camera.Current == true, _rac1ParticleView?.RenderedCount ?? 0, _rac1ParticleView?.DrawCalls ?? 0),
        weapon => _rac1EquipmentViews.GetValueOrDefault(weapon), weapon => EquipmentMuzzle(weapon), item => _rac1Weapons.GetAmmo(item),
        (nativeClass, sound) => _rac1PickupAssets?.Sounds?.TryGetValue((nativeClass, sound), out var voice) == true && voice.Clip.InterleavedPcm16.Any(sample => sample != 0),
        () => [.. _rac1WeaponSoundTrace.Select(cue => new Rac1WeaponAudioDiagnostic(ObpSourceGame.Rac1, cue.Tick, cue.Class, cue.Sound, cue.Loop))],
        () => [.. _rac1EquipmentShots.Select(shot => new GodotWeaponDiagnosticWitness<Rac1EquipmentShotDiagnostic>(new(worlds), () => new(
            new(ObpSourceGame.Rac1, (int)shot.Weapon), shot.Id, GodotMovementPlayer.Copy(shot.Position), shot.Grounded, GodotWeaponModelFacts.Read(shot.Node))))],
        () => [.. _rac1DoomAgents.Select(agent => new GodotWeaponDiagnosticWitness<Rac1DoomAgentDiagnostic>(new(worlds), () => new(
            new(ObpSourceGame.Rac1, (int)Rac1WeaponId.GloveOfDoom), agent.Id, GodotMovementPlayer.Copy(agent.Body.GlobalPosition), agent.Motion.Wander,
            GodotWeaponModelFacts.Read(agent.View))))], () => _rac1WeaponPreviewInventory is not null, SetRac1WeaponPreview));

    private GodotRac1ArsenalDiagnosticFixture CreateRac1ArsenalDiagnosticFixture(IDiagnosticWorldObservations worlds, GodotRac1WeaponTargetFixture targets) => new(new(
        () => new(_rac1RynoSalvo.Remaining, _rac1ArsenalId, _rac1Drones.Count, _rac1ChickenCargo.Count,
            [.. _rac1ArsenalBeams.Select(beam => beam.Visible)], [.. _rac1ArsenalBeams.Select(beam => beam.MaterialOverride is ShaderMaterial)]),
        () => [.. _rac1ArsenalMissiles.Select(missile => new GodotWeaponDiagnosticWitness<Rac1ArsenalMissileDiagnostic>(new(worlds), () => new(
            new(ObpSourceGame.Rac1, (int)missile.Weapon), missile.Id, GodotWeaponModelFacts.Read(missile.View))))],
        () => [.. _rac1Decoys.Select(decoy => new GodotRac1DecoyDiagnosticFixture(new(worlds), () => new(new(ObpSourceGame.Rac1, (int)Rac1WeaponId.DecoyGlove),
            GodotWeaponModelFacts.Read(decoy.View), decoy.Grounded, decoy.RetiringTicks), () => decoy.GroundTicks = Rac1ArsenalRules.DecoyLifetime - 1))],
        () => [.. _rac1Chickens.Values.Select(chicken => new Rac1ChickenDiagnostic(targets.Key(chicken.Enemy.Node), IsRac1GameplayActive(chicken.Enemy.Node),
            chicken.Enemy.RewardEmitted, GodotWeaponModelFacts.Read(chicken.View)))],
        () =>
        {
            foreach (var drone in _rac1Drones.Where(drone => IsInstanceValid(drone.View))) drone.View.QueueFree();
            _rac1Drones.Clear();
        },
        () => _rac1WeaponWorldTick = 0,
        (actor, player) =>
        {
            targets.Read(actor); return GodotMovementPlayer.Copy(ArsenalEnemyGoal(
            _rac1Enemies.TryGetValue(actor.Target.InstanceIndex, out var enemy) ? enemy : throw new DiagnosticUnavailableException("The arsenal fixture enemy is unavailable."),
            GodotMovementPlayer.Engine(player)));
        }));

    private Rac1EncounterDiagnosticHarness CreateRac1EncounterDiagnosticHarness()
    {
        var actors = new GodotRac1EnemyDiagnosticFixture(new(() => [.. _rac1Enemies.Values],
            () => _rac1Class1440, () => _rac1Peckbots, () => _rac1Birdbots, () => _rac1Hostiles,
            () => new(_rac1Enemies.Count, _rac1Class1440?.RegisteredCount ?? 0, _rac1Peckbots?.RegisteredCount ?? 0,
                _rac1Birdbots?.RegisteredCount ?? 0, _rac1BirdbotShotCount, _rac1Nanotech.Probe().Nanotech,
                _worldSwitches, _rac1CombatRetry?.Visible == true), () => _player, node => TryStrikeRac1Hostile(node),
            TryGroundRac1Enemy, EnemyForward, EnemySocket, EnemyRayTouchesPlayer,
            id => _rac1BirdbotShots.Values.Any(shot => shot.OwnerInstance == id)), new GodotDiagnosticWorldObservations(WorldScenes));
        var movement = CreateMovementDiagnosticHarness();
        var loot = new GodotRac1EncounterLoot(() => new(_rac1BoltWallet.Balance, _rac1BoltPickups.OutstandingCount,
            [.. _rac1PickupMotion.Select(pair => new Rac1DiagnosticPickup(pair.Key, pair.Value.NativeClassId,
                pair.Value.Position, pair.Value.NativeState, pair.Value.CollectionStep))],
            [.. _rac1Debris.Select(piece => piece.Motion.NativeClassId)]));
        var lifecycle = new GodotRac1EncounterLifecycle(() => _rac1Nanotech.Probe(), ApplyRac1EnemyPlayerDamage, () => _rac1CombatRetry);
        var approach = new GodotRac1WrenchApproachFixture(() => _player, _rac1Wrench.ResolveFirstSwingFacing, Rac1WrenchHostRootHeight);
        var combat = new Rac1CombatScenarioPorts(actors, actors, CreateRac1CombatDiagnosticFixture(actors), approach, movement.Equipment,
            new DiagnosticNavigationActions(destination => { OpenDestinationFromBootstrap(destination); return _world is not null && _currentDestinationId == destination; }),
            CreateUiDiagnosticHarness(), movement.Environment, lifecycle);
        return new(movement.Ports, loot,
            new GodotRac1EnemyDiagnosticCapture(this, () => _ui, () => _player, new GodotDiagnosticFrameClock(this)),
            CreateRac1AridiaDiagnosticFixture(actors), CreateRac1FrogDiagnosticFixture(actors), combat);
    }

    private GodotRac1CombatDiagnosticFixture CreateRac1CombatDiagnosticFixture(GodotRac1EnemyDiagnosticFixture actors) => new(actors,
        new GodotDiagnosticWorldObservations(WorldScenes), new()
        {
            Crates = () => [.. _rac1CrateNodes],
            Hostiles = () => [.. _rac1HostileNodes.Values],
            World = () => _world,
            Player = () => _player,
            Opening = () => new(_args.Rac1CampaignPersistenceRequested, _rac1CampaignPersistence.Enabled, _rac1CampaignRestoreKind,
                _rac1CampaignSession.Campaign.CurrentLevel, _rac1CampaignSession.Campaign.AdmittedDestinationCount, _rac1Weapons.OwnsFirstRanged),
            Population = () => new(_world?.DynamicObjects?.Count(source => source.NativeClassId == Rac1Class749Hostile.NativeClassId) ?? 0,
                _rac1Class749PresentationNodes.Count(node => IsInstanceValid(node.Root) && node.Root.Visible), _rac1HostileNodes.Count, _rac1Hostiles.RegisteredCount),
            Presentation = () =>
            {
                var wrench = _rac1WrenchView; bool present = wrench is not null && IsInstanceValid(wrench);
                return new(_hudState.Current.CurrentWeapon, present, present && wrench!.Visible,
                    present && _playerAvatarView is not null && wrench!.GetParent() == _playerAvatarView);
            },
            Loot = () => new(_rac1BoltCrates.DestroyedCrateCount, _rac1BoltCrates.CollectedBolts,
                [.. _rac1PickupNodes.Select(pair => new Rac1CombatPickupDiagnostic(pair.Key, IsInstanceValid(pair.Value),
                    IsInstanceValid(pair.Value) ? GodotMovementPlayer.Copy(pair.Value.GlobalPosition) : System.Numerics.Vector3.Zero, _rac1BoltCrates.OwnsPickup(pair.Key)))]),
            Bombs = () => new(_rac1BombFireRequested, [.. _rac1Projectiles.Keys],
                _rac1LastBombContactResolution is { } contact ? new(contact.ProjectileId, [.. contact.DamageResults]) : null),
            BombWitness = () =>
            {
                var shot = _rac1Projectiles.FirstOrDefault();
                return shot.Value is null ? null : new GodotRac1BombDiagnosticWitness(() => new(shot.Key,
                    new(ObpSourceGame.Rac1, (int)Rac1WeaponId.FirstRanged), shot.Value.Motion.Position, shot.Value.Motion.NativeState));
            },
            Active = IsRac1GameplayActive,
            Forward = EnemyForward,
            Presence = SetRac1HostedPresence,
            PrimaryAttack = OnRac1PrimaryAttackRequested,
            Damage = () => _rac1DamageTransport,
        });

    private GodotRac1AridiaDiagnosticFixture CreateRac1AridiaDiagnosticFixture(GodotRac1EnemyDiagnosticFixture actors)
    {
        return new(actors, new(() => _rac1MobyRuntime, () => _rac1AridiaEnemies, session => _rac1AridiaEnemies = session,
            () => _rac1AridiaClipVolumes.Count, index => _rac1AridiaClipVolumes[index].Centre,
            (index, point) => _rac1AridiaClipVolumes[index].Contains(point),
            id => _rac1ConstructorbotNativeWaypoints.TryGetValue(id, out var point) ? point : null,
            id => _rac1ConstructorbotRoamingTargets.TryGetValue(id, out var point) ? point : null,
            id => _rac1ConstructorbotRoamExitFlags.TryGetValue(id, out var flags) ? flags : null,
            id => _rac1ConstructorbotRoamingTargets.Remove(id),
            id => _rac1ConstructorbotNativeLiftCounts.GetValueOrDefault(id),
            id => _rac1ConstructorbotSweepProbeCounts.GetValueOrDefault(id),
            id => _rac1ConstructorbotStateThreeVerticalSteps.GetValueOrDefault(id), AridiaHostUpdateRadius,
            ProbeRac1ConstructorbotStateThreeFloor, ProbeRac1AridiaWalkerGroundLift, ProbeRac1AridiaWalkerSupport,
            ProbeRac1AridiaWalkerSpherePushouts, MoveRac1AridiaWalkerContact, SelectRac1EnemySequence, ActivateNearbyRac1AridiaShark));
    }

    private GodotRac1FrogDiagnosticFixture CreateRac1FrogDiagnosticFixture(GodotRac1EnemyDiagnosticFixture actors) => new(actors,
        new GodotRac1DiagnosticWorldScope(new GodotDiagnosticWorldObservations(WorldScenes)), new()
        {
            Frogs = () => _rac1Frogs,
            Shuttle = () => _rac1FrogShuttle,
            Waves = () => _rac1FrogWaves,
            WaveControllers = () => [.. _rac1FrogWaveNodes.Keys],
            Carriers = () => System.Collections.Immutable.ImmutableDictionary.CreateRange(_rac1FrogCarrierChildren),
            HasPickupModel = nativeClass => _rac1PickupAssets?.Models.ContainsKey(nativeClass) == true,
            Counts = () => (_rac1CrateNodes.Count, _rac1PickupMotion.Count, _rac1Debris.Count),
            TickFrog = TickRac1Frog,
            TickCarrier = TickRac1FrogCarrier,
            MeleeTouchesPlayer = EnemyMeleeTouchesPlayer,
            ResolveWrench = ResolveRac1AnimatedWrenchContacts,
            TickShuttle = TickRac1FrogShuttleSwitch,
            ApplyShuttle = ApplyRac1FrogShuttleSelection,
            TickWaves = TickRac1FrogWaves,
            Queen = CreateRac1QueenDiagnosticBindings(),
        });

    private GodotRac1QueenDiagnosticBindings CreateRac1QueenDiagnosticBindings() => new()
    {
        Group = () => _rac1FrogQueen,
        Recovery = () => _rac1QueenRecovery,
        Entrance = () => _rac1QueenEntrance,
        Flight = () => _rac1QueenFlight,
        PendingCamera = () => _rac1QueenPendingCamera,
        Linked = () => _rac1QueenLinkedGate,
        Tuning = () => _rac1QueenGateNative,
        Animation = () => _rac1QueenAnimation,
        Node = () => _rac1QueenNode,
        AuthoredNativeZ = () => _rac1QueenAuthoredNativeZ,
        Parents = () => System.Collections.Immutable.ImmutableDictionary.CreateRange(_rac1QueenLinkedNodes),
        Children = () => System.Collections.Immutable.ImmutableDictionary.CreateRange(_rac1QueenLinkedChildVisuals),
        WorldRoot = () => _sceneResult?.Root,
        Player = () => _player,
        ActiveCamera = () => _activeCamera,
        QueenCamera = () => _rac1QueenScriptedCamera,
        GuidedCamera = () => _rac1MissileCamera,
        DamageSequence = () => _rac1DamageTransport.Sequence,
        ForgetWrenchContact = key => _rac1WrenchHitTargets.Remove(key),
        ResolveWrench = ResolveRac1QueenWrenchContacts,
        InvalidateEquipmentGeometry = () => _rac1EquipmentGeometryFrame = ulong.MaxValue,
        GeometryContainsQueen = () => _rac1QueenNode is { } queen && EquipmentGeometry.Any(entry => entry.Node == queen),
        AimTargetAdmitted = () => _rac1QueenNode is { } queen && IsRac1WeaponAimTarget(queen),
        TickFlight = () => TickRac1QueenFlight(),
        TickRecovery = TickRac1QueenRecovery,
        TickLinked = TickRac1QueenLinkedChildren,
        SynchronizeLinked = SynchronizeRac1QueenLinkedParents,
        ObserveFrogEvent = ObserveRac1QueenFrogEvent,
        BeginGuided = BeginRac1GuidedCamera,
        EndGuided = EndRac1GuidedCamera,
        EndQueen = EndRac1QueenScriptedCamera,
    };

    private CaptureDiagnosticHarness CreateCaptureDiagnosticHarness()
    {
        if (_captureDiagnostics is { } active) return active;
        var clock = new GodotDiagnosticFrameClock(this);
        var recorder = new GodotCaptureRecorderDiagnosticFixture(() => new()
        {
            RecorderPresent = _playtest is not null,
            WaitingForEntry = _playtest?.WaitingForEntry == true,
            PreparingWorld = _playtest?.PreparingWorld == true,
            Finished = _playtest?.Finished == true,
            PlayerPresent = _player is not null,
            LevelId = _world?.LevelId,
            Destination = _currentDestinationId,
            Configuration = _playerSession?.Capture().Configuration,
            ProfileJson = _playerSession is null ? "" : System.Text.Json.JsonSerializer.Serialize(OBP.Fusion.ProfilePlayer.Capture(_playerSession.Capture()) with { Generation = 0 }),
            GenuineAvatar = _playerAvatarView is not null && _playerAvatar?.Identity == _playerSession?.Capture().Configuration.Avatar,
            NativeProfile = _player?.ControlProfile == PlayerHostProfiles.Native,
            PhysicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
            HudHealth = _hudState.Current.Health is not null,
            HudBolts = _hudState.Current.Bolts is not null,
            HudVisible = _playerHud?.Visible == true,
            QuickSelectOpen = QuickSelectOpen,
            PauseOpen = PauseMenuOpen,
            TreePaused = GetTree().Paused,
            PhysicsTick = _playerPhysicsClock?.Tick ?? 0,
            PlayerNode = _player?.GetInstanceId(),
            Shots = _fusion?.ShotsFired ?? 0,
        }, (width, height) => GetWindow().Size = new(width, height), () => _args.RecordPlaytest,
            path => _playtest!.RequestBookmark(path), () => _playtest!.Save());
        var navigation = new DiagnosticNavigationActions(destination => destination == "obp:fusion-lab" ? EnterFusionLab() :
            TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native));
        var equipment = new CaptureEquipmentActions((slot, item) => AssignPlayerQuickSelect(slot, item).Accepted, item => SelectPlayerEquipment(item).Accepted);
        var reports = new GodotBugReporterDiagnosticFixture(this, () => _bugReportUi, () => BugReportModalOpen,
            () => new(GodotMovementPlayer.Copy(_player!.GlobalPosition), _rac1BoltWallet.Balance,
                System.Text.Json.JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState()), QuickSelectOpen, PauseMenuOpen, GetTree().Paused),
            () => _bugReports, () => _bugSessionId, () => _playtest);
        var acceptance = new OwnedDiagnosticAcceptanceReporter(() => _currentDestinationId, () => _args.AcceptanceOut,
            () => new(DisplayServer.GetName() == "headless", [.. _sources.Attached.Values.Select(source => source.Identity)],
                _playerSession?.Capture().Configuration, [.. ReadPlayerEquipment().Select(entry => new AcceptanceEquipment(entry.Definition.Id,
                    entry.Definition.SourceGame, entry.Definition.Implementation, entry.Usability, entry.Reason))]),
            message => GD.PrintErr(message), (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code));
        var captures = ProjectSettings.GlobalizePath("res://../captures");
        var ports = new CaptureDiagnosticPorts(recorder, equipment, reports, CreateMovementStateDiagnosticFixture,
            CreateTrilogyPlayerDiagnosticFixture(), CreateTrilogyActorDiagnosticFixture(), navigation,
            token => CreateFusionWeaponDiagnosticHarness().RunRecordingAsync(token),
            () => CaptureDiagnosticIsolation.Isolated(SharedProfilePath, captures) && CaptureDiagnosticIsolation.Isolated(_bugReports?.Root, captures));
        return _captureDiagnostics = new(new(() => [.. _sources.Attached.Values.Select(source => new DiagnosticSourceRequirement(source.Game, source.Identity.BuildId))],
                [.. TrilogySourceDefinitions.All.Select(source => new DiagnosticSourceRequirement(source.Game, source.Authority.BuildId))]),
            ports, CreateMovementDiagnosticHarness().Ports, new GodotCaptureDiagnosticInputFactory(), new GodotCaptureDiagnosticTime(), acceptance,
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. _sources.Attached.Values.Select(source => new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]), clock),
            _hostOperations, (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.Print(message), message => GD.PrintErr(message));
    }

    private IMovementStateDiagnosticFixture CreateMovementStateDiagnosticFixture() => new GodotMovementStateDiagnosticFixture(new()
    {
        Player = () => _player,
        Enemies = () => [.. _rac1Enemies.Values.Select(enemy => enemy.Node)],
        Crates = () => [.. _rac1CrateNodes],
        Active = IsRac1GameplayActive,
        Damage = ApplyRac1EnemyWeaponDamage,
        Strike = node => TryStrikeRac1Crate(node),
        Read = () => new([.. _rac1Enemies.Values.Select(enemy => new MovementStateEnemyDiagnostic(new(ObpSourceGame.Rac1, enemy.Node.Source.NativeClassId,
                enemy.Node.Source.InstanceIndex), IsRac1GameplayActive(enemy.Node), enemy.Node.Root.Visible, enemy.Body.CollisionLayer, enemy.RewardEmitted))],
            [.. _rac1CrateNodes.Select(node => new MovementStateCrateDiagnostic(new(ObpSourceGame.Rac1, node.Source.NativeClassId, node.Source.InstanceIndex),
                IsRac1GameplayActive(node), _rac1CrateBodies.ContainsKey(node.Source.InstanceIndex)))], _rac1BoltCrates.DestroyedCrateCount,
            _rac1Weapons.Equipped, _rac1Weapons.FirstRangedAmmo, _rac1BoltWallet.Balance, _rac1PickupMotion.Keys.Any(id => id > 0)),
        Consume = () => { OnRac1WeaponSelectionRequested(Rac1WeaponId.FirstRanged); return _rac1Weapons.TryUseEquipped(); },
        Credit = amount => _rac1BoltWallet.Credit(amount),
        Loading = () => _movementStateLoading,
        Override = () => _movementStatePathOverride,
        SetOverride = path => _movementStatePathOverride = path,
        Path = () => MovementStatePath,
        Load = async (path, frames, token) =>
        {
            var result = await LoadMovementStateAsync(path, recordFrames: frames, cancellationToken: token); token.ThrowIfCancellationRequested();
            if (result is null) throw new InvalidOperationException("Movement state load/replay failed.");
            return [.. result.Select(sample => new MovementStateReplayDiagnostic(sample.Position, sample.ControlHeading))];
        },
        Forward = async (frames, token) => [.. (await RecordForwardMovementReplayAsync(frames, token)).Select(sample => new MovementStateReplayDiagnostic(sample.Position, sample.ControlHeading))],
    });

    private ITrilogyPlayerDiagnosticFixture CreateTrilogyPlayerDiagnosticFixture() => new GodotTrilogyPlayerDiagnosticFixture(this, new()
    {
        Session = () => _playerSession!,
        Player = () => _player,
        Scene = () => _sceneResult!.Root,
        Read = () => new(OrdinaryPlayerSession, _playerNative is not null, PlayerPresentationAssets is not null, _rac1PlayerAudio is not null,
            _rac1HeldItem.Hand.WrenchHeld, _playerPhysicsClock?.Tick ?? 0, _fusion?.ShotsFired ?? 0, _pauseMenu?.UniversalEnabled == true,
            _pauseMenu?.RetailEnabled == true, _rac1PyroEmitting, _rac1Particles?.Live.Any(particle => particle.Type == 12) == true,
            _rac1ParticleView?.RenderedCount ?? 0, _playerNative?.Life.Capture().Current ?? 0, GodotMovementPlayer.Copy(_player?.GlobalPosition ?? Vector3.Zero),
            _player?.Rac1CurrentYaw ?? 0, _fusionAcceptedHits, _fusionProjectiles.Count, _fusionAudioPlayers.Count),
        ResetAudio = () => { _rac1PlayerAudioRandom = Rac1PickupRandom.FromSeed(1440); _selectedPlayerAudioTrace.Clear(); },
        Audio = () => [.. _selectedPlayerAudioTrace.Select(cue => new TrilogyAudioDiagnostic(cue.Source, cue.Build, cue.Level, cue.Class, cue.Sound, cue.LevelSound, cue.Loop, cue.Tick))],
        Trial = token => CreatePlayerDiagnosticHarness().CaptureTrialAsync(token),
        OpenPause = TryOpenPauseMenu,
        PauseSection = section => _pauseMenu!.ShowUniversalSection(section),
        ClosePause = () => TryClosePauseMenu(),
        Equipment = () => [.. ReadPlayerEquipment().Select(entry => new TrilogyEquipmentDiagnostic(entry.Definition.Id, entry.Definition.Item,
            entry.Selectable, entry.Reason, entry.Definition.Item is { } item ? _fusion!.Status(item).Ammo : 0))],
        Respawn = () => _playerNative!.Life.Respawn(),
    });

    private ITrilogyActorDiagnosticFixture CreateTrilogyActorDiagnosticFixture() => new GodotTrilogyActorDiagnosticFixture(new()
    {
        World = () => _actorWorld,
        Sources = () => ActorSources,
        Rac2Animation = () => _rac2OpeningMsr1.Count == 1 && _rac2OpeningMsr1.Values.Single().Node.AnimationSample is not null,
        Pump = TickActorPreparation,
        Reset = id => ResetPlacedActor(id).Accepted,
        ResetEncounter = () => { ClearFusionTransients(); _rac1EffectRandom = Rac1PickupRandom.FromSeed(1440); },
        ClearSources = ClearActorSourceCaches,
        ResetSharedAssets = ResetSharedWeaponAssets,
        Prepare = PrepareActorSource,
        DamageTarget = key => _playerDamageActors.Contains(key),
        Scene = () => _sceneResult!.Root,
        Enemies = () => _rac1Enemies.Count,
        Hostiles = () => _rac1HostileNodes.Count,
        FaultBindings = () => new(_rac1Hostiles, _rac1HostileNodes, _rac1Enemies, ObserveRac1Actor, ApplyRegisteredRac1Damage, ClearRegisteredActorTransients,
            index => _rac1Captures.RemoveAll(capture => capture.Key == new Rac1MobyRuntimeKey(749, index)),
            _ => throw new InvalidDataException("actor construction fixture"), _ => { }),
    });

    private UiDiagnosticHarness CreateUiDiagnosticHarness()
    {
        var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var menus = new GodotDiagnosticMenus(this, _worldRoot,
            () => new(_mode == Mode.Title, _mode == Mode.Selector, _mode == Mode.World, _mode == Mode.Picker,
                _diagnosticSourceAccess, _sourceManager is not null, _titleScreen is not null, _world is not null,
                _currentDestinationId, _profileDocument?.Destination, _profileWriteBlocked, null, false, false),
            () => new([], _player?.VisualRoot.FallbackVisible == true, _sceneResult is not null,
                _player is not null && IsInstanceValid(_player), _playerAvatarView is not null && IsInstanceValid(_playerAvatarView),
                _player is not null && ReferenceEquals(_activeCamera, _player.Camera) && _player.Camera.Current, _world?.LevelId),
            surface => surface switch
            {
                DiagnosticMenuSurface.Title => _titleScreen,
                DiagnosticMenuSurface.WorldSelect => _destinationSelector,
                DiagnosticMenuSurface.Overlay => _ui,
                DiagnosticMenuSurface.World => _sceneResult?.Root,
                DiagnosticMenuSurface.Pause => _pauseMenu,
                _ => null
            },
            () => _titleScreen, () => _destinationSelector, () => _pauseMenu,
            ShowTitleScreen, ShowDestinationSelector, TryHandleInteractiveBack, TryOpenPauseMenu, TryClosePauseMenu);
        return new(_args, !_diagnosticSourceAccess,
            [.. TrilogySourceDefinitions.All.Select(source => new DiagnosticSourceRequirement(source.Game, source.Authority.BuildId))], menus,
            new DiagnosticPlayerContinuityAdapter(() => _playerSession?.Capture() ?? throw new DiagnosticUnavailableException("The player session is missing.")),
            new DiagnosticProfileEvidence(() => SharedProfilePath, SaveSharedProfile),
            new DiagnosticSourceRepairFixture(_sources.Remove, RequireOrdinarySourceSetup, () => _rac1CampaignSession),
            new GodotRetailPauseFixture(this, () => _player, () => _pauseMenu, () => PauseMenuOpen, TryOpenPauseMenu, TryClosePauseMenu, () => _rac1Weapons, worlds), worlds,
            new DiagnosticNavigationActions(destination => { OpenDestinationFromBootstrap(destination); return _world is not null && _currentDestinationId == destination; }),
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. _sources.Attached.Values.Select(source => new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
                new GodotDiagnosticFrameClock(this)), _hostOperations,
            (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.Print(message), message => GD.PrintErr(message));
    }

    private PlayerSessionDiagnosticHarness CreatePlayerDiagnosticHarness()
    {
        var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var player = new GodotPersistentPlayerFixture(new(() => OrdinaryPlayerSession, () => _playerSession, () => _playerNative,
            () => _fusion, () => _player, () => _playerAvatarView, () => _playerAvatar?.Identity, _playerRoot, _worldRoot,
            () => (_fusionProjectiles.Count, _fusionAudioPlayers.Count, _fusionEffects.Count), () => _activeDestination is not null, () => _currentDestinationId),
            worlds, SelectWeaponKey, SetRac1WeaponPreview);
        var sources = new DiagnosticSourceAttachmentFixture(_sources, RefreshPlayerSourceAvailability);
        var navigation = new DiagnosticNavigationActions(destination => destination == "obp:fusion-lab" ? EnterFusionLab() :
            TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native));
        var menus = CreateUiDiagnosticHarness().Menus;
        var equipment = new DiagnosticPlayerEquipmentFixture(ReadPlayerEquipment, SelectPlayerEquipment, AssignPlayerQuickSelect,
            UnequipPlayerEquipment, SetPlayerGadgetEnabled, () => _playerNative?.GadgetEnabled(OBP.RAC1.Player.Rac1TraversalEquipmentId.HeliPack) == true);
        var ui = new DiagnosticPlayerInterfaceFixture(() => new(_playerHud?.Visible == true, _hudState.Current.Health, _hudState.Current.Bolts,
            _quickSelect is null ? [] : [.. _quickSelect.Entries.Select(entry => entry.Weapon)], _rac1DetectorEquipped),
            () => { using var press = new InputEventAction { Action = ControllerBindings.QuickSelect, Pressed = true }; return TryOpenWeaponQuickSelect(press); },
            () => CloseWeaponQuickSelect(), TryToggleRac1MetalDetector);
        var profile = new DiagnosticSharedProfileFixture(() => new(OrdinaryPlayerSession, SharedProfilePath is not null, _profileWriteBlocked,
            _fusionPreparation is not null, _profileDocument?.Destination, _currentDestinationId, _profileDocument?.Sources.Count ?? 0), () => _playerNative);
        var samples = new GodotCapabilitySamples(new(() => _player, () => _playerPhysicsClock, () => _playerServiceTick,
            () => _playerAvatarAnimationController, () => _fusion?.ShotsFired ?? 0));
        var health = new GodotCapabilityHealthFixture(new(() => _playerNative, () => _player, () => _rac2RatchetNanotech.Current,
            () => _hudState.Current.Health, () => [.. _rac2OpeningMsr1.Values.Select(session => (session.Hostile.InstanceIndex, session.Node.Root))], RespawnPlayer));
        var audio = new GodotPlayerAudioFixture(this, new(() => _player,
            () => new(_rac1PlayerAudio is not null, _world?.LevelId, _rac1PackSound, _rac1ThrownWrenchSound, _rac1WeaponLoop is not null,
                _rac1WrenchThrow.Active, _rac1BoltCrates.DestroyedCrateCount, _rac1Nanotech.Probe().Nanotech, _rac1Nanotech.Probe().IsDead),
            ClearMovementSmokeInput, SetAnalogueSmokeInput, SetRac1SmokeConditionedInput, TryHandleInteractiveBack,
            OnRac1WeaponSelectionRequested, SetRac1WeaponPreview, () => [.. _rac1CrateNodes], IsRac1GameplayActive,
            (node, position) => GodotRac1CrateStaging.Place(node, position, _rac1FallingCrates), ApplyRac1EnemyPlayerDamage));
        var ports = new PlayerSessionDiagnosticPorts(player,
            new GodotPlayerObservations(() => _player, () => _playerAvatar?.Identity,
                () => _playerPhysicsClock?.Tick ?? throw new DiagnosticUnavailableException("The native player clock is unavailable."), () => _playerNative?.Life.Capture().Current),
            new GodotPlayerInputFixture(() => _player), navigation, menus, sources,
            new GodotWorldLifecycleFixture(this, WorldScenes, EnterFusionLab,
                () => EnterWorld(DiagnosticWorldFailures.Destination, DiagnosticWorldFailures.Load, null, null)),
            new GodotSessionOperationChecks(this, token => CreateCaptureDiagnosticHarness().CheckMovementCancellationAsync(token), () => _hostOperations.Stopping, message => GD.Print(message)),
            equipment, ui, profile, new DiagnosticProfileEvidence(() => SharedProfilePath, SaveSharedProfile),
            CreateCaptureDiagnosticHarness().Acceptance, samples,
            new GodotCommonContactFixture(() => _sceneResult?.Root, () => _player),
            new DiagnosticRetiredActorProbe(() => [.. _rac1HostileNodes.Values], ApplyPlayerSourceDamage), health,
            () => PlayerEquipmentConfigurationIdentity, audio, _playerAudioDiagnosticEvents);
        return new(_args, [.. TrilogySourceDefinitions.All.Select(source => new DiagnosticSourceRequirement(source.Game, source.Authority.BuildId))], ports,
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. _sources.Attached.Values.Select(source => new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
                new GodotDiagnosticFrameClock(this)), _hostOperations, (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code),
            message => GD.Print(message), message => GD.PrintErr(message));
    }

    public void RunRac1StartupVisibilitySmokeFromBootstrap() => CreateUiDiagnosticHarness().Start("rac1-startup-visibility-smoke");
    private Task RunRac1VeldinRetailPauseSmokeAsync() => CreateUiDiagnosticHarness().RunVeldinPauseAsync(LegacyDiagnosticCancellation.Current);
    private Rac1WorldRouteDiagnosticHarness CreateRac1WorldRouteDiagnosticHarness()
    {
        var movement = CreateMovementDiagnosticHarness(); var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var ui = CreateUiDiagnosticHarness();
        var lifetime = new GodotRac1WorldRouteLifetimeFixture(this,
            () => new(worlds.ReadWorld(), _rac1NovalisMechanisms.Count, _rac1TaxiNodes.Count, _rac1TaxiCollisionBodies.Count,
                _rac1Music?.Tracks.Count ?? 0, _rac1MusicDecoded.Count, _rac1MusicReader is not null, _rac1TaxiCameraWorldOwned,
                _rac1FlyingTaxi is not null || _rac1TaxiPendingStop is not null || _rac1TaxiRiderFrame is not null, SharedProfilePath is not null),
            () => _worldHost.Root, () => _rac1TaxiCollisionBodies.Values, () => _player?.Camera);
        return new(movement.Ports,
            () => new GodotRac1NovalisRouteFixture(() => _rac1NovalisMechanisms, () => _player, worlds),
            () => new GodotRac1NovalisMusicFixture(worlds, () => new(worlds.ReadWorld(), _rac1Music is { } music
                    ? [.. music.Tracks.Select(track => new Rac1NovalisMusicTrackDiagnostic(track.Index, track.Sector, track.DurationSeconds))] : [],
                _rac1MusicArea, _rac1MusicSlot, _rac1MusicIntroRemaining, _rac1MusicDecoded.Count,
                [.. _worldHost.AudioDiagnostics.Select(item => item.Message)], _worldHost.AudioStatusLine), OnRac1NovalisCrankMusic, TickRac1NovalisMusic),
            CreateRac1TaxiDiagnosticFixture, ui, lifetime, ui.Menus,
            new DiagnosticNavigationActions(destination => TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native)),
            new DiagnosticSourceAttachmentFixture(_sources, RefreshPlayerSourceAvailability));
    }

    private IRac1TaxiDiagnosticFixture CreateRac1TaxiDiagnosticFixture() => new GodotRac1TaxiDiagnosticFixture(new()
    {
        Session = () => _rac1MetropolisTaxis,
        Nodes = () => _rac1TaxiNodes,
        Hulls = () => _rac1TaxiCollisionBodies,
        Hovers = () => _rac1TaxiHovers.Count,
        Player = () => _player,
        Pending = () => _rac1TaxiPendingStop,
        HeroTarget = () => _rac1TaxiHeroTarget,
        Flying = () => _rac1FlyingTaxi,
        Rider = () => _rac1TaxiRiderFrame,
        CameraOwned = () => _rac1TaxiCameraWorldOwned,
        Hud = GetRac1MetropolisTaxiHudLine,
        Input = TryHandleRac1MetropolisTaxiInput,
        Abort = AbortRac1MetropolisTaxiRide,
        Landing = TryResolveRac1TaxiLanding,
        ToNative = ToTaxiPoint,
        ToScene = FromTaxiPoint,
        Basis = ToTaxiCabBasis,
    }, new GodotDiagnosticWorldObservations(WorldScenes));

    private ShotListCliService CreateShotListCliService() => new(_args, (game, path) => RememberCommandLineSource(game, path),
        new GodotShotListCaptureFixture(this, _camera, camera => _activeCamera = camera, OpenDestinationFromBootstrap,
            () => _mode == Mode.World ? _world : null, FrameShowcaseCamera, ApplyOverlaySpec, UpdateWorldHud, CaptureMetadata),
        (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.Print(message), message => GD.PrintErr(message));

    private StressSwitchCliService CreateStressSwitchCliService() => new(
        new GodotStressWorldFixture(() => new(new GodotDiagnosticWorldObservations(WorldScenes).ReadWorld(),
                _worldHost.Root?.GetInstanceId(), _player?.GetInstanceId(), _rac1CampaignSession.Campaign.CurrentLevel),
            path => _activeDestination is { } destination && _worldHost.Root is not null && !EnterProviderWorld(path, destination),
            ShowDestinationSelector, OpenDestinationFromBootstrap, _worldRoot, () => _world?.LevelId ?? -1, () => _player?.IsOnFloor()),
        new GodotDiagnosticFrameClock(this), (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code),
        message => GD.Print(message), message => GD.PrintErr(message));

    private void StartFusionLabTraversalSmoke(PlayerHost player)
    {
        var runner = new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => []), new GodotDiagnosticFrameClock(this));
        var scenario = new FusionLabTraversalScenario(new GodotFusionLabTraversalFixture(player, () => _frame,
            new GodotDiagnosticWorldObservations(WorldScenes), ShowDestinationSelector, EnterFusionLab, () => _player is not null),
            message => GD.Print(message), message => GD.PrintErr(message));
        _hostOperations.Start("fusion-lab-traversal", async token =>
        {
            var result = await runner.RunAsync(scenario, explicitlyRequested: true, token);
            if (result.Status is not (DiagnosticRunStatus.Passed or DiagnosticRunStatus.Canceled))
                throw result.Error ?? new InvalidOperationException(result.Detail);
        }, lifetime: CancellationToken.None);
    }

    private void BuildScene(string scene)
    {
        _mode = Mode.Smoke;
        _sceneKind = scene;
        EnsurePlainEnvironment();

        var sun = new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-55, -35, 0) };
        AddChild(sun);

        _worldRoot.AddChild(new MeshInstance3D
        {
            Name = "Floor",
            Mesh = new PlaneMesh { Size = new Vector2(12, 12) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.2f, 0.24f) },
        });
        _worldRoot.AddChild(new MeshInstance3D
        {
            Name = "SmokeCube",
            Mesh = new BoxMesh { Size = new Vector3(2, 2, 2) },
            Position = new Vector3(0, 1, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.45f, 0.15f) },
        });

        _camera.Position = new Vector3(4.5f, 3.5f, 5.5f);
        _camera.LookAt(new Vector3(0, 0.8f, 0), Vector3.Up);
        _ui.AddChild(new Label { Name = "Banner", Text = $"One Big Package — {scene}", Position = new Vector2(16, 12) });

        if (scene.Equals("hud", StringComparison.OrdinalIgnoreCase))
        {
            BuildHudFixture();
        }
    }

    private void BuildHudFixture()
    {
        EnsurePlayerHud();
        var snapshot = _hudState.BeginSession(new HudPresentationState(
            Health: new HudHealth(3, 6, "nanotech", HudLifeState.Alive),
            Bolts: new HudCurrency("bolts", 12_450),
            CurrentWeapon: new HudWeapon(
                PresentationKey: "hud.fixture.vector-blaster",
                NameKey: "Vector Blaster",
                Ammo: new HudAmmo(7, 12)),
            ContextPrompt: new HudContextPrompt(
                PromptId: "hud-fixture-console",
                ActionId: "interact",
                MessageKey: "Activate test console",
                Progress: 0.42)));
        _playerHud!.Render(snapshot);
    }

}
