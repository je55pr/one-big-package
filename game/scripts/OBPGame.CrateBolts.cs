using Godot;
using OBP.RAC2.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>
    /// OBP-only placeholder presentation for the retail-backed physical Bolt
    /// denominations. Native pickup model, scatter motion and magnet behaviour
    /// are not reconstructed yet, so these are deliberately simple gold orbs.
    /// </summary>
    private void SpawnCrateBoltPickups(Vector3 centre, GcClass500Payout payout)
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
            area.BodyEntered += body => OnCrateBoltPickupEntered(body, area, pickup);
        }
    }

    private void OnCrateBoltPickupEntered(Node3D body, Area3D area, GcBoltPickup pickup)
    {
        if (_player is null || body != _player || !IsInstanceValid(area))
        {
            return;
        }

        int denomination;
        try
        {
            denomination = _crateBoltSession.CollectPickup(pickup.PickupId);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        area.SetDeferred(Area3D.PropertyName.Monitoring, false);
        area.QueueFree();
        _crateRewardStatus = $"collected +{denomination}; session total {_crateBoltSession.CollectedBolts}; " +
            $"{_crateBoltSession.OutstandingPickupCount} physical remain, {_crateBoltSession.DeferredBolts} deferred";
        GD.Print($"[crate-bolts] pickup={pickup.PickupId} denomination={denomination} " +
                 $"collected={_crateBoltSession.CollectedBolts} deferred={_crateBoltSession.DeferredBolts}");
    }
}
