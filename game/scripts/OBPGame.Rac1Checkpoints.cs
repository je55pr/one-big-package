using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Progression;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OneBigPackage;

public partial class OBPGame
{
    private Rac1CheckpointTriggerSession? _rac1CheckpointTriggers;

    private void ResetRac1CheckpointTriggers() => _rac1CheckpointTriggers = null;

    private void ConfigureRac1CheckpointTriggers(RuntimeWorld world)
    {
        if (world.LevelId == 0) return;
        var source = _sources.Get(ObpSourceGame.Rac1)
            ?? throw new InvalidOperationException("R&C1 checkpoint volumes require an attached retail source.");
        using var reader = new FileRandomAccessReader(source.Path);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(entry => entry.LevelId == world.LevelId);
        var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        _rac1CheckpointTriggers = new(Rac1ClipVolumes.Read(gameplay));
        var native = Rac1Instances.Parse(gameplay).MobyInstances.ToDictionary(obj => obj.Index);
        foreach (var moby in (world.DynamicObjects ?? [])
            .Where(item => item.NativeClassId == Rac1CommonMobyAuthored.CheckpointMarker))
        {
            var authored = Rac1CommonMobyAuthored.ReadCheckpointMarker(moby)
                ?? throw new InvalidDataException($"Native checkpoint {moby.InstanceIndex} lacks authored fields.");
            var placement = native[moby.InstanceIndex];
            if (placement.OClass != moby.NativeClassId)
                throw new InvalidDataException($"Native checkpoint {moby.InstanceIndex} class identity changed.");
            _rac1CheckpointTriggers.Register(placement, authored);
        }
        GD.Print($"[rac1-checkpoints] {_rac1CheckpointTriggers.RegisteredCount} original class-805 markers, " +
            $"{_rac1CheckpointTriggers.UngatedCount} volume-driven markers enabled");
    }

    private void TickRac1CheckpointTriggers()
    {
        if (_rac1CheckpointTriggers is null || _player is null ||
            _world is not { Game.SourceGame: ObpSourceGame.Rac1 } world ||
            _rac1CampaignSession.LevelCheckpoint is not { } checkpoint)
            return;
        NVector3 nativePlayer = ToEffectNative(_player.GlobalPosition);
        foreach (var trigger in _rac1CheckpointTriggers.Step(nativePlayer))
        {
            var pos = FromEffectNative(new NVector3(
                (float)trigger.NativeX, (float)trigger.NativeY, (float)trigger.NativeZ));
            var query = PhysicsRayQueryParameters3D.Create(
                pos + Vector3.Up * .5f, pos - Vector3.Up * 2.5f);
            // The authoring Moby is a checkpoint, not an ordinary walkable
            // crate. Do not snap to nearby active crates/enemies.
            query.Exclude = [_player.GetRid()];
            foreach (var body in _rac1CrateBodies.Values)
                if (IsInstanceValid(body)) query.Exclude.Add(body.GetRid());
            foreach (var enemy in _rac1Enemies.Values)
                if (IsInstanceValid(enemy.Body)) query.Exclude.Add(enemy.Body.GetRid());
            var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
            double groundedZ = trigger.NativeZ;
            if (hit.TryGetValue("position", out var value))
            {
                float height = value.AsVector3().Y;
                if (Math.Abs(trigger.NativeZ - height) <= 2d)
                    groundedZ = height;
            }
            var spawn = new RuntimeSpawn(trigger.NativeX, groundedZ, trigger.NativeY, trigger.NativeYaw);
            if (checkpoint.Activate(new Rac1CheckpointActivation(world.LevelId, spawn)))
            {
                _rac1CombatStatus = $"native checkpoint i{trigger.InstanceIndex} activated (clip {trigger.CuboidIndex})";
                GD.Print($"[rac1-checkpoints] {_rac1CombatStatus}");
            }
        }
    }
}
