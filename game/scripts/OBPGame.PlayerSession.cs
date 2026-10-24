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

namespace OneBigPackage;

public partial class OBPGame
{
    private ObpPlayerSession? _playerSession;
    private PlayerSessionNativeState? _playerNative;
    private WorldEntrySession? _worldEntries;
    private (long Length, long Written)? _avatarSourceStamp;
    private WorldEntryMode? _playerEntryMode;
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
        var configuration = new PlayerSessionConfiguration(avatar, profile, profile,
            new("obp/input/v1", "obp/equipment-actions/v1", "obp/rac1-derived-health/v1", "obp/player-camera/v1",
                "obp/player-hud/v1", "obp/pause/v1", "obp/quick-select/v1", "obp/player-audio/v1"), 60);
        if (OrdinaryPlayerSession)
        {
            // Import source campaign equipment once as a deliberate compatibility
            // policy. Subsequent player state is independent of that campaign.
            _playerNative = new(_rac1CampaignSession.Weapons, _parityBoltWallet.Balance, _args.FixedSeed ?? 1440);
            _fusion = _playerNative.Weapons;
        }
        var initial = new PlayerSessionSnapshot("obp-player", 0, configuration,
            new(new SourceWeaponKey("rac1", 10), null, []), new(4, 4, true), new Dictionary<string, long>());
        _playerSession = new(initial, _playerNative, new PlayerCameraPreferences());
        _worldEntries = new(_playerSession);
        RefreshPlayerSourceAvailability();
    }

    private void RefreshPlayerSourceAvailability() =>
        _playerSession?.SetAttachedSources(_sources.Attached.Values.Select(source => source.Identity));

    private void PrepareSelectedPlayerAssets()
    {
        EnsurePlayerSession();
        RefreshPlayerSourceAvailability();
        var avatar = _playerSession!.Capture().Configuration.Avatar;
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
        }
        if (_playerAvatar.Identity != avatar) throw new InvalidOperationException("Decoded avatar identity differs from the selected player profile.");
        if (avatar.SourceGame == ObpSourceGame.Rac1 && (_rac1WrenchAsset is null || _rac1WrenchSourcePath != source.Path))
        {
            _rac1WrenchAsset = Rac1WrenchAsset.Load(source.Path);
            _rac1WrenchSourcePath = source.Path;
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
