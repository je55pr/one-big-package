using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.IO;
using OBP.RAC1.Progression;
using OBP.RAC2;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OneBigPackage;

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
    private enum Mode { Smoke, Picker, Selector, World }

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

    // the live world
    private readonly WorldHost _worldHost = new();
    private readonly HudStateAdapter _hudState = new();
    private RuntimeWorld? _world;
    private RuntimeWorldScene.Result? _sceneResult;
    private DebugOverlay? _overlay;
    private WorldInspectorPanel? _inspector;
    private PlayerHost? _player;
    private int _worldSwitches;

    private Camera3D _activeCamera = null!;
    private Label? _worldHud;
    private PlayerHud? _playerHud;
    private volatile string? _verifyOutcome;
    private long _verifyProgressBits = -1;
    private string _loadSummary = string.Empty;

    public override void _Ready()
    {
        ApplicationLifecycle.InstallWindowCloseInterception(GetTree());
        _args = CommandLineArgs.Parse(OS.GetCmdlineUserArgs());
        if (_args.FixedSeed is { } seed)
        {
            GD.Seed((ulong)seed);
        }

        // Composition / fusion lab — a separate host that holds several
        // RuntimeWorld scenes at once under independent neutral transform roots.
        // It does not touch the single-world planet-hopping path below.
        if (_args.Compose)
        {
            AddChild(new CompositionLab { Name = "CompositionLab", StartupArgs = _args });
            GD.Print("[OBPGame] booting composition lab");
            return;
        }

        BuildSkeleton();

        // A --shots run works for any provider and drives the same neutral
        // destination/provider path used by ordinary world entry.
        if (_args.ShotsPath is { } shotList)
        {
            _ = RunShotsAsync(shotList);
            return;
        }

        bool bootstrapOwnsScene =
            _args.Destination is not null ||
            _args.GcIso is not null ||
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
            _ = RunCaptureAsync(frame);
        }
    }

    public override void _Process(double delta)
    {
        _frame++;

        if (_mode == Mode.World)
        {
            // Sky-follow, animated mobies and per-region hero light / fog.
            _worldHost.Tick(delta, _activeCamera?.GlobalPosition ?? Vector3.Zero);
            TickPlayerAvatar(delta);
            TickGcGameplay(delta);
            TickRac1Gameplay(delta);
            TickUyaGameplay(delta);
            UpdatePlayerHud();
            UpdateWorldHud();
        }

        if (_verifyOutcome is { } outcome)
        {
            _verifyOutcome = null;
            _verifyProgressBits = -1;
            ReportVerificationStatus($"{_loadSummary}   —   {outcome}", error: outcome.StartsWith('⚠'));
        }
        else if (System.Threading.Interlocked.Read(ref _verifyProgressBits) is var bits and >= 0)
        {
            double p = System.BitConverter.Int64BitsToDouble(bits);
            ReportVerificationStatus($"{_loadSummary}   —   verifying disc SHA-256… ({p * 100:0}%)");
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
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

        if (_mode != Mode.World)
        {
            return;
        }

        if (key.Keycode == Key.K)
        {
            _worldHost.SetAnimationPlaying(!_worldHost.AnimationPlaying);
            UpdateWorldHud();
        }
        else if (key.Keycode == Key.I)
        {
            _inspector?.Toggle();
        }
        else if (_overlay is { } overlay && HandleOverlayKey(key.Keycode, overlay))
        {
            UpdateWorldHud();
        }
    }

    public bool TryHandleInteractiveBack()
    {
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

        if (TryReturnSelectorToSources())
        {
            ApplicationLifecycle.ReportNavigation("selector-to-sources");
            return true;
        }

        return false;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            ApplicationLifecycle.RequestQuit(this, "desktop-window-close");
        }
    }

    public override void _ExitTree() =>
        ApplicationLifecycle.ReportRootExit(Name);

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
    private void TeardownWorld()
    {
        ClearPlayerAvatarView();
        ResetRac1LevelGameplay();
        _hudState.ResetSession();
        _playerHud?.Render(_hudState.Current);
        _player?.QueueFree();
        _player = null;

        _worldHost.Unload(); // scene tree, environment, hero light + GC.Collect

        _sceneResult = null;
        _overlay = null; // its nodes live under the world sub-tree that was just freed
        _inspector?.QueueFree();
        _inspector = null;
        _world = null;
        ResetGcGameplayHost();
        ResetUyaGameplay();
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
        if (_args.Inspect)
        {
            CallDeferred(nameof(OpenInspectorDeferred));
        }
    }

    private void OpenInspectorDeferred() => _inspector?.Toggle();

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
                while (overlay.IsolatedKind != want)
                {
                    string? before = overlay.IsolatedKind;
                    overlay.IsolateNextKind();
                    if (overlay.IsolatedKind == before)
                    {
                        GD.PrintErr($"[OBPGame] --overlay isolate:{want} — no such asset kind");
                        break;
                    }
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
        RuntimeSpawn? rac1LevelEntryStart = null;
        if (world.Game == "rac1")
        {
            Rac1LevelCheckpointSession checkpoint = _rac1CampaignSession.LevelCheckpoint
                ?? throw new InvalidOperationException("R&C1 level entry is missing its checkpoint/progress session.");
            if (checkpoint.NativeLevelId != world.LevelId)
                throw new InvalidOperationException("R&C1 checkpoint/progress session does not match the loaded world.");
            rac1LevelEntryStart = checkpoint.AuthoredClass0;
        }

        var preferredStart = rac1LevelEntryStart ?? world.PreferredPlayerStart;
        bool explicitPlayerStart = rac1LevelEntryStart is not null || world.PlayerStart is not null;
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
            string source = rac1LevelEntryStart is not null
                ? "R&C1 checkpoint/progress class-0 entry"
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
            UseRac1Gameplay = world.Game == "rac1",
            // Aranos LEVEL0's authored class-0 start (native Z 49.89) sits
            // beneath valid overhead prison collision around scene Y 58.5.
            // Keep the generic 8-unit snap everywhere else, but prevent the
            // Aranos authored start from snapping upward onto that roof.
            InitialGroundSnapUpwardReach =
                world.Game == "rac2" && world.LevelId == 0 && world.PlayerStart is not null
                    ? 0.75f
                    : 8f,
            // OBP deliberately reuses the retail-derived R&C1 controller in
            // all supported trilogy worlds; seed its facing from the authored
            // host spawn heading regardless of source game.
            Rac1CurrentYaw = -yaw,
            Position = spawn,
        };
        _playerRoot.AddChild(player);
        player.RotateY(yaw);
        _camera.Current = false;
        _activeCamera = player.Camera;
        _player = player;
        AttachPlayerAvatarVisual(player);
        ArmGcGameplayHost(player);
        ArmRac1Gameplay(player);
        ArmUyaGameplay(player);
    }

    // --- HUD ---------------------------------------------------------------

    private void EnsurePlayerHud()
    {
        if (_playerHud is not null && IsInstanceValid(_playerHud))
        {
            return;
        }

        _playerHud = new PlayerHud();
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
            _worldHud.Visible = true;
            return;
        }

        _worldHud = new Label { Name = "WorldHud", Position = new Vector2(16, 12) };
        _worldHud.AddThemeColorOverride("font_color", new Color(0.85f, 0.92f, 1f));
        _ui.AddChild(_worldHud);
    }

    private void UpdateWorldHud()
    {
        if (_worldHud is null || _world is not { } w || _sceneResult is not { } r)
        {
            return;
        }

        var gcEntry = w.Game == "rac2" ? GcPlanetCatalogue.Find(w.LevelId) : null;
        string gameLabel = w.Game switch
        {
            "rac1" => "Ratchet & Clank",
            "rac2" => "Going Commando",
            "rac3" => "Up Your Arsenal",
            _ => w.Game,
        };
        string destinationName = w.PlanetName is { Length: > 0 }
            ? w.DisplayName
            : _activeDestination?.DisplayName ?? "Unknown destination";
        string container = _activeDestination?.NativeContainer ?? gcEntry?.ContainerFile ?? "(native container unresolved)";
        string pos = "-";
        string ground = "-";
        if (_player is { } p && IsInstanceValid(p))
        {
            var gp = p.GlobalPosition;
            pos = $"{gp.X:0.0} {gp.Y:0.0} {gp.Z:0.0}";
            ground = p.IsOnFloor() ? "grounded" : "air";
        }

        string crateDebug = GetCrateDebugHudLine();
        string gcGameplay = GetGcGameplayHudLine();
        string rac1Gameplay = GetRac1GameplayHudLine();
        string uyaGameplay = GetUyaGameplayHudLine();

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
            (string.IsNullOrEmpty(gcGameplay) ? "" : $"\n{gcGameplay}") +
            (string.IsNullOrEmpty(rac1Gameplay) ? "" : $"\n{rac1Gameplay}") +
            (string.IsNullOrEmpty(uyaGameplay) ? "" : $"\n{uyaGameplay}");
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
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.04f, 0.05f, 0.08f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
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
