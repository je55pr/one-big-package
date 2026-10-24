using OBP.Core;
using OBP.IO;
using OBP.PS2.Textures;
using OBP.RAC1.Level;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>
/// Retail-backed provider for the integrated R&amp;C1 Ratchet avatar.
/// It owns its source read and never depends on a currently loaded RuntimeWorld.
/// </summary>
public sealed class Rac1PlayerAvatarProvider : IPlayerAvatarProvider, IPlayerAnimationControllerProvider
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlayerAvatar, float[]> _equipmentMasks = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlayerAvatar, Rac1RatchetEquipmentRig> _equipmentRigs = new();
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

    public ObpSourceGame SourceGame => ObpSourceGame.Rac1;
    public string BuildId => Rac1Authority.Primary.BuildId;
    public string DefaultAvatarId => RatchetAvatarId;

    public bool CanLoad(string avatarId) =>
        string.Equals(avatarId, RatchetAvatarId, StringComparison.Ordinal);

    public IPlayerAnimationPresentationController CreateAnimationController(PlayerAvatar avatar)
    {
        if (avatar.Identity.SourceGame != SourceGame ||
            !CanLoad(avatar.Identity.AvatarId))
        {
            throw new ArgumentException("Avatar does not belong to the R&C1 player provider.", nameof(avatar));
        }

        _equipmentMasks.TryGetValue(avatar, out var mask);
        _equipmentRigs.TryGetValue(avatar, out var rig);
        return new Rac1PlayerAnimationPresentationController(avatar, mask, rig);
    }

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

        var avatar = new PlayerAvatar(
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
        var ratchet = Rac1StaticClasses.Read(core).Mobies[0];
        var payload = core.Assets.AsSpan(ratchet.AssetOffset, ratchet.AssetLength);
        var layerJoints = Animation.Rac1MobyAttachment.ReadAnimationLayerJoints(payload, 12)
            .Concat(Animation.Rac1MobyAttachment.ReadAnimationLayerJoints(payload, 13)).ToArray();
        // 0x20e4c8 selects the source arm lists through table0x15f6a0.
        var upper = new bool[native.Skeleton.Count];
        foreach (int joint in layerJoints) upper[joint] = true;
        var mask = new float[native.Mesh.Positions.Length / 3];
        for (int vertex = 0; vertex < mask.Length; vertex++)
            for (int influence = 0; influence < 3; influence++)
                if (upper[native.Mesh.VertexJoints[vertex * 3 + influence]])
                    mask[vertex] += native.Mesh.VertexWeights[vertex * 3 + influence];
        var rig = new Rac1RatchetEquipmentRig(native, layerJoints);
        avatar = avatar with { Skeleton = new(rig.Asset.Skeleton.Joints.Select(j => j.Parent).ToArray()), Skeletal = rig.Asset };
        _equipmentMasks.Add(avatar, mask);
        _equipmentRigs.Add(avatar, rig);
        return avatar;
    }

    internal static (string Id, PlayerAvatarAnimationRole Role) AnimationDescriptor(int sequenceId) =>
        sequenceId switch
        {
            Rac1RatchetAvatar.StandingSequenceId => ("standing", PlayerAvatarAnimationRole.Standing),
            Rac1RatchetAvatar.LocomotionStartSequenceId => ("locomotion-start", PlayerAvatarAnimationRole.LocomotionStart),
            Rac1RatchetAvatar.SustainedLocomotionSequenceId => ("sustained-locomotion", PlayerAvatarAnimationRole.SustainedLocomotion),
            Rac1RatchetAvatar.LocomotionStopVariantASequenceId => ("locomotion-stop-a", PlayerAvatarAnimationRole.LocomotionStopVariant),
            Rac1RatchetAvatar.LocomotionStopVariantBSequenceId => ("locomotion-stop-b", PlayerAvatarAnimationRole.LocomotionStopVariant),
            Rac1RatchetAvatar.LocomotionStopMiddleSequenceId => ("locomotion-stop-middle", PlayerAvatarAnimationRole.LocomotionStopVariant),
            Rac1RatchetAvatar.StationaryJumpSequenceId => ("stationary-jump", PlayerAvatarAnimationRole.StationaryJump),
            Rac1RatchetAvatar.MovingJumpSequenceId => ("moving-jump", PlayerAvatarAnimationRole.MovingJump),
            Rac1RatchetAvatar.CrouchSequenceId => ("crouch", PlayerAvatarAnimationRole.Crouch),
            Rac1RatchetAvatar.CrouchTurnRightSequenceId => ("crouch-turn-right", PlayerAvatarAnimationRole.CrouchTurnRight),
            Rac1RatchetAvatar.CrouchTurnLeftSequenceId => ("crouch-turn-left", PlayerAvatarAnimationRole.CrouchTurnLeft),
            Rac1RatchetAvatar.WrenchAttackSequenceId => ("primary-attack", PlayerAvatarAnimationRole.PrimaryAttack),
            24 => ("wrench-combo-second", PlayerAvatarAnimationRole.AttackVariant),
            25 => ("wrench-combo-third", PlayerAvatarAnimationRole.AttackVariant),
            26 => ("wrench-comet-strike", PlayerAvatarAnimationRole.AttackVariant),
            43 => ("wrench-hyper-strike", PlayerAvatarAnimationRole.AttackVariant),
            44 => ("bomb-glove-throw", PlayerAvatarAnimationRole.AttackVariant),
            51 => ("pyrocitor-hold", PlayerAvatarAnimationRole.AttackVariant),
            56 => ("blaster-hold", PlayerAvatarAnimationRole.AttackVariant),
            57 => ("blaster-fire", PlayerAvatarAnimationRole.AttackVariant),
            62 => ("blaster-moving", PlayerAvatarAnimationRole.AttackVariant),
            54 => ("devastator-hold", PlayerAvatarAnimationRole.AttackVariant),
            73 => ("devastator-fire", PlayerAvatarAnimationRole.AttackVariant),
            81 => ("ryno-hold", PlayerAvatarAnimationRole.AttackVariant),
            103 => ("walloper-punch", PlayerAvatarAnimationRole.AttackVariant),
            17 => ("thruster-high-jump", PlayerAvatarAnimationRole.JumpVariant),
            18 => ("heli-stretch-jump", PlayerAvatarAnimationRole.JumpVariant),
            19 => ("pack-glide", PlayerAvatarAnimationRole.Glide),
            21 => ("heli-high-jump", PlayerAvatarAnimationRole.JumpVariant),
            22 => ("aerial-boost", PlayerAvatarAnimationRole.JumpVariant),
            28 => ("directional-flip-forward-right", PlayerAvatarAnimationRole.JumpVariant),
            29 => ("directional-flip-left", PlayerAvatarAnimationRole.JumpVariant),
            31 => ("directional-flip-back", PlayerAvatarAnimationRole.JumpVariant),
            38 => ("thruster-stretch-jump", PlayerAvatarAnimationRole.JumpVariant),
            32 => ("ledge-grab", PlayerAvatarAnimationRole.Traversal),
            33 => ("ledge-hang", PlayerAvatarAnimationRole.Traversal),
            34 => ("ledge-jump", PlayerAvatarAnimationRole.JumpVariant),
            36 => ("ledge-shimmy-a", PlayerAvatarAnimationRole.Traversal),
            37 => ("ledge-shimmy-b", PlayerAvatarAnimationRole.Traversal),
            48 => ("swingshot-swing", PlayerAvatarAnimationRole.Traversal),
            55 => ("ice-slide", PlayerAvatarAnimationRole.Traversal),
            59 => ("underwater-moving", PlayerAvatarAnimationRole.Traversal),
            58 => ("surface-idle", PlayerAvatarAnimationRole.Traversal),
            101 => ("surface-paddling", PlayerAvatarAnimationRole.Traversal),
            95 => ("drowning", PlayerAvatarAnimationRole.Traversal),
            114 => ("water-exit-jump", PlayerAvatarAnimationRole.JumpVariant),
            60 => ("underwater-idle", PlayerAvatarAnimationRole.Traversal),
            61 => ("hydro-pack-underwater", PlayerAvatarAnimationRole.Traversal),
            52 => ("swingshot-attach", PlayerAvatarAnimationRole.Traversal),
            53 => ("swingshot-pendulum", PlayerAvatarAnimationRole.Traversal),
            42 => ("thruster-power-slam", PlayerAvatarAnimationRole.Traversal),
            35 => ("wall-jump", PlayerAvatarAnimationRole.JumpVariant),
            _ => throw new InvalidDataException(
                $"R&C1 Ratchet native sequence {sequenceId} has no admitted neutral avatar role."),
        };

    private static PlayerAvatarAnimationClip RuntimeClip(Rac1RatchetAvatar.AnimationClip clip)
    {
        var neutral = AnimationDescriptor(clip.SequenceId);
        return new PlayerAvatarAnimationClip(
            neutral.Id,
            neutral.Role,
            clip.LocalFrames,
            clip.FrameDurationsSeconds,
            clip.FrameTimelinePositions,
            clip.WrenchSocketFrames is null ? null :
                new Dictionary<string, IReadOnlyList<PlayerAvatarAttachmentPose>>
                {
                    [Rac1WrenchAttachment.SocketKey] = clip.WrenchSocketFrames,
                    [Gameplay.Rac1BombProjectileSession.GloveSocketKey] = clip.BombGloveSocketFrames
                        ?? throw new InvalidDataException("R&C1 Bomb Glove attachment track is absent."),
                    [Gameplay.Rac1WeaponProfiles.HandSocket(0)] = clip.WrenchSocketFrames,
                    [Gameplay.Rac1WeaponProfiles.HandSocket(1)] = clip.LeftHandSocketFrames!,
                    [Gameplay.Rac1WeaponProfiles.HandSocket(6)] = clip.BombGloveSocketFrames!,
                });
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
