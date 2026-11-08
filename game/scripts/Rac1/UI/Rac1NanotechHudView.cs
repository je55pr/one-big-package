using Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Presentation;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>Projects the recovered class-806 sphere animation and particles into an OBP-authored health row.</summary>
public partial class Rac1NanotechHudView : Control
{
    private HudHealth? _health;
    private Rac1NanotechOrbAssets? _assets;
    private SubViewport? _viewport;
    private ImmediateMesh? _mesh;
    private Rac1FxMaterials? _materials;
    private Rac1NanotechCluster? _sphere;
    private Rac1NanotechOrbSpin _spin = new();
    private Rac1ParticleSession? _particles;
    private Rac1ParticleView? _particleView;
    private Camera3D? _camera;
    private CanvasItemMaterial? _composite;
    private double _animationTime;
    private float _framingScale = 1;
    private static readonly NVector3 Eye = new(10, 0, .5f);
    private const double NativeTick = 1d / 60d;

    public override void _Ready()
    {
        Name = "NanotechSpheres";
        MouseFilter = MouseFilterEnum.Ignore;
        AnchorRight = 1;
        OffsetBottom = 84;
        // Transparent viewport RGB already includes coverage. Applying alpha
        // again exaggerates the recovered halo's brightness pulse.
        _composite = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha };
        Material = _composite;
        Resized += QueueRedraw;
    }

    internal void Render(HudHealth? health)
    {
        if (health is null)
        {
            _health = null;
            Visible = false;
            return;
        }
        var assets = Rac1RetailUiAssets.NativeNanotech;
        if (_health == health && ReferenceEquals(_assets, assets) && Visible) return;
        _health = health;
        Visible = true;
        if (!ReferenceEquals(_assets, assets)) Prepare(assets);
        QueueRedraw();
    }

    private void Prepare(Rac1NanotechOrbAssets assets)
    {
        Release();
        _assets = assets;
        var random = Rac1PickupRandom.FromSeed(0);
        _particles = new Rac1ParticleSession(assets.Particles, random);
        _sphere = new Rac1NanotechCluster(NVector3.Zero, assets, _spin, _particles, random);
        var surfaces = _sphere.Build(Eye);
        var points = surfaces.SelectMany(surface => surface.Positions).ToArray();
        float left = points.Min(point => point.Y), right = points.Max(point => point.Y);
        float bottom = points.Min(point => point.Z), top = points.Max(point => point.Z);
        float originalWidth = Math.Max(right - left, top - bottom) * 1.08f;
        float width = originalWidth + 2 * Math.Abs(assets.K.BobAmplitude);
        _framingScale = width / originalWidth;
        _materials = new Rac1FxMaterials(assets.FxTextures, priorityBias: -10);
        _mesh = new ImmediateMesh();
        _viewport = new SubViewport
        {
            Name = "RecoveredNanotechSphere",
            Size = new Vector2I(128, 128),
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            HandleInputLocally = false,
        };
        AddChild(_viewport);
        _viewport.AddChild(new MeshInstance3D
        {
            Mesh = _mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = width,
            Position = ToGodot(Eye),
            Current = true,
        };
        _viewport.AddChild(_camera);
        _camera.LookAt(new Vector3(0, .5f, 0), Vector3.Up);
        _particleView = new Rac1ParticleView { Name = "RecoveredNanotechParticles" };
        _viewport.AddChild(_particleView);
        StepAnimation();
        Present();
    }

    public override void _Process(double delta)
    {
        if (_sphere is null || !IsVisibleInTree() || GetTree().Paused) return;
        _animationTime += delta;
        bool advanced = false;
        while (_animationTime >= NativeTick)
        {
            _animationTime -= NativeTick;
            StepAnimation();
            advanced = true;
        }
        if (advanced) Present();
    }

    private void StepAnimation()
    {
        var assets = _assets!;
        _spin.Step(assets.K.SpinStep, assets.K.OrbRadius);
        // A riding cluster supplies the same bob, halo pulse, orbiting sprites
        // and sparkle trails as an intact world crate, without a pickup route.
        _sphere!.Step(new(NVector3.Zero, NVector3.Zero, NVector3.UnitZ, true));
        _particles!.Hero = NVector3.Zero;
        _particles.Step(Eye + NVector3.UnitZ * _sphere.Bob, enforceLevelBounds: false);
    }

    private void Present()
    {
        // Follow the source bob with the HUD camera so the displayed centre
        // stays fixed while the native halo pulse and orbit animations continue.
        var eye = Eye + NVector3.UnitZ * _sphere!.Bob;
        _camera!.Position = ToGodot(eye);
        _materials!.Write(_mesh!, _sphere.Build(eye), ToGodot);
        _particleView!.Render(_particles!, _assets!.Particles.PartTextures);
        _viewport!.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    public override void _Draw()
    {
        if (_health is not { } health || _viewport is null) return;
        int count = Math.Max(health.Current, health.Capacity ?? health.Current);
        if (count <= 0) return;
        float spacing = Math.Min(57f, Size.X * .36f / count);
        float size = Math.Min(60f, spacing * 1.05f) * _framingScale;
        float left = Size.X * .5f - (count - 1) * spacing * .5f;
        var texture = _viewport.GetTexture();
        for (int i = 0; i < count; i++)
            DrawTextureRect(texture, new Rect2(left + i * spacing - size * .5f, 42 - size * .5f, size, size),
                // With premultiplied blending, tint RGB must include its opacity too.
                false, i < health.Current ? Colors.White : new Color(.08f, .08f, .08f, .4f));
    }

    public override void _ExitTree()
    {
        Release();
        Material = null;
        _composite?.Dispose();
        _composite = null;
    }

    private static Vector3 ToGodot(NVector3 point) => new(-point.X, point.Z, point.Y);

    private void Release()
    {
        _sphere?.Dispose();
        _sphere = null;
        _particles?.Clear();
        _particles = null;
        _viewport?.Free();
        _viewport = null;
        _camera = null;
        _particleView = null;
        _mesh?.Dispose();
        _mesh = null;
        _materials?.Dispose();
        _materials = null;
        _assets = null;
        _spin = new Rac1NanotechOrbSpin();
        _animationTime = 0;
    }
}
