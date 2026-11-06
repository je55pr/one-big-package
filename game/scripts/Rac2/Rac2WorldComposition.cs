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
    private const int DefaultRac1PickupState = 0;
    private const int Rac1BoltGrabberItem = 34;

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
            }, () => RefreshRac1HudState(), nativeClass => resources.Rac2Gameplay?.CrateEffects.PlayPickup(nativeClass),
            () => Sources.Setup.Library.Get(ObpSourceGame.Rac2)?.Path,
            () => _args.FixedSeed ?? Random.Shared.Next(),
            // Selected-player attraction adapter; see research/GC_CRATES.md.
            () => OBP.RAC1.Gameplay.Rac1BoltMotionSession.AttractionProfile(DefaultRac1PickupState,
                _playerNative?.Inventory.OwnsNativeItem(Rac1BoltGrabberItem) == true),
            ActorHost.AddRayExclusions, _worldHost.PlayEffect),
        new Rac2CrateSupplyPorts(
            () =>
            {
                var health = _playerNative?.Life.Capture();
                return health is null ? (0, 0) : ((int)health.Current, (int)health.Capacity);
            },
            amount =>
            {
                if (_playerNative is not { } native || !PlayerLife.Alive) return 0;
                int before = (int)native.Life.Capture().Current;
                // Explicit OBP supply conversion: one recovered RAC2 unit heals
                // one unit in the selected player's health controller.
                native.Life.Heal(amount);
                return (int)native.Life.Capture().Current - before;
            },
            amount => PlayerCrateSupplyAdapter.Read(_playerNative, ReadPlayerEquipment(), amount),
            (weapon, amount) => PlayerCrateSupplyAdapter.Grant(_playerNative, weapon, amount)));
}
