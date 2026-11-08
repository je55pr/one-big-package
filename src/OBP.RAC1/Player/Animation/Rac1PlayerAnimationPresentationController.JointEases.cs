using System.Numerics;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

public sealed partial class Rac1PlayerAnimationPresentationController
{
    private Rac1RatchetJointEases? _jointEases;
    private IReadOnlyList<Rac1RatchetJointEases.Offset> _easeOffsets = [];
    private PlayerAnimationPresentation? _easeBase;
    private int _sourceHandJoint = 56;
    /// <summary>Independently skinned poses before/after this update's procedural rotations.</summary>
    public (IReadOnlyList<double> Before, IReadOnlyList<double> After)? JointEaseSkinChange { get; private set; }

    /// <summary>True once a retail ease table has been admitted for this avatar.</summary>
    public bool HasJointEases => _equipmentRig?.HeroEases is not null;

    /// <summary>Why the joint layer is unavailable for this avatar, or null when it is running.</summary>
    public string? JointLayerStatus => _equipmentRig?.HeroEaseStatus ?? (_equipmentRig is null ? "Procedural joint layer unavailable: avatar has no skeletal rig." : null);

    /// <summary>The last stepped ease angles, for diagnostics and tests.</summary>
    public Rac1RatchetJointEases? JointEases => _jointEases;

    /// <summary>Height and length of the forward wall probe the host should cast before the next step.</summary>
    public (float Height, float Length) JointEaseWallProbe(bool equipmentActive) =>
        (Rac1RatchetJointEases.WallProbeHeight(equipmentActive), _jointEases?.WallProbeLength ?? 1.3f);

    /// <summary>
    /// Step Ratchet's procedural joint layer once per 60 Hz update, after the
    /// ordinary animation step and before equipment is evaluated. The layer is a
    /// presentation overlay: it never alters native clocks, sequences or movement.
    /// </summary>
    public void StepJointEases(float yawError, bool carriesEquipment = false,
        float? forwardWallDistance = null)
    {
        if (_equipmentRig?.HeroEases is not { } table) return;
        _jointEases ??= new(table);
        int sequence = EquipmentBaseSample().Sequence;
        var pose = Current.SemanticState switch
        {
            PlayerAnimationState.Walk or PlayerAnimationState.Run when sequence is 3 or 4 => Rac1EasePose.Run,
            PlayerAnimationState.JumpRise or PlayerAnimationState.Fall when sequence is 7 or 8 => Rac1EasePose.Airborne,
            PlayerAnimationState.Land => Rac1EasePose.Land,
            PlayerAnimationState.Idle when sequence is 0 or 1 or 2 => Rac1EasePose.Idle,
            _ => Rac1EasePose.None,
        };
        _easeOffsets = _jointEases.Step(new(pose, yawError, sequence,
            CarriesEquipment: carriesEquipment, ForwardWallDistance: forwardWallDistance));
    }

    /// <summary>Discard eased angles, e.g. after respawn or a world reload.</summary>
    public void ResetJointEases()
    {
        // Drop the ease overlay from the presented pose, not only the spring state.
        if (_easeBase is not null)
        {
            Current = _easeBase;
            _easeBase = null;
        }
        _jointEases?.Reset();
        _easeOffsets = [];
        JointEaseSkinChange = null;
    }

    private PlayerAnimationPresentation ApplyJointEases()
    {
        if (_equipmentRig is not { } rig || _easeOffsets.Count == 0) return Current;
        var basis = EquipmentBaseSample();
        var baseline = Current.SampledPose?.LocalPositions ?? CurrentClip.LocalFrames[
            CurrentClip.FrameIndexAt(Current.ElapsedAt(_clockSeconds), Current.Loop)];
        var socketJoints = SocketJoints();
        var (positions, deltas) = rig.ApplyJointOffsets(basis, baseline, _easeOffsets, socketJoints.Values.Distinct().ToArray(), ResolvedBodyPose());
        JointEaseSkinChange = rig.JointEaseSkinChange;
        if (ReferenceEquals(positions, baseline)) return Current;
        if (_attachments.TryGetValue(baseline, out var sources))
        {
            var sockets = _attachments.GetOrCreateValue(positions);
            foreach (var (key, pose) in sources)
                sockets[key] = socketJoints.TryGetValue(key, out int joint) && deltas.TryGetValue(joint, out var delta)
                    ? Move(pose, delta) : pose;
        }
        _easeBase = Current;
        Current = Current with { SampledPose = new(Current.SampledPose?.FrameIndex ?? 0, positions) };
        return Current;
    }

    // Hand and back sockets ride on joints the procedural layer can move.
    private Dictionary<string, int> SocketJoints() => new()
    {
        [Rac1WrenchAttachment.SocketKey] = 56,
        [Gameplay.Rac1BombProjectileSession.GloveSocketKey] = 54,
        [Gameplay.Rac1WeaponProfiles.HandSocket(0)] = 56,
        [Gameplay.Rac1WeaponProfiles.HandSocket(1)] = 55,
        [Gameplay.Rac1WeaponProfiles.HandSocket(6)] = 54,
        [PlayerEquipmentPose.HandAttachmentKey] = _sourceHandJoint,
        [Rac1ClankPackAssets.SocketKey] = Rac1ClankPackAssets.RatchetJoint,
    };

    private static PlayerAvatarAttachmentPose Move(PlayerAvatarAttachmentPose pose, Matrix4x4 delta)
    {
        static Vector3 V(PlayerAvatarPoint p) => new((float)p.X, (float)p.Y, (float)p.Z);
        static PlayerAvatarPoint P(Vector3 v) => new(v.X, v.Y, v.Z);
        return new(P(Vector3.Transform(V(pose.Origin), delta)), P(Vector3.TransformNormal(V(pose.X), delta)),
            P(Vector3.TransformNormal(V(pose.Y), delta)), P(Vector3.TransformNormal(V(pose.Z), delta)));
    }
}
