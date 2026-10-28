using OBP.Runtime.Player;
using OBP.Runtime.Gameplay;
using OBP.Runtime;
using OBP.Fusion;
using OBP.Godot.Player;
using Godot;
using OBP.Core;
using OBP.Game.Diagnostics;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Audio;

namespace OBP.Game;

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

    private void ResetRac1PlayerAudio(bool includeReactions = true, bool freeImmediately = false)
    {
        _rac1PlayerOneShots.StopAll(freeImmediately); _rac1PackAudio.StopAll(freeImmediately);
        _rac1ThrownWrenchAudio.StopAll(freeImmediately); _rac1AnimationAudio.StopAll(freeImmediately);
        _rac1PackSound = _rac1ThrownWrenchSound = _rac1AnimationSound = null;
        _rac1PlayerAudio?.Reset();
        if (includeReactions) { _rac1PlayerReactions?.Reset(); _rac1ReactionAudio.StopAll(freeImmediately); }
    }

    private void StepRac1PlayerAudio(Rac1PlayerAnimationPresentationController controller)
    {
        if (_rac1PlayerAudio is null || _player is null) return;
        if (_movementStateLoading || !_player.Rac1GameplayAlive || _player.DevelopmentControls.FlyEnabled)
        { ResetRac1PlayerAudio(includeReactions: _player.Rac1GameplayAlive); return; }
        var frame = _rac1PlayerAudio.Step(_playerAudioBankLevel, new(controller.AnimationSoundSample,
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
        if (_rac1PlayerAudio is null || _avatarPresentation.Controller is not Rac1PlayerAnimationPresentationController controller)
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
            PlayerPresentationAssets?.Sounds?.TryGetValue((0, local), out var sound) != true) return;
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
        if (cue.LevelSound) PlayerPresentationAssets?.PlayerAudio?.Footsteps.TryGetValue(cue.Sound, out sound);
        else PlayerPresentationAssets?.Sounds?.TryGetValue((cue.Class, cue.Sound), out sound);
        if (sound is null) { ReportPlayerAudioMissing(cue.Class, cue.Sound); return; }
        var clip = sound.SelectPitch(PlayerAudioRandom);
        if (clip.FrameCount == 0)
        {
            // Retain a source-qualified diagnostic outcome for an admitted probability branch; this is not playback.
            if (_args.Rac1PlayerAudioSmoke) _playerAudioDiagnosticEvents.Record(new(ObpSourceGame.Rac1, OBP.RAC1.Rac1Authority.Primary.BuildId,
                _playerAudioBankLevel, _avatarPresentation.Clock?.Tick ?? 0, cue.Class, cue.Sound, false, cue.LevelSound)
            { Playback = DiagnosticAudioPlayback.NativeSilence });
            return;
        }
        // Listener-relative self audio is host presentation policy. Native
        // SoundDef gain/pitch and sample/loop data remain source-owned; Godot's
        // default inverse-distance attenuation must not mute the player's feet.
        if ((reaction ? _rac1ReactionAudio : _rac1PlayerOneShots).PlayEffect(reaction ? this : _player,
            new(clip, RuntimeAudioCategory.SoundEffect, gain: sound.Gain)))
            TraceRac1PlayerSound(cue, false);
    }

    private void StepRac1PlayerAudioPhysics()
    {
        if (_rac1PlayerReactions?.Step() is int sound) PlayRac1PlayerSound(new(0, sound), reaction: true);
        // Avatar updates stop at death; detached source particles still age out.
        if (!_player!.Rac1GameplayAlive) StepRac1BombEffectsPhysics();
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
        TraceSelectedPlayerAudio(cue.Class, cue.Sound, loop, cue.LevelSound);
        if (!_args.Rac1WrenchSmoke && !_args.Rac1WeaponsSmoke && !_args.Rac1PlayerAudioSmoke) return;
        _playerAudioDiagnosticEvents.Record(new(ObpSourceGame.Rac1, OBP.RAC1.Rac1Authority.Primary.BuildId, _playerAudioBankLevel,
            _avatarPresentation.Clock?.Tick ?? 0, cue.Class, cue.Sound, loop, cue.LevelSound));
    }

    private void TryPlayRac1WrenchWorldContact(global::Godot.Collections.Dictionary hit)
    {
        // Contact geometry remains the same bounded Godot adapter as wrench
        // damage. The retail surface predicate excludes ordinary face type0.
        if (_rac1PlayerAudio?.WrenchImpactAdmitted != true || !hit.ContainsKey("collider") ||
            hit["collider"].As<Node>() is not RuntimeWorldScene.RuntimeCollisionBody3D body ||
            !hit.ContainsKey("face_index") || body.MaterialIdForFace((int)hit["face_index"], OBP.RAC1.Rac1Authority.Primary) is not int material ||
            (material & 0x1f) == 0) return;
        if (_rac1PlayerAudio.WrenchContact(false, world: true) is { } cue) PlayRac1PlayerSound(cue);
    }


    // Audio cue policy and diagnostics remain for the dedicated #354 extraction.
    private int _playerAudioBankLevel;
    private readonly List<string> _playerAudioDiagnostics = [];
    private readonly List<(string Source, string Build, int Level, int Class, int Sound, bool Loop, bool LevelSound, long Tick)> _selectedPlayerAudioTrace = [];
    private Rac1PickupAssets.Catalogue? PlayerPresentationAssets =>
        OrdinaryPlayerSession ? _equipmentPresentation.SelectedAssets : _rac1PickupAssets;


    private bool PlayerEquipmentModelAvailable(SourceWeaponKey key) =>
        key == PlayerEquipmentRegistrations.Wrench ? _avatarPresentation.WrenchAsset is not null :
        key == FusionWeaponSession.BombGlove ? _avatarPresentation.GloveAsset is not null : key.SourceGame == ObpSourceGame.Rac1
            ? _rac1EquipmentAssets?.Models.ContainsKey(Rac1WeaponProfiles.Get((Rac1WeaponId)key.NativeItemId).ModelClass) == true
            : _fusionAssets?.Available(key) == true;


    private void BindPlayerEquipmentPresentation()
    {
        _equipmentPresentation.Bind(_avatarPresentation.Controller is null ? null : _avatarPresentation.Avatar,
            PlayerEquipmentSources, PlayerEquipmentModelAvailable, key =>
            {
                if (_avatarPresentation.Avatar?.Skeletal is not { } rig ||
                    _fusionAssets is not { } assets ||
                    !assets.AvatarAnimations.TryGetValue(key, out var animation))
                    return false;
                try
                {
                    new PlayerSkeletalSampler(rig.Skeleton).RequireCompatible(animation.Skeleton);
                    return true;
                }
                catch (InvalidDataException) { return false; }
            });
    }


    private void ConfigurePlayerEquipmentPresentation(RuntimeWorld world)
    {
        _equipmentPresentation.ConfigureWorld(world, PlayerPresentationAssets);
        _playerAudioDiagnostics.Clear();
        _selectedPlayerAudioTrace.Clear();
        var assets = PlayerPresentationAssets;

        _playerAudioBankLevel = OrdinaryPlayerSession ? 0 : world.LevelId;
        _rac1PlayerAudio = assets?.PlayerAudio is { } audio ? new Rac1PlayerAudioSession(audio.Animations) : null;
        _rac1PlayerReactions = assets?.PlayerAudio is { } reactions ? new Rac1PlayerReactionAudioSession(reactions.Animations) : null;
        _playerAudioDiagnosticEvents.Clear();
        if (assets is null) _playerAudioDiagnostics.Add("Selected player audio bank is unavailable.");
        // Foreign/unclassified contact material is deliberately treated as a
        // dry surface by the selected movement adapter. The authentic LEVEL0
        // footstep bank is an OBP fallback, not a recovered GC/UYA surface map.
    }

    private Node3D CreatePlayerEquipmentEffectNode(int nativeClass, string name) =>
        CreateRac1EffectNode(nativeClass, name, playerEquipment: true);

    private void PlayPlayerEquipmentSound(int nativeClass, int localSound, Vector3 position)
    {
        if (PlayerPresentationAssets?.Sounds?.TryGetValue((nativeClass, localSound), out var sound) != true)
        {
            ReportPlayerAudioMissing(nativeClass, localSound);
            return;
        }
        var clip = sound!.SelectPitch(PlayerAudioRandom);
        if (clip.FrameCount == 0) return; // Source probability program can choose silence.
        if (!_worldHost.PlayEffect(new(clip, RuntimeAudioCategory.SoundEffect, gain: sound.Gain,
            position: new(-position.X, position.Y, position.Z)))) return;
        TraceSelectedPlayerAudio(nativeClass, localSound, false);
        RecordRac1WeaponSound(nativeClass, localSound);
    }

    private void ReportPlayerAudioMissing(int nativeClass, int localSound)
    {
        string reason = $"R&C1 {_playerAudioBankLevel} class {nativeClass} sound {localSound} is unavailable in the selected source bank.";
        if (_playerAudioDiagnostics.Contains(reason)) return;
        _playerAudioDiagnostics.Add(reason);
        GD.Print("[player-audio] " + reason);
    }

    private void TraceSelectedPlayerAudio(int nativeClass, int localSound, bool loop, bool levelSound = false)
    {
        if (_selectedPlayerAudioTrace.Count == 1024) _selectedPlayerAudioTrace.RemoveRange(0, 512);
        _selectedPlayerAudioTrace.Add(("rac1", OBP.RAC1.Rac1Authority.Primary.BuildId, _playerAudioBankLevel, nativeClass, localSound, loop, levelSound, _avatarPresentation.Clock?.Tick ?? 0));
    }

    private void SetPlayerAudioPaused(bool paused)
    {
        // Suspend playback without rewinding native cue cursors, reselecting
        // probability programs or allocating duplicate looping voices.
        foreach (var host in new[] { _rac1PlayerOneShots, _rac1PackAudio, _rac1ThrownWrenchAudio,
            _rac1AnimationAudio, _rac1ReactionAudio, _rac1WeaponLoopAudio, _rac1TaunterAudio }) host.SetPaused(paused);
        foreach (var owned in _fusionAudioPlayers)
            if (IsInstanceValid(owned.Player)) owned.Player.StreamPaused = paused;
    }


    private Control? CreateEquipmentAppearancePreview(EquipmentCatalogEntry entry) =>
        _avatarPresentation.CreateAppearancePreview(entry);




}