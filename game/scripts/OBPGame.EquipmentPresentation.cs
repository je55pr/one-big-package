using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Audio;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Game;

public partial class OBPGame
{
    // OBP's ordinary R&C1 player/equipment library is the original LEVEL0 bank.
    // It is selected by the player, not the destination. World pickups retain
    // their own level bank below; equal class/texture numbers are not aliases.
    private Rac1PickupAssets.Catalogue? _selectedRac1PresentationAssets;
    private WorldMaterialFactory? _playerEquipmentEffectMaterials;
    private readonly Dictionary<int, List<(ArrayMesh Mesh, Material Material)>> _playerEquipmentEffectMeshes = [];
    private Rac1PickupAssets.Catalogue? PlayerPresentationAssets =>
        OrdinaryPlayerSession ? _selectedRac1PresentationAssets : _rac1PickupAssets;
    private int _playerAudioBankLevel;
    private readonly List<string> _playerAudioDiagnostics = [];
    private readonly List<(string Source, string Build, int Level, int Class, int Sound, bool Loop, bool LevelSound, long Tick)> _selectedPlayerAudioTrace = [];
    private readonly Dictionary<SourceWeaponKey, EquipmentPresentationBinding> _equipmentPresentationBindings = [];

    private bool PlayerEquipmentModelAvailable(SourceWeaponKey key) =>
        key == PlayerEquipmentRegistrations.Wrench ? _rac1WrenchAsset is not null :
        key == FusionWeaponSession.BombGlove ? _rac1BombGloveAsset is not null : key.SourceGame == ObpSourceGame.Rac1
            ? _rac1EquipmentAssets?.Models.ContainsKey(Rac1WeaponProfiles.Get((Rac1WeaponId)key.NativeItemId).ModelClass) == true
            : _fusionAssets?.Available(key) == true;

    private void BindPlayerEquipmentPresentation()
    {
        _equipmentPresentationBindings.Clear();
        if (_playerAvatar is not { } avatar || _playerAvatarAnimationController is null) return;
        var sockets = avatar.AnimationClips.SelectMany(clip => clip.AttachmentFrames?.Keys ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var item in PlayerEquipmentRegistrations.Catalog.Definitions.Where(definition => definition.Category == EquipmentCategory.Weapon))
        {
            var key = item.Item!.Value;
            bool foreign = key.SourceGame != ObpSourceGame.Rac1;
            string socket = foreign ? PlayerEquipmentPose.HandAttachmentKey : key == PlayerEquipmentRegistrations.Wrench ? Rac1WrenchAttachment.SocketKey :
                key == FusionWeaponSession.BombGlove ? Rac1BombProjectileSession.GloveSocketKey : Rac1WeaponProfiles.HandSocket(Rac1WeaponProfiles.Get((Rac1WeaponId)key.NativeItemId).HandSemantic);
            bool compatible = avatar.Skeletal is not null;
            if (foreign)
            {
                compatible = false;
                if (avatar.Skeletal is { } rig && _fusionAssets?.AvatarAnimations.TryGetValue(key, out var animation) == true)
                {
                    try { new PlayerSkeletalSampler(rig.Skeleton).RequireCompatible(animation.Skeleton); compatible = true; sockets.Add(socket); }
                    catch (InvalidDataException) { /* explicit unavailable binding below */ }
                }
            }
            string model = key == PlayerEquipmentRegistrations.Wrench ? "moby:71" : key == FusionWeaponSession.BombGlove ? "bomb-glove/held" :
                key.SourceGame == ObpSourceGame.Rac1 ? "moby:" + Rac1WeaponProfiles.Get((Rac1WeaponId)key.NativeItemId).ModelClass :
                key == FusionWeaponSession.Lancer ? "moby:" + OBP.RAC2.Gameplay.Rac2LancerProfile.HeldMobyClass : "moby:" + OBP.RAC3.Gameplay.Rac3ShockCannonProfile.HeldMobyClass;
            _equipmentPresentationBindings[key] = EquipmentPresentationBinding.Resolve(item, avatar.Identity, model, socket,
                PlayerEquipmentSources, sockets, PlayerEquipmentModelAvailable(key), compatible);
        }
    }

    private void ConfigurePlayerEquipmentPresentation(RuntimeWorld world)
    {
        _playerEquipmentEffectMeshes.Clear();
        _playerAudioDiagnostics.Clear();
        _selectedPlayerAudioTrace.Clear();
        var assets = PlayerPresentationAssets;
        _playerEquipmentEffectMaterials = assets is null ? null : new(world with { Textures = assets.Textures });
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
        _selectedPlayerAudioTrace.Add(("rac1", OBP.RAC1.Rac1Authority.Primary.BuildId, _playerAudioBankLevel, nativeClass, localSound, loop, levelSound, _playerPhysicsClock?.Tick ?? 0));
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

    private Control? CreateEquipmentAppearancePreview(EquipmentCatalogEntry entry)
    {
        if (!entry.Selectable || entry.Definition.Appearance != _playerAvatar?.Identity || _playerAvatar is not { } avatar) return null;
        var container = new SubViewportContainer
        {
            CustomMinimumSize = new(220, 170),
            Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        var viewport = new SubViewport
        {
            Size = new(300, 190),
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false
        };
        container.AddChild(viewport);
        var controller = CreatePlayerAnimationController(PlayerAvatarProviders.Get(avatar.Identity.SourceGame)!, avatar);
        var view = new PlayerAvatarView(avatar, controller.Current, alignGeometricBase: true); viewport.AddChild(view); view.SetClock(0);
        float height = (float)avatar.AnimationBounds.Height;
        var camera = new Camera3D { Position = new(0, height * .65f, height * 2.2f), Current = true };
        viewport.AddChild(camera); camera.Basis = Basis.LookingAt(new Vector3(0, height * .5f, 0) - camera.Position);
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new(-35, -25, 0), LightEnergy = 1.5f });
        return container;
    }
}
