using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OneBigPackage;

/// <summary>Class192 skinning and its semantic0 staged-bomb socket under Ratchet joint54.</summary>
public sealed partial class Rac1BombGloveView : Node3D
{
    private readonly PlayerAvatar _avatar;
    private readonly Rac1BombGloveAssets.Asset _asset;
    private readonly List<(ArrayMesh Mesh, Vector2[] Uvs, int[] Indices)> _surfaces = [];
    private readonly Rac1AnimationFrameClock _clock;
    private long _generation;
    private int _sequence = 1;
    public Vector3 BombLocal { get; private set; }
    public int NativeSequence => _sequence;

    public Rac1BombGloveView(Rac1BombGloveAssets.Asset asset, PlayerAvatar avatar, WorldMaterialFactory materials)
    {
        _asset = asset; _avatar = avatar;
        Name = "Rac1RetailBombGlove_Joint54";
        _clock = new(asset.Clips.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<double>)pair.Value.Durations), 1, 0, 1);
        foreach (var surface in asset.Surfaces)
        {
            var uv = Enumerable.Range(0, surface.Uvs.Length / 2).Select(i => new Vector2(surface.Uvs[i * 2], surface.Uvs[i * 2 + 1])).ToArray();
            var mesh = new ArrayMesh();
            _surfaces.Add((mesh, uv, surface.Indices));
            AddChild(new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = materials.Instanced("moby", surface.TextureId, false, surface.MaterialPresentation)
            });
        }
    }

    public void StepAttached(PlayerAvatarAttachmentPose hand, long firingGeneration)
    {
        if (firingGeneration != _generation)
        {
            _generation = firingGeneration;
            // Sequence0 is the equip mechanism, not a throw. The native throw
            // moves Ratchet's hand while the glove stays in its static pose.
            _sequence = 1;
            _clock.ResumePlayback(_sequence, 0, 0, 1);
        }
        else if (_sequence == 0)
        {
            _clock.Advance();
            if (_clock.WrappedThisUpdate)
            {
                _sequence = 1;
                _clock.ResumePlayback(1, 0, 0, 1);
            }
        }
        var sample = _clock.Current;
        var clip = _asset.Clips[_sequence];
        var source = clip.Frames[sample.CurrentFrame];
        var target = clip.Frames[sample.NextFrame];
        var vertices = new double[source.Length];
        for (int i = 0; i < vertices.Length; i += 3)
        {
            var point = hand.Transform(new(source[i] + (target[i] - source[i]) * sample.Fraction,
                source[i + 1] + (target[i + 1] - source[i + 1]) * sample.Fraction,
                source[i + 2] + (target[i + 2] - source[i + 2]) * sample.Fraction));
            vertices[i] = point.X; vertices[i + 1] = point.Y; vertices[i + 2] = point.Z;
        }
        var origin = System.Numerics.Vector3.Lerp(clip.BombOrigins[sample.CurrentFrame], clip.BombOrigins[sample.NextFrame], sample.Fraction);
        BombLocal = PlayerAvatarView.ToGodotLocal(hand.Transform(new(origin.X, origin.Y, origin.Z)),
            _avatar.Axes, _avatar.BaseHeight, true);
        var converted = PlayerAvatarView.ConvertFrame(vertices, _avatar.Axes, _avatar.BaseHeight, true);
        foreach (var surface in _surfaces)
        {
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = converted;
            arrays[(int)Mesh.ArrayType.TexUV] = surface.Uvs;
            arrays[(int)Mesh.ArrayType.Index] = surface.Indices;
            surface.Mesh.ClearSurfaces();
            surface.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
    }
}
