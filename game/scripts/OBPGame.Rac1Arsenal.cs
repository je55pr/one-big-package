using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    private void ResetRac1Arsenal()
    {
        Rac1World.Weapons.Arsenal.Reset();
        if (_player is not null) { _player.Rac1EquipmentActionActive = false; _player.Rac1EquipmentLungeVelocity = Vector3.Zero; }
    }

    private RuntimeWorldScene.DynamicObjectNode? FindArsenalTarget(Vector3 origin, float range, float cone = -.1f,
        IEnumerable<int>? excluded = null)
    {
        var skip = excluded?.ToHashSet();
        return Rac1World.Enemies.Targets.Where(node => Rac1World.Weapons.Contacts.IsAimTarget(node) && !Rac1World.Weapons.Morph.Contains(node.Source.InstanceIndex) &&
                skip?.Contains(node.Source.InstanceIndex) != true)
            .Where(node =>
            {
                var delta = node.Root.GlobalPosition + Vector3.Up * .5f - origin;
                return delta.LengthSquared() < range * range && delta.LengthSquared() > .00001f &&
                    delta.Normalized().Dot(EquipmentForward) > cone && Rac1World.Weapons.Contacts.HasLineOfSight(origin, node.Root.GlobalPosition + Vector3.Up * .5f);
            }).OrderBy(node => node.Root.GlobalPosition.DistanceSquaredTo(origin)).FirstOrDefault();
    }

    private void StepRac1ArsenalWorld()
    {
        if (_player is null) return;
        Rac1World.Weapons.Arsenal.Tick();
    }

    private void ApplyArsenalBlast(Rac1WeaponId weapon, Vector3 position, float radius, double damage, uint flags)
    {
        foreach (var (node, bounds) in Rac1World.Weapons.Contacts.Geometry.ToArray())
        {
            var closest = position.Clamp(bounds.Position, bounds.End);
            if (!Rac1World.Enemies.IsRac1GameplayActive(node) || position.DistanceTo(closest) > radius ||
                !Rac1World.Weapons.Contacts.HasLineOfSight(position + Vector3.Up * .1f, bounds.GetCenter())) continue;
            ApplyRac1AdditionalDamage(node, Rac1WeaponProfiles.DirectDamage(weapon,
                new(node.Source.NativeClassId, node.Source.InstanceIndex), damage, flags));
        }
    }
}
