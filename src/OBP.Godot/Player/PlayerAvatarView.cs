using Godot;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OBP.Godot.Player;

/// <summary>
/// Presentation-only Godot view of an engine-independent <see cref="PlayerAvatar"/>.
/// Avatar frames remain model-local; this node performs only local axis conversion,
/// optional geometric-base alignment, material construction, and deterministic frame display.
/// </summary>
public sealed class PlayerAvatarView : Node3D
{
    private sealed record SurfaceState(ArrayMesh Mesh, int[] Indices, Vector2[] Uvs);

    private readonly PlayerAvatar _avatar;
    private readonly Vector3[][] _frames;
    private readonly List<SurfaceState> _surfaces = [];
    private int _currentFrame = -1;

    public PlayerAvatarView(PlayerAvatar avatar, bool alignGeometricBase = true)
    {
        Validate(avatar);
        _avatar = avatar;
        AlignGeometricBase = alignGeometricBase;
        _frames = avatar.LocalAnimationFrames
            .Select(frame => ConvertFrame(frame, avatar.Axes, avatar.BaseHeight, alignGeometricBase))
            .ToArray();

        Name = $"PlayerAvatar_{avatar.Identity.AvatarId}";
        BuildSurfaces();
        ApplyFrame(0);
    }

    public PlayerAvatarIdentity Identity => _avatar.Identity;
    public bool AlignGeometricBase { get; }
    public float FramesPerSecond => _avatar.FramesPerSecond;
    public int FrameCount => _frames.Length;
    public int CurrentFrame => Math.Max(0, _currentFrame);

    /// <summary>Display the deterministic loop frame selected for an absolute local avatar clock.</summary>
    public void SetClock(double clockSeconds) => ApplyFrame(FrameIndexAt(_avatar, clockSeconds));

    /// <summary>Display one exact frame, useful for deterministic captures and inspection.</summary>
    public void SetFrame(int frame)
    {
        if ((uint)frame >= (uint)_frames.Length)
            throw new ArgumentOutOfRangeException(nameof(frame));
        ApplyFrame(frame);
    }

    public static int FrameIndexAt(PlayerAvatar avatar, double clockSeconds) =>
        AnimationClock.FrameAt(avatar.LocalAnimationFrames.Count, avatar.FramesPerSecond, clockSeconds, LoopMode.Loop);

    /// <summary>
    /// Convert a neutral avatar-local point into Godot local space. The neutral
    /// contract names right/forward/up axes explicitly; Godot local forward is -Z.
    /// </summary>
    public static Vector3 ToGodotLocal(
        PlayerAvatarPoint point,
        PlayerAvatarAxes axes,
        double baseHeight = 0,
        bool alignGeometricBase = false)
    {
        double right = Component(point, axes.Right);
        double forward = Component(point, axes.Forward);
        double up = Component(point, axes.Up) - (alignGeometricBase ? baseHeight : 0);
        return new Vector3((float)right, (float)up, (float)-forward);
    }

    public static Vector3[] ConvertFrame(
        double[] xyz,
        PlayerAvatarAxes axes,
        double baseHeight = 0,
        bool alignGeometricBase = false)
    {
        if (xyz.Length == 0 || xyz.Length % 3 != 0)
            throw new InvalidDataException("Player avatar frame is not a non-empty XYZ stream.");

        var result = new Vector3[xyz.Length / 3];
        for (int i = 0, vertex = 0; i < xyz.Length; i += 3, vertex++)
        {
            result[vertex] = ToGodotLocal(
                new PlayerAvatarPoint(xyz[i], xyz[i + 1], xyz[i + 2]),
                axes,
                baseHeight,
                alignGeometricBase);
        }
        return result;
    }

