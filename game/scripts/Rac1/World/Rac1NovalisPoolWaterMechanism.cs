using Godot;
using OBP.Godot;
using OBP.IO;
using OBP.Core;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>
/// Presents Novalis's pool tiles (rock pools and cave tanks). The class-751 controller selects tiles by
/// the clip volume that holds the camera, steps a ripple height field, and the native draw callback
/// submits each visible 4x4-cell sub-tile in two GS contexts: a scrolling pattern texture and a
/// reflection-mapped texture, both alpha-blended by constant ALPHA FIX / 128 with a grey light taken
/// from the surface normals. The sim and surface maths live in OBP.RAC1; this class only hosts them.
/// </summary>
internal sealed class Rac1NovalisPoolWaterMechanism(Rac1AuthoredMechanismPorts ports, Rac1NovalisUnderwaterLook underwaterLook) : Rac1AuthoredMechanism
{
    private const double NativeFrameRate = 60;
    private const int MaximumFramesPerTick = 4;

    private sealed class TileView(Rac1NovalisPoolWater.Tile source, MeshInstance3D node, ShaderMaterial material)
    {
        public Rac1NovalisPoolWater.Tile Source { get; } = source;
        public MeshInstance3D Node { get; } = node;
        public ShaderMaterial Material { get; } = material;
        public ArrayMesh Mesh { get; } = (ArrayMesh)node.Mesh;
        public int BuiltMask { get; set; } = -1;   // no index list built yet (every ushort is a valid mask, 0xffff included)
        public int[] Indices { get; set; } = [];
        public Rac1PoolWaterSurface.Buffers Buffers { get; } = new();
        public readonly Vector3[] ScenePositions = new Vector3[Rac1PoolWaterSurface.VertexCount];
        public readonly Color[] Colours = new Color[Rac1PoolWaterSurface.VertexCount];
        public readonly Vector2[] BaseUv = new Vector2[Rac1PoolWaterSurface.VertexCount];
        public readonly Vector2[] ReflectionUv = new Vector2[Rac1PoolWaterSurface.VertexCount];
    }

    private readonly List<TileView> _tiles = [];
    private readonly Dictionary<int, ImageTexture> _textures = [];
    private readonly Random _random = new(0x4e4f56);   // host RNG: the native game RNG is not part of the recovered state
    private IReadOnlyList<Rac1ClipVolume> _volumes = [];
    private Rac1NovalisPoolWater? _water;
    private Rac1PoolWaterSimulation? _simulation;
    private Node3D? _root;
    private Shader? _shader;
    private ushort[] _masks = [];
    private Func<int, bool>? _tileActive;
    private double _carry;
    private long _frame;
    private Rac1WaterDomain _previousWater;
    private Rac1NovalisPoolBubbles? _bubbleEmitter;
    private Rac1EffectAssets? _bubbleAssets;
    private Rac1ParticleSession? _bubbles;
    private Rac1ParticleView? _bubbleView;
    private readonly Rac1PickupRandom _bubbleRandom = Rac1PickupRandom.FromSeed(0x4e4f5643);

