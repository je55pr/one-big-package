using System.Linq;
using Godot;
using OBP.Composition;
using OBP.Core.Math;
using OBP.Godot;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// Phase 1–7: the neutral multi-world composition / fusion lab.
///
/// <para>
/// Holds two or more independently reconstructed <see cref="RuntimeWorld"/>
/// scenes in one Godot process, each under its own
/// <c>WorldTransformRoot</c> (a <see cref="Node3D"/> whose <see cref="Transform3D"/>
/// is the placement's <see cref="CompositionTransform"/>). The decoded worlds are
/// never mutated — every alignment, overlay and toggle lives on or above the
/// generated scene. A <see cref="PlanarAlignmentSolver"/> turns landmark anchor
/// pairs into a reproducible rigid (optionally uniform-scale) transform and
/// reports the fit residuals.
/// </para>
///
/// <para>Tree: <c>CompositionLab / CompositionRoot / &lt;worldId&gt; / RuntimeWorldScene</c>.</para>
/// </summary>
public partial class CompositionLab : Node3D
{
    private sealed class LoadedWorld
    {
        public required WorldPlacement Placement { get; set; }

        public required Node3D TransformRoot { get; init; }

        public required RuntimeWorld World { get; init; }

        public required WorldHost Host { get; init; }

        public RuntimeWorldScene.Result Scene => Host.Result!;
    }

    private readonly System.Collections.Generic.Dictionary<string, LoadedWorld> _worlds = new(System.StringComparer.Ordinal);
    private readonly CompositionWorldLoader _loader = new();

    private WorldComposition _composition = new();
    private string? _compositionPath;
    private Node3D _compositionRoot = null!;
    private CanvasLayer _ui = null!;
    private Label _hud = null!;
    private Camera3D _camera = null!;
    private CommandLineArgs _args = new();

    private string? _activeWorldId;
    private long _frame;

    // free-fly camera state
    private float _camYaw;
    private float _camPitch = -0.35f;
    private bool _mouseCaptured;

    // pending anchor capture: worldId -> local point, keyed by "slot" A/B
    private readonly System.Collections.Generic.List<(string Label, Vec3? A, Vec3? B)> _pendingAnchors = new();
    private string _status = string.Empty;
    private PlanarAlignmentResult? _lastSolve;
    private readonly System.Collections.Generic.List<MeshInstance3D> _anchorMarkers = new();
    private Node3D _markerRoot = null!;

    public override void _Ready()
    {
        _args = CommandLineArgs.Parse(OS.GetCmdlineUserArgs());

        _compositionRoot = new Node3D { Name = "CompositionRoot" };
        AddChild(_compositionRoot);
        _markerRoot = new Node3D { Name = "AnchorMarkers" };
        AddChild(_markerRoot);

        AddChild(new WorldEnvironment
        {
            Name = "LabEnvironment",
            Environment = new global::Godot.Environment
            {
                BackgroundMode = global::Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.02f, 0.03f, 0.05f),
                AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(1, 1, 1),
                AmbientLightEnergy = 1.0f,
            },
        });
        AddChild(new DirectionalLight3D { Name = "LabSun", RotationDegrees = new Vector3(-55, -40, 0), LightEnergy = 0.9f });

        _camera = new Camera3D { Name = "LabCamera", Current = true, Far = 40000f, Near = 0.1f };
        AddChild(_camera);

        _ui = new CanvasLayer { Name = "LabUI" };
        AddChild(_ui);
        _hud = new Label { Name = "LabHud", Position = new Vector2(14, 10) };
        _hud.AddThemeColorOverride("font_color", new Color(0.85f, 0.95f, 1f));
        _ui.AddChild(_hud);

        RegisterSources();
        LoadComposition();

        if (_args.ComposeReload is { } reloads and > 0)
        {
            _ = RunReloadStressAsync(reloads);
            return;
        }

        BuildAllWorlds();

