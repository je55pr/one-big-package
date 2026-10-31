using System.Collections.Immutable;
using Godot;
using OBP.Game.Diagnostics;
using OBP.Godot;
using OBP.Runtime.Audio;
using OBP.Runtime.Player;
using OBP.RAC1.Player;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

/// <summary>Owns only the native player voices. World/equipment one-shots retain WorldHost ownership.</summary>
internal sealed class GodotRac1PlayerAudioPlayback(Node reactionOwner, Func<Node?> player,
    Func<RuntimeAudioPlaybackIntent, bool> playEquipment) : IRac1PlayerAudioPlayback
{
    private readonly RuntimeAudioHost[] _hosts = [new(), new(), new(), new(), new()];
    public bool Play(Rac1PlayerAudioChannel channel, RuntimeAudioPlaybackIntent intent) =>
        player() is { } owner && GodotObject.IsInstanceValid(owner) &&
        _hosts[(int)channel].PlayEffect(channel == Rac1PlayerAudioChannel.Reaction ? reactionOwner : owner, intent);
    public bool PlayEquipment(RuntimeAudioPlaybackIntent intent) => playEquipment(intent);
    public void Stop(Rac1PlayerAudioChannel channel, bool immediately) => _hosts[(int)channel].StopAll(immediately);
    public void SetPaused(bool paused) { foreach (var host in _hosts) host.SetPaused(paused); }
    public ImmutableArray<DiagnosticAudioVoice> ReadVoices() => [.. _hosts.SelectMany(host => host.ReadVoices())
        .Select(voice => new DiagnosticAudioVoice(voice.Name, voice.Pcm, voice.ForwardLoop, voice.Playing, voice.Paused, voice.CanProcess))];

    internal static void Step(Rac1PlayerAudioPresenter? audio, Rac1PlayerAnimationPresentationController controller,
        PlayerHost? player, bool loading)
    {
        if (player is null) return;
        audio?.Step(new(controller.AnimationSoundSample, controller.Current.ClipStartedAtSeconds,
            controller.AudioNativeAction, controller.AudioActionAge, player.Rac1AudioSurfaceEffectMode,
            player.Rac1AudioPackId, player.Rac1AudioGrounded, MovementAction: player.Rac1AudioMovementAction,
            FractionAdvance: controller.OrdinaryLocomotionFractionAdvance ?? 0), player.Rac1WrenchFrame.Generation,
            loading || player.DevelopmentControls.FlyEnabled, player.Rac1GameplayAlive);
    }

    // Wrench reset keeps reaction voices alive across attack-state retirement.
    // The world host asks through this audio adapter, not through a player-source selector.
    internal static void ResetWrench(Rac1PlayerAudioPresenter? audio) =>
        audio?.Reset(includeReactions: false);

    internal static void StepWrench(Rac1PlayerAudioPresenter? audio, IPlayerAnimationPresentationController? presentation,
        Rac1WrenchAttackSession.Frame attack, Rac1WrenchThrowSession thrown, Vector3 hand, Vector3 wrench)
    {
        if (presentation is not Rac1PlayerAnimationPresentationController controller) return;
        bool nearCatch = thrown.NativeState == 11 && thrown.StepLength > 0 && wrench.DistanceTo(hand) / thrown.StepLength < 4;
        audio?.StepWrench(attack.Active ? attack.NativeAction : 0, (float)controller.TraversalAuthoredAnimationTime,
            controller.TraversalSample?.IsBlending ?? true, thrown.NativeState, nearCatch);
    }

    internal static void WrenchWorldContact(Rac1PlayerAudioPresenter? audio, global::Godot.Collections.Dictionary hit)
    {
        // Geometry is a Godot adapter; retain the retail predicate excluding face type0.
        if (audio?.WrenchImpactAdmitted != true || !hit.ContainsKey("collider") ||
            hit["collider"].As<Node>() is not RuntimeWorldScene.RuntimeCollisionBody3D body ||
            !hit.ContainsKey("face_index") || body.MaterialIdForFace((int)hit["face_index"], OBP.RAC1.Rac1Authority.Primary) is not int material ||
            (material & 0x1f) == 0) return;
        audio.WrenchContact(false, world: true);
    }
}
