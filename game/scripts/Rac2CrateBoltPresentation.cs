using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.Fusion;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Explicit lease and native-grant ports for R&C2 crate bolt presentation.</summary>
internal sealed record Rac2CrateBoltPorts(
    Func<PlayerHost?> Player,
    Func<bool> Alive,
    Func<bool> Current,
    Func<bool> HasNativePlayer,
    Func<int> WalletBalance,
    Func<int, ActorInstanceKey> ActorKey,
    Func<ActorInstanceKey, bool> ActorCurrent,
    Action<ActorInstanceKey, string, string, Func<bool>> RegisterActor,
    Func<ActorInstanceKey, int, int, string?> Credit,
    Action RefreshHud,
    Action PickupAudio);

/// <summary>
/// One R&C2 scene lease owns its physical class-500 pickup areas. The native
/// payout session decides denominations and collection; this owner only displays
/// admitted pickups and forwards exact actor/pickup identities to the grant port.
/// </summary>
internal sealed class Rac2CrateBoltPresentation(
    RuntimeWorld world, RuntimeWorldScene.Result scene, GodotWorldResources resources, Rac2CrateBoltPorts ports,
    Func<bool> canSpawn)
    : IDisposable
{
    private sealed class PlayerBoltReceipt(ActorInstanceKey actor)
    {
        internal ActorInstanceKey Actor { get; } = actor;
        internal bool Collected;
    }

    private readonly List<Area3D> _areas = [];
    private int _emittedRewardIndex;
    private bool _disposed;

    internal int OutstandingNodes => _areas.Count(area => GodotObject.IsInstanceValid(area) && !area.IsQueuedForDeletion());

    internal void Spawn(Vector3 centre, Rac2Class500Payout payout)
    {
        // The owning module admits only a valid R&C2 source, including class-500
        // actors in a mixed-source world and pre-publication diagnostic auto-strikes.
        if (_disposed || !canSpawn()) return;
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
                // OBP-only trigger; not a claim about native magnet geometry.
                Shape = new SphereShape3D { Radius = 1.5f },
            });
            scene.Root.AddChild(area);
            area.GlobalPosition = centre + offset;
            _areas.Add(area);
            PlayerBoltReceipt? receipt = null;
            if (ports.HasNativePlayer())
            {
                int index = checked((world.DynamicObjects?.Select(source => source.InstanceIndex)
                    .DefaultIfEmpty(0).Max() ?? 0) + ++_emittedRewardIndex);
                receipt = new(ports.ActorKey(index));
                var owned = receipt;
                ports.RegisterActor(owned.Actor, $"class500:{payout.Uid}:bolt:{pickup.PickupId}",
                    $"uid:{payout.Uid}", () => !owned.Collected && !_disposed);
            }
            area.BodyEntered += body => OnPickupEntered(body, area, pickup, receipt);
        }
    }

    private void OnPickupEntered(Node3D body, Area3D area, Rac2BoltPickup pickup, PlayerBoltReceipt? receipt)
    {
        if (_disposed || !ports.Current() || !ports.Alive() || body != ports.Player() ||
            !GodotObject.IsInstanceValid(area) || area.IsQueuedForDeletion() ||
            receipt is not null && (receipt.Collected || !ports.ActorCurrent(receipt.Actor)))
            return;

        var rewards = resources.Rac2CrateBolts;
        if (ports.HasNativePlayer() && (long)ports.WalletBalance() + pickup.Denomination > int.MaxValue) return;
        int denomination;
        try { denomination = rewards.CollectPickup(pickup.PickupId); }
        catch (InvalidOperationException) { return; }

        if (ports.HasNativePlayer() && receipt is not null)
        {
            receipt.Collected = true;
            // Preserve native payout and exact pickup identity; no new reward formula.
            string? rejection = ports.Credit(receipt.Actor, pickup.PickupId, denomination);
            if (rejection is not null) throw new InvalidOperationException(rejection);
            ports.RefreshHud();
        }
        area.SetDeferred(Area3D.PropertyName.Monitoring, false);
        area.QueueFree();
        _areas.Remove(area);
        ports.PickupAudio();
        resources.CrateDiagnostics.Probe.RewardStatus = $"collected +{denomination}; session total {rewards.CollectedBolts}; " +
            $"{rewards.OutstandingPickupCount} physical remain, {rewards.DeferredBolts} deferred";
        GD.Print($"[crate-bolts] pickup={pickup.PickupId} denomination={denomination} " +
                 $"collected={rewards.CollectedBolts} deferred={rewards.DeferredBolts}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var area in _areas)
        {
            if (!GodotObject.IsInstanceValid(area) || area.IsQueuedForDeletion()) continue;
            area.SetDeferred(Area3D.PropertyName.Monitoring, false);
            area.QueueFree();
        }
        _areas.Clear();
    }
}