        if (_args.ComposeSolve is { } mode)
        {
            _composition = _composition with { Comparison = _composition.Comparison with { FitScale = mode == "scale" } };
            SolveAndApply();
        }

        FrameAllWorlds();
        UpdateHud();

        GD.Print($"[CompositionLab] ready — {_worlds.Count} world(s), composition '{_composition.Name ?? "(unnamed)"}'");

        if (_args.CaptureFrame is { } frame)
        {
            Engine.MaxFps = 60;
            _ = RunCaptureAsync(frame);
        }
    }

    private void RegisterSources()
    {
        if (_args.GcIso is { } gc)
        {
            _loader.RegisterSource(OBP.Core.ObpSourceGame.Rac2, gc);
        }

        if (_args.Rac1Iso is { } rc1)
        {
            _loader.RegisterSource(OBP.Core.ObpSourceGame.Rac1, rc1);
        }

        if (_args.UyaIso is { } uya)
        {
            _loader.RegisterSource(OBP.Core.ObpSourceGame.Rac3, uya);
        }
    }

    private void LoadComposition()
    {
        if (_args.CompositionPath is { } path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Composition JSON not found: {path}", path);
            }

            _compositionPath = path;
            _composition = CompositionJson.Load(path);
            GD.Print($"[CompositionLab] loaded {_composition.Worlds.Count} world(s) from {path}");
        }
        else if (_args.ComposeWorlds is { } spec)
        {
            _composition = SeedFromSpec(spec);
        }
        else
        {
            // Keep a source-light default that needs only the mature GC authority.
            // Cross-game examples live under compositions/ and can be selected explicitly.
            _composition = SeedFromSpec("oozla=gc:1,endako=gc:3");
            _composition = _composition with { Name = "GC Oozla vs Endako (default composition-lab smoke case)" };
        }

        _activeWorldId = _composition.Comparison.ActiveWorldId
            ?? _composition.Worlds.Skip(1).FirstOrDefault()?.Id
            ?? _composition.Worlds.FirstOrDefault()?.Id;
    }

    private static WorldComposition SeedFromSpec(string spec)
    {
        var worlds = new System.Collections.Generic.List<WorldPlacement>();
        foreach (var token in spec.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
        {
            int eq = token.IndexOf('=');
            string id = eq > 0 ? token[..eq] : token;
            string rhs = eq > 0 ? token[(eq + 1)..] : token;

            // Accept a canonical destination id ("rac2:LEVEL1") or the shorthand
            // "game:level" ("gc:1", "uya:5").
            if (rhs.Contains(":LEVEL", System.StringComparison.OrdinalIgnoreCase)
                || (rhs.StartsWith("rac", System.StringComparison.OrdinalIgnoreCase) && rhs.Contains(':')))
            {
                worlds.Add(new WorldPlacement { Id = id, DestinationId = rhs, Label = rhs });
                continue;
            }

            string[] parts = rhs.Split(':', 2);
            string game = parts.Length == 2 ? parts[0] : "gc";
            int level = int.TryParse(parts[^1], out int l) ? l : 1;
            worlds.Add(new WorldPlacement { Id = id, SourceGame = game, LevelId = level, Label = $"{game}:{level}" });
        }

        return new WorldComposition { Worlds = worlds };
    }

    private void BuildAllWorlds()
    {
        foreach (var placement in _composition.Worlds)
        {
            try
            {
                BuildWorld(placement);
            }
            catch (System.Exception ex)
            {
                _status = $"world '{placement.Id}' failed: {ex.Message}";
                GD.PrintErr($"[CompositionLab] {_status}");
            }
        }
    }

    private void BuildWorld(WorldPlacement placement)
    {
        if (_worlds.ContainsKey(placement.Id))
        {
            return;
        }

        var world = _loader.Load(placement);
        var transformRoot = new Node3D { Name = placement.Id };
        _compositionRoot.AddChild(transformRoot);

        // Each world gets its own WorldHost (geometry / collision / animation
        // tick), but no environment — one viewport holds one environment, so the
        // lab shares a single one, driven by the active world.
        var host = new WorldHost();
        host.Load(transformRoot, transformRoot, world, $"RuntimeWorldScene_{placement.Id}", new WorldHost.Options
        {
            ManageEnvironment = false,
            IncludeSky = false, // sky shells follow one camera — meaningless with two worlds overlaid
            IncludeMobyMarkers = true,
            IncludeCollision = true,
        });

        var loaded = new LoadedWorld { Placement = placement, TransformRoot = transformRoot, World = world, Host = host };
        _worlds[placement.Id] = loaded;

        ApplyTransform(loaded);
        CompositionView.ApplyDisplayState(loaded.Scene, placement);
    }

    private void ApplyTransform(LoadedWorld w) =>
        w.TransformRoot.Transform = CompositionView.ToGodotTransform(w.Placement.Transform);

    /// <summary>Free every loaded world sub-tree. Safe to call when nothing is loaded.</summary>
    private void TeardownWorlds()
    {
        foreach (var w in _worlds.Values)
        {
            w.Host.Unload(); // scene tree + animation state + GC.Collect
            if (GodotObject.IsInstanceValid(w.TransformRoot))
            {
                w.TransformRoot.QueueFree();
            }
        }

        _worlds.Clear();
        foreach (var m in _anchorMarkers)
        {
            if (GodotObject.IsInstanceValid(m))
            {
                m.QueueFree();
            }
        }

        _anchorMarkers.Clear();
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
    }

    /// <summary>
    /// Checkpoint G: build every world in the composition, let it settle, tear it
    /// all down, and log node / object / orphan / memory counts per cycle so a
    /// leak shows as a trend. Headless — quits when done.
    /// </summary>
    private async System.Threading.Tasks.Task RunReloadStressAsync(int cycles)
    {
        var rows = new System.Collections.Generic.List<string>();
        double baseMem = 0;

        for (int i = 0; i < cycles; i++)
        {
            BuildAllWorlds();
            for (int f = 0; f < 40; f++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            int builtWorlds = _worlds.Count;
            TeardownWorlds();
            for (int f = 0; f < 20; f++)
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

            string row = $"#{i,2} built {builtWorlds} world(s) | mem {mem,7:0.0} MB (+{mem - baseMem,6:0.0}) | " +
                         $"objects {objs,7:0} nodes {nodes,6:0} orphans {orphans,4:0} | compRoot desc {CountDescendants(_compositionRoot),4}";
            rows.Add(row);
            GD.Print("[compose-reload] " + row);
        }

        GD.Print("[compose-reload] ================ summary ================");
        foreach (var r in rows)
        {
            GD.Print("[compose-reload] " + r);
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

    private void UpdatePlacement(string id, System.Func<WorldPlacement, WorldPlacement> mutate)
    {
        if (!_worlds.TryGetValue(id, out var w))
        {
            return;
        }

        var updated = mutate(w.Placement);
        w.Placement = updated;
        _composition = _composition with
        {
            Worlds = _composition.Worlds.Select(p => p.Id == id ? updated : p).ToList(),
        };
        ApplyTransform(w);
        CompositionView.ApplyDisplayState(w.Scene, updated);
        UpdateHud();
    }

    // --- per-frame: free-fly camera + HUD ---------------------------------

    public override void _Process(double delta)
    {
        _frame++;
        FlyCamera((float)delta);

        // Advance each world's animation (no environment / sky tick — the lab
        // owns the shared environment).
        foreach (var w in _worlds.Values)
        {
            w.Host.Tick(delta, _camera.GlobalPosition);
        }

        UpdateHud();
    }

    private void FlyCamera(float dt)
    {
        if (_args.CaptureFrame is not null)
        {
            return;
        }

        var input = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) input.Z -= 1;
        if (Input.IsKeyPressed(Key.S)) input.Z += 1;
        if (Input.IsKeyPressed(Key.A)) input.X -= 1;
        if (Input.IsKeyPressed(Key.D)) input.X += 1;
        if (Input.IsKeyPressed(Key.E)) input.Y += 1;
        if (Input.IsKeyPressed(Key.Q)) input.Y -= 1;

        float speed = Input.IsKeyPressed(Key.Shift) ? 320f : 80f;
        var basis = new Basis(Vector3.Up, _camYaw) * new Basis(Vector3.Right, _camPitch);
        _camera.Position += basis * input.Normalized() * speed * dt;
        _camera.Basis = basis;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right } rmb)
        {
            _mouseCaptured = rmb.Pressed;
            Input.MouseMode = _mouseCaptured ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        }
        else if (@event is InputEventMouseMotion mm && _mouseCaptured)
        {
            _camYaw -= mm.Relative.X * 0.005f;
            _camPitch = Mathf.Clamp(_camPitch - mm.Relative.Y * 0.005f, -1.5f, 1.5f);
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            HandleKey(key.Keycode);
        }
    }

    private void HandleKey(Key k)
    {
        switch (k)
        {
            case Key.Escape:
                GetTree().Quit();
                break;
            case Key.Tab:
                CycleActiveWorld();
                break;
            case Key.Bracketright:
                NudgeActive(new Vec3(0, 0, 0), rotationDelta: 1.0);
                break;
            case Key.Bracketleft:
                NudgeActive(new Vec3(0, 0, 0), rotationDelta: -1.0);
                break;
            case Key.Left: NudgeActive(new Vec3(-StepXZ(), 0, 0)); break;
            case Key.Right: NudgeActive(new Vec3(StepXZ(), 0, 0)); break;
            case Key.Up: NudgeActive(new Vec3(0, 0, -StepXZ())); break;
            case Key.Down: NudgeActive(new Vec3(0, 0, StepXZ())); break;
            case Key.Pageup: NudgeActive(new Vec3(0, StepXZ(), 0)); break;
            case Key.Pagedown: NudgeActive(new Vec3(0, -StepXZ(), 0)); break;
            case Key.V: ToggleActiveVisibility(); break;
            case Key.O: ToggleSolo(); break;
            case Key.F: FocusActive(); break;
            case Key.Key1: CaptureAnchor(0); break;
            case Key.Key2: CaptureAnchor(1); break;
            case Key.Key0: _pendingAnchors.Clear(); _status = "cleared pending anchors"; break;
            case Key.Enter or Key.KpEnter: SolveAndApply(); break;
            case Key.S when Input.IsKeyPressed(Key.Ctrl): SaveComposition(); break;
            case Key.C: ToggleActiveCategory(); break;
        }

        UpdateHud();
    }

    private float StepXZ() => Input.IsKeyPressed(Key.Shift) ? 10f : 1f;

    private int _categoryCursor;

    private void ToggleActiveCategory()
    {
        if (_activeWorldId is not { } id || !_worlds.TryGetValue(id, out var w))
        {
            return;
        }

        string kind = CompositionView.ToggleableKinds[_categoryCursor % CompositionView.ToggleableKinds.Length];
        _categoryCursor++;
        var map = w.Placement.CategoryVisibility is null
            ? new System.Collections.Generic.Dictionary<string, bool>()
            : new System.Collections.Generic.Dictionary<string, bool>(w.Placement.CategoryVisibility);
        map[kind] = !(map.TryGetValue(kind, out var v) ? v : true);
        _status = $"{id}: {kind} -> {(map[kind] ? "on" : "off")}";
        UpdatePlacement(id, p => p with { CategoryVisibility = map });
    }

    private void CycleActiveWorld()
    {
        var ids = _composition.Worlds.Select(w => w.Id).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        int i = _activeWorldId is null ? 0 : (ids.IndexOf(_activeWorldId) + 1) % ids.Count;
        _activeWorldId = ids[i];
        _composition = _composition with { Comparison = _composition.Comparison with { ActiveWorldId = _activeWorldId } };
        _status = $"active world: {_activeWorldId}";
    }

    private void NudgeActive(Vec3 translationDelta, double rotationDelta = 0)
    {
        if (_activeWorldId is not { } id)
        {
            return;
        }

        UpdatePlacement(id, p =>
        {
            var t = p.Transform;
            return p with
            {
                Transform = new CompositionTransform(
                    t.TranslationX + translationDelta.X,
                    t.TranslationY + translationDelta.Y,
                    t.TranslationZ + translationDelta.Z,
                    CompositionTransform.NormalizeDegrees(t.RotationYDegrees + rotationDelta),
                    t.Scale),
            };
        });
    }

    private void ToggleActiveVisibility()
    {
        if (_activeWorldId is { } id)
        {
            UpdatePlacement(id, p => p with { Visible = !p.Visible });
        }
    }

    private void ToggleSolo()
    {
        string? solo = _composition.Comparison.SoloWorldId is null ? _activeWorldId : null;
        _composition = _composition with { Comparison = _composition.Comparison with { SoloWorldId = solo } };
        foreach (var (id, w) in _worlds)
        {
            bool visible = solo is null || id == solo;
            w.Placement = w.Placement with { Visible = visible };
            CompositionView.ApplyDisplayState(w.Scene, w.Placement);
        }

        _composition = _composition with
        {
            Worlds = _composition.Worlds.Select(p => _worlds.TryGetValue(p.Id, out var lw) ? lw.Placement : p).ToList(),
        };
        _status = solo is null ? "show all" : $"solo: {solo}";
    }

    // --- anchors + solve -------------------------------------------------

    /// <summary>
    /// Ray-cast the screen centre against world collision; store the hit as a
    /// world-local anchor point in slot 0 (A) or 1 (B) of the current pending
    /// pair. Two filled slots for a world pair make one anchor.
    /// </summary>
    private void CaptureAnchor(int slot)
    {
        var hit = RaycastCentre();
        if (hit is not ({ } worldId, { } localPoint))
        {
            _status = "anchor: crosshair is not over any world's collision (toggle collision on)";
            return;
        }

        if (_pendingAnchors.Count == 0 || (_pendingAnchors[^1].A is not null && _pendingAnchors[^1].B is not null))
        {
            _pendingAnchors.Add(($"anchor {_composition.Anchors.Count + _pendingAnchors.Count + 1}", null, null));
        }

        var cur = _pendingAnchors[^1];
        if (slot == 0)
        {
            cur = cur with { A = localPoint };
        }
        else
        {
            cur = cur with { B = localPoint };
        }

        _pendingAnchors[^1] = cur;
        _status = $"anchor {_pendingAnchors.Count}: {(slot == 0 ? "A" : "B")} = {worldId} ({localPoint.X:0.0},{localPoint.Y:0.0},{localPoint.Z:0.0})";
        AddAnchorMarker(worldId, localPoint, slot == 0 ? new Color(0.3f, 0.9f, 1f) : new Color(1f, 0.6f, 0.2f));

        // Commit the pair to the composition once both slots are set.
        if (cur.A is { } a && cur.B is { } b)
        {
            var (aId, bId) = AlignmentPair();
            _composition = _composition.AddAnchor(new AnchorPair
            {
                WorldAId = aId,
                WorldBId = bId,
                LocalA = a,
                LocalB = b,
                Label = cur.Label,
            });
            _status = $"committed {cur.Label} ({_composition.AnchorsBetween(aId, bId).Count} pair(s) — press Enter to solve)";
        }
    }

    /// <summary>
    /// The world pair the lab aligns: the first pair that already has anchors,
    /// otherwise the first two declared worlds (A is held fixed, B is solved onto
    /// A). Canonical serialization sorts worlds by id, so this is derived, never
    /// positional-after-a-save.
    /// </summary>
    private (string A, string B) AlignmentPair()
    {
        var anchored = _composition.AnchoredWorldPairs();
        if (anchored.Count > 0)
        {
            return anchored[0];
        }

        var ids = _composition.Worlds.Select(w => w.Id).ToList();
        return (ids.ElementAtOrDefault(0) ?? "a", ids.ElementAtOrDefault(1) ?? ids.ElementAtOrDefault(0) ?? "b");
    }

    private (string WorldId, Vec3 Local)? RaycastCentre()
    {
        var space = GetWorld3D().DirectSpaceState;
        var from = _camera.GlobalPosition;
        var to = from + (-_camera.GlobalBasis.Z) * 20000f;
        var q = PhysicsRayQueryParameters3D.Create(from, to);
        var result = space.IntersectRay(q);
        if (result.Count == 0 || !result.ContainsKey("position") || !result.ContainsKey("collider"))
        {
            return null;
        }

        var point = (Vector3)result["position"];
        var collider = result["collider"].As<Node>();
        var loaded = OwningWorld(collider);
        if (loaded is null)
        {
            return null;
        }

        // Godot global -> transform-root local -> un-mirror X -> OBP local.
        var localGodot = loaded.TransformRoot.GlobalTransform.AffineInverse() * point;
        return (loaded.Placement.Id, new Vec3(-localGodot.X, localGodot.Y, localGodot.Z));
    }

    private LoadedWorld? OwningWorld(Node? node)
    {
        for (var n = node; n is not null; n = n.GetParent())
        {
            foreach (var w in _worlds.Values)
            {
                if (w.TransformRoot == n)
                {
                    return w;
                }
            }
        }

        return null;
    }

    private void SolveAndApply()
    {
        var (aId, bId) = AlignmentPair();
        var pairs = _composition.AnchorsBetween(aId, bId);
        if (pairs.Count == 0)
        {
            _status = "no anchor pairs between the first two worlds — capture some with 1 / 2";
            return;
        }

        bool fitScale = _composition.Comparison.FitScale;
        var (updated, result) = _composition.SolveAlignment(aId, bId, fitScale);
        _composition = updated;
        _lastSolve = result;

        if (_worlds.TryGetValue(bId, out var wb))
        {
            wb.Placement = updated.World(bId)!;
            ApplyTransform(wb);
        }

        var t = result.Transform;
        GD.Print($"[CompositionLab] SOLVE {bId} -> {aId}  ({(fitScale ? "uniform-scale" : "rigid")})");
        GD.Print($"  translation = ({t.TranslationX:0.###}, {t.TranslationY:0.###}, {t.TranslationZ:0.###})");
        GD.Print($"  Y rotation  = {t.RotationYDegrees:0.###}°   scale = {t.Scale:0.#####}");
        GD.Print($"  quality={result.Quality}  mean err={result.MeanError:0.###}  max err={result.MaxError:0.###}  rms={result.RmsError:0.###}");
        for (int i = 0; i < result.PerAnchorResiduals.Count; i++)
        {
            GD.Print($"  anchor[{i}] residual = {result.PerAnchorResiduals[i]:0.###}");
        }

        if (result.Note is { } note)
        {
            GD.Print($"  note: {note}");
        }

        _status = $"solved: {result.Quality}, mean {result.MeanError:0.##}, max {result.MaxError:0.##}"
            + (result.Note is { } n ? $" — {n}" : string.Empty);
        UpdateHud();
    }

    private void AddAnchorMarker(string worldId, Vec3 local, Color colour)
    {
        if (!_worlds.TryGetValue(worldId, out var w))
        {
            return;
        }

        var marker = new MeshInstance3D
        {
            Name = $"anchor_{worldId}_{_anchorMarkers.Count}",
            Mesh = new SphereMesh { Radius = 1.2f, Height = 2.4f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = colour,
            },
            // marker sits under the transform root so it tracks alignment edits
            Position = new Vector3(-(float)local.X, (float)local.Y, (float)local.Z),
        };
        w.TransformRoot.AddChild(marker);
        _anchorMarkers.Add(marker);
    }

    // --- camera framing -------------------------------------------------

    private void FocusActive()
    {
        if (_activeWorldId is { } id && _worlds.TryGetValue(id, out var w))
        {
            FrameBounds(w.World.Bounds, w.TransformRoot.Transform);
            _status = $"focus: {id}";
        }
    }

    private void FrameAllWorlds()
    {
        if (_worlds.Count == 0)
        {
            _camera.Position = new Vector3(0, 200, 400);
            _camera.LookAt(Vector3.Zero, Vector3.Up);
            return;
        }

        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var w in _worlds.Values)
        {
            foreach (var corner in Corners(w.World.Bounds))
            {
                var p = w.TransformRoot.Transform * corner;
                min = min.Min(p);
                max = max.Max(p);
            }
        }

        FrameAabb(min, max);
    }

    private void FrameBounds(ObpBounds bounds, Transform3D root)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var corner in Corners(bounds))
        {
            var p = root * corner;
            min = min.Min(p);
            max = max.Max(p);
        }

        FrameAabb(min, max);
    }

    private void FrameTopDown()
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var w in _worlds.Values)
        {
            foreach (var corner in Corners(w.World.Bounds))
            {
                var p = w.TransformRoot.Transform * corner;
                min = min.Min(p);
                max = max.Max(p);
            }
        }

        var centre = (min + max) * 0.5f;
        float span = Mathf.Max((max - min).X, (max - min).Z);
        _camera.Position = new Vector3(centre.X, max.Y + span * 1.1f + 50f, centre.Z + 0.01f);
        _camera.LookAt(centre, Vector3.Forward);
        _camera.Far = span * 40f + 1000f;
    }

    private void FrameAabb(Vector3 min, Vector3 max)
    {
        var centre = (min + max) * 0.5f;
        float radius = Mathf.Max(1f, (max - min).Length() * 0.5f);
        var dir = new Vector3(0.55f, 0.5f, 0.9f).Normalized();
        _camera.Position = centre + dir * radius * 1.6f;
        _camera.LookAt(centre, Vector3.Up);
        _camera.Far = radius * 40f;
        _camYaw = _camera.Rotation.Y;
        _camPitch = _camera.Rotation.X;
    }

    private static Vector3[] Corners(ObpBounds b)
    {
        // world-local geometry is X-mirrored by RuntimeWorldScene.ToScene
        var lo = new Vector3(-(float)b.Max.X, (float)b.Min.Y, (float)b.Min.Z);
        var hi = new Vector3(-(float)b.Min.X, (float)b.Max.Y, (float)b.Max.Z);
        return new[]
        {
            new Vector3(lo.X, lo.Y, lo.Z), new Vector3(hi.X, lo.Y, lo.Z),
            new Vector3(lo.X, hi.Y, lo.Z), new Vector3(lo.X, lo.Y, hi.Z),
            new Vector3(hi.X, hi.Y, lo.Z), new Vector3(hi.X, lo.Y, hi.Z),
            new Vector3(lo.X, hi.Y, hi.Z), new Vector3(hi.X, hi.Y, hi.Z),
        };
    }

    // --- persistence ---------------------------------------------------

    private void SaveComposition()
    {
        string path = _compositionPath
            ?? Path.GetFullPath($"captures/composition-{System.DateTime.Now:yyyyMMdd-HHmmss}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _composition = _composition with { Comparison = _composition.Comparison with { ActiveWorldId = _activeWorldId } };
        CompositionJson.Save(_composition, path);
        _compositionPath = path;
        _status = $"saved -> {path}";
        GD.Print($"[CompositionLab] {_status}");
    }

    // --- HUD ---------------------------------------------------------

    private void UpdateHud()
    {
        if (_hud is null)
        {
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"COMPOSITION LAB — {_composition.Name ?? "(unnamed)"}");
        sb.AppendLine("RMB look · WASD/QE move · Shift faster · Tab active world · [ ] rotate · arrows/PgUp/PgDn move · V vis · O solo · C category · F focus · 1/2 anchor A/B · 0 clear · Enter solve · Ctrl+S save · Esc quit");
        sb.AppendLine();
        foreach (var p in _composition.Worlds)
        {
            bool loaded = _worlds.ContainsKey(p.Id);
            var t = p.Transform;
            string mark = p.Id == _activeWorldId ? "▶" : " ";
            sb.AppendLine($"{mark} {p.Id,-12} {(loaded ? "" : "[LOAD FAILED] ")}{(p.Visible ? "vis" : "hid")}  " +
                          $"T=({t.TranslationX:0.#},{t.TranslationY:0.#},{t.TranslationZ:0.#})  Ry={t.RotationYDegrees:0.#}°  s={t.Scale:0.###}");
        }

        sb.AppendLine();
        var (aId, bId) = AlignmentPair();
        sb.AppendLine($"anchors {aId}<->{bId}: {_composition.AnchorsBetween(aId, bId).Count}   scale-fit: {(_composition.Comparison.FitScale ? "ON" : "off (rigid)")}");
        if (_lastSolve is { } s)
        {
            sb.AppendLine($"last solve: {s.Quality}  Ry={s.Transform.RotationYDegrees:0.##}°  s={s.Transform.Scale:0.####}  mean={s.MeanError:0.##}  max={s.MaxError:0.##}");
        }

        if (_status.Length > 0)
        {
            sb.AppendLine($"» {_status}");
        }

        _hud.Text = sb.ToString();
    }

    // --- deterministic capture ------------------------------------------

    private async System.Threading.Tasks.Task RunCaptureAsync(int frameArg)
    {
        string view = _args.CompositionView ?? "overview";
        ApplyCaptureView(view);

        var result = await CaptureHarness.CaptureAsync(
            this,
            _args.CaptureOut ?? $"captures/composition-{view}.png",
            frameArg,
            () => new System.Collections.Generic.Dictionary<string, object?>
            {
                ["capture"] = "composition",
                ["view"] = view,
                ["composition"] = _composition.Name,
                ["renderer"] = CaptureHarness.ActiveRenderer(),
                ["worlds"] = _composition.Worlds.Select(w => new
                {
                    w.Id,
                    w.SourceGame,
                    w.LevelId,
                    w.Visible,
                    transform = new { t = new[] { w.Transform.TranslationX, w.Transform.TranslationY, w.Transform.TranslationZ }, ry = w.Transform.RotationYDegrees, s = w.Transform.Scale },
                    loaded = _worlds.ContainsKey(w.Id),
                }).ToArray(),
                ["anchors"] = _composition.Anchors.Count,
                ["lastSolve"] = _lastSolve is { } s ? new { s.Quality, s.MeanError, s.MaxError, ry = s.Transform.RotationYDegrees, scale = s.Transform.Scale } : null,
                ["camera"] = new[] { _camera.GlobalPosition.X, _camera.GlobalPosition.Y, _camera.GlobalPosition.Z },
            });

        GetTree().Quit(result.Ok ? 0 : 1);
    }

    private void ApplyCaptureView(string view)
    {
        var (aId, bId) = AlignmentPair();
        switch (view)
        {
            case "a-only":
                SetOnlyVisible(aId);
                FrameAllWorlds();
                break;
            case "b-only":
                SetOnlyVisible(bId);
                FrameAllWorlds();
                break;
            case "top":
                foreach (var w in _worlds.Values)
                {
                    w.Placement = w.Placement with { Visible = true };
                    CompositionView.ApplyDisplayState(w.Scene, w.Placement);
                }

                FrameTopDown();
                break;
            case "overlay":
            case "overview":
            default:
                foreach (var w in _worlds.Values)
                {
                    w.Placement = w.Placement with { Visible = true };
                    CompositionView.ApplyDisplayState(w.Scene, w.Placement);
                }

                FrameAllWorlds();
                break;
        }
    }

    private void SetOnlyVisible(string id)
    {
        foreach (var (wid, w) in _worlds)
        {
            w.Placement = w.Placement with { Visible = wid == id };
            CompositionView.ApplyDisplayState(w.Scene, w.Placement);
        }
    }
}
