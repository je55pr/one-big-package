using Godot;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>World-scoped native R&C1 explosions and particles; decoded source assets are borrowed.</summary>
internal readonly record struct Rac1ExplosionVisualEvent(ObpSourceGame Source, ulong Frame,
    int NativeClass, Vector3 Origin, string Kind);

internal sealed class Rac1ExplosionPresenter
{
    private readonly List<Rac1ExplosionVisualEvent> _events = [];
    private ulong _frame;
    internal Rac1ExplosionVisualEvent[] Events => [.. _events];

    private void Record(int nativeClass, Vector3 origin, string kind)
    {
        if (_events.Count == 512) _events.RemoveAt(0);
        _events.Add(new(ObpSourceGame.Rac1, _frame, nativeClass, origin, kind));
    }
    private readonly List<(Node3D Node, Rac1BombExplosion.Burst Motion, List<StandardMaterial3D> Materials)> _rac1BombBursts = [];
    private readonly List<(Node3D Node, Rac1BombExplosion.Fragment Motion)> _rac1BombFragments = [];
    private readonly Dictionary<int, QuadMesh> _rac1BombParticleMeshes = [];
    private Rac1ParticleSession? _rac1Particles;
    private Rac1ParticleView? _rac1ParticleView;

    internal Rac1ParticleSession? Particles => _rac1Particles;
    internal int FragmentCount => _rac1BombFragments.Count;
    internal int BurstCount => _rac1BombBursts.Count;
    internal int RenderedCount => _rac1ParticleView?.RenderedCount ?? 0;
    internal int DrawCalls => _rac1ParticleView?.DrawCalls ?? 0;
    internal int[] ParticleTypes => _rac1Particles is { } p ? [.. p.Live.Select(x => x.Type).Take(8192)] : [];

    internal Rac1ParticleSession? EnsureParticles(Rac1EquipmentAssets.Catalogue? assets, int? seed)
    {
        if (_rac1Particles is null && assets?.Effects is { } effects)
            _rac1Particles = new(effects, Rac1PickupRandom.FromSeed(unchecked((uint)(seed ?? System.Environment.TickCount))));
        return _rac1Particles;
    }

    internal void RetireWorld()
    {
        foreach (var node in _rac1BombBursts.Select(burst => burst.Node)
            .Concat(_rac1BombFragments.Select(fragment => fragment.Node)))
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        _rac1BombBursts.Clear(); _rac1BombFragments.Clear();
        _rac1Particles?.Clear(); _rac1Particles = null;
        if (_rac1ParticleView is not null && GodotObject.IsInstanceValid(_rac1ParticleView)) _rac1ParticleView.QueueFree();
        _rac1ParticleView = null;
        _rac1BombParticleMeshes.Clear();
        _events.Clear(); _frame = 0;
    }

    internal void SpawnBomb(Vector3 origin, Vector3 camera, Rac1PickupRandom random, Func<int, string, Node3D> createEffect,
        Action<Vector3> sound, Action<Node3D, NVector3> setOrientation,
        Rac1EquipmentAssets.Catalogue? assets, int? seed, bool playSound = true,
        Vector3? impactNormal = null, Vector3? reflectedPush = null)
    {
        bool distant = camera.DistanceTo(origin) > 9;
        SpawnBursts(origin, Rac1BombExplosion.Bursts(distant), random, createEffect);
        var push = ToEffectNative(reflectedPush ?? Vector3.Up * (2f / 60));
        SpawnFragments(Rac1BombExplosion.Fragments(ToEffectNative(origin), random,
            ToEffectNative(impactNormal ?? Vector3.Up), push, ToEffectNative(camera)), createEffect, setOrientation);
        if (playSound) sound(origin);
        if (EnsureParticles(assets, seed) is { } particles)
        {
            particles.Bomb(ToEffectNative(origin), ToEffectNative(camera), push);
            Record(121, origin, "native-bomb-pool");
        }
    }

