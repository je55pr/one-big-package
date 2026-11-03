using OBP.Core;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Player;
using Xunit;

namespace OBP.Tests;

/// <summary>
/// Shared synthetic avatar builders and playback harness for the player-avatar presentation tests. Synthetic data only; no retail content.
/// </summary>
internal static class PlayerAvatarTestFixtures
{
    internal static readonly PlayerAvatarAxes Rac1Axes = new(
        PlayerAvatarAxisDirection.PositiveX,
        PlayerAvatarAxisDirection.PositiveY,
        PlayerAvatarAxisDirection.PositiveZ);

    internal static SourceAwarePlaybackHarness NewPlayback() => new(Avatar());

    internal static PlayerAvatar LocomotionAvatar()
    {
        var avatar = Avatar();
        return avatar with
        {
            AnimationClips = avatar.AnimationClips.Select(clip => clip.Role switch
            {
                PlayerAvatarAnimationRole.LocomotionStart => Clip(clip.Id, clip.Role, Enumerable.Repeat(1d / 15d, 33).ToArray()),
                PlayerAvatarAnimationRole.SustainedLocomotion => Clip(clip.Id, clip.Role, Enumerable.Repeat(1d / 30d, 23).ToArray()),
                PlayerAvatarAnimationRole.Crouch => Clip(clip.Id, clip.Role, Enumerable.Repeat(1d / 15d, 15).ToArray()),
                PlayerAvatarAnimationRole.CrouchTurnLeft or PlayerAvatarAnimationRole.CrouchTurnRight =>
                    Clip(clip.Id, clip.Role, Enumerable.Repeat(1d / 15d, 7).ToArray()),
                _ => clip,
            }).ToArray(),
        };
    }

    internal static PlayerAvatar LandingAvatar()
    {
        var avatar = StopAvatar();
        return avatar with
        {
            AnimationClips = [.. avatar.AnimationClips.Select(clip => clip.Role == PlayerAvatarAnimationRole.Standing
                ? Clip(clip.Id, clip.Role, Enumerable.Repeat(1d / 7.5, 8).ToArray()) with
                    { FrameTimelinePositions = Enumerable.Range(0, 8).Select(frame => (double)frame * 4).ToArray() }
                : clip),
                Clip("directional-flip-forward-right", PlayerAvatarAnimationRole.JumpVariant,
                    Enumerable.Repeat(1d / 15, 16).ToArray()) with
                    { FrameTimelinePositions = Enumerable.Range(0, 16).Select(frame => (double)frame * 2).ToArray() }],
        };
    }

    internal static void StopStep(Rac1PlayerAnimationPresentationController controller, int tick,
        float magnitude, float scalar, float contactLength) =>
        controller.StepOrdinaryAnimation(magnitude > 0 ? PlayerAnimationState.Run : PlayerAnimationState.Walk,
            tick / 60d, new(magnitude >= .25f, true, scalar,
                InputMagnitude: magnitude, ContactMotionLength: contactLength, UncappedInputMagnitude: magnitude));

    internal static PlayerAvatar StopAvatar()
    {
        var avatar = LocomotionAvatar();
        var clips = avatar.AnimationClips.Select(clip => clip.Role == PlayerAvatarAnimationRole.LocomotionStopVariant
            ? Clip(clip.Id, clip.Role, Enumerable.Repeat(1d / 15, 13).ToArray()) : clip).ToList();
        clips.Add(Clip("locomotion-stop-middle", PlayerAvatarAnimationRole.LocomotionStopVariant,
            Enumerable.Repeat(1d / 15, 14).ToArray()));
        return avatar with
        {
            AnimationClips = clips.Select(clip => clip with
            {
                FrameTimelinePositions = Enumerable.Range(0, clip.FrameCount)
                    .Select(frame => (double)frame * (clip.Role == PlayerAvatarAnimationRole.SustainedLocomotion ? 1 : 2)).ToArray(),
            }).ToArray(),
        };
    }

