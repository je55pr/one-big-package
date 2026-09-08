using Godot;
using OBP.Godot;
using OBP.IO;
using OBP.RAC2;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// Application root and the Going Commando planet-hopping runtime.
///
/// <para>Flow: launch → (system file dialog picks a GC retail ISO, or one is
/// passed with <c>--gc-iso</c>) → identify the supported build → show the
/// <see cref="PlanetSelectorUi"/> → pick a planet → the generic
/// <see cref="EnterWorld"/> path imports that level with
/// <see cref="GcWorldImport"/>, rebuilds <c>WorldRoot</c> through
/// <see cref="RuntimeWorldScene"/>, resets the environment and spawns the debug
/// player → Esc returns to the selector, pick another, repeat — all without
/// restarting the process.</para>
///
/// <para>No planet is special: every level goes through the same
/// <c>import → RuntimeWorld → RuntimeWorldScene → WorldRoot</c> pipe. Nothing
/// Ratchet-specific is parsed here.</para>
///
/// Scenes: <c>--test-scene smoke</c> (trivial cube), <c>--test-scene picker</c>
/// (the disc dialog, default when interactive), <c>--gc-iso &lt;path&gt;</c>
/// (skip the dialog → selector), plus <c>--planet &lt;name|id&gt;</c> /
/// <c>--test-scene player</c> to jump straight into a world (deterministic
/// capture), and <c>--capture-frame</c> / <c>--capture-out</c> for the harness.
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
    private long _frame;
    private Mode _mode = Mode.Smoke;

    // capture / smoke bookkeeping
    private string _sceneKind = "smoke";

    // disc session
    private string? _isoPath;
    private GcIsoLoad.Identity? _identity;

    // the live world
    private readonly WorldHost _worldHost = new();
    private RuntimeWorld? _world;
    private RuntimeWorldScene.Result? _sceneResult;
    private DebugOverlay? _overlay;
    private WorldInspectorPanel? _inspector;
    private DebugPlayer? _player;
    private int _worldSwitches;

    private Camera3D _activeCamera = null!;
    private PlanetSelectorUi? _selector;
    private PanelContainer? _pickerPanel;
    private Label? _pickerStatus;
    private Label? _worldHud;
    private volatile string? _verifyOutcome;
    private long _verifyProgressBits = -1;
    private string _loadSummary = string.Empty;

    public override void _Ready()
    {
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
            AddChild(new CompositionLab { Name = "CompositionLab" });
            GD.Print("[OBPGame] booting composition lab");
            return;
        }

        BuildSkeleton();

        bool wantsWorldDirectly = _args.TestScene == "player" || _args.DirectLoad;

        // A --shots run works for any provider (--rac1-iso / --gc-iso / --uya-iso)
        // and drives its own world entry — handle it before the GC-only branch.
        if (_args.ShotsPath is { } shotList)
        {
            _ = RunShotsAsync(shotList);
            return;
        }

        if (_args.GcIso is { } iso)
        {
            _isoPath = iso;
            if (!IdentifyDisc(iso))
            {
                BuildScene("smoke");
            }
            else if (_args.StressSwitch is { } seq)
            {
                _ = RunStressSwitchAsync(seq);
            }
            else if (wantsWorldDirectly)
            {
                EnterWorld(ResolveRequestedLevel());
            }
            else
            {
                ShowSelector();
            }
        }
        else if (_args.TestScene == "picker")
        {
            BuildPickerScreen();
        }
        else
        {
            BuildScene(_args.TestScene);
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

    private int ResolveRequestedLevel()
    {
        if (_args.Planet is { } token && GcPlanetCatalogue.Resolve(token) is { } id)
        {
            return id;
        }

        return _args.GcLevel;
    }

    public override void _Process(double delta)
    {
        _frame++;

        if (_mode == Mode.World)
        {
            // Sky-follow, animated mobies and per-region hero light / fog.
            _worldHost.Tick(delta, _activeCamera?.GlobalPosition ?? Vector3.Zero);
            UpdateWorldHud();
        }

        if (_verifyOutcome is { } outcome)
        {
            _verifyOutcome = null;
            _verifyProgressBits = -1;
            SetPickerStatus($"{_loadSummary}   —   {outcome}", error: outcome.StartsWith('⚠'));
        }
        else if (System.Threading.Interlocked.Read(ref _verifyProgressBits) is var bits and >= 0)
        {
            double p = System.BitConverter.Int64BitsToDouble(bits);
            SetPickerStatus($"{_loadSummary}   —   verifying disc SHA-256… ({p * 100:0}%)");
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
            if (_mode == Mode.World && _selector is null && _isoPath is not null && _args.CaptureFrame is null)
            {
                ReturnToSelector();
            }
            else
            {
                GetTree().Quit();
            }

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

    // --- disc identification --------------------------------------------------

    private bool IdentifyDisc(string isoPath)
    {
        try
        {
            using var reader = new FileRandomAccessReader(isoPath);
            var identity = GcIsoLoad.Identify(reader);
            _identity = identity;
            if (!identity.Supported)
            {
                GD.PrintErr($"[OBPGame] {identity.Problem}");
                SetPickerStatus(identity.Problem ?? "Unsupported disc image.", error: true);
                return false;
            }

            GD.Print($"[OBPGame] disc accepted: {identity.DiscSerial} · {identity.BuildId}");
            return true;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[OBPGame] could not read disc image: {ex.Message}");
            SetPickerStatus($"Could not read disc image: {ex.Message}", error: true);
            return false;
        }
    }

    // --- planet selector -----------------------------------------------------

    private void ShowSelector()
    {
        _mode = Mode.Selector;
        _sceneKind = "gc-selector";
        TeardownWorld();
        EnsurePlainEnvironment();

        _pickerPanel?.QueueFree();
        _pickerPanel = null;
        if (_worldHud is not null && IsInstanceValid(_worldHud))
        {
            _worldHud.Visible = false;
        }

        _camera.Current = true;
        _activeCamera = _camera;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _selector?.QueueFree();
        _selector = new PlanetSelectorUi();
        string build = _identity is { } id
            ? $"{id.DiscSerial}  ·  {id.BuildId}  ·  {(id.SizeMatches ? "size ✓" : "size mismatch")}"
            : "Going Commando";
        _selector.PlanetChosen += OnPlanetChosen;
        AddChild(_selector);
        _selector.Populate(build, _world?.LevelId);

        GD.Print("[OBPGame] planet selector shown");
    }

    private void OnPlanetChosen(int levelId)
    {
        GD.Print($"[OBPGame] planet chosen: LEVEL{levelId}");
        CallDeferred(nameof(EnterWorldDeferred), levelId);
    }

    private void EnterWorldDeferred(int levelId) => EnterWorld(levelId);

    private void ReturnToSelector()
    {
        GD.Print("[OBPGame] returning to selector");
        ShowSelector();
    }

    // --- generic world load / unload ---------------------------------------

    private void EnterWorld(int levelId)
    {
        _selector?.QueueFree();
        _selector = null;
        _pickerPanel?.QueueFree();
        _pickerPanel = null;

        TeardownWorld();

        var entry = GcPlanetCatalogue.Find(levelId);
        string label = entry?.DisplayName ?? $"LEVEL{levelId}";
        _sceneKind = $"gc-{(entry?.Planet ?? $"level{levelId}").ToLowerInvariant().Replace(' ', '-')}";

        RuntimeWorld world;
        try
        {
            using var reader = new FileRandomAccessReader(_isoPath!);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            world = GcWorldImport.Build(reader, levelId);
            sw.Stop();
            GD.Print($"[OBPGame] imported {label} in {sw.ElapsedMilliseconds} ms — " +
                     $"{world.Meshes.Count} meshes / {world.TotalRenderTriangles:N0} tris / {world.TotalCollisionTriangles:N0} coll tris");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[OBPGame] import of LEVEL{levelId} failed: {ex.Message}\n{ex.StackTrace}");
            _world = null;
            ShowSelector();
            SetSelectorHint($"⚠ {label} failed to import: {ex.Message}");
            return;
        }

        _world = world;
        _worldSwitches++;

        // A framed showcase capture (--direct + --capture-frame, no player) or a
        // --shots run parks a static camera over the level; everything else gets
        // the capsule.
        bool framedCapture = _args.CaptureFrame is not null && _args.DirectLoad && _args.TestScene != "player"
            && !_args.CrateFocus && !_args.CrateAutoStrike;
        bool staticCamera = framedCapture || _args.ShotsPath is not null;

        // Environment, hero light, welded geometry + collision, sky-follow and the
        // per-frame presentation tick all live in the game-neutral WorldHost.
        var result = _worldHost.Load(this, _worldRoot, world, $"Gc_{entry?.Planet ?? levelId.ToString()}", new WorldHost.Options
        {
            IncludeMobyMarkers = !(_args.CaptureFrame is not null && !framedCapture),
            IncludeSky = !_args.AnimSolo,
            IncludeCollision = true,
            ShowCollisionDebug = _args.CollisionDebug,
            OnlyAnimatedMobies = _args.AnimSolo,
        });
        _sceneResult = result;
        SetupOverlay(result, world);
        ConfigureCrateDebugHarness();

        // spawn + camera
        if (_args.AnimSolo)
        {
            FrameAnimatedMobies(world);
        }
        else if (staticCamera)
        {
            FrameShowcaseCamera(world);
        }
        else
        {
            SpawnPlayer(world);
        }

        _mode = Mode.World;
        EnsureWorldHud();
        UpdateWorldHud();

        _loadSummary = $"✓ {label} · {result.MeshInstances} meshes / {result.Triangles:N0} tris / {result.CollisionBodies} colliders";
        GD.Print($"[OBPGame] world ready — {_loadSummary} (switch #{_worldSwitches})");

        if (_args.VerifyHash && _worldSwitches == 1)
        {
            StartBackgroundVerify(_isoPath!);
        }
    }

    /// <summary>
    /// Phase 14 lifecycle stress test: load each planet in <paramref name="seq"/>
    /// through the generic path, let it settle, and log node / object / memory
    /// counts so a leak (orphan nodes, duplicated meshes, creeping memory) shows
    /// up as a trend. Headless — quits when done.
    /// </summary>
    private async System.Threading.Tasks.Task RunStressSwitchAsync(string seq)
    {
        var tokens = seq.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        var rows = new System.Collections.Generic.List<string>();
        double baseMem = 0;

        for (int i = 0; i < tokens.Length; i++)
        {
            string tok = tokens[i];
            int? lvl = GcPlanetCatalogue.Resolve(tok);
            if (lvl is null)
            {
                GD.PrintErr($"[stress] unknown planet '{tok}' — skipped");
                continue;
            }

            // Every third hop, bounce through the selector — exercises the same
            // teardown the interactive Esc path uses.
            if (i > 0 && i % 3 == 0)
            {
                ShowSelector();
                for (int f = 0; f < 10; f++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }

            EnterWorld(lvl.Value);
            for (int f = 0; f < 45; f++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            double mem = Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0;
            double objs = Performance.GetMonitor(Performance.Monitor.ObjectCount);
            double nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            double orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
            if (i == 0)
            {
                baseMem = mem;
            }

            string row = $"#{i,2} {tok,-10} L{lvl,-2} | mem {mem,7:0.0} MB (+{mem - baseMem,6:0.0}) | " +
                         $"objects {objs,7:0} nodes {nodes,6:0} orphans {orphans,4:0} | " +
                         $"worldRoot desc {CountDescendants(_worldRoot),5} | onFloor {_player?.IsOnFloor()}";
            rows.Add(row);
            GD.Print("[stress] " + row);
        }

        GD.Print("[stress] ================ summary ================");
        foreach (var r in rows)
        {
            GD.Print("[stress] " + r);
        }

        GetTree().Quit(0);
    }

    private static int CountDescendants(Node n)
    {
        int c = n.GetChildCount();
        foreach (var child in n.GetChildren())
        {
            c += CountDescendants(child);
        }

        return c;
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
        _player?.QueueFree();
        _player = null;

        _worldHost.Unload(); // scene tree, environment, hero light + GC.Collect

        _sceneResult = null;
        _overlay = null; // its nodes live under the world sub-tree that was just freed
        _inspector?.QueueFree();
        _inspector = null;
        _world = null;
        ResetCrateDebugHarness();
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
        var ship = world.Ship;

        bool shipInside = ship is { } s
            && s.X > b.Min.X && s.X < b.Max.X && s.Z > b.Min.Z && s.Z < b.Max.Z
            && s.Y > b.Min.Y - 4 && s.Y < b.Max.Y + 40;

        Vector3 spawn;
        float yaw = 0f;
        if (shipInside && ship is { } sp)
        {
            // native ship point → a safe grounded spawn a little above it; the
            // capsule raycasts down onto the collision on its first frames.
            spawn = RuntimeWorldScene.ToScene(sp.X, sp.Y + 3.0, sp.Z);
            yaw = RuntimeWorldScene.ToSceneYaw(sp.Yaw);
            GD.Print($"[OBPGame] spawn from native ship point ({sp.X:0},{sp.Y:0},{sp.Z:0})");
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

        // For a ship-less spawn (Aranos), a deterministic capture has no heading
        // to use, so face the world centre; a real ship point already faces the
        // way you fly in — the designers' intended view.
        if (scripted && !shipInside)
        {
            var centre = RuntimeWorldScene.ToScene(
                (b.Min.X + b.Max.X) * 0.5, spawn.Y, (b.Min.Z + b.Max.Z) * 0.5);
            var toCentre = centre - spawn;
            if (toCentre.LengthSquared() > 1f)
            {
                yaw = Mathf.Atan2(toCentre.X, toCentre.Z) + Mathf.Pi;
            }
        }

        var player = new DebugPlayer
        {
            Name = "DebugPlayer",
            Scripted = scripted,
            ScriptedStill = _args.CrateFocus && !_args.CrateAutoStrike,
            Position = spawn,
        };
        _playerRoot.AddChild(player);
        player.RotateY(yaw);
        _camera.Current = false;
        _activeCamera = player.Camera;
        _player = player;
        ArmCrateDebugHarness(player);
    }

    // --- HUD ---------------------------------------------------------------

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
        string planet = w.PlanetName ?? _activeDestination?.PlanetLabel ?? "?";
        string location = w.LocationName ?? _activeDestination?.LocationLabel ?? "?";
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

        _worldHud.Text =
            $"Game: {gameLabel}   Build: {w.BuildId}\n" +
            $"Planet: {planet}   Location: {location}\n" +
            $"Level id: {w.LevelId}   Container: {container}   (switch #{_worldSwitches})\n" +
            $"Player XYZ: {pos}   {ground}\n" +
            $"Render tris: {r.Triangles:N0}   Collision tris: {r.CollisionTriangles:N0}\n" +
            $"TIE meshes: {r.TieInstances}   Shrub meshes: {r.ShrubInstances}   Moby meshes: {r.MobyInstances}" +
            (r.AnimatedMobies > 0 ? $"   Animated mobies: {r.AnimatedMobies}" : "") +
            (r.DynamicObjects > 0 ? $"   Dynamic objects: {r.DynamicObjects}" : "") +
            $"\n{_overlay?.StatusLine() ?? "overlays: off"}   (F1 isolate · F2 tint · F3 collision · F4 bounds · F5 lights · F6 sky · F7 clear)" +
            (string.IsNullOrEmpty(crateDebug) ? "" : $"\n{crateDebug}");
    }

    private void SetSelectorHint(string text)
    {
        GD.Print($"[OBPGame] {text}");
        _selector?.SetHint(text);
    }

    // --- disc picker screen ----------------------------------------------

    private void BuildPickerScreen()
    {
        _mode = Mode.Picker;
        _sceneKind = "gc-picker";
        EnsurePlainEnvironment();

        _pickerPanel = new PanelContainer { Name = "PickerPanel", Position = new Vector2(28, 52) };
        _pickerPanel.CustomMinimumSize = new Vector2(640, 0);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        _pickerPanel.AddChild(box);

        box.AddChild(new Label { Text = "One Big Package", ThemeTypeVariation = "HeaderLarge" });
        box.AddChild(new Label
        {
            Text = $"Open a Going Commando disc image  —  {Rac2Authority.Primary.Region} {Rac2Authority.Primary.Revision} ({Rac2Authority.Primary.Serial})",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        var button = new Button { Text = "Open Going Commando ISO…" };
        button.Pressed += OnChooseIsoPressed;
        box.AddChild(button);

        _pickerStatus = new Label
        {
            Text = "No disc loaded.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 44),
        };
        box.AddChild(_pickerStatus);
        _ui.AddChild(_pickerPanel);
    }

    private void OnChooseIsoPressed()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Select a Going Commando disc image",
            UseNativeDialog = true,
        };
        dialog.AddFilter("*.iso", "PS2 disc image");
        dialog.AddFilter("*.001", "Split disc image (first part)");
        dialog.FileSelected += path =>
        {
            dialog.QueueFree();
            _isoPath = path;
            if (IdentifyDisc(path))
            {
                ShowSelector();
            }
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(1000, 640));
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
    }

    // --- authority verification (background) ----------------------------

    private void StartBackgroundVerify(string isoPath)
    {
        System.Threading.Interlocked.Exchange(ref _verifyProgressBits, System.BitConverter.DoubleToInt64Bits(0));
        var progress = new System.Progress<double>(p =>
            System.Threading.Interlocked.Exchange(ref _verifyProgressBits, System.BitConverter.DoubleToInt64Bits(p)));

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                using var reader = new FileRandomAccessReader(isoPath);
                var verified = GcIsoLoad.Verify(reader, progress);
                _verifyOutcome = $"✓ authority build verified — {verified.Serial} {verified.Revision}";
            }
            catch (System.Exception ex)
            {
                _verifyOutcome = $"⚠ disc does not match the known-good dump: {ex.Message}";
            }
        });
    }

    private void SetPickerStatus(string text, bool error = false)
    {
        GD.Print($"[OBPGame] {text}");
        if (_pickerStatus is not null && IsInstanceValid(_pickerStatus))
        {
            _pickerStatus.Text = text;
            _pickerStatus.Modulate = error ? new Color(1f, 0.5f, 0.45f) : new Color(0.7f, 0.95f, 0.7f);
        }
    }

    // --- deterministic capture ----------------------------------------

    private async System.Threading.Tasks.Task RunCaptureAsync(int frameArg)
    {
        var result = await CaptureHarness.CaptureAsync(
            this,
            _args.CaptureOut ?? $"captures/{_sceneKind}.png",
            frameArg,
            () =>
            {
                var meta = CaptureMetadata();
                meta["capture"] = _sceneKind;
                meta["captureFrameArg"] = frameArg;
                return meta;
            });
        GetTree().Quit(result.Ok ? 0 : 1);
    }

    /// <summary>The common world / render / player metadata for any capture (shot or single frame). Call after the world has settled.</summary>
    private System.Collections.Generic.Dictionary<string, object?> CaptureMetadata()
    {
        var r = _sceneResult;
        return new System.Collections.Generic.Dictionary<string, object?>
        {
            ["renderedFrames"] = _frame,
            ["renderer"] = CaptureHarness.ActiveRenderer(),
            ["engine"] = (string)Engine.GetVersionInfo()["string"],
            ["authorityBuild"] = _world?.BuildId ?? Rac2Authority.Primary.BuildId,
            ["game"] = _world?.Game,
            ["planet"] = _world?.PlanetName,
            ["location"] = _world?.LocationName,
            ["levelId"] = _world?.LevelId,
            ["worldSwitches"] = _worldSwitches,
            ["camera"] = new[] { _activeCamera.GlobalPosition.X, _activeCamera.GlobalPosition.Y, _activeCamera.GlobalPosition.Z },
            ["overlays"] = _overlay?.StatusLine(),
            ["animClockSeconds"] = System.Math.Round(_worldHost.AnimationClockSeconds, 3),
            ["animations"] = _worldHost.AnimationStates()
                .Select(a => new { a.Name, a.CurrentFrame, a.FrameCount, a.Playing }).ToArray(),
            ["meshInstances"] = r?.MeshInstances ?? 0,
            ["triangles"] = r?.Triangles ?? 0,
            ["textures"] = r?.Textures ?? 0,
            ["collisionBodies"] = r?.CollisionBodies ?? 0,
            ["collisionTriangles"] = r?.CollisionTriangles ?? 0,
            ["tieInstances"] = r?.TieInstances ?? 0,
            ["shrubInstances"] = r?.ShrubInstances ?? 0,
            ["mobyInstances"] = r?.MobyInstances ?? 0,
            ["dynamicObjects"] = r?.DynamicObjects ?? 0,
            ["crateDebug"] = GetCrateDebugSnapshot(),
            ["player"] = _player is { } pl && IsInstanceValid(pl)
                ? new { position = new[] { pl.GlobalPosition.X, pl.GlobalPosition.Y, pl.GlobalPosition.Z }, onFloor = pl.IsOnFloor() }
                : null,
        };
    }
}
