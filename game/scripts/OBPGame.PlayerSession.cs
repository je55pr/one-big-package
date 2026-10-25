using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OBP.Game;

public partial class OBPGame
{
    private ObpPlayerSession? _playerSession;
    private PlayerSessionNativeState? _playerNative;
    private WorldEntrySession? _worldEntries;
    private (long Length, long Written)? _avatarSourceStamp;
    private WorldEntryMode? _playerEntryMode;
    private PlayerControlProfile? _playerControlProfile;
    private PlayerAnimationProfile? _playerAnimationProfile;
    private bool OrdinaryPlayerSession => PlayerEntryMode == WorldEntryMode.OrdinaryFusion;
    private WorldEntryMode PlayerEntryMode => _playerEntryMode ?? (_args.DiagnosticPlayer ? WorldEntryMode.Diagnostic :
        _args.NativeParityRequested ? WorldEntryMode.RetailParity : WorldEntryMode.OrdinaryFusion);

    private void EnsurePlayerSession()
    {
        if (_playerSession is not null) return;
        _playerEntryMode = PlayerEntryMode;
        EnsureSourceLibraryInitialized();
        bool diagnostic = PlayerEntryMode == WorldEntryMode.Diagnostic;
        var provider = diagnostic ? (IPlayerAvatarProvider)DiagnosticAvatarProvider.Instance : Rac1PlayerAvatarProvider.Instance;
        var avatar = new PlayerAvatarIdentity(provider.SourceGame, provider.BuildId, provider.DefaultAvatarId,
            diagnostic ? "obp:diagnostic-capsule" : "rac1:ratchet:moby-class-0");
        var profile = new SourceProfileKey(provider.SourceGame, provider.BuildId, diagnostic ? "obp/diagnostic-player/v1" : "rac1/ordinary-player/v1");
        var configuration = new PlayerSessionConfiguration(avatar, profile, profile, PlayerHostProfiles.Services, 60);
        if (OrdinaryPlayerSession)
        {
            // Import source campaign equipment once as a deliberate compatibility
            // policy. Subsequent player state is independent of that campaign.
            _playerNative = new(_rac1CampaignSession.Weapons, _parityBoltWallet.Balance, _args.FixedSeed ?? 1440);
            _fusion = _playerNative.Weapons;
        }
        var initial = new PlayerSessionSnapshot("obp-player", 0, configuration,
            new(new SourceWeaponKey(ObpSourceGame.Rac1, 10), null, []), new(4, 4, true), new Dictionary<string, long>());
        _playerSession = new(initial, _playerNative, new PlayerCameraPreferences());
        _worldEntries = new(_playerSession);
        RefreshPlayerSourceAvailability();
        InitializeSharedProfile();
        if (_captureStartingProfile is { } capture)
        {
            ApplySharedProfile(capture);
            _captureStartingProfile = null;
        }
    }

    private void RefreshPlayerSourceAvailability()
    {
        _playerSession?.SetAttachedSources(_sources.Attached.Values.Select(source => source.Identity));
        _playerNative?.RefreshDevelopmentSources(PlayerEquipmentSources);
        RefreshPlayerEquipmentAvailability();
    }

