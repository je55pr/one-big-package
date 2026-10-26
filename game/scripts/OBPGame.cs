using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.IO;
using OBP.RAC1.Progression;
using OBP.RAC2;
using OBP.Runtime;
using OBP.Runtime.Presentation;

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
        if (_args.OrdinaryUiSmoke) _ = RunOrdinaryUiSmokeAsync();

        // Dedicated deterministic fixture through the same playable Lab host
        // used by the ordinary World Select button.
        if (_args.TestScene == "fusion-lab")
        {
            if (EnterFusionLab() && _args.CaptureFrame is { } fusionCapture)
                _hostOperations.Start("capture", token => RunCaptureAsync(fusionCapture, token), lifetime: CancellationToken.None);
            return;
        }

        // A --shots run works for any provider and drives the same neutral
        // destination/provider path used by ordinary world entry.
        if (_args.ShotsPath is { } shotList)
        {
            _hostOperations.Start("shots", token => RunShotsAsync(shotList, token), lifetime: CancellationToken.None);
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
        TickSharedProfile(delta);
        TickRac1CampaignSaves(delta);

        if (_mode == Mode.World)
        {
            if (_playtest is { WaitingForEntry: true } or { PreparingWorld: true }) return;
            // Sky-follow, animated mobies and per-region hero light / fog.
            _worldHost.Tick(delta, _activeCamera?.GlobalPosition ?? Vector3.Zero);
            TickFusionLabTraversalSmoke();
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

        if (TryGetCrateFocusPose(world, out var crateSpawn, out var crateYaw))
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

        string crateDebug = GetCrateDebugHudLine();
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
