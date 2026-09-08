using Godot;
using OBP.Runtime.Presentation;

namespace OBP.Godot;

/// <summary>
/// One CPU-skinned animated moby: cycles a pre-baked list of per-frame
/// scene-space vertex arrays into a single <see cref="MeshInstance3D"/>.
///
/// <para>Driven by a <em>world clock in seconds</em> (via
/// <see cref="RuntimeWorldScene.AdvanceAnimated"/>), not a per-frame tick count,
/// so a deterministic capture at a fixed settle time reproduces the same pose
/// regardless of frame rate. Frame selection is the pure
/// <see cref="AnimationClock"/>.</para>
///
/// <para>This is a plain object, not a <see cref="Node"/> — <c>OBP.Godot</c> has
/// no Godot source generator, so an engine <c>_Process</c> callback on a node
/// subclass defined here would never fire; the host advances it.</para>
/// </summary>
public sealed class AnimatedMesh
{
    private readonly Vector3[][] _frames;
    private readonly int[] _indices;
    private readonly Vector2[] _uv;
    private readonly Color[]? _colors;
    private readonly ArrayMesh _mesh;
    private int _current = -1;

    internal AnimatedMesh(
        MeshInstance3D instance, ArrayMesh mesh, Vector3[][] frames,
        int[] indices, Vector2[] uv, Color[]? colors, float framesPerSecond, string name)
    {
        Instance = instance;
        Name = name;
        FramesPerSecond = framesPerSecond;
        _mesh = mesh;
        _frames = frames;
        _indices = indices;
        _uv = uv;
        _colors = colors;
        Apply(0);
    }

    public MeshInstance3D Instance { get; }

    public string Name { get; }

    public float FramesPerSecond { get; }

    public int FrameCount => _frames.Length;

    public int CurrentFrame => System.Math.Max(0, _current);

    public LoopMode Loop { get; set; } = LoopMode.Loop;

    /// <summary>Move to the frame for <paramref name="clockSeconds"/> of elapsed (unpaused) world time.</summary>
    public void Advance(double clockSeconds)
    {
        if (_frames.Length < 2)
        {
            return;
        }

        Apply(AnimationClock.FrameAt(_frames.Length, FramesPerSecond, clockSeconds, Loop));
    }

    private void Apply(int frame)
    {
        if (frame == _current || frame < 0 || frame >= _frames.Length)
        {
            return;
        }

        _current = frame;
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _frames[frame];
        arrays[(int)Mesh.ArrayType.Index] = _indices;
        if (_uv.Length == _frames[frame].Length)
        {
            arrays[(int)Mesh.ArrayType.TexUV] = _uv;
        }

        if (_colors is not null && _colors.Length == _frames[frame].Length)
        {
            arrays[(int)Mesh.ArrayType.Color] = _colors;
        }

        _mesh.ClearSurfaces();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
}
