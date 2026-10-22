using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Audio;

namespace OneBigPackage;

public partial class OBPGame
{
    private Rac1PlayerAudioSession? _rac1PlayerAudio;
    private Rac1PlayerReactionAudioSession? _rac1PlayerReactions;
    private readonly RuntimeAudioHost _rac1ReactionAudio = new();
    private readonly RuntimeAudioHost _rac1PlayerOneShots = new(), _rac1PackAudio = new(),
        _rac1ThrownWrenchAudio = new(), _rac1AnimationAudio = new();
    private int? _rac1PackSound, _rac1ThrownWrenchSound, _rac1AnimationSound;
    // Host playback variation has its own stream; adding an audible footstep
    // must not advance the separate crate/debris simulation's random state.
    private Rac1PickupRandom? _rac1PlayerAudioRandom;
    private Rac1PickupRandom PlayerAudioRandom => _rac1PlayerAudioRandom ??=
        Rac1PickupRandom.FromSeed(unchecked((uint)(_args.FixedSeed ?? System.Environment.TickCount)));
    private readonly List<(Rac1PlayerAudioSession.Cue Cue, bool Loop)> _rac1PlayerSoundTrace = [];

    private void ResetRac1PlayerAudio(bool includeReactions = true)
    {
        _rac1PlayerOneShots.StopAll(); _rac1PackAudio.StopAll();
        _rac1ThrownWrenchAudio.StopAll(); _rac1AnimationAudio.StopAll();
        _rac1PackSound = _rac1ThrownWrenchSound = _rac1AnimationSound = null;
        _rac1PlayerAudio?.Reset();
        if (includeReactions) { _rac1PlayerReactions?.Reset(); _rac1ReactionAudio.StopAll(); }
    }

    private void StepRac1PlayerAudio(Rac1PlayerAnimationPresentationController controller)
    {
        if (_rac1PlayerAudio is null || _player is null || _world?.Game != "rac1") return;
        if (_movementStateLoading || !_player.Rac1GameplayAlive || _player.DevelopmentControls.FlyEnabled)
        { ResetRac1PlayerAudio(includeReactions: _player.Rac1GameplayAlive); return; }
        var frame = _rac1PlayerAudio.Step(_world.LevelId, new(controller.AnimationSoundSample,
            controller.Current.ClipStartedAtSeconds, controller.AudioNativeAction, controller.AudioActionAge,
            _player.Rac1AudioSurfaceEffectMode, _player.Rac1AudioPackId, _player.Rac1AudioGrounded,
            MovementAction: _player.Rac1AudioMovementAction,
            FractionAdvance: controller.OrdinaryLocomotionFractionAdvance ?? 0),
            _player.Rac1WrenchFrame.Generation);
        foreach (var cue in frame.OneShots) PlayRac1PlayerSound(cue);
        SetRac1PlayerLoop(_rac1PackAudio, ref _rac1PackSound, frame.PackLoop);
        SetRac1PlayerLoop(_rac1AnimationAudio, ref _rac1AnimationSound, frame.AnimationLoop);
    }

    private void StepRac1WrenchAudio(Rac1WrenchAttackSession.Frame attack, Vector3 hand)
    {
        if (_rac1PlayerAudio is null || _playerAvatarAnimationController is not Rac1PlayerAnimationPresentationController controller)
            return;
        bool nearCatch = _rac1WrenchThrow.NativeState == 11 && _rac1WrenchThrow.StepLength > 0 &&
            WrenchWorldPosition().DistanceTo(hand) / _rac1WrenchThrow.StepLength < 4;
        var frame = _rac1PlayerAudio.StepWrench(attack.Active ? attack.NativeAction : 0,
            (float)controller.TraversalAuthoredAnimationTime, controller.TraversalSample?.IsBlending ?? true,
            _rac1WrenchThrow.NativeState, nearCatch);
        foreach (var cue in frame.OneShots) PlayRac1PlayerSound(cue);
        SetRac1PlayerLoop(_rac1ThrownWrenchAudio, ref _rac1ThrownWrenchSound, frame.Loop);
    }