    internal override void Configure(RuntimeWorld world, RuntimeWorldScene.Result scene)
    {
        if (world.Game != OBP.Core.ObpSourceGame.Rac1 || world.LevelId != Rac1NovalisPoolWater.NativeLevelId) return;
        if (ports.SourcePath() is not { } path)
        {
            GD.PushError("[rac1-novalis-pools] source disc path unavailable; pool surfaces are not presented.");
            return;
        }
        IReadOnlyDictionary<int, RuntimeTexture> fx;
        try
        {
            using var reader = new FileRandomAccessReader(path);
            var level = Rac1DiscIndex.Read(reader).Levels.Single(candidate => candidate.LevelId == world.LevelId);
            byte[] gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            _water = Rac1NovalisPoolWater.Read(Rac1ExecutableOverlay.Read(reader, level), Rac1Instances.Parse(gameplay).MobyInstances);
            _volumes = Rac1ClipVolumes.Read(gameplay);
            foreach (var zone in _water.Zones)
                if (zone.ClipVolume >= _volumes.Count)
                    throw new InvalidDataException($"Pool zone {zone.Index} names missing clip volume {zone.ClipVolume}.");
            var wanted = _water.Tiles.SelectMany(tile => new[] { tile.Texture, tile.SecondTexture }).ToHashSet();
            var core = Rac1LevelCore.Open(reader, level);
            fx = Rac1FxTextures.Read(core, wanted);
            _bubbleEmitter = Rac1NovalisPoolBubbles.Read(Rac1ExecutableOverlay.Read(reader, level));
            _bubbleAssets = Rac1EffectAssets.DecodePoolBubbles(core);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or InvalidOperationException or OverflowException)
        {
            _water = null;
            GD.PushError($"[rac1-novalis-pools] retail pool tables unavailable; not presented: {exception.Message}");
            return;
        }

        _simulation = new Rac1PoolWaterSimulation(_water.Tiles);
        _simulation.SeedInitialRipples((low, high) => low + (float)_random.NextDouble() * (high - low));
        _masks = new ushort[_water.Tiles.Count];
        _root = new Node3D { Name = "Rac1NovalisPoolWater", TopLevel = true };
        scene.Root.AddChild(_root);
        _bubbles = new Rac1ParticleSession(_bubbleAssets!, Rac1PickupRandom.FromSeed(0x4e4f5642));   // host RNG, visual only
        _bubbleView = new Rac1ParticleView
        {
            Name = "Rac1NovalisPoolBubbles",
            SourceBank = new ObpSourceRef(ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, "LEVEL1"),
        };
        _root.AddChild(_bubbleView);
        _shader = new Shader { Code = ShaderCode };
        foreach (var tile in _water.Tiles)
        {
            var material = new ShaderMaterial { Shader = _shader, RenderPriority = Rac1NovalisScrollingWaterMechanism.WaterRenderPriority };
            material.SetShaderParameter("tex_scroll", TextureFor(fx, tile.SecondTexture));
            material.SetShaderParameter("tex_reflect", TextureFor(fx, tile.Texture));
            material.SetShaderParameter("fix_alpha", new Vector2(tile.Alpha1Fix / 128f, tile.Alpha2Fix / 128f));
            var node = new MeshInstance3D
            {
                Name = $"PoolTile{tile.Index}",
                Mesh = new ArrayMesh(),
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                ExtraCullMargin = 2f,
                Visible = false,
            };
            _root.AddChild(node);
            _tiles.Add(new TileView(tile, node, material));
        }
        GD.Print($"[rac1-novalis-pools] {_tiles.Count} native pool tiles in {_water.Zones.Count} camera zones ready");
    }

    internal override void Tick(double delta, Rac1AuthoredTickPhase phase, PlayerHost? player)
    {
        if (phase != Rac1AuthoredTickPhase.Visual || _water is null || _simulation is null) return;
        _carry = Math.Min(_carry + Math.Max(0, delta) * NativeFrameRate, MaximumFramesPerTick);
        var camera = _root!.GetViewport().GetCamera3D();
        if (camera is null) return;
        // Scene (x, y, z) is native (-x, z, y); the controller reads the camera position in native space.
        var cameraNative = new NVector3(-camera.GlobalPosition.X, camera.GlobalPosition.Z, camera.GlobalPosition.Y);
        TrackPlayerSplashes(player);
        while (_carry >= 1)
        {
            _carry--;
            UpdateFrame(cameraNative);
        }
        Present(cameraNative);
    }

