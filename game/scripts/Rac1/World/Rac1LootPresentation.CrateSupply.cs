using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

internal sealed partial class Rac1LootPresentation
{
    private readonly List<(Node3D Node, Rac1AmmoPickupSession Motion)> _rac1AmmoPickups = [];

    internal void ResetRac1CrateSupply()
    {
        foreach (var node in _rac1AmmoPickups.Select(pickup => pickup.Node))
            if (IsInstanceValid(node)) node.QueueFree();
        _rac1AmmoPickups.Clear();
        ResetRac1NanotechOrbs();
    }

    internal void SpawnRac1CrateSupply(RuntimeWorldScene.DynamicObjectNode crate, Vector3 origin)
    {
        if (_sceneResult is null) return;
        if (crate.Source.NativeClassId == Rac1ClassIds.NanotechCrate)
        {
            SpawnRac1NanotechCloud(crate, origin);
        }
        else if (crate.Source.NativeClassId == Rac1ClassIds.AmmoCrate)
        {
            var definition = Rac1CrateSupply.SelectFirstAmmo(crate.Source, _rac1Weapons, Rac1EffectRandom);
            if (_rac1PickupAssets?.Models.ContainsKey(definition.ModelClass) != true)
            {
                GD.PrintErr($"[rac1-crates] ammo model {definition.ModelClass} missing for preferred item {definition.Item}");
                return;
            }
            int count = Rac1CrateSupply.SelectFirstAmmoPieceCount(Rac1EffectRandom);
            for (int i = 0; i < count; i++)
            {
                var motion = new Rac1AmmoPickupSession(definition, ToEffectNative(origin), Rac1EffectRandom);
                var node = CreateRac1EffectNode(definition.ModelClass, $"Rac1RetailAmmo_item{definition.Item}");
                node.GlobalPosition = origin;
                _rac1AmmoPickups.Add((node, motion));
            }
        }
    }

    internal void TickRac1CrateSupply(NVector3 root, NVector3 socket)
    {
        for (int i = _rac1AmmoPickups.Count - 1; i >= 0; i--)
        {
            var pickup = _rac1AmmoPickups[i];
            var contact = ProbeRac1EffectContact(pickup.Motion.Position,
                pickup.Motion.Position + pickup.Motion.Velocity, .4f);
            pickup.Motion.Step(root, _rac1Weapons, !_rac1Nanotech.Probe().IsDead, contact);
            pickup.Node.GlobalPosition = FromEffectNative(pickup.Motion.Position);
            pickup.Node.Rotation = new(0, pickup.Motion.Rotation, 0);
            if (!pickup.Motion.Collected) continue;
            pickup.Node.QueueFree();
            _rac1AmmoPickups.RemoveAt(i);
            PersistRac1CampaignState("crate ammo collection");
            RefreshRac1HudState();
        }
        TickRac1NanotechOrbs(root, socket);
    }
}
