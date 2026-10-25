using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    // Pickup reach is an explicitly provisional Godot contact policy, not
    // recovered native collection geometry.
    private const float Rac1GoldBoltHostPickupRadius = 1.2f;
    private Rac1GoldBoltSession _rac1GoldBolts = new();
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _rac1GoldBoltNodes = [];

    private void ResetRac1GoldBolts()
    {
        _rac1GoldBolts = new(_rac1CollectedNovalisGoldBolts);
        _rac1GoldBoltNodes.Clear();
    }

    private void ConfigureRac1GoldBolts(RuntimeWorld world,
        RuntimeWorldScene.Result result)
    {
        if (world.LevelId != Rac1GoldBoltSession.NovalisLevelId) return;
        foreach (var source in world.DynamicObjects ?? [])
        {
            if (source.NativeClassId != Rac1GoldBoltSession.GoldBoltNativeClassId)
                continue;

            _rac1GoldBolts.Register(world.LevelId, source.NativeClassId, source.InstanceIndex);
            var node = FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException("Novalis gold bolt has no retail presentation.");
            _rac1GoldBoltNodes.Add(source.InstanceIndex, node);
            if (_rac1GoldBolts.IsCollected(source.InstanceIndex))
                node.ApplyState(node.State.WithPresence(RuntimeEntityPresence.Inactive));
        }

        if (_rac1GoldBolts.RegisteredCount != Rac1GoldBoltSession.ExpectedNovalisBolts)
        {
            GD.PrintErr($"[rac1-gold] Novalis has {_rac1GoldBolts.RegisteredCount} class-1134 placements; expected 3");
        }
        else GD.Print("[rac1-gold] Novalis: 3 original gold bolts ready");
    }

    private void TickRac1GoldBolts()
    {
        if (_world is not { Game.SourceGame: ObpSourceGame.Rac1, LevelId: Rac1GoldBoltSession.NovalisLevelId } ||
            _player is null || _rac1Nanotech.Probe().IsDead)
            return;

        foreach (var pair in _rac1GoldBoltNodes)
        {
            if (_rac1GoldBolts.IsCollected(pair.Key)) continue;
            var node = pair.Value;
            if (!IsInstanceValid(node.Root)) continue;
            // Provisional host feet/torso proximity; the exact native collect
            // query has not been recovered and must not be inferred from this.
            float distance = node.Root.GlobalPosition.DistanceTo(
                _player.GlobalPosition + Vector3.Up * .7f);
            if (!_rac1GoldBolts.TryCollect(pair.Key,
                    contactAdmitted: distance <= Rac1GoldBoltHostPickupRadius))
                continue;

            node.ApplyState(node.State.WithPresence(RuntimeEntityPresence.Inactive));
            _rac1CollectedNovalisGoldBolts.Add(pair.Key);
            PersistRac1CampaignState("Novalis Gold Bolt collection");
            _rac1CombatStatus = $"Novalis gold bolts {_rac1GoldBolts.CollectedCount}/{Rac1GoldBoltSession.ExpectedNovalisBolts}";
            GD.Print($"[rac1-gold] collected authored instance {pair.Key}: {_rac1CombatStatus}");
            RefreshRac1HudState();
        }
    }
}
