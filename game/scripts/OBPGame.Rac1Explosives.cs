using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    private Rac1ExplosiveCrateSession? _rac1ExplosiveCrates;
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _rac1ExplosiveNodes = [];
    private readonly Dictionary<int, (StandardMaterial3D Material, Color Original)[]> _rac1ExplosiveMaterials = [];
    private readonly HashSet<(int Source, int Target)> _rac1ExplosiveHits = [];
    private readonly HashSet<int> _rac1ExplosivePlayerHits = [];
    private double _rac1ExplosiveTickCarry;

    private void ResetRac1ExplosiveCrates()
    {
        _rac1ExplosiveCrates = null;
        _rac1ExplosiveNodes.Clear();
        _rac1ExplosiveMaterials.Clear();
        _rac1ExplosiveHits.Clear();
        _rac1ExplosivePlayerHits.Clear();
        _rac1ExplosiveTickCarry = 0;
    }

    private void ConfigureRac1ExplosiveCrates(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        _rac1ExplosiveCrates = new Rac1ExplosiveCrateSession(Rac1World.Context.MobyRuntime,
            (min, max) => Rac1EffectRandom.Inclusive(min, max));
        foreach (var source in world.DynamicObjects ?? [])
        {
            if (source.NativeClassId != Rac1ExplosiveCrateSession.ClassId) continue;
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException($"Native class-505 explosive {source.InstanceIndex} has no original model.");
            _rac1ExplosiveCrates.Register(source, node.State);
            _rac1ExplosiveNodes.Add(source.InstanceIndex, node);
            Rac1World.Presentation.CrateNodes.Add(node);
            AddRac1CrateCollider(node);
            _rac1ExplosiveMaterials.Add(source.InstanceIndex, node.Root.GetChildren()
                .OfType<MeshInstance3D>().Where(mesh => mesh.MaterialOverride is StandardMaterial3D)
                .Select(mesh =>
                {
                    var original = (StandardMaterial3D)mesh.MaterialOverride;
                    var material = (StandardMaterial3D)original.Duplicate();
                    mesh.MaterialOverride = material;
                    return (material, material.AlbedoColor);
                }).ToArray());
        }
        GD.Print($"[rac1-explosives] {_rac1ExplosiveNodes.Count} original class-505 crates registered");
    }

    private bool TryTriggerRac1ExplosiveDamage(RuntimeWorldScene.DynamicObjectNode target,
        Rac1GameplayDamageEvent damage)
    {
        if (_rac1ExplosiveCrates is null || target.Source.NativeClassId != Rac1ExplosiveCrateSession.ClassId ||
            !Rac1World.Context.MobyRuntime.CanDispatchDamage(damage)) return false;
        Rac1World.Context.MobyRuntime.DispatchDamage<Rac1ExplosiveCrateTriggered>(damage);
        return true;
    }

    private void TickRac1ExplosiveCrates(double delta)
    {
        if (_rac1ExplosiveCrates is null || _rac1ExplosiveNodes.Count == 0 || _player is null) return;

        // Native class 505 reacts to Ratchet's grounded/touched Moby contact,
        // not a fabricated proximity sphere. Godot is the interim contact producer.
        for (int i = 0; i < _player.GetSlideCollisionCount(); i++)
        {
            if (_player.GetSlideCollision(i).GetCollider() is not StaticBody3D body) continue;
            foreach (var pair in _rac1ExplosiveNodes)
                if (_rac1CrateBodies.TryGetValue(pair.Key, out var crateBody) && crateBody == body)
                    _rac1ExplosiveCrates.TryTouch(pair.Key);
        }

        _rac1ExplosiveTickCarry += Math.Max(0, delta) * OBP.RAC1.Player.Rac1RatchetMovementController.UpdateHz;
        while (_rac1ExplosiveTickCarry >= 1)
        {
            _rac1ExplosiveTickCarry--;
            foreach (var pair in _rac1ExplosiveNodes)
            {
                var node = pair.Value;
                // A hidden crate cannot prove the native draw-range predicate.
                // Until that camera predicate is recovered, do not guess a respawn.
                var frame = _rac1ExplosiveCrates.Step(pair.Key, beyondNativeDrawRange: false);
                if (_rac1ExplosiveMaterials.TryGetValue(pair.Key, out var materials))
                    foreach (var (mat, original) in materials)
                        mat.AlbedoColor = frame.FlashRed
                            ? new Color(.78f, .25f, .25f, original.A)
                            : original;
                if (frame.WarningSound)
                    PlayRac1PickupSound(Rac1ExplosiveCrateSession.ClassId, 0x20, node.Root.GlobalPosition);
                if (frame.Detonated)
                {
                    _rac1ExplosiveHits.RemoveWhere(hit => hit.Source == pair.Key);
                    _rac1ExplosivePlayerHits.Remove(pair.Key);
                    SpawnRac1CrateDebris(node);
                    SpawnRac1BoltPickups(node.Root.GlobalPosition,
                        Rac1World.Context.BoltPickups.Emit(SelectRac1CrateReward(
                            _rac1ExplosiveCrates.Authored(pair.Key).RewardCentre)));
                    PlayRac1PickupSound(Rac1CrateAudio.BreakSoundClass(node.Source.NativeClassId),
                        Rac1CrateAudio.BreakLocalSound, node.Root.GlobalPosition);
                    DisableRac1CrateCollider(pair.Key);
                    // Retain the original native state-3 expanding blast.
                    node.Root.Visible = false;
                }
                if (frame.MobyBlastRadius > 0 || frame.PlayerBlastRadius > 0)
                    ApplyRac1ExplosiveBlast(node, frame);
                if (frame.EntityState.Presentation.Presence != node.State.Presentation.Presence)
                {
                    node.ApplyState(frame.EntityState);
                    if (frame.Reappeared) AddRac1CrateCollider(node);
                }
            }
        }
    }

    private void ApplyRac1ExplosiveBlast(RuntimeWorldScene.DynamicObjectNode source,
        Rac1ExplosiveCrateFrame frame)
    {
        if (_player is null || _rac1ExplosiveCrates is null) return;
        var origin = source.Root.GlobalPosition + Vector3.Up * .5f;
        int sourceIndex = source.Source.InstanceIndex;
        if (!_rac1ExplosivePlayerHits.Contains(sourceIndex) &&
            origin.DistanceTo(_player.GlobalPosition + Vector3.Up * .5f) <= frame.PlayerBlastRadius &&
            BombHasLineOfSight(origin, _player.GlobalPosition + Vector3.Up * .5f))
        {
            _rac1ExplosivePlayerHits.Add(sourceIndex);
            Rac1World.Enemies.ApplyRac1EnemyPlayerDamage(Rac1ExplosiveCrateBlast.ToPlayer(sourceIndex));
        }

        foreach (var target in Rac1World.Presentation.CrateNodes.Concat(Rac1World.Enemies.Targets))
        {
            int targetIndex = target.Source.InstanceIndex;
            if (targetIndex == sourceIndex || _rac1ExplosiveHits.Contains((sourceIndex, targetIndex)) ||
                !IsInstanceValid(target.Root) || !Rac1World.Enemies.IsRac1GameplayActive(target)) continue;
            var centre = target.Root.GlobalPosition + Vector3.Up * .5f;
            if (origin.DistanceTo(centre) > frame.MobyBlastRadius + .35f ||
                !BombHasLineOfSight(origin, centre)) continue;
            var damage = Rac1ExplosiveCrateBlast.ToMoby(sourceIndex,
                new(target.Source.NativeClassId, targetIndex));
            bool accepted;
            if (target.Source.NativeClassId == Rac1ExplosiveCrateSession.ClassId)
                accepted = TryTriggerRac1ExplosiveDamage(target, damage);
            else if (Rac1BoltCrate.IsCrateClass(target.Source.NativeClassId) &&
                Rac1World.Context.MobyRuntime.CanDispatchDamage(damage))
            {
                CompleteRac1CrateDamage(target,
                    Rac1World.Context.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(damage));
                accepted = true;
            }
            else accepted = Rac1World.Enemies.Roster.ContainsKey(targetIndex) &&
                Rac1World.Enemies.ApplyRac1EnemyWeaponDamage(target, damage);
            if (accepted)
            {
                _rac1ExplosiveHits.Add((sourceIndex, targetIndex));
                Rac1World.Context.DamageTransport.Publish(damage);
            }
        }
    }
}
