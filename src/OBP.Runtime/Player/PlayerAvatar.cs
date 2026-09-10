namespace OBP.Runtime.Player;

/// <summary>
/// Stable, source-game-neutral identity for a playable avatar/model.
/// </summary>
public sealed record PlayerAvatarIdentity(
    string SourceGame,
    string BuildId,
    string AvatarId,
    string ModelId);

public readonly record struct PlayerAvatarPoint(double X, double Y, double Z);

public sealed record PlayerAvatarBounds(PlayerAvatarPoint Min, PlayerAvatarPoint Max)
{
    public double Width => Max.X - Min.X;
    public double Depth => Max.Y - Min.Y;
    public double Height => Max.Z - Min.Z;
}

public enum PlayerAvatarAxisDirection
{
    PositiveX,
    PositiveY,
    PositiveZ,
}

public sealed record PlayerAvatarAxes(
    PlayerAvatarAxisDirection Right,
    PlayerAvatarAxisDirection Forward,
    PlayerAvatarAxisDirection Up);

/// <summary>
/// One material/texture surface over the shared avatar vertex stream.
/// UVs are local model UVs and indices address every local animation frame.
/// </summary>
public sealed record PlayerAvatarSurface(
    string MaterialId,
    int TextureId,
    float[] Uvs,
    int[] Indices)
{
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>Decoded RGBA payload required to present one avatar surface.</summary>
public sealed record PlayerAvatarTexture(
    string MaterialId,
    int TextureId,
    int Width,
    int Height,
    byte[] Rgba);

/// <summary>
/// Optional hierarchy metadata only. It deliberately does not invent bind or
/// animation transforms where a source decoder has not established them.
/// </summary>
public sealed record PlayerAvatarSkeleton(IReadOnlyList<int> ParentIndices);

/// <summary>
/// Engine-independent playable-avatar presentation data.
/// <para>
/// Every clip frame is MODEL/LOCAL-SPACE XYZ. Frames are not instance-placed,
/// axis-remapped into a world, or controller-offset. This differs from
/// <see cref="RuntimeAnimatedMesh"/>, whose frames are already OBP Y-up
/// WORLD-SPACE positions.
/// </para>
/// </summary>
public sealed record PlayerAvatar(
    PlayerAvatarIdentity Identity,
    IReadOnlyList<PlayerAvatarAnimationClip> AnimationClips,
    IReadOnlyList<PlayerAvatarSurface> Surfaces,
    IReadOnlyList<PlayerAvatarTexture> Textures,
    PlayerAvatarBounds RestBounds,
    PlayerAvatarBounds AnimationBounds,
    PlayerAvatarPoint Origin,
    double BaseHeight,
    PlayerAvatarAxes Axes,
    PlayerAvatarSkeleton? Skeleton = null)
{
    /// <summary>Compatibility view of the standing clip used by existing presentation callers.</summary>
    public IReadOnlyList<double[]> LocalAnimationFrames =>
        RequiredAnimationClip(PlayerAvatarAnimationRole.Standing).LocalFrames;

    /// <summary>Constant standing FPS when available; variable-timed standing clips report zero.</summary>
    public float FramesPerSecond =>
        RequiredAnimationClip(PlayerAvatarAnimationRole.Standing).ConstantFramesPerSecond ?? 0f;

    public int VertexCount => AnimationClips.Count > 0 && AnimationClips[0].LocalFrames.Count > 0
        ? AnimationClips[0].LocalFrames[0].Length / 3
        : 0;

    public PlayerAvatarAnimationClip RequiredAnimationClip(PlayerAvatarAnimationRole role) =>
        AnimationClips.Single(clip => clip.Role == role);
}
