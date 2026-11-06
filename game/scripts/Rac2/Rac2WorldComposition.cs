using OBP.Core;
using OBP.Godot;
using OBP.Runtime;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OBP.Game;

/// <summary>
/// Binds an R&amp;C2 world lease's module to the application's owned components. The
/// module and its source library own the native behaviour; these are typed ports only.
/// </summary>
internal sealed class Rac2WorldComposition(ApplicationComponents components) : CompositionBase(components)
{

    internal Rac2WorldModule CreateRac2WorldModule(RuntimeWorld world, GodotWorldResources resources) => new(
        world, resources.Scene, resources, new Rac2WorldPorts(
            Current: () => ReferenceEquals(_worldScenes?.Current?.Resources, resources),
            SourceValid: () => Sources.Setup.Library.Get(ObpSourceGame.Rac2)?.Identity.BuildId == OBP.RAC2.Rac2Authority.Primary.BuildId,
            Ordinary: () => OrdinaryPlayerSession,
            Alive: () => PlayerLife.Alive,
            HasNativePlayer: () => _playerNative is not null,
            Actors: () => ActorHost,
            Audio: node => _worldHost.PlayRepresentativeAudioOneShot(node.Source),
            CrateReward: (centre, payout) => resources.Rac2Gameplay?.CrateBolts.Spawn(centre, payout),
            PlayerDamage: (node, hit, amount, mask) => PlayerLife.ApplySourceDamage(node, OBP.RAC2.Rac2Authority.Primary.BuildId,
                PlayerSessionNativeState.Rac2DamageAdapter, PlayerSessionNativeState.Rac2DamageDescriptor,
                hit, amount, mask).Applied,
            CrateFocus: _args.CrateFocus, CrateAutoStrike: _args.CrateAutoStrike),
        new Rac2CrateBoltPorts(
            () => _player, () => PlayerLife.Alive, () => ReferenceEquals(_worldScenes?.Current?.Resources, resources),
            () => _playerNative is not null, () => _playerNative?.Wallet.Balance ?? 0,
            index => new(PlayerLife.WorldId!, PlayerLife.Generation, new(ObpSourceGame.Rac2, 500, index)),
            actor => actor.WorldInstanceId == PlayerLife.WorldId && actor.Generation == PlayerLife.Generation,
            (actor, source, provenance, isActive) => _playerNative!.Life.RegisterActor(new(actor,
                new(ObpSourceGame.Rac2, OBP.RAC2.Rac2Authority.Primary.BuildId, source),
                "rac2/collected-class500-bolt/v1", provenance, []), isActive),
            (actor, pickupId, denomination) =>
            {
                var result = _playerNative!.Life.Credit(new(actor, OBP.RAC2.Rac2Authority.Primary.BuildId,
                    pickupId, "obp/bolts", denomination),
                    value => _playerNative.Wallet.Credit(checked((int)value)));
                return result.Applied ? null : result.Reason;
            }, () => RefreshRac1HudState(), () => _worldHost.PlayRepresentativeAudioOneShot(),
            () => Sources.Setup.Library.Get(ObpSourceGame.Rac2)?.Path,
            () => _args.FixedSeed ?? 1,
            () => OBP.RAC1.Gameplay.Rac1BoltMotionSession.AttractionProfile(0, _playerNative?.Inventory.OwnsNativeItem(34) == true)));
}
