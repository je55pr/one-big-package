using OBP.IO;
using OBP.PS2.Textures;
using OBP.RAC1.Level;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>
/// Retail-backed provider for the integrated R&amp;C1 Ratchet avatar.
/// It owns its source read and never depends on a currently loaded RuntimeWorld.
/// </summary>
public sealed class Rac1PlayerAvatarProvider : IPlayerAvatarProvider
{
    public const string RatchetAvatarId = "ratchet";
    public const string RatchetModelId = "rac1:ratchet:moby-class-0";

    /// <summary>
    /// Explicit retail carrier for Ratchet model + Moby textures. Retail-gated
    /// tests prove the required Ratchet surface/texture payload is invariant
    /// across every present native R&amp;C1 level before this fixed choice is used.
    /// </summary>
    public const int CanonicalAvatarLevelId = 0;

    public static readonly Rac1PlayerAvatarProvider Instance = new();

    private Rac1PlayerAvatarProvider() { }

    public string SourceGame => "rac1";
    public string BuildId => Rac1Authority.Primary.BuildId;

    public bool CanLoad(string avatarId) =>
        string.Equals(avatarId, RatchetAvatarId, StringComparison.Ordinal);

    public PlayerAvatar Load(string sourcePath, string avatarId)
    {
        if (!CanLoad(avatarId))
            throw new ArgumentException($"Unknown R&C1 player avatar '{avatarId}'.", nameof(avatarId));

        using var reader = new FileRandomAccessReader(sourcePath);
        var level = Rac1DiscIndex.Read(reader).Levels
            .SingleOrDefault(level => level.LevelId == CanonicalAvatarLevelId)
            ?? throw new InvalidDataException(
                $"R&C1 canonical avatar level {CanonicalAvatarLevelId} is absent.");
        var core = Rac1LevelCore.Open(reader, level);
        var native = Rac1RatchetAvatar.Decode(core);

        var decodedTextures = RcLevelTextureTable.Read(
            core.Index,
            core.Assets,
            core.GsRam,
            core.Header.TexturesBaseOffset,
            new RcLevelTextureTable.Range(
                core.Header.MobyTextures.Count,
                core.Header.MobyTextures.Offset));

        var byId = decodedTextures.ToDictionary(texture => texture.Index);
        var surfaces = native.Surfaces
            .Select(surface => new PlayerAvatarSurface(
                MaterialId(surface.TextureId),
                surface.TextureId,
                native.Mesh.Uvs,
                surface.Indices))
            .ToArray();

        var textures = native.TextureIds
            .Distinct()
            .OrderBy(id => id)
            .Select(id =>
            {
                if (!byId.TryGetValue(id, out var texture))
                    throw new InvalidDataException(
                        $"R&C1 Ratchet surface references missing Moby texture {id}.");
                return new PlayerAvatarTexture(
                    MaterialId(id), id, texture.Width, texture.Height, texture.Rgba);
            })
            .ToArray();

        return new PlayerAvatar(
            new PlayerAvatarIdentity(SourceGame, BuildId, RatchetAvatarId, RatchetModelId),
            native.AnimationClips.Select(RuntimeClip).ToArray(),
            surfaces,
            textures,
            Bounds(native.RestBounds),
            Bounds(native.StandingBounds),
            Point(native.Origin),
            native.BaseHeightZ,
            new PlayerAvatarAxes(
                Axis(native.RightAxis), Axis(native.ForwardAxis), Axis(native.UpAxis)),
            Skeleton: null);
    }

    private static PlayerAvatarAnimationClip RuntimeClip(Rac1RatchetAvatar.AnimationClip clip)
    {
        (string Id, PlayerAvatarAnimationRole Role) neutral = clip.SequenceId switch
        {
            Rac1RatchetAvatar.StandingSequenceId => ("standing", PlayerAvatarAnimationRole.Standing),
            Rac1RatchetAvatar.LocomotionStartSequenceId => ("locomotion-start", PlayerAvatarAnimationRole.LocomotionStart),
            Rac1RatchetAvatar.SustainedLocomotionSequenceId => ("sustained-locomotion", PlayerAvatarAnimationRole.SustainedLocomotion),
            Rac1RatchetAvatar.LocomotionStopVariantASequenceId => ("locomotion-stop-a", PlayerAvatarAnimationRole.LocomotionStopVariant),
            Rac1RatchetAvatar.LocomotionStopVariantBSequenceId => ("locomotion-stop-b", PlayerAvatarAnimationRole.LocomotionStopVariant),
            Rac1RatchetAvatar.StationaryJumpSequenceId => ("stationary-jump", PlayerAvatarAnimationRole.StationaryJump),
            Rac1RatchetAvatar.MovingJumpSequenceId => ("moving-jump", PlayerAvatarAnimationRole.MovingJump),
            Rac1RatchetAvatar.CrouchSequenceId => ("crouch", PlayerAvatarAnimationRole.Crouch),
            Rac1RatchetAvatar.CrouchTurnRightSequenceId => ("crouch-turn-right", PlayerAvatarAnimationRole.CrouchTurnRight),
            Rac1RatchetAvatar.CrouchTurnLeftSequenceId => ("crouch-turn-left", PlayerAvatarAnimationRole.CrouchTurnLeft),
            Rac1RatchetAvatar.WrenchAttackSequenceId => ("primary-attack", PlayerAvatarAnimationRole.PrimaryAttack),
            _ => throw new InvalidDataException(
                $"R&C1 Ratchet native sequence {clip.SequenceId} has no admitted neutral avatar role."),
        };

        return new PlayerAvatarAnimationClip(
            neutral.Id,
            neutral.Role,
            clip.LocalFrames,
            clip.FrameDurationsSeconds);
    }

    private static string MaterialId(int textureId) => $"rac1:moby:{textureId}";

    private static PlayerAvatarPoint Point(Rac1RatchetAvatar.LocalPoint point) =>
        new(point.X, point.Y, point.Z);

    private static PlayerAvatarBounds Bounds(Rac1RatchetAvatar.LocalBounds bounds) =>
        new(Point(bounds.Min), Point(bounds.Max));

    private static PlayerAvatarAxisDirection Axis(Rac1RatchetAvatar.AxisDirection axis) => axis switch
    {
        Rac1RatchetAvatar.AxisDirection.PositiveX => PlayerAvatarAxisDirection.PositiveX,
        Rac1RatchetAvatar.AxisDirection.PositiveY => PlayerAvatarAxisDirection.PositiveY,
        Rac1RatchetAvatar.AxisDirection.PositiveZ => PlayerAvatarAxisDirection.PositiveZ,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };
}
