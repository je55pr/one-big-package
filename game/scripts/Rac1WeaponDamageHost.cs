using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// World-lease damage dispatch for R&C1 additional weapons. The recovered
/// Moby admission, crate and enemy authorities remain in their existing
/// sessions; the shared contact owner supplies the current scene geometry.
/// No player inventory, native damage formulas or random stream lives here.
/// </summary>
internal sealed class Rac1WeaponDamageHost(
    Rac1WorldContext context, Rac1EnemyHost enemies,
    Rac1BreakableHost breakables, Rac1WrenchDamageHost wrenchDamage,
    Rac1WeaponContactHost contacts) : IDisposable
{
    private bool _disposed;

    internal bool Apply(RuntimeWorldScene.DynamicObjectNode target, Rac1GameplayDamageEvent damage)
    {
        if (_disposed) return false;
        if (target.Source.NativeClassId == Rac1FrogQueenGroup.QueenClassId)
            return enemies.Frogs.ApplyRac1QueenWeaponDamage(target, damage);
        if (!context.MobyRuntime.CanDispatchDamage(damage)) return false;
        bool applied;
        if (Rac1BoltCrate.IsCrateClass(target.Source.NativeClassId))
        {
            wrenchDamage.CompleteCrateDamage(target,
                context.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(damage));
            applied = true;
        }
        else if (target.Source.NativeClassId == Rac1ExplosiveCrateSession.ClassId)
            applied = breakables.TryTriggerExplosiveDamage(target, damage);
        else applied = enemies.ApplyRac1EnemyWeaponDamage(target, damage);
        if (applied) context.DamageTransport.Publish(damage);
        return applied;
    }

    internal void ApplyBlast(Rac1WeaponId weapon, Vector3 position, float radius, double damage, uint flags)
    {
        if (_disposed) return;
        foreach (var (node, bounds) in contacts.Geometry.ToArray())
        {
            var closest = position.Clamp(bounds.Position, bounds.End);
            if (!enemies.IsRac1GameplayActive(node) || position.DistanceTo(closest) > radius ||
                !contacts.HasLineOfSight(position + Vector3.Up * .1f, bounds.GetCenter())) continue;
            Apply(node, Rac1WeaponProfiles.DirectDamage(weapon,
                new(node.Source.NativeClassId, node.Source.InstanceIndex), damage, flags));
        }
    }

    public void Dispose() => _disposed = true;
}
