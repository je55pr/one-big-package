using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

public partial class OBPGame
{
    private bool _rac1ArsenalHeld;

    private void ResetRac1Arsenal()
    {
        Rac1World.Weapons.Missiles.Reset();
        Rac1World.Weapons.Deployables.Reset();
        Rac1World.Weapons.Morph.Reset();
        Rac1World.Weapons.Beams.Reset();
        Rac1World.Weapons.Taunter.Reset();
        Rac1World.Weapons.Walloper.Reset();
        _rac1ArsenalHeld = false;
        Rac1World.Weapons.Tesla.Reset();
        if (_player is not null) { _player.Rac1EquipmentActionActive = false; _player.Rac1EquipmentLungeVelocity = Vector3.Zero; }
    }

    private bool AdmitRac1ArsenalUse() => _rac1Weapons.Equipped switch
    {
        Rac1WeaponId.VisibombGun => !Rac1World.Weapons.Missiles.GuidedActive,
        Rac1WeaponId.DroneDevice => Rac1World.Weapons.Deployables.CanFireDrone,
        Rac1WeaponId.Ryno => Rac1World.Weapons.Missiles.CanBeginRyno,
        Rac1WeaponId.DecoyGlove => Rac1World.Weapons.Deployables.CanFireDecoy,
        _ => true,
    };

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

    private void FireRac1Arsenal(Rac1AdditionalWeaponSession.Shot shot)
    {
        switch (shot.Weapon)
        {
            case Rac1WeaponId.Devastator: Rac1World.Weapons.Missiles.Fire(shot.Weapon); break;
            case Rac1WeaponId.VisibombGun: Rac1World.Weapons.Missiles.Fire(shot.Weapon); break;
            case Rac1WeaponId.Ryno: Rac1World.Weapons.Missiles.Fire(shot.Weapon); break;
            case Rac1WeaponId.DroneDevice: Rac1World.Weapons.Deployables.FireDrones(); break;
            case Rac1WeaponId.DecoyGlove: Rac1World.Weapons.Deployables.FireDecoy(); break;
            case Rac1WeaponId.Walloper: Rac1World.Weapons.Walloper.Begin(); break;
        }
    }

    private void StepRac1ArsenalWorld()
    {
        if (_player is null) return;
        Rac1World.Weapons.Missiles.Tick();
        StepRac1ArsenalDeployables(); StepRac1ArsenalBeams(); Rac1World.Weapons.Walloper.Tick();
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

    private void StepRac1ArsenalDeployables()
    {
        Rac1World.Weapons.Deployables.Tick();
        Rac1World.Weapons.Morph.TickChickens();
    }

    private void StepRac1ArsenalBeams()
    {
        Rac1World.Weapons.Beams.BeginFrame();
        Rac1World.Weapons.Morph.TickExposure();
        if (!_rac1ArsenalHeld) { Rac1World.Weapons.Tesla.Release(); return; }
        var weapon = _rac1Weapons.Equipped;
        if (weapon != Rac1WeaponId.TeslaClaw) Rac1World.Weapons.Tesla.Release();
        if (weapon is not (Rac1WeaponId.TeslaClaw or Rac1WeaponId.MorphORay or Rac1WeaponId.Taunter)) return;
        if (weapon == Rac1WeaponId.TeslaClaw && _rac1Weapons.GetAmmo(19) <= 0) return;
        if (weapon == Rac1WeaponId.TeslaClaw) Rac1World.Weapons.Beams.AdvanceTesla();
        var from = EquipmentMuzzle(weapon);
        if (weapon is Rac1WeaponId.TeslaClaw or Rac1WeaponId.MorphORay && _rac1WeaponWorldTick % 3 == 0)
            SpawnEquipmentFlash(from, 27, weapon == Rac1WeaponId.TeslaClaw ? new(.3f, .6f, 1, .85f) : new(.4f, 1, .25f, .75f), .28f, 5);
        if (weapon == Rac1WeaponId.Taunter)
        {
            Rac1World.Weapons.Taunter.Tick(from, EquipmentForward, _player!.GlobalPosition);
            return;
        }
        if (weapon == Rac1WeaponId.TeslaClaw)
        {
            Rac1World.Weapons.Tesla.Tick(from, EquipmentForward);
            return;
        }

        Rac1World.Weapons.Morph.Fire(from, EquipmentForward);
    }
}
