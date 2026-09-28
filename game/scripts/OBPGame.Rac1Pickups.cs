using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OneBigPackage;

public partial class OBPGame
{
    private void SpawnRac1BoltPickups(Vector3 origin, IReadOnlyList<Rac1BoltPickup> pickups)
    {
        if (_sceneResult is null)
        {
            return;
        }

        for (int i = 0; i < pickups.Count; i++)
        {
            Rac1BoltPickup pickup = pickups[i];
            float radius = pickup.Value >= 5 ? 0.24f : 0.16f;
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.82f, 0.18f),
            };
            var node = new Node3D { Name = $"rac1_bolt_{pickup.PickupId}_class{pickup.NativeClassId}" };
            node.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = radius, Height = radius * 2f },
                MaterialOverride = material,
            });
            _sceneResult.Root.AddChild(node);
            float angle = (float)(i * Math.Tau / Math.Max(1, pickups.Count));
            node.GlobalPosition = origin + new Vector3(Mathf.Cos(angle) * 0.7f, 0.65f, Mathf.Sin(angle) * 0.7f);
            _rac1PickupNodes.Add(pickup.PickupId, node);
        }
    }

    private void TickRac1Pickups()
    {
        if (_player is null || _rac1PickupNodes.Count == 0)
        {
            return;
        }
        var collected = new List<int>();
        foreach (var pair in _rac1PickupNodes)
        {
            if (!IsInstanceValid(pair.Value))
            {
                collected.Add(pair.Key);
                continue;
            }
            if (pair.Value.GlobalPosition.DistanceTo(_player.GlobalPosition) > Rac1PickupCollectRadius)
            {
                continue;
            }

            int value = _rac1BoltCrates.CollectPickup(pair.Key);
            pair.Value.QueueFree();
            collected.Add(pair.Key);
            _rac1CombatStatus = $"collected +{value} bolt{(value == 1 ? "" : "s")}";
            RefreshRac1HudState(Rac1HudProjection.CollectedBoltFeedback(value));
            GD.Print($"[rac1-gameplay] {_rac1CombatStatus}; total {_rac1BoltCrates.CollectedBolts}");
        }

        foreach (int pickupId in collected)
        {
            _rac1PickupNodes.Remove(pickupId);
        }
    }

}
