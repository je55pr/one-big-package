using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Progression;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>
/// R&C1 authored checkpoint triggers belong to the current source-world lease.
/// The persistent native checkpoint session remains with Rac1CampaignHost.
/// Root supplies only an attached source path and the current player/session;
/// retired world volumes cannot activate a replacement world's checkpoint.
/// </summary>
internal sealed class Rac1CheckpointHost(
    RuntimeWorld world, Rac1WorldContext context, Rac1LootPresentation loot,
    Rac1EnemyHost enemies) : IDisposable
{
    private Rac1CheckpointTriggerSession? _triggers;
    private bool _disposed;

    internal void Reset()
    {
        if (!_disposed) _triggers = null;
    }

    internal void Configure(Func<string?> sourcePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (world.LevelId == 0) return;
        string path = sourcePath() ??
            throw new InvalidOperationException("R&C1 checkpoint volumes require an attached retail source.");
        using var reader = new FileRandomAccessReader(path);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(entry => entry.LevelId == world.LevelId);
        var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        _triggers = new(Rac1ClipVolumes.Read(gameplay));
        var native = Rac1Instances.Parse(gameplay).MobyInstances.ToDictionary(obj => obj.Index);
        foreach (var moby in (world.DynamicObjects ?? [])
            .Where(item => item.NativeClassId == Rac1CommonMobyAuthored.CheckpointMarker))
        {
            var authored = Rac1CommonMobyAuthored.ReadCheckpointMarker(moby)
                ?? throw new InvalidDataException($"Native checkpoint {moby.InstanceIndex} lacks authored fields.");
            var placement = native[moby.InstanceIndex];
            if (placement.OClass != moby.NativeClassId)
                throw new InvalidDataException($"Native checkpoint {moby.InstanceIndex} class identity changed.");
            _triggers.Register(placement, authored);
        }
        GD.Print($"[rac1-checkpoints] {_triggers.RegisteredCount} original class-805 markers, " +
            $"{_triggers.UngatedCount} volume-driven markers enabled");
    }

    internal void Tick(PlayerHost? player, Rac1LevelCheckpointSession? checkpoint)
    {
        if (_disposed || _triggers is null || player is null ||
            !GodotObject.IsInstanceValid(player) || world.Game != ObpSourceGame.Rac1 ||
            checkpoint is null || checkpoint.NativeLevelId != world.LevelId)
            return;
        NVector3 nativePlayer = Rac1LootPresentation.ToEffectNative(player.GlobalPosition);
        foreach (var trigger in _triggers.Step(nativePlayer))
        {
            var pos = Rac1LootPresentation.FromEffectNative(new NVector3(
                (float)trigger.NativeX, (float)trigger.NativeY, (float)trigger.NativeZ));
            var query = PhysicsRayQueryParameters3D.Create(
                pos + Vector3.Up * .5f, pos - Vector3.Up * 2.5f);
            // A checkpoint marker is not a walkable crate. Preserve the source
            // grounding probe exclusions and its original two-unit snap bound.
            query.Exclude = [player.GetRid()];
            foreach (var body in loot.CrateBodies.Values)
                if (GodotObject.IsInstanceValid(body)) query.Exclude.Add(body.GetRid());
            foreach (var enemy in enemies.Roster.Values)
                if (GodotObject.IsInstanceValid(enemy.Body)) query.Exclude.Add(enemy.Body.GetRid());
            var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
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
                context.CombatStatus = $"native checkpoint i{trigger.InstanceIndex} activated (clip {trigger.CuboidIndex})";
                GD.Print($"[rac1-checkpoints] {context.CombatStatus}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _triggers = null;
        _disposed = true;
    }
}