    internal void SpawnWeapon(Rac1WeaponId weapon, Vector3 origin, Vector3 camera, bool groundedMine,
        Rac1PickupRandom random, Func<int, string, Node3D> createEffect,
        Action<Node3D, NVector3> setOrientation, Rac1EquipmentAssets.Catalogue? assets, int? seed)
    {
        var recipe = Rac1WeaponExplosion.ForWeapon(weapon);
        if (weapon == Rac1WeaponId.MineGlove && groundedMine)
        {
            if (EnsureParticles(assets, seed) is { } mineParticles)
            {
                mineParticles.MineGroundExplosion(ToEffectNative(origin), ToEffectNative(camera));
                Record(74, origin, "native-mine-ground-pool");
            }
            return;
        }
        SpawnBursts(origin, recipe.Bursts(origin.DistanceTo(camera)), random, createEffect);
        SpawnFragments(recipe.Fragments(ToEffectNative(origin), ToEffectNative(camera), random), createEffect, setOrientation);
        if (EnsureParticles(assets, seed) is { } particles)
        {
            particles.WeaponExplosion(recipe, ToEffectNative(origin), ToEffectNative(camera));
            Record(NativeExplosionClass(weapon), origin, "native-weapon-pool");
        }
    }

    // The item's equipment class is not always the child/explosion emitter's native Moby class.
    internal static int NativeExplosionClass(Rac1WeaponId weapon) => weapon switch
    {
        Rac1WeaponId.MineGlove => 74,
        Rac1WeaponId.GloveOfDoom => 230,
        _ => Rac1WeaponProfiles.Get(weapon).ModelClass
    };

    internal void SpawnBursts(Vector3 origin, IReadOnlyList<Rac1BombExplosion.Burst> bursts,
        Rac1PickupRandom random, Func<int, string, Node3D> createEffect)
    {
        foreach (var motion in bursts)
        {
            var node = createEffect(Rac1BombExplosion.BurstClass, "Rac1RetailBombBurst_Class1192");
            node.GlobalPosition = origin;
            node.Rotation = new(random.Angle(), random.Angle(), random.Angle());
            node.Scale = Vector3.One * .001f;
            var materials = new List<StandardMaterial3D>();
            foreach (var mesh in node.GetChildren().OfType<MeshInstance3D>())
            {
                if (mesh.MaterialOverride?.Duplicate() is not StandardMaterial3D material) continue;
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                material.AlbedoColor = new(motion.Color.X, motion.Color.Y, motion.Color.Z, motion.Alpha);
                material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                mesh.MaterialOverride = material;
                mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                materials.Add(material);
            }
            _rac1BombBursts.Add((node, motion, materials));
            Record(Rac1BombExplosion.BurstClass, origin, "burst");
        }
    }

    private void SpawnFragments(IReadOnlyList<Rac1BombExplosion.Fragment> fragments,
        Func<int, string, Node3D> createEffect, Action<Node3D, NVector3> setOrientation)
    {
        foreach (var motion in fragments)
        {
            var node = createEffect(122, "Rac1RetailBombFragment_Class122");
            node.GlobalPosition = FromEffectNative(motion.Position);
            node.Scale = Vector3.One * motion.Scale;
            setOrientation(node, motion.Angles);
            _rac1BombFragments.Add((node, motion));
            Record(122, node.GlobalPosition, "fragment");
        }
    }

