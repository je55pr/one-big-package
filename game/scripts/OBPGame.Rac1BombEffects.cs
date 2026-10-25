using Godot;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    private readonly List<(Node3D Node, Rac1BombExplosion.Burst Motion, List<StandardMaterial3D> Materials)> _rac1BombBursts = [];
    private readonly List<(Node3D Node, Rac1BombExplosion.Fragment Motion)> _rac1BombFragments = [];
    private readonly Dictionary<int, QuadMesh> _rac1BombParticleMeshes = [];
    private Rac1ParticleSession? _rac1Particles;
    private Rac1ParticleView? _rac1ParticleView;

    private Rac1ParticleSession? EnsureRac1Particles()
    {
        if (_rac1Particles is null && _rac1EquipmentAssets?.Effects is { } effects)
            _rac1Particles = new(effects, Rac1PickupRandom.FromSeed(unchecked((uint)(_args.FixedSeed ?? System.Environment.TickCount))));
        return _rac1Particles;
    }

    private void ResetRac1BombPresentation()
    {
        foreach (var node in _rac1BombBursts.Select(burst => burst.Node)
            .Concat(_rac1BombFragments.Select(fragment => fragment.Node)))
            if (IsInstanceValid(node)) node.QueueFree();
        _rac1BombBursts.Clear(); _rac1BombFragments.Clear();
        _rac1Particles?.Clear(); _rac1Particles = null;
        if (_rac1ParticleView is not null && IsInstanceValid(_rac1ParticleView)) _rac1ParticleView.QueueFree();
        _rac1ParticleView = null;
        _rac1BombParticleMeshes.Clear();
        _rac1BombThrowGeneration = 0; _rac1BombThrowActive = false; _rac1BombImpactCount = 0;
    }

    private void SpawnRac1BombExplosion(Vector3 origin, bool playSound = true, Vector3? impactNormal = null, Vector3? reflectedPush = null)
    {
        Vector3 camera = GetViewport().GetCamera3D()?.GlobalPosition ?? _player?.GlobalPosition ?? origin;
        bool distant = camera.DistanceTo(origin) > 9;
        SpawnRac1ExplosionBursts(origin, Rac1BombExplosion.Bursts(distant));
        var push = ToEffectNative(reflectedPush ?? Vector3.Up * (2f / 60));
        SpawnRac1ExplosionFragments(Rac1BombExplosion.Fragments(ToEffectNative(origin), Rac1EffectRandom,
            ToEffectNative(impactNormal ?? Vector3.Up), push, ToEffectNative(camera)));
        if (playSound) PlayPlayerEquipmentSound(121, 0, origin);
        EnsureRac1Particles()?.Bomb(ToEffectNative(origin), ToEffectNative(camera), push);
    }

    private void SpawnRac1WeaponExplosion(Rac1WeaponId weapon, Vector3 origin)
    {
        var recipe = Rac1WeaponExplosion.ForWeapon(weapon);
        Vector3 camera = GetViewport().GetCamera3D()?.GlobalPosition ?? _player?.GlobalPosition ?? origin;
        if (weapon == Rac1WeaponId.MineGlove && EquipmentGround(origin, .5f, 2.5f) is { } ground && origin.Y - ground.Y <= 2)
        {
            EnsureRac1Particles()?.MineGroundExplosion(ToEffectNative(origin), ToEffectNative(camera));
            return;
        }
        SpawnRac1ExplosionBursts(origin, recipe.Bursts(origin.DistanceTo(camera)));
        SpawnRac1ExplosionFragments(recipe.Fragments(ToEffectNative(origin), ToEffectNative(camera), Rac1EffectRandom));
        EnsureRac1Particles()?.WeaponExplosion(recipe, ToEffectNative(origin), ToEffectNative(camera));
    }

    private void SpawnRac1ExplosionBursts(Vector3 origin, IReadOnlyList<Rac1BombExplosion.Burst> bursts)
    {
        foreach (var motion in bursts)
        {
            var node = CreatePlayerEquipmentEffectNode(Rac1BombExplosion.BurstClass, "Rac1RetailBombBurst_Class1192");
            node.GlobalPosition = origin;
            node.Rotation = new(Rac1EffectRandom.Angle(), Rac1EffectRandom.Angle(), Rac1EffectRandom.Angle());
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
        }
    }

    private void SpawnRac1ExplosionFragments(IReadOnlyList<Rac1BombExplosion.Fragment> fragments)
    {
        foreach (var motion in fragments)
        {
            var node = CreatePlayerEquipmentEffectNode(122, "Rac1RetailBombFragment_Class122");
            node.GlobalPosition = FromEffectNative(motion.Position);
            node.Scale = Vector3.One * motion.Scale;
            SetBombOrientation(node, motion.Angles);
            _rac1BombFragments.Add((node, motion));
        }
    }

    private void StepRac1BombEffectsPhysics()
    {
        for (int i = _rac1BombBursts.Count - 1; i >= 0; i--)
        {
            var burst = _rac1BombBursts[i];
            burst.Motion.Step();
            if (burst.Motion.Finished || !IsInstanceValid(burst.Node))
            {
                if (IsInstanceValid(burst.Node)) burst.Node.QueueFree();
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
            if (fragment.Motion.Trail) EnsureRac1Particles()?.Trail(fragment.Motion.Position);
            fragment.Motion.Step();
            if (fragment.Motion.Finished || !IsInstanceValid(fragment.Node))
            {
                if (IsInstanceValid(fragment.Node))
                {
                    fragment.Node.QueueFree();
                }
                _rac1BombFragments.RemoveAt(i); continue;
            }
            fragment.Node.GlobalPosition = FromEffectNative(fragment.Motion.Position);
            SetBombOrientation(fragment.Node, fragment.Motion.Angles);
            fragment.Node.Scale = Vector3.One * MathF.Max(.001f, fragment.Motion.Scale);
        }
        if (_rac1Particles is { } particles && _rac1EquipmentAssets?.Effects is { } effects && _sceneResult is not null)
        {
            Vector3 camera = GetViewport().GetCamera3D()?.GlobalPosition ?? _player?.GlobalPosition ?? Vector3.Zero;
            // OBP worlds use shared coordinates, including negative axes and
            // floor heights below the retail particle cube. Terrain contacts
            // limit flame reach; the original coordinate cull is parity-only.
            particles.Step(ToEffectNative(camera), enforceLevelBounds: !OrdinaryPlayerSession && !_args.Rac1WeaponsSmoke && !_args.Rac1BombGloveSmoke);
            if (_rac1ParticleView is null)
            {
                _rac1ParticleView = new Rac1ParticleView { Name = "Rac1RetailParticlePool" };
                _sceneResult.Root.AddChild(_rac1ParticleView);
            }
            _rac1ParticleView.Render(particles, effects.PartTextures);
        }
    }

    private QuadMesh GetRac1ParticleMesh(int poolType)
    {
        if (!_rac1BombParticleMeshes.TryGetValue(poolType, out var mesh))
        {
            var texture = _rac1EquipmentAssets?.ParticleTextures.GetValueOrDefault(poolType) ??
                _rac1BombGloveAsset!.ParticleTexturesByPoolType[poolType];
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
}