    /// <summary>
    /// Hero water-state splashes (hero_set_state / the swim and air motion updates): entering from the air
    /// pushes the surface down by 0.4 (radius 0.5), surfacing from a dive up by 0.3, diving down by 0.3 and
    /// leaving the water up by 0.35 (radius 0.4 each). OBP detects the same transitions from the recovered
    /// swim state; the native one-frame timers inside the states are not reproduced.
    /// </summary>
    private void TrackPlayerSplashes(PlayerHost? player)
    {
        if (player is null || !GodotObject.IsInstanceValid(player)) { _previousWater = Rac1WaterDomain.Dry; return; }
        var current = player.Rac1SwimState?.Water ?? Rac1WaterDomain.Dry;
        if (current != _previousWater)
        {
            // Native Z-up: scene (x, y, z) is native (-x, z, y).
            float x = -player.GlobalPosition.X, y = player.GlobalPosition.Z;
            var simulation = _simulation!;
            switch ((_previousWater, current))
            {
                case (Rac1WaterDomain.Dry, Rac1WaterDomain.Surface) when player.Velocity.Y < -0.5f:
                    simulation.Splash(x, y, 0.5f, -0.4f, add: false);
                    break;
                case (Rac1WaterDomain.Underwater, Rac1WaterDomain.Surface):
                    simulation.Splash(x, y, 0.4f, 0.3f, add: false);
                    break;
                case (Rac1WaterDomain.Surface, Rac1WaterDomain.Underwater):
                    simulation.Splash(x, y, 0.4f, -0.3f, add: false);
                    break;
                case (_, Rac1WaterDomain.Dry) when _previousWater != Rac1WaterDomain.Dry:
                    simulation.Splash(x, y, 0.4f, 0.35f, add: false);
                    break;
            }
        }
        _previousWater = current;
    }

    /// <summary>One native game frame of the class-751 update (0x2fd0e8): zone selection, splashes, flush.</summary>
    private void UpdateFrame(NVector3 camera)
    {
        var water = _water!;
        _frame++;
        Array.Clear(_masks);
        int found = -1;
        foreach (var zone in water.Zones)
        {
            if (zone.ClipVolume < 0 || !_volumes[zone.ClipVolume].Contains(camera)) continue;
            found = zone.Index;   // the last zone holding the camera, whether or not it owns tiles
            for (int k = 0; k < zone.TileCount; k++)
            {
                var tile = water.Tiles[zone.FirstTile + k];
                _masks[tile.Index] = tile.ZoneMask;
                if (_random.Next(zone.SplashOdds) == 0)
                {
                    float x = tile.X + (float)(_random.NextDouble() * 8 - 4);
                    float y = tile.Y + (float)(_random.NextDouble() * 8 - 4);
                    _simulation!.Splash(x, y, 1f, -0.05f, add: true, tile.Index, 1);
                }
            }
        }
        if (found >= 0) underwaterLook.SelectZone(found);   // the controller rewrites the underwater colours each frame it finds a zone
        if (found == Rac1NovalisPoolBubbles.Zone) _bubbleEmitter!.Emit(_bubbles!, _bubbleRandom, _frame);
        _bubbles!.Step(camera, enforceLevelBounds: false);
        _simulation!.Flush(_tileActive ??= index => _masks[index] != 0);
    }

