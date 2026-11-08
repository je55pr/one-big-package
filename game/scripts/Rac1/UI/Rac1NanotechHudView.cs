using Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Presentation;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>Projects the recovered class-806 sphere mesh and halo into an OBP-authored health row.</summary>
public partial class Rac1NanotechHudView : Control
{
    private HudHealth? _health;
    private Rac1NanotechOrbAssets? _assets;
    private SubViewport? _viewport;
    private ImmediateMesh? _mesh;
    private Rac1FxMaterials? _materials;

    public override void _Ready()
    {
        Name = "NanotechSpheres";
        MouseFilter = MouseFilterEnum.Ignore;
        AnchorRight = 1;
        OffsetBottom = 84;
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
        var particles = new Rac1ParticleSession(assets.Particles, Rac1PickupRandom.FromSeed(0));
        using var sphere = new Rac1NanotechCluster(NVector3.Zero, assets, new Rac1NanotechOrbSpin(),
            particles, Rac1PickupRandom.FromSeed(0));
        // Use the source draw callback's mesh and halo without starting a pickup session.
        var surfaces = sphere.Build(new NVector3(10, 0, .5f));
        var points = surfaces.SelectMany(surface => surface.Positions).ToArray();
        float left = points.Min(point => point.Y), right = points.Max(point => point.Y);
        float bottom = points.Min(point => point.Z), top = points.Max(point => point.Z);
        float width = Math.Max(right - left, top - bottom) * 1.08f;
        _materials = new Rac1FxMaterials(assets.FxTextures);
        _mesh = new ImmediateMesh();
        _materials.Write(_mesh, surfaces, point => new Vector3(point.Y - (left + right) * .5f,
            point.Z - (bottom + top) * .5f, point.X));
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
        _viewport.AddChild(new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = width,
            Position = new Vector3(0, 0, 10),
            Current = true,
        });
    }

    public override void _Draw()
    {
        if (_health is not { } health || _viewport is null) return;
        int count = Math.Max(health.Current, health.Capacity ?? health.Current);
        if (count <= 0) return;
        float spacing = Math.Min(38f, Size.X * .36f / count);
        float size = Math.Min(40f, spacing * 1.05f);
        float left = Size.X * .5f - (count - 1) * spacing * .5f;
        var texture = _viewport.GetTexture();
        for (int i = 0; i < count; i++)
            DrawTextureRect(texture, new Rect2(left + i * spacing - size * .5f, 42 - size * .5f, size, size),
                false, i < health.Current ? Colors.White : new Color(.2f, .2f, .2f, .4f));
    }

    public override void _ExitTree() => Release();

    private void Release()
    {
        _viewport?.Free();
        _viewport = null;
        _mesh?.Dispose();
        _mesh = null;
        _materials?.Dispose();
        _materials = null;
        _assets = null;
    }
}