    internal void Step(Node3D? worldRoot, Vector3 camera, Rac1EquipmentAssets.Catalogue? assets,
        int? seed, bool enforceLevelBounds, Action<Node3D, NVector3> setOrientation)
    {
        _frame++;
        for (int i = _rac1BombBursts.Count - 1; i >= 0; i--)
        {
            var burst = _rac1BombBursts[i];
            burst.Motion.Step();
            if (burst.Motion.Finished || !GodotObject.IsInstanceValid(burst.Node))
            {
                if (GodotObject.IsInstanceValid(burst.Node)) burst.Node.QueueFree();
                _rac1BombBursts.RemoveAt(i); continue;
            }
            burst.Node.Scale = Vector3.One * MathF.Max(.001f, burst.Motion.Scale);
            burst.Node.Rotation += Vector3.One * Rac1BombExplosion.BurstSpin;
            foreach (var material in burst.Materials)
                material.AlbedoColor = new(burst.Motion.Color.X, burst.Motion.Color.Y, burst.Motion.Color.Z, burst.Motion.Alpha);
        }
        for (int i = _rac1BombFragments.Count - 1; i >= 0; i--)
        {
            var fragment = _rac1BombFragments[i];
            if (fragment.Motion.Trail) EnsureParticles(assets, seed)?.Trail(fragment.Motion.Position);
            fragment.Motion.Step();
            if (fragment.Motion.Finished || !GodotObject.IsInstanceValid(fragment.Node))
            {
                if (GodotObject.IsInstanceValid(fragment.Node))
                {
                    fragment.Node.QueueFree();
                }
                _rac1BombFragments.RemoveAt(i); continue;
            }
            fragment.Node.GlobalPosition = FromEffectNative(fragment.Motion.Position);
            setOrientation(fragment.Node, fragment.Motion.Angles);
            fragment.Node.Scale = Vector3.One * MathF.Max(.001f, fragment.Motion.Scale);
        }
        if (_rac1Particles is { } particles && assets?.Effects is { } effects && worldRoot is not null)
        {
            // OBP worlds use shared coordinates, including negative axes and
            // floor heights below the retail particle cube. Terrain contacts
            // limit flame reach; the original coordinate cull is parity-only.
            particles.Step(ToEffectNative(camera), enforceLevelBounds: enforceLevelBounds);
            if (_rac1ParticleView is null)
            {
                _rac1ParticleView = new Rac1ParticleView { Name = "Rac1RetailParticlePool" };
                worldRoot.AddChild(_rac1ParticleView);
            }
            _rac1ParticleView.Render(particles, effects.PartTextures);
        }
    }

    internal QuadMesh GetParticleMesh(int poolType, IReadOnlyDictionary<int, RuntimeTexture>? equipmentTextures,
        IReadOnlyDictionary<int, RuntimeTexture>? gloveTextures)
    {
        if (!_rac1BombParticleMeshes.TryGetValue(poolType, out var mesh))
        {
            var texture = equipmentTextures?.GetValueOrDefault(poolType) ??
                gloveTextures?[poolType] ?? throw new InvalidOperationException("Native R&C1 particle texture bank unavailable.");
            var image = Image.CreateFromData(texture.Width, texture.Height, false, Image.Format.Rgba8, texture.Rgba);
            var material = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, blend_add, cull_disabled, depth_draw_never;
            uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_disable;
            instance uniform vec4 tint : source_color = vec4(1.0);
            void vertex() {
                MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
                MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz),0,0,0), vec4(0,length(MODEL_MATRIX[1].xyz),0,0), vec4(0,0,length(MODEL_MATRIX[2].xyz),0), vec4(0,0,0,1));
            }
            void fragment() {
                vec4 texel = texture(albedo_texture, UV);
                ALBEDO = texel.rgb * COLOR.rgb * tint.rgb;
                ALPHA = texel.a * COLOR.a * tint.a;
            }
            """ } };
            material.SetShaderParameter("albedo_texture", ImageTexture.CreateFromImage(image));
            mesh = new QuadMesh { Size = Vector2.One, Material = material };
            _rac1BombParticleMeshes.Add(poolType, mesh);
        }
        return mesh;
    }
    private static NVector3 ToEffectNative(Vector3 point) => new(-point.X, point.Z, point.Y);
    private static Vector3 FromEffectNative(NVector3 point) => new(-point.X, point.Z, point.Y);
}
