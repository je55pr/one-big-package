using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Game;

/// <summary>
/// Sole persistent ordinary-player owner: native state and progression outlive
/// worlds, destinations, and menu pages. WorldEntrySession is a lease, not a new player.
/// </summary>
internal sealed class PlayerSessionHost
{
    private PlayerEquipmentFacade? _equipment;
    internal PlayerEquipmentFacade Equipment => _equipment ?? throw new InvalidOperationException("Player equipment ports are not bound.");

    internal void BindEquipment(PlayerEquipmentPorts ports)
    {
        if (_equipment is not null) throw new InvalidOperationException("Player equipment ports are already bound.");
        _equipment = new(ports);
    }

    internal void RetireEquipment() => _equipment?.Dispose();

    internal ObpPlayerSession? Session { get; private set; }
    internal PlayerSessionNativeState? Native { get; private set; }
    internal WorldEntrySession? WorldEntries { get; private set; }
    internal WorldEntryMode? EntryMode { get; private set; }
    internal PlayerControlProfile? ControlProfile { get; private set; }
    internal PlayerAnimationProfile? AnimationProfile { get; private set; }
    internal (long Length, long Written)? AvatarSourceStamp { get; set; }

    internal bool Ensure(WorldEntryMode entryMode, Rac1WeaponInventory campaignWeapons, int campaignBolts, int seed, IPlayerSessionPreferencesAdapter preferences)
    {
        if (Session is not null) return false;
        EntryMode = entryMode;
        bool diagnostic = entryMode == WorldEntryMode.Diagnostic;
        var provider = diagnostic ? (IPlayerAvatarProvider)DiagnosticAvatarProvider.Instance : Rac1PlayerAvatarProvider.Instance;
        var avatar = new PlayerAvatarIdentity(provider.SourceGame, provider.BuildId, provider.DefaultAvatarId,
            diagnostic ? "obp:diagnostic-capsule" : "rac1:ratchet:moby-class-0");
        var profile = new SourceProfileKey(provider.SourceGame, provider.BuildId,
            diagnostic ? "obp/diagnostic-player/v1" : "rac1/ordinary-player/v1");
        var configuration = new PlayerSessionConfiguration(avatar, profile, profile, PlayerHostProfiles.Services, 60);
        if (entryMode == WorldEntryMode.OrdinaryFusion)
            Native = new(campaignWeapons, campaignBolts, seed);
        var initial = new PlayerSessionSnapshot("obp-player", 0, configuration,
            new(new SourceWeaponKey(ObpSourceGame.Rac1, 10), null, []), new(4, 4, true), new Dictionary<string, long>());
        Session = new(initial, Native, preferences);
        WorldEntries = new(Session);
        return true;
    }

    internal void ResolvePresentationProfiles()
    {
        var selected = Session?.Capture().Configuration ?? throw new InvalidOperationException("Player session not initialized.");
        (ControlProfile, AnimationProfile) = PlayerHostProfiles.Registry.Resolve(selected);
    }
}