    private void SetRac1PlayerLoop(RuntimeAudioHost host, ref int? playing, int? wanted)
    {
        if (playing == wanted) return;
        host.StopAll(); playing = null;
        if (wanted is not int local || _player is null ||
            _rac1PickupAssets?.Sounds?.TryGetValue((0, local), out var sound) != true) return;
        var clip = sound!.SelectPitch(PlayerAudioRandom);
        if (clip.FrameCount == 0) return;
        if (host.PlayEffect(_player, new(clip, RuntimeAudioCategory.SoundEffect, loop: sound.Loop, gain: sound.Gain)))
        {
            playing = wanted;
            TraceRac1PlayerSound(new(0, local), true);
        }
    }

    private void PlayRac1PlayerSound(Rac1PlayerAudioSession.Cue cue, bool reaction = false)
    {
        if (_player is null) return;
        Rac1CrateAudio.Sound? sound = null;
        if (cue.LevelSound) _rac1PickupAssets?.PlayerAudio?.Footsteps.TryGetValue(cue.Sound, out sound);
        else _rac1PickupAssets?.Sounds?.TryGetValue((cue.Class, cue.Sound), out sound);
        if (sound is null) return;
        var clip = sound.SelectPitch(PlayerAudioRandom);
        if (clip.FrameCount == 0) return; // Native probability programs may select silence.
        // Listener-relative self audio is host presentation policy. Native
        // SoundDef gain/pitch and sample/loop data remain source-owned; Godot's
        // default inverse-distance attenuation must not mute the player's feet.
        if ((reaction ? _rac1ReactionAudio : _rac1PlayerOneShots).PlayEffect(reaction ? this : _player,
            new(clip, RuntimeAudioCategory.SoundEffect, gain: sound.Gain)))
            TraceRac1PlayerSound(cue, false);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_mode != Mode.World || _movementStateLoading || _player is null || _player.DevelopmentControls.FlyEnabled) return;
        if (_rac1PlayerReactions?.Step() is int sound) PlayRac1PlayerSound(new(0, sound), reaction: true);
        // Avatar updates stop at death; detached source particles still age out.
        if (!_player.Rac1GameplayAlive) StepRac1BombEffectsPhysics();
    }

    private void NotifyRac1PlayerDamageAudio(Rac1RatchetNanotechSnapshot before, Rac1RatchetNanotechSnapshot after)
    {
        if (after.Nanotech >= before.Nanotech) return;
        if (after.IsDead)
        {
            ResetRac1PlayerAudio(includeReactions: false);
            // Dead players stop producing avatar updates, so the usual held
            // weapon loop's release check cannot run on the following tick.
            ResetRac1WeaponAudio();
        }
        _rac1PlayerReactions?.NotifyDamage(before.Nanotech, after.Nanotech);
    }

    private void TraceRac1PlayerSound(Rac1PlayerAudioSession.Cue cue, bool loop)
    {
        if (!_args.Rac1WrenchSmoke && !_args.Rac1WeaponsSmoke && !_args.Rac1PlayerAudioSmoke) return;
        _rac1PlayerSoundTrace.Add((cue, loop));
    }

    private void TryPlayRac1WrenchWorldContact(global::Godot.Collections.Dictionary hit)
    {
        // Contact geometry remains the same bounded Godot adapter as wrench
        // damage. The retail surface predicate excludes ordinary face type0.
        if (_rac1PlayerAudio?.WrenchImpactAdmitted != true || !hit.ContainsKey("collider") ||
            hit["collider"].As<Node>() is not RuntimeWorldScene.RuntimeCollisionBody3D body ||
            !hit.ContainsKey("face_index") || body.MaterialIdForFace((int)hit["face_index"]) is not int material ||
            (material & 0x1f) == 0) return;
        if (_rac1PlayerAudio.WrenchContact(false, world: true) is { } cue) PlayRac1PlayerSound(cue);
    }
}
