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
/// <see cref="LocalAnimationFrames"/> are always MODEL/LOCAL-SPACE XYZ. They
/// are not instance-placed, axis-remapped into a world, or controller-offset.
/// This is intentionally different from <see cref="RuntimeAnimatedMesh"/>,
/// whose frames are already OBP Y-up WORLD-SPACE positions.
/// </para>
/// </summary>
public sealed record PlayerAvatar(
    PlayerAvatarIdentity Identity,
    IReadOnlyList<double[]> LocalAnimationFrames,
    float FramesPerSecond,
    IReadOnlyList<PlayerAvatarSurface> Surfaces,
    IReadOnlyList<PlayerAvatarTexture> Textures,
    PlayerAvatarBounds RestBounds,
    PlayerAvatarBounds AnimationBounds,
    PlayerAvatarPoint Origin,
    double BaseHeight,
    PlayerAvatarAxes Axes,
    PlayerAvatarSkeleton? Skeleton = null)
{
    public int VertexCount => LocalAnimationFrames.Count > 0
        ? LocalAnimationFrames[0].Length / 3
        : 0;
}
