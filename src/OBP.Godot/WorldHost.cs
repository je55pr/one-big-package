using Godot;
using OBP.Core.Math;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Godot;

/// <summary>
/// Owns the Godot side of one loaded <see cref="RuntimeWorld"/>: the built
/// <see cref="RuntimeWorldScene.Result"/> sub-tree, the world
/// <see cref="WorldEnvironment"/>, the "hero" <see cref="DirectionalLight3D"/>
/// that follows the level's env-sample lighting, the camera-followed sky root,
/// and the single per-frame presentation tick (advance animated meshes, keep the
/// sky centred, resolve + apply the region hero light / ambient / fog).
///
/// <para>The application host (<c>game/</c>) creates one of these per session and
/// drives it with <see cref="Load"/> / <see cref="Unload"/> / <see cref="Tick"/>.
/// Spawn points, cameras, HUD and input stay in <c>game/</c> — this is only the
/// world presentation. It is game-independent: the GC importer feeds it today,
/// R&amp;C1 / UYA later, through the same <see cref="RuntimeWorld"/>.</para>
/// </summary>
public sealed class WorldHost
{
    public sealed record Options
    {
        /// <summary>Render the camera-followed sky shells.</summary>
        public bool IncludeSky { get; init; } = true;

        /// <summary>Build the trimesh collision bodies (the surface the debug capsule stands on).</summary>
        public bool IncludeCollision { get; init; } = true;

        /// <summary>Render the moby instance markers (cubes for triggers / spawners with no decoded mesh).</summary>
        public bool IncludeMobyMarkers { get; init; } = true;

        /// <summary>Add the translucent collision debug overlay.</summary>
        public bool ShowCollisionDebug { get; init; }

        /// <summary>Build only the animated mobies (the MobySequence showcase).</summary>
        public bool OnlyAnimatedMobies { get; init; }

        /// <summary>Resting orientation for the hero light before a region light steers it.</summary>
        public Vector3 HeroLightRestRotationDegrees { get; init; } = new(-52, -37, 0);

        /// <summary>Hero light intensity.</summary>
        public float HeroLightEnergy { get; init; } = 1.1f;

        /// <summary>
        /// When the world declares no <see cref="RuntimeWorld.AmbientAnimations"/>,
        /// synthesise a gentle drift on the sky shells so something is alive.
        /// Turn off for frame-stable non-sky captures.
        /// </summary>
        public bool AnimateSky { get; init; } = true;
    }

    private Node? _hostNode;
    private Node3D? _sceneParent;
    private WorldEnvironment? _env;
    private DirectionalLight3D? _heroLight;
    private Node3D? _skyRoot;

    private readonly System.Collections.Generic.List<AmbientAnimationTarget> _ambient = new();
    private double _worldTime;

    // Separate clock for moby animation so a debug pause (K) freezes the mobies
    // without stopping the sky drift.
    private double _animClock;
    private bool _animPlaying = true;

    /// <summary>One resolved ambient animation bound to its Godot targets.</summary>
    private sealed record AmbientAnimationTarget(
        RuntimeAmbientAnimation Animation,
        System.Collections.Generic.IReadOnlyList<StandardMaterial3D> Materials,
        Node3D? SpinNode);

    public RuntimeWorld? World { get; private set; }

    public RuntimeWorldScene.Result? Result { get; private set; }

    /// <summary>The built world sub-tree root, or null when nothing is loaded.</summary>
    public Node3D? Root => Result?.Root;

    /// <summary>The live world environment resource, for the HUD / debug overlays.</summary>
    public global::Godot.Environment? Environment => _env?.Environment;

    public bool IsLoaded => Result is not null;

    /// <summary>
    /// Build and attach a world. <paramref name="hostNode"/> parents the
    /// <see cref="WorldEnvironment"/> + hero light; <paramref name="sceneParent"/>
    /// parents the geometry/collision sub-tree. Call <see cref="Unload"/> first if
    /// a world is already loaded.
    /// </summary>
    public RuntimeWorldScene.Result Load(Node hostNode, Node3D sceneParent, RuntimeWorld world, string name, Options options)
    {
        if (IsLoaded)
        {
            Unload();
        }

        _hostNode = hostNode;
        _sceneParent = sceneParent;
        World = world;

        _env = new WorldEnvironment { Name = "WorldEnvironment", Environment = new global::Godot.Environment() };
        PresentationEnvironment.Configure(_env.Environment, world);
        hostNode.AddChild(_env);

        _heroLight = new DirectionalLight3D
        {
            Name = "HeroLight",
            RotationDegrees = options.HeroLightRestRotationDegrees,
            LightEnergy = options.HeroLightEnergy,
        };
        _env.AddChild(_heroLight);

        var result = RuntimeWorldScene.Build(world, name, new RuntimeWorldScene.Options
        {
            IncludeSky = options.IncludeSky,
            IncludeCollision = options.IncludeCollision,
            IncludeMobyMarkers = options.IncludeMobyMarkers,
            ShowCollisionDebug = options.ShowCollisionDebug,
            OnlyAnimatedMobies = options.OnlyAnimatedMobies,
        });
        Result = result;
        _skyRoot = result.SkyRoot;
        sceneParent.AddChild(result.Root);

        _worldTime = 0;
        ResolveAmbientAnimations(world, options);
        return result;
    }

