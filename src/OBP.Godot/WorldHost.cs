using Godot;
using OBP.Core.Math;
using OBP.Runtime;
using OBP.Runtime.Audio;
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

        /// <summary>Host-specific pass-through materials, filtered only from Godot physics, not the source world.</summary>
        public Func<int, Vector3, Vector3, Vector3, bool>? AdmitCollisionFace { get; init; }

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
        /// Own a <see cref="WorldEnvironment"/> + hero <see cref="DirectionalLight3D"/>
        /// for this world (default). The composition lab turns this off — one
        /// viewport can hold only one environment, so it shares a single one for
        /// the active world.
        /// </summary>
        public bool ManageEnvironment { get; init; } = true;

        /// <summary>
        /// Opt in to an OBP presentation fallback that synthesises gentle sky
        /// drift when the world declares no native ambient animation. Disabled by
        /// default so normal trilogy presentation does not imply recovered motion.
        /// </summary>
        public bool AnimateSky { get; init; } = false;
    }

    private Node? _hostNode;
    private Node3D? _sceneParent;
    private WorldEnvironment? _env;
    private DirectionalLight3D? _heroLight;
    private Node3D? _skyRoot;

    private readonly System.Collections.Generic.List<AmbientAnimationTarget> _ambient = new();
    private readonly RuntimeAudioHost _audio = new();
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

    public IReadOnlyList<RuntimeAudioHost.Diagnostic> AudioDiagnostics => _audio.Diagnostics;

    public string AudioStatusLine => _audio.StatusLine;

    /// <summary>Replace the current level's music/ambience with decoded source-owned audio.</summary>
    public int StartLevelAudio(IEnumerable<RuntimeAudioPlaybackIntent> intents) =>
        IsLoaded && _hostNode is { } host ? _audio.StartLevelAudio(host, intents) : 0;

    /// <summary>Present one neutral gameplay sound effect in the loaded world.</summary>
    public bool PlayEffect(RuntimeAudioPlaybackIntent intent) =>
        IsLoaded && _hostNode is { } host && _audio.PlayEffect(host, intent);

    /// <summary>
    /// Build and attach a world. <paramref name="hostNode"/> parents the
    /// <see cref="WorldEnvironment"/> + hero light; <paramref name="sceneParent"/>
    /// parents the geometry/collision sub-tree. Call <see cref="Unload"/> first if
    /// a world is already loaded.
    /// </summary>
    public static RuntimeWorldScene.Result PrepareScene(RuntimeWorld world, string name, Options options) =>
        RuntimeWorldScene.Build(world, name, new RuntimeWorldScene.Options
        {
            IncludeSky = options.IncludeSky,
            IncludeCollision = options.IncludeCollision,
            AdmitCollisionFace = options.AdmitCollisionFace,
            IncludeMobyMarkers = options.IncludeMobyMarkers,
            ShowCollisionDebug = options.ShowCollisionDebug,
            OnlyAnimatedMobies = options.OnlyAnimatedMobies,
        });

    public RuntimeWorldScene.Result Load(
        Node hostNode, Node3D sceneParent, RuntimeWorld world, string name, Options options,
        RuntimeWorldScene.Result? preparedScene = null)
    {
        if (IsLoaded)
        {
            Unload();
        }

        _hostNode = hostNode;
        _sceneParent = sceneParent;
        World = world;

        if (options.ManageEnvironment)
        {
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
        }

        var result = preparedScene ?? PrepareScene(world, name, options);
        Result = result;
        _skyRoot = result.SkyRoot;
        sceneParent.AddChild(result.Root);

        _worldTime = 0;
        ResolveAmbientAnimations(world, options);
        if (world.LevelAudio is { } levelAudio)
        {
            _audio.StartLevelAudio(hostNode, levelAudio);
        }
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

        var bindings = Result.AmbientMeshBindings ?? System.Array.Empty<RuntimeWorldScene.AmbientMeshBinding>();
        IsolateUvScrollingMaterials(source, bindings);

        foreach (var anim in source)
        {
            var materials = new System.Collections.Generic.List<StandardMaterial3D>();
            var seen = new System.Collections.Generic.HashSet<ulong>();
            Node3D? spinNode = null;

            if (anim.Kind == RuntimeAmbientAnimationKind.Spin)
            {
                spinNode = anim.TargetGroup is { Length: > 0 } group
                    ? FindPresentationGroup(root, group)
                    : string.Equals(anim.TargetKind, "sky", System.StringComparison.Ordinal) ? _skyRoot : null;
            }

            if (anim.Kind == RuntimeAmbientAnimationKind.UvScroll)
            {
                foreach (var binding in bindings)
                {
                    if (!anim.Matches(binding.AssetKind, binding.TextureId, binding.PresentationGroup))
                    {
                        continue;
                    }

                    if (binding.Mesh.MaterialOverride is StandardMaterial3D mat && seen.Add(mat.GetInstanceId()))
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

    /// <summary>
    /// Separate only materials with different UV-scroll targets. Keep normal cache sharing,
    /// and share one duplicate per identical animation profile rather than per mesh.
    /// </summary>
    private static void IsolateUvScrollingMaterials(
        IReadOnlyList<RuntimeAmbientAnimation> animations,
        IReadOnlyList<RuntimeWorldScene.AmbientMeshBinding> bindings)
    {
        var scrolls = animations.Where(a => a.Kind == RuntimeAmbientAnimationKind.UvScroll).ToArray();
        if (scrolls.Length == 0) return;

        string Profile(RuntimeWorldScene.AmbientMeshBinding binding) =>
            string.Join(",", Enumerable.Range(0, scrolls.Length)
                .Where(i => scrolls[i].Matches(binding.AssetKind, binding.TextureId, binding.PresentationGroup)));

        // Group before mutating overrides: the factory deliberately shares materials by
        // texture and render state, including across independently animated shells.
        var materialShares = bindings
            .Where(b => b.Mesh.MaterialOverride is StandardMaterial3D)
            .GroupBy(b => b.Mesh.MaterialOverride!.GetInstanceId())
            .ToArray();

        foreach (var shares in materialShares)
        {
            var profiles = shares.GroupBy(Profile).ToArray();
            if (profiles.Length < 2) continue;

            var original = (StandardMaterial3D)shares.First().Mesh.MaterialOverride!;
            // Leave non-scrolling users on the original material when possible.
            var keep = profiles.FirstOrDefault(group => group.Key.Length == 0) ?? profiles[0];
            foreach (var profile in profiles)
            {
                if (ReferenceEquals(profile, keep)) continue;

                var isolated = (StandardMaterial3D)original.Duplicate();
                foreach (var binding in profile)
                    binding.Mesh.MaterialOverride = isolated;
            }
        }
    }

    private static Node3D? FindPresentationGroup(Node node, string group)
    {
        string targetName = RuntimeWorldScene.PresentationGroupNodeName(group);
        foreach (var child in node.GetChildren())
        {
            if (child is Node3D node3D && child.Name.ToString() == targetName) return node3D;
            var nested = FindPresentationGroup(child, group);
            if (nested is not null) return nested;
        }
        return null;
    }

    /// <summary>Free the loaded world and everything it owns. Safe when nothing is loaded.</summary>
    public void Unload(bool releaseAudioImmediately = false)
    {
        _audio.StopAll(releaseAudioImmediately);

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
                var axis = RuntimeWorldScene.ToSceneRotationAxis(
                    target.Animation.Rate.X, target.Animation.Rate.Y, target.Animation.Rate.Z);
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

        // Native per-region ambient + fog override.
        PresentationEnvironment.ApplyResolvedRegion(env, r, World.Bounds.Diagonal);
    }
}