    public static void Validate(PlayerAvatar avatar)
    {
        if (avatar.LocalAnimationFrames.Count == 0)
            throw new InvalidDataException("Player avatar has no animation frames.");
        if (avatar.FramesPerSecond <= 0 || !float.IsFinite(avatar.FramesPerSecond))
            throw new InvalidDataException("Player avatar FPS must be finite and positive.");

        int coordinateCount = avatar.LocalAnimationFrames[0].Length;
        if (coordinateCount == 0 || coordinateCount % 3 != 0 ||
            avatar.LocalAnimationFrames.Any(frame => frame.Length != coordinateCount))
            throw new InvalidDataException("Player avatar frames do not share one XYZ vertex stream.");

        int vertexCount = coordinateCount / 3;
        var textures = avatar.Textures.ToDictionary(texture => texture.MaterialId, StringComparer.Ordinal);
        foreach (var surface in avatar.Surfaces)
        {
            if (surface.Uvs.Length != vertexCount * 2)
                throw new InvalidDataException($"Player avatar surface '{surface.MaterialId}' UV count does not match the vertex stream.");
            if (surface.Indices.Length < 3 || surface.Indices.Length % 3 != 0 || surface.Indices.Any(index => index < 0 || index >= vertexCount))
                throw new InvalidDataException($"Player avatar surface '{surface.MaterialId}' has invalid triangle indices.");
            if (!textures.TryGetValue(surface.MaterialId, out var texture) || texture.TextureId != surface.TextureId)
                throw new InvalidDataException($"Player avatar surface '{surface.MaterialId}' has no matching texture payload.");
        }

        if (avatar.Surfaces.Count == 0)
            throw new InvalidDataException("Player avatar has no render surfaces.");
        foreach (var texture in avatar.Textures)
        {
            if (texture.Width <= 0 || texture.Height <= 0 || texture.Rgba.Length != texture.Width * texture.Height * 4)
                throw new InvalidDataException($"Player avatar texture '{texture.MaterialId}' has an invalid RGBA payload.");
        }
    }

    private void BuildSurfaces()
    {
        var textures = _avatar.Textures.ToDictionary(texture => texture.MaterialId, StringComparer.Ordinal);
        foreach (var surface in _avatar.Surfaces)
        {
            var texture = textures[surface.MaterialId];
            var image = Image.CreateFromData(texture.Width, texture.Height, false, Image.Format.Rgba8, texture.Rgba);
            var imageTexture = ImageTexture.CreateFromImage(image);
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                AlbedoColor = Colors.White,
                AlbedoTexture = imageTexture,
            };
            if (AlphaProfile.Analyse(texture.Rgba).AnyBelowHalf)
            {
                material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                material.AlphaScissorThreshold = 0.5f;
            }

            var mesh = new ArrayMesh();
            AddChild(new MeshInstance3D
            {
                Name = $"Surface_{surface.TextureId}",
                Mesh = mesh,
                MaterialOverride = material,
            });
            _surfaces.Add(new SurfaceState(mesh, surface.Indices, ToUvs(surface.Uvs)));
        }
    }

    private void ApplyFrame(int frame)
    {
        if (frame == _currentFrame)
            return;
        _currentFrame = frame;
        foreach (var surface in _surfaces)
        {
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _frames[frame];
            arrays[(int)Mesh.ArrayType.TexUV] = surface.Uvs;
            arrays[(int)Mesh.ArrayType.Index] = surface.Indices;
            surface.Mesh.ClearSurfaces();
            surface.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
    }

    private static Vector2[] ToUvs(float[] uv)
    {
        var result = new Vector2[uv.Length / 2];
        for (int i = 0, vertex = 0; i < uv.Length; i += 2, vertex++)
            result[vertex] = new Vector2(uv[i], uv[i + 1]);
        return result;
    }

    private static double Component(PlayerAvatarPoint point, PlayerAvatarAxisDirection axis) => axis switch
    {
        PlayerAvatarAxisDirection.PositiveX => point.X,
        PlayerAvatarAxisDirection.PositiveY => point.Y,
        PlayerAvatarAxisDirection.PositiveZ => point.Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };
}