    private void PrepareSelectedPlayerAssets()
    {
        EnsurePlayerSession();
        RefreshPlayerSourceAvailability();
        var selected = _playerSession!.Capture().Configuration;
        (_playerControlProfile, _playerAnimationProfile) = PlayerHostProfiles.Registry.Resolve(selected);
        if (Engine.PhysicsTicksPerSecond != _playerAnimationProfile.NativeTicksPerSecond)
            throw new InvalidOperationException($"Selected player animation requires {_playerAnimationProfile.NativeTicksPerSecond} physics ticks per second.");
        var avatar = selected.Avatar;
        if (avatar.SourceGame == ObpSourceGame.Synthetic) return;
        var availability = _playerSession.Availability(avatar.SourceGame, avatar.BuildId, true, true);
        if (!availability.Usable) throw new InvalidOperationException(availability.Reason);
        var source = _sources.Get(avatar.SourceGame)!;
        var file = new FileInfo(source.Path);
        if (!file.Exists || file.Length != source.SizeBytes) throw new InvalidOperationException("The selected avatar source is missing or has changed size. Reattach it in Game Sources.");
        var stamp = (file.Length, file.LastWriteTimeUtc.Ticks);
        var provider = PlayerAvatarProviders.Get(avatar.SourceGame)
            ?? throw new InvalidOperationException($"No provider for selected avatar {avatar.AvatarId}.");
        if (_playerAvatar is null || _playerAvatarSourcePath != source.Path || _playerAvatarSourceGame != avatar.SourceGame || _avatarSourceStamp != stamp)
        {
            using var reader = new FileRandomAccessReader(source.Path);
            var probe = _sources.Probe(reader);
            if (!probe.Supported || probe.Definition?.Authority.BuildId != avatar.BuildId)
                throw new InvalidOperationException("The selected avatar source no longer identifies as the selected supported build.");
            _playerAvatar = provider.Load(source.Path, avatar.AvatarId);
            _playerAvatarSourcePath = source.Path; _playerAvatarSourceGame = avatar.SourceGame;
            _avatarSourceStamp = stamp;
            _rac1WrenchAsset = null; _rac1BombGloveAsset = null;
            _rac1EquipmentAssets = null; _rac1EquipmentSource = null;
            _selectedRac1PresentationAssets = null;
        }
        if (_playerAvatar.Identity != avatar) throw new InvalidOperationException("Decoded avatar identity differs from the selected player profile.");
        if (avatar.SourceGame == ObpSourceGame.Rac1 && (_rac1WrenchAsset is null || _rac1WrenchSourcePath != source.Path))
        {
            _rac1WrenchAsset = Rac1WrenchAsset.Load(source.Path);
            _rac1WrenchSourcePath = source.Path;
        }
        if (avatar.SourceGame == ObpSourceGame.Rac1)
        {
            if (_rac1BombGloveAsset is null || _rac1BombGloveSourcePath != source.Path)
            {
                _rac1BombGloveAsset = OBP.RAC1.Gameplay.Rac1BombGloveAssets.Load(source.Path);
                _rac1BombGloveSourcePath = source.Path;
            }
            if (_rac1EquipmentAssets is null || _rac1EquipmentSource != source.Path)
            {
                _rac1EquipmentAssets = OBP.RAC1.Gameplay.Rac1EquipmentAssets.Load(source.Path);
                _rac1EquipmentSource = source.Path;
            }
            if (OrdinaryPlayerSession && _selectedRac1PresentationAssets is null)
                _selectedRac1PresentationAssets = OBP.RAC1.Gameplay.Rac1PickupAssets.Load(source.Path, 0);
        }
    }

    private PlayerSessionConfiguration SelectedPlayerConfiguration => _playerSession!.Capture().Configuration;

    private void PublishPersistentPlayerHud(params HudFeedbackDraft[] feedback)
    {
        var state = _playerSession!.Capture();
        var health = state.Health;
        var projection = Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons, capturedMobyCount: _rac1AdditionalWeapons?.Cargo.Count ?? 0,
            swimming: _player?.Rac1SwimState, ownsOxygenMask: _player?.Rac1MovementEquipment.OxygenMask == true) with
        {
            Health = new((int)health.Current, (int)health.Capacity, "nanotech", health.Alive ? HudLifeState.Alive : HudLifeState.Dead),
            Bolts = new(Rac1HudProjection.BoltsResourceKey, (int)state.Currencies.GetValueOrDefault("obp/bolts")),
        };
        if (_hudState.Current.Epoch == 0) _hudState.BeginSession(FusionHud(projection));
        else _hudState.Publish(FusionHud(projection), feedback);
    }
}
