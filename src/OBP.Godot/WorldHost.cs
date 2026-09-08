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
    }

    private Node? _hostNode;
    private Node3D? _sceneParent;
    private WorldEnvironment? _env;
    private DirectionalLight3D? _heroLight;
    private Node3D? _skyRoot;

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
        return result;
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

        RuntimeWorldScene.AdvanceAnimated(result);

        UpdateRegionLighting(cameraGlobalPosition);
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