    private void Present(NVector3 camera)
    {
        // The light heading the controller samples from the level key light when it first runs is not
        // recovered; it only tilts highlights on rippled water (flat water is lit by the vertical term alone).
        const float sun = 0f;
        _bubbleView!.Render(_bubbles!, _bubbleAssets!.PartTextures);
        foreach (var view in _tiles)
        {
            ushort mask = _masks[view.Source.Index];
            view.Node.Visible = mask != 0;
            if (mask == 0) continue;
            Rac1PoolWaterSurface.Build(view.Source, _simulation!, camera, sun, view.Buffers);
            var scroll = Rac1PoolWaterSurface.ScrollAt(view.Source, _frame);
            view.Material.SetShaderParameter("offsets", new Vector4(scroll.FirstU, scroll.FirstV, scroll.SecondU, scroll.SecondV));
            if (view.BuiltMask != mask) { view.Indices = BuildIndices(mask); view.BuiltMask = mask; }
            for (int j = 0; j < Rac1PoolWaterSurface.Vertices; j++)
                for (int i = 0; i < Rac1PoolWaterSurface.Vertices; i++)
                {
                    int index = j * Rac1PoolWaterSurface.Vertices + i;
                    var p = view.Buffers.Positions[index];
                    view.ScenePositions[index] = new Vector3(-p.X, p.Z, p.Y);
                    float grey = view.Buffers.Grey[index] / 255f;
                    view.Colours[index] = new Color(grey, grey, grey, ((i + j) & 1) == 0 ? 0f : 1f);   // alpha: scroll phase
                    view.BaseUv[index] = new Vector2(i / 4f, j / 4f);
                    var r = view.Buffers.ReflectionUv[index];
                    view.ReflectionUv[index] = new Vector2(r.X, r.Y);
                }
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = view.ScenePositions;
            arrays[(int)Mesh.ArrayType.Color] = view.Colours;
            arrays[(int)Mesh.ArrayType.TexUV] = view.BaseUv;
            arrays[(int)Mesh.ArrayType.TexUV2] = view.ReflectionUv;
            arrays[(int)Mesh.ArrayType.Index] = view.Indices;
            view.Mesh.ClearSurfaces();
            if (view.Indices.Length > 0) view.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
    }

    /// <summary>Two triangles for every cell of each sub-tile whose bit is set (bit i: row i >> 2, column i &amp; 3).</summary>
    private static int[] BuildIndices(ushort mask)
    {
        var indices = new List<int>();
        const int V = Rac1PoolWaterSurface.Vertices;
        for (int row = 0; row < Rac1NovalisPoolWater.Cells; row++)
            for (int column = 0; column < Rac1NovalisPoolWater.Cells; column++)
            {
                int sub = (row >> 2) * 4 + (column >> 2);
                if ((mask & (1 << sub)) == 0) continue;
                int a = row * V + column, b = a + 1, c = a + V, d = c + 1;
                indices.AddRange([a, c, b, b, c, d]);
            }
        return [.. indices];
    }

    private ImageTexture TextureFor(IReadOnlyDictionary<int, RuntimeTexture> fx, int id)
    {
        if (_textures.TryGetValue(id, out var existing)) return existing;
        var source = fx[id];
        var texture = ImageTexture.CreateFromImage(Image.CreateFromData(source.Width, source.Height, false,
            Image.Format.Rgba8, source.Rgba));
        _textures.Add(id, texture);
        return texture;
    }

    public override void Dispose()
    {
        if (_root is not null && GodotObject.IsInstanceValid(_root)) _root.QueueFree();
        _root = null;
        foreach (var view in _tiles) { view.Material.Dispose(); view.Mesh.Dispose(); }
        _tiles.Clear();
        foreach (var texture in _textures.Values) texture.Dispose();
        _textures.Clear();
        _shader?.Dispose();
        _shader = null;
        _bubbles?.Clear();
        _bubbles = null;
        _bubbleView = null;
        _bubbleAssets = null;
        _bubbleEmitter = null;
        _simulation = null;
        _water = null;
    }

    // Context 1: scrolling pattern (texture +0x18, ALPHA_1 = byte +0x1d); the vertex colour alpha selects
    // which of the two checkerboard scroll phases the vertex uses. Context 2: reflection-mapped texture
    // (+0x14, ALPHA_2 = +0x1c) on the per-vertex coordinates. Context 2 is drawn over context 1 with the
    // same grey light (0x80 = 1.0); the two constant-alpha blends fold into one:
    // result = D(1-a1)(1-a2) + a1(1-a2)C1 + a2 C2.
    private const string ShaderCode = """
        shader_type spatial;
        render_mode unshaded, blend_mix, cull_disabled, depth_draw_never;
        uniform sampler2D tex_scroll : source_color, filter_linear, repeat_enable;
        uniform sampler2D tex_reflect : source_color, filter_linear, repeat_enable;
        uniform vec4 offsets = vec4(0.0);
        uniform vec2 fix_alpha = vec2(0.5, 0.25);
        varying vec2 uv_scroll;
        void vertex() {
            uv_scroll = UV + (COLOR.a > 0.5 ? offsets.zw : offsets.xy);
        }
        void fragment() {
            vec3 light = vec3(COLOR.r * (255.0 / 128.0));
            vec3 first = min(texture(tex_scroll, uv_scroll).rgb * light, vec3(1.0));
            vec3 second = min(texture(tex_reflect, UV2).rgb * light, vec3(1.0));
            float a1 = fix_alpha.x;
            float a2 = fix_alpha.y;
            float coverage = 1.0 - (1.0 - a1) * (1.0 - a2);
            ALBEDO = (a1 * (1.0 - a2) * first + a2 * second) / max(coverage, 0.0001);
            ALPHA = coverage;
        }
        """;
}