    // Very slow synthetic sky drift when a world declares no animations — cloud
    // texture scroll plus a barely-there dome rotation.
    private static readonly RuntimeAmbientAnimation[] SyntheticSky =
    {
        new("sky", null, RuntimeAmbientAnimationKind.UvScroll, (0.012, 0.003, 0)),
        new("sky", null, RuntimeAmbientAnimationKind.Spin, (0, 0.01, 0)),
    };

    private void ResolveAmbientAnimations(RuntimeWorld world, Options options)
    {
        _ambient.Clear();
        if (Result?.Root is not { } root)
        {
            return;
        }

        var declared = world.AmbientAnimations;
        var source = declared is { Count: > 0 }
            ? declared
            : options.AnimateSky && _skyRoot is not null ? SyntheticSky : System.Array.Empty<RuntimeAmbientAnimation>();

        foreach (var anim in source)
        {
            var materials = new System.Collections.Generic.List<StandardMaterial3D>();
            var seen = new System.Collections.Generic.HashSet<ulong>();
            Node3D? spinNode = null;

            if (anim.Kind == RuntimeAmbientAnimationKind.Spin
                && string.Equals(anim.TargetKind, "sky", System.StringComparison.Ordinal))
            {
                spinNode = _skyRoot;
            }

            if (anim.Kind == RuntimeAmbientAnimationKind.UvScroll)
            {
                foreach (var mi in MeshInstances(root))
                {
                    if (!TryParseKind(mi.Name, out string kind, out int textureId) || !anim.Matches(kind, textureId))
                    {
                        continue;
                    }

                    if (mi.MaterialOverride is StandardMaterial3D mat && seen.Add(mat.GetInstanceId()))
                    {
                        // A repeating sampler so the scrolled UV wraps instead of
                        // sliding the texture off its shell.
                        mat.TextureRepeat = true;
                        materials.Add(mat);
                    }
                }
            }

            if (materials.Count > 0 || spinNode is not null)
            {
                _ambient.Add(new AmbientAnimationTarget(anim, materials, spinNode));
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<MeshInstance3D> MeshInstances(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mi)
            {
                yield return mi;
            }

            foreach (var nested in MeshInstances(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary><see cref="RuntimeWorldScene"/> names each mesh instance <c>"{kind}_{textureId}"</c>.</summary>
    private static bool TryParseKind(string name, out string kind, out int textureId)
    {
        kind = string.Empty;
        textureId = 0;
        int underscore = name.LastIndexOf('_');
        if (underscore <= 0 || !int.TryParse(name.AsSpan(underscore + 1), out textureId))
        {
            return false;
        }

        kind = name[..underscore];
        return true;
    }

    /// <summary>Free the loaded world and everything it owns. Safe when nothing is loaded.</summary>
    public void Unload()
    {
        if (Result?.Root is { } root && GodotObject.IsInstanceValid(root))
        {
            root.QueueFree();
        }

        if (_env is { } env && GodotObject.IsInstanceValid(env))
        {
            env.QueueFree(); // frees the hero light with it
        }

        _env = null;
        _heroLight = null;
        _skyRoot = null;
        Result = null;
        World = null;
        _hostNode = null;
        _sceneParent = null;
        _ambient.Clear();
        _worldTime = 0;
        _animClock = 0;
        _animPlaying = true;

        // Drop the ArrayMesh / ImageTexture / ConcavePolygonShape resources the
        // freed nodes held so memory does not creep across world switches.
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
    }

    /// <summary>
    /// The per-frame presentation tick. <paramref name="cameraGlobalPosition"/> is
    /// the active camera position in Godot scene space. No-op when nothing is
    /// loaded.
    /// </summary>
    public void Tick(double delta, Vector3 cameraGlobalPosition)
    {
        if (Result is not { } result)
        {
            return;
        }

        // Keep the retail sky backdrop centred on the camera so it reads as a
        // distant dome however far the player walks.
        if (_skyRoot is { } sky && GodotObject.IsInstanceValid(sky))
        {
            sky.GlobalPosition = cameraGlobalPosition;
        }

        _worldTime += delta;
        if (_animPlaying)
        {
            _animClock += delta;
        }

        RuntimeWorldScene.AdvanceAnimated(result, _animClock);
        ApplyAmbientAnimations();
        UpdateRegionLighting(cameraGlobalPosition);
    }

    /// <summary>Freeze / resume moby animation (a debug pause; the sky keeps drifting).</summary>
    public void SetAnimationPlaying(bool playing) => _animPlaying = playing;

    public bool AnimationPlaying => _animPlaying;

    /// <summary>Seconds of unpaused animation clock since load — pins an animated capture's pose.</summary>
    public double AnimationClockSeconds => _animClock;

    /// <summary>Playback state for one animated mesh by name, or null.</summary>
    public AnimationState? AnimationStateByName(string name)
    {
        foreach (var s in AnimationStates())
        {
            if (s.Name == name)
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>Per-animated-mesh playback state for the HUD / inspector.</summary>
    public System.Collections.Generic.IReadOnlyList<AnimationState> AnimationStates()
    {
        var list = new System.Collections.Generic.List<AnimationState>();
        foreach (var am in Result?.AnimatedMeshes ?? System.Array.Empty<AnimatedMesh>())
        {
            list.Add(new AnimationState(am.Name, am.FramesPerSecond, am.FrameCount, am.CurrentFrame, _animPlaying));
        }

        return list;
    }

    public readonly record struct AnimationState(string Name, float FramesPerSecond, int FrameCount, int CurrentFrame, bool Playing);

    /// <summary>Evaluate every resolved ambient animation at the current world time and apply it.</summary>
    private void ApplyAmbientAnimations()
    {
        foreach (var target in _ambient)
        {
            var s = AmbientAnimator.Sample(target.Animation, _worldTime);

            if (target.Animation.Kind == RuntimeAmbientAnimationKind.UvScroll)
            {
                var offset = new Vector3((float)s.U, (float)s.V, 0f);
                foreach (var mat in target.Materials)
                {
                    if (GodotObject.IsInstanceValid(mat))
                    {
                        mat.Uv1Offset = offset;
                    }
                }
            }
            else if (target.Animation.Kind == RuntimeAmbientAnimationKind.Spin
                     && target.SpinNode is { } node && GodotObject.IsInstanceValid(node))
            {
                var axis = new Vector3((float)target.Animation.Rate.X, (float)target.Animation.Rate.Y, (float)target.Animation.Rate.Z);
                node.Basis = axis.LengthSquared() > 1e-12f
                    ? new Basis(axis.Normalized(), (float)s.Radians)
                    : Basis.Identity;
            }
        }
    }

    /// <summary>
    /// Resolve the GC hero lighting + per-region fog at the camera and apply it:
    /// the level's env sample points and env-transition doorway volumes, via
    /// <see cref="EnvResolver"/>. No-op for worlds with no decoded lighting.
    /// </summary>
    private void UpdateRegionLighting(Vector3 cameraGlobalPosition)
    {
        if (World?.Lighting is not { } lighting
            || _env?.Environment is not { } env
            || _heroLight is null || !GodotObject.IsInstanceValid(_heroLight))
        {
            return;
        }

        // Godot camera position -> OBP space (X is mirrored by RuntimeWorldScene).
        var r = EnvResolver.Evaluate(lighting, new Vec3(-cameraGlobalPosition.X, cameraGlobalPosition.Y, cameraGlobalPosition.Z));

        _heroLight.Visible = r.HasHeroLight;
        if (r.HasHeroLight)
        {
            _heroLight.LightColor = PresentationEnvironment.ToColor(r.HeroColour);
            // travel dir is OBP space; mirror X for Godot. A DirectionalLight3D
            // shines down its local -Z, so aim -Z along the travel direction.
            var godotTravel = new Vector3(-(float)r.HeroTravel.X, (float)r.HeroTravel.Y, (float)r.HeroTravel.Z);
            if (godotTravel.LengthSquared() > 1e-4f)
            {
                _heroLight.LookAtFromPosition(_heroLight.GlobalPosition, _heroLight.GlobalPosition + godotTravel, Vector3.Up);
            }
        }

        // Scene ambient lift + per-region fog override.
        PresentationEnvironment.ApplyResolvedRegion(env, r, World.Bounds.Diagonal);
    }
}
