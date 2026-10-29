using OBP.Core;
using Godot;
using OBP.Fusion;
using OBP.RAC2.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    private sealed class PlayerBoltReceipt(ActorInstanceKey actor)
    {
        public ActorInstanceKey Actor { get; } = actor;
        public bool Collected;
    }
    private int _playerEmittedRewardIndex;
    /// <summary>
    /// OBP-only placeholder presentation for the retail-backed physical Bolt
    /// denominations. Native pickup model, scatter motion and magnet behaviour
    /// are not reconstructed yet, so these are deliberately simple gold orbs.
    /// </summary>
    private void SpawnCrateBoltPickups(Vector3 centre, Rac2Class500Payout payout)
    {
        int count = payout.PhysicalPickups.Count;
        for (int i = 0; i < count; i++)
        {
            var pickup = payout.PhysicalPickups[i];
            float angle = count <= 1 ? 0f : Mathf.Tau * i / count;
            var offset = new Vector3(Mathf.Cos(angle) * 1.8f, 1.1f + i * 0.18f, Mathf.Sin(angle) * 1.8f);

            var area = new Area3D
            {
                Name = $"ObpPlaceholderBolt_{pickup.PickupId}_{pickup.Denomination}",
                Monitoring = true,
                Monitorable = true,
            };

            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.78f, 0.12f),
            };
            area.AddChild(new MeshInstance3D
            {
                Name = "PlaceholderBoltVisual",
                Mesh = new SphereMesh { Radius = 0.35f, Height = 0.7f },
                MaterialOverride = material,
            });
            area.AddChild(new CollisionShape3D
            {
                // OBP-only generous trigger for deterministic/manual testing.
                // It is not a claim about native Bolt pickup magnet distance.
                Shape = new SphereShape3D { Radius = 1.5f },
            });

            _worldRoot.AddChild(area);
            area.GlobalPosition = centre + offset;
            PlayerBoltReceipt? receipt = null;
            if (_playerNative is not null)
            {
                int index = checked((_world!.DynamicObjects?.Select(source => source.InstanceIndex).DefaultIfEmpty(0).Max() ?? 0) + ++_playerEmittedRewardIndex);
                receipt = new(new(PlayerLife.WorldId!, PlayerLife.Generation, new(ObpSourceGame.Rac2, 500, index)));
                var owned = receipt;
                _playerNative.Life.RegisterActor(new(owned.Actor,
                    new(ObpSourceGame.Rac2, OBP.RAC2.Rac2Authority.Primary.BuildId, $"class500:{payout.Uid}:bolt:{pickup.PickupId}"),
                    "rac2/collected-class500-bolt/v1", $"uid:{payout.Uid}", []), () => !owned.Collected);
            }
            area.BodyEntered += body => OnCrateBoltPickupEntered(body, area, pickup, receipt);
        }
    }

    private void OnCrateBoltPickupEntered(Node3D body, Area3D area, Rac2BoltPickup pickup, PlayerBoltReceipt? receipt = null)
    {
        if (_player is null || body != _player || !IsInstanceValid(area) || !PlayerLife.Alive ||
            receipt is not null && (receipt.Collected || receipt.Actor.WorldInstanceId != PlayerLife.WorldId || receipt.Actor.Generation != PlayerLife.Generation))
        {
            return;
        }

        if (_worldScenes?.Current?.Resources is not { } resources) return;
        var rewards = resources.Rac2CrateBolts;
        int denomination;
        if (_playerNative is not null && (long)_playerNative.Wallet.Balance + pickup.Denomination > int.MaxValue) return;
        try
        {
            denomination = rewards.CollectPickup(pickup.PickupId);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (_playerNative is not null && receipt is not null)
        {
            receipt.Collected = true;
            // Explicit OBP 1:1 conversion of a source-admitted physical pickup.
            // Deferred native values stay with GcFreshBoltSession, not this wallet.
            var result = _playerNative.Life.Credit(new(receipt.Actor, OBP.RAC2.Rac2Authority.Primary.BuildId,
                pickup.PickupId, "obp/bolts", denomination), value => _playerNative.Wallet.Credit(checked((int)value)));
            if (!result.Applied) throw new InvalidOperationException(result.Reason);
            RefreshRac1HudState();
        }
        area.SetDeferred(Area3D.PropertyName.Monitoring, false);
        area.QueueFree();
        _worldHost.PlayRepresentativeAudioOneShot();
        resources.CrateDiagnostics.Probe.RewardStatus = $"collected +{denomination}; session total {rewards.CollectedBolts}; " +
            $"{rewards.OutstandingPickupCount} physical remain, {rewards.DeferredBolts} deferred";
        GD.Print($"[crate-bolts] pickup={pickup.PickupId} denomination={denomination} " +
                 $"collected={rewards.CollectedBolts} deferred={rewards.DeferredBolts}");
    }
}
