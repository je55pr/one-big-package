using Godot;
using OBP.RAC1.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private readonly List<(Node3D Node, Rac1BombExplosion.Burst Motion, List<StandardMaterial3D> Materials)> _rac1BombBursts = [];
    private readonly List<(Node3D Node, Rac1BombExplosion.Fragment Motion)> _rac1BombFragments = [];
    private readonly List<(Node3D Node, int Ticks)> _rac1BombParticles = [];
    private readonly Dictionary<int, QuadMesh> _rac1BombParticleMeshes = [];

    private void ResetRac1BombPresentation()
    {
        foreach (var node in _rac1BombBursts.Select(burst => burst.Node)
            .Concat(_rac1BombFragments.Select(fragment => fragment.Node)).Concat(_rac1BombParticles.Select(particle => particle.Node)))
            if (IsInstanceValid(node)) node.QueueFree();
        _rac1BombBursts.Clear(); _rac1BombFragments.Clear(); _rac1BombParticles.Clear();
        _rac1BombParticleMeshes.Clear();
        _rac1BombThrowGeneration = 0; _rac1BombThrowActive = false; _rac1BombImpactCount = 0;
    }

    private void SpawnRac1BombExplosion(Vector3 origin, bool playSound = true)
    {
        bool distant = _player is not null && _player.GlobalPosition.DistanceTo(origin) > 9;
        foreach (var motion in Rac1BombExplosion.Bursts(distant))
        {
            var node = CreateRac1EffectNode(Rac1BombExplosion.BurstClass, "Rac1RetailBombBurst_Class1192");
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
        foreach (var motion in Rac1BombExplosion.Fragments(ToEffectNative(origin), Rac1EffectRandom))
        {
            var node = CreateRac1EffectNode(122, "Rac1RetailBombFragment_Class122");
            node.GlobalPosition = FromEffectNative(motion.Position);
            node.Scale = Vector3.One * motion.Scale;
            SetBombOrientation(node, motion.Angles);
            _rac1BombFragments.Add((node, motion));
            var trail = CreateBombParticles(4, 45, .75, 0, Vector3.Zero, 0, 1, .05f, .12f, new(1, .32f, .03f, .6f));
            trail.Name = "BombFragmentTrail_RetailType4Art";
            node.AddChild(trail);
            trail.LocalCoords = false;
            trail.Emitting = true;
        }
        if (playSound) PlayRac1PickupSound(121, 0, origin);
        SpawnBombParticleAdapter(origin);
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
            fragment.Motion.Step();
            if (fragment.Motion.Finished || !IsInstanceValid(fragment.Node))
            {
                if (IsInstanceValid(fragment.Node))
                {
                    foreach (var trail in fragment.Node.GetChildren().OfType<CpuParticles3D>())
                    {
                        trail.Reparent(_sceneResult!.Root, keepGlobalTransform: true);
                        trail.Emitting = false;
                        _rac1BombParticles.Add((trail, 45));
                    }
                    fragment.Node.QueueFree();
                }
                _rac1BombFragments.RemoveAt(i); continue;
            }
            fragment.Node.GlobalPosition = FromEffectNative(fragment.Motion.Position);
            SetBombOrientation(fragment.Node, fragment.Motion.Angles);
            fragment.Node.Scale = Vector3.One * MathF.Max(.001f, fragment.Motion.Scale);
        }
        for (int i = _rac1BombParticles.Count - 1; i >= 0; i--)
        {
            var particle = _rac1BombParticles[i];
            if (particle.Ticks > 1) _rac1BombParticles[i] = (particle.Node, particle.Ticks - 1);
            else
            {
                if (IsInstanceValid(particle.Node)) particle.Node.QueueFree();
                _rac1BombParticles.RemoveAt(i);
            }
        }
    }

    private void SpawnBombParticleAdapter(Vector3 origin)
    {
        if (_sceneResult is null) return;
        // Source type34 texture33 and 150-particle count. Godot supplies the
        // billboard rasterizer and approximate particle dynamics; native pool
        // collision, size projection and exact GS blend equations remain open.
        var particles = CreateBombParticles(34, 150, .65, 1, new(0, -11, 0), 2, 7, .035f, .1f, new(1.25f, 1.25f, 1.25f, 1));
        particles.Name = "BombSparks_RetailType34Art";
        _sceneResult.Root.AddChild(particles);
        particles.GlobalPosition = origin;
        particles.Emitting = true;
        _rac1BombParticles.Add((particles, 60));
    }

    private CpuParticles3D CreateBombParticles(int poolType, int count, double lifetime, float explosiveness,
        Vector3 gravity, float speedMin, float speedMax, float sizeMin, float sizeMax, Color color)
    {
        var mesh = GetRac1ParticleMesh(poolType);
        return new CpuParticles3D
        {
            Amount = count,
            OneShot = explosiveness == 1,
            Explosiveness = explosiveness,
            Lifetime = lifetime,
            Emitting = false,
            Direction = Vector3.Up,
            Spread = 180,
            InitialVelocityMin = speedMin,
            InitialVelocityMax = speedMax,
            Gravity = gravity,
            ScaleAmountMin = sizeMin,
            ScaleAmountMax = sizeMax,
            Color = color,
            ColorRamp = new Gradient { Colors = [color, color, new(color.R, color.G, color.B, 0)], Offsets = [0, .8f, 1] },
            Mesh = mesh,
        };
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