    internal sealed class SourceAwarePlaybackHarness
    {
        private readonly Rac1PlayerAnimationPresentationController _controller;
        private readonly PlayerAvatarView.Playback _playback;

        public SourceAwarePlaybackHarness(PlayerAvatar avatar)
        {
            _controller = new Rac1PlayerAnimationPresentationController(avatar);
            _playback = new PlayerAvatarView.Playback(avatar, _controller.Current);
        }

        public PlayerAnimationPresentation CurrentAnimationPresentation => _playback.CurrentAnimationPresentation;
        public PlayerAnimationState CurrentAnimationState => _playback.CurrentAnimationState;
        public PlayerAvatarAnimationClip CurrentClip => _playback.CurrentClip;
        public double ClipStartedAtSeconds => _playback.ClipStartedAtSeconds;
        public double ClipElapsedSeconds => _playback.ClipElapsedSeconds;
        public int CurrentFrame => _playback.CurrentFrame;

        public void SetAnimationState(PlayerAnimationState state) =>
            _playback.SetAnimationPresentation(_controller.SetAnimationState(state));

        public void SetClock(double clockSeconds)
        {
            _playback.SetClock(clockSeconds);
            _playback.SetAnimationPresentation(_controller.SetClock(clockSeconds));
        }
    }

    internal static PlayerAvatar Avatar(
        IReadOnlyList<PlayerAvatarTexture>? textures = null,
        IReadOnlyList<PlayerAvatarAnimationClip>? clips = null)
    {
        var surface = new PlayerAvatarSurface("mat", 0, [0, 0, 1, 0, 0, 1], [0, 1, 2]);
        textures ??= [new PlayerAvatarTexture("mat", 0, 1, 1, [255, 255, 255, 255])];
        clips ??=
        [
            Clip("standing", PlayerAvatarAnimationRole.Standing, [0.1, 0.1]),
            Clip("locomotion-start", PlayerAvatarAnimationRole.LocomotionStart, [0.1, 0.1]),
            Clip("sustained-locomotion", PlayerAvatarAnimationRole.SustainedLocomotion, [0.1, 0.1]),
            Clip("locomotion-stop-a", PlayerAvatarAnimationRole.LocomotionStopVariant, [0.1]),
            Clip("locomotion-stop-b", PlayerAvatarAnimationRole.LocomotionStopVariant, [0.1]),
            Clip("stationary-jump", PlayerAvatarAnimationRole.StationaryJump, [0.1, 0.2, 0.3]),
            Clip("moving-jump", PlayerAvatarAnimationRole.MovingJump, [0.05, 0.1]),
            Clip("crouch", PlayerAvatarAnimationRole.Crouch, [0.1]),
            Clip("crouch-turn-right", PlayerAvatarAnimationRole.CrouchTurnRight, [0.1]),
            Clip("crouch-turn-left", PlayerAvatarAnimationRole.CrouchTurnLeft, [0.1]),
            Clip("primary-attack", PlayerAvatarAnimationRole.PrimaryAttack, [0.1, 0.2]),
        ];
        return new PlayerAvatar(
            new PlayerAvatarIdentity(ObpSourceGame.Synthetic, "test", "avatar", "model"),
            clips,
            [surface],
            textures,
            new PlayerAvatarBounds(new(0, 0, 0), new(1, 1, 1)),
            new PlayerAvatarBounds(new(0, 0, 0), new(2, 2, 2)),
            new PlayerAvatarPoint(0, 0, 0),
            0,
            Rac1Axes);
    }

    internal static PlayerAvatarAnimationClip Clip(
        string id,
        PlayerAvatarAnimationRole role,
        IReadOnlyList<double> durations) =>
        new(id, role, durations.Select((_, index) => Frame(index)).ToArray(), durations);

    internal static double[] Frame(double marker) =>
        [marker, 0, 0, 1 + marker, 0, 0, marker, 1, 0];
}
