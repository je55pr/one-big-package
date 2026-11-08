using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Game;

/// <summary>World-scoped meshes; all pack selection, poses and timing are source-owned.</summary>
internal sealed partial class Rac1ClankPackView : Node3D
{
    private readonly Rac1ClankPackAssets _assets;
    private readonly PlayerAvatar _avatar;
    private readonly Rac1PickupRandom _random;
    private Rac1ClankPackEffects _effects;
    private readonly Rac1ParticleView _particles = new() { TopLevel = true };
    private readonly List<(MeshInstance3D View, ArrayMesh Mesh)> _jets = [];
    private readonly Dictionary<int, StandardMaterial3D> _jetMaterials = [];
    private readonly Dictionary<int, QuadMesh> _jetBillboards = [];
    private readonly StandardMaterial3D _jetGlowMaterial;
    private readonly Dictionary<int, List<(MeshInstance3D View, ArrayMesh Mesh, Vector2[] Uvs, int[] Indices)>> _surfaces = [];
    internal Rac1ClankPackSession Session { get; private set; }

    internal Rac1ClankPackView(Rac1ClankPackAssets assets, PlayerAvatar avatar,
        WorldMaterialFactory materials, Rac1PickupRandom random)
    {
        _assets = assets; _avatar = avatar;
        // OBP presentation stream: cosmetic draws cannot alter gameplay/audio rolls.
        _random = random.Fork();
        Session = new(assets, _random);
        _effects = new(assets, _random);
        Name = "Rac1RetailClank_BackAttachment";
        foreach (var model in assets.Models.Values)
        {
            var entries = new List<(MeshInstance3D, ArrayMesh, Vector2[], int[])>();
            foreach (var surface in model.Surfaces)
            {
                var mesh = new ArrayMesh();
                var view = new MeshInstance3D
                {
                    Name = $"Class{model.NativeClass}_Texture{surface.TextureId}",
                    Mesh = mesh,
                    MaterialOverride = materials.Instanced("moby", surface.TextureId, false, surface.MaterialPresentation),
                    Visible = false,
                };
                entries.Add((view, mesh, Enumerable.Range(0, surface.Uvs.Length / 2)
                    .Select(i => new Vector2(surface.Uvs[i * 2], surface.Uvs[i * 2 + 1])).ToArray(), surface.Indices));
                AddChild(view);
            }
            _surfaces.Add(model.NativeClass, entries);
        }
        AddChild(_particles);
        foreach (var texture in assets.Effects.PartTextures.Values)
            _jetMaterials.Add(texture.TextureId, new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureRepeat = true,
                AlbedoTexture = ImageTexture.CreateFromImage(Image.CreateFromData(texture.Width, texture.Height, false, Image.Format.Rgba8, texture.Rgba)),
            });
        _jetGlowMaterial = (StandardMaterial3D)_jetMaterials[assets.Effects.ParticleChoices[11][0]].Duplicate();
        _jetGlowMaterial.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        for (int i = 0; i < 8; i++)
        {
            var mesh = new ArrayMesh(); var view = new MeshInstance3D { Mesh = mesh, Visible = false };
            _jets.Add((view, mesh)); AddChild(view);
        }
    }

    internal void Reset()
    {
        Session = new(_assets, _random);
        _effects = new(_assets, _random);
        foreach (var surface in _surfaces.Values.SelectMany(entries => entries)) surface.View.Visible = false;
        foreach (var jet in _jets) jet.View.Visible = false;
        _particles.Visible = false;
    }

    internal void StepEffects(PlayerAvatarAttachmentPose back, Rac1ClankPackEffects.Input input)
    {
        back = Rac1ClankPackSession.AttachmentPose(back);
        static System.Numerics.Vector3 Native(Vector3 p) => new(-p.X, p.Z, p.Y);
        System.Numerics.Vector3 World(PlayerAvatarPoint point) => Native(ToGlobal(PlayerAvatarView.ToGodotLocal(
            back.Transform(point), _avatar.Axes, _avatar.BaseHeight, true)));
        var origin = World(new(0, 0, 0));
        static PlayerAvatarPoint Point(System.Numerics.Vector3 p) => new(p.X, p.Y, p.Z);
        var worldBack = new PlayerAvatarAttachmentPose(Point(origin), Point(World(new(1, 0, 0)) - origin),
            Point(World(new(0, 1, 0)) - origin), Point(World(new(0, 0, 1)) - origin));
        Vector3 Local(double x, double y, double z) => ToLocal(new(-(float)x, (float)z, (float)y));
        var jets = _effects.StepJets(Session, input, worldBack);
        for (int i = 0; i < _jets.Count; i++)
        {
            _jets[i].View.Visible = Visible && i < jets.Count;
            if (i >= jets.Count) continue;
            var jet = jets[i];
            if (jet.BillboardSize > 0)
            {
                if (!_jetBillboards.TryGetValue(i, out var quad))
                    _jetBillboards.Add(i, quad = new QuadMesh { Size = Vector2.One * jet.BillboardSize });
                _jets[i].View.Mesh = quad;
                _jets[i].View.Position = Local(jet.Positions[0], jet.Positions[1], jet.Positions[2]);
                uint color = jet.Color;
                _jetGlowMaterial.AlbedoColor = new((color & 255) / 128f, (color >> 8 & 255) / 128f,
                    (color >> 16 & 255) / 128f, (color >> 24) / 128f);
                _jets[i].View.MaterialOverride = _jetGlowMaterial;
                continue;
            }
            _jets[i].View.Mesh = _jets[i].Mesh;
            _jets[i].View.Position = Vector3.Zero;
            var vertices = Enumerable.Range(0, jet.Positions.Length / 3)
                .Select(at => Local(jet.Positions[at * 3], jet.Positions[at * 3 + 1], jet.Positions[at * 3 + 2])).ToArray();
            var arrays = new global::Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.TexUV] = Enumerable.Range(0, jet.Uvs.Length / 2)
                .Select(at => new Vector2(jet.Uvs[at * 2], jet.Uvs[at * 2 + 1])).ToArray();
            arrays[(int)Mesh.ArrayType.Index] = jet.Indices;
            _jets[i].Mesh.ClearSurfaces(); _jets[i].Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            _jets[i].View.MaterialOverride = _jetMaterials[jet.Texture];
        }
        var heroX = Native(GlobalBasis * PlayerAvatarView.ToGodotLocal(new(1, 0, 0), _avatar.Axes, 0, false));
        _effects.StepParticles(Session, input, worldBack, heroX, Native(GetViewport().GetCamera3D()?.GlobalPosition ?? GlobalPosition));
        _particles.Visible = Visible;
        _particles.Render(_effects.Particles, _assets.Effects.PartTextures);
    }

    internal void Step(PlayerAvatarAttachmentPose back, Rac1ClankPackSession.Input input, bool clankAvailable = true)
    {
        Session.Step(input);
        foreach (var (id, surfaces) in _surfaces)
        {
            bool visible = clankAvailable && (id == Rac1ClankPackAssets.ClankClass || Session.Upgrade?.NativeClass == id);
            foreach (var surface in surfaces) surface.View.Visible = visible;
        }
        if (!clankAvailable) return;
        Present(Rac1ClankPackAssets.ClankClass, Session.Clank.Positions, back);
        if (Session.Upgrade is not null) Present(Session.Upgrade.NativeClass, Session.Upgrade.Positions, back);
    }

    private void Present(int id, double[] positions, PlayerAvatarAttachmentPose back)
    {
        var vertices = AttachedVertices(positions, back);
        foreach (var surface in _surfaces[id])
        {
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.TexUV] = surface.Uvs;
            arrays[(int)Mesh.ArrayType.Index] = surface.Indices;
            surface.Mesh.ClearSurfaces();
            surface.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
    }

    private Vector3[] AttachedVertices(double[] positions, PlayerAvatarAttachmentPose back)
    {
        back = Rac1ClankPackSession.AttachmentPose(back);
        var vertices = new Vector3[positions.Length / 3];
        for (int i = 0; i < positions.Length; i += 3)
        {
            var point = back.Transform(new(positions[i], positions[i + 1], positions[i + 2]));
            vertices[i / 3] = PlayerAvatarView.ToGodotLocal(point, _avatar.Axes, _avatar.BaseHeight, true);
        }
        return vertices;
    }
}
