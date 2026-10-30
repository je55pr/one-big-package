using Godot;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

/// <summary>
/// Player-lifetime equipment selection and animation inputs borrowed by the
/// per-world arsenal coordinator. It does not own native ammo or the player.
/// </summary>
internal sealed record Rac1ArsenalUsePorts(
    Func<Rac1WeaponId> Equipped,
    Func<int> TeslaAmmo,
    Func<ulong> WorldTick,
    Func<Rac1WeaponId, Vector3> EquipmentMuzzle,
    Func<Vector3> EquipmentForward,
    Func<Vector3> PlayerPosition,
    Action<Vector3, int, Color, float, int> SpawnFlash);

/// <summary>
/// R&C1 world-lease orchestration for native arsenal families. Each weapon
/// host continues to own its recovered rules and visual lifetime; this owner
/// only preserves the shared held-use state and original tick order.
/// </summary>
internal sealed class Rac1ArsenalUseHost(Rac1WeaponHost weapons, Rac1ArsenalUsePorts ports) : IDisposable
{
    private bool _held;
    private bool _disposed;

    internal bool Held => !_disposed && _held;

    internal void SetHeld(bool held)
    {
        if (!_disposed) _held = held;
    }

    internal void ClearHeld() => _held = false;

    internal void Reset()
    {
        if (_disposed) return;
        weapons.Missiles.Reset();
        weapons.Deployables.Reset();
        weapons.Morph.Reset();
        weapons.Beams.Reset();
        weapons.Taunter.Reset();
        weapons.Walloper.Reset();
        ClearHeld();
        weapons.Tesla.Reset();
    }

    internal bool AdmitUse() => !_disposed && (ports.Equipped() switch
    {
        Rac1WeaponId.VisibombGun => !weapons.Missiles.GuidedActive,
        Rac1WeaponId.DroneDevice => weapons.Deployables.CanFireDrone,
        Rac1WeaponId.Ryno => weapons.Missiles.CanBeginRyno,
        Rac1WeaponId.DecoyGlove => weapons.Deployables.CanFireDecoy,
        _ => true,
    });

    internal void Fire(Rac1WeaponId weapon)
    {
        if (_disposed) return;
        switch (weapon)
        {
            case Rac1WeaponId.Devastator: weapons.Missiles.Fire(weapon); break;
            case Rac1WeaponId.VisibombGun: weapons.Missiles.Fire(weapon); break;
            case Rac1WeaponId.Ryno: weapons.Missiles.Fire(weapon); break;
            case Rac1WeaponId.DroneDevice: weapons.Deployables.FireDrones(); break;
            case Rac1WeaponId.DecoyGlove: weapons.Deployables.FireDecoy(); break;
            case Rac1WeaponId.Walloper: weapons.Walloper.Begin(); break;
        }
    }

    internal void Tick()
    {
        if (_disposed) return;
        weapons.Missiles.Tick();
        weapons.Deployables.Tick();
        weapons.Morph.TickChickens();
        TickBeams();
        weapons.Walloper.Tick();
    }

    private void TickBeams()
    {
        weapons.Beams.BeginFrame();
        weapons.Morph.TickExposure();
        if (!_held) { weapons.Tesla.Release(); return; }
        var weapon = ports.Equipped();
        if (weapon != Rac1WeaponId.TeslaClaw) weapons.Tesla.Release();
        if (weapon is not (Rac1WeaponId.TeslaClaw or Rac1WeaponId.MorphORay or Rac1WeaponId.Taunter)) return;
        if (weapon == Rac1WeaponId.TeslaClaw && ports.TeslaAmmo() <= 0) return;
        if (weapon == Rac1WeaponId.TeslaClaw) weapons.Beams.AdvanceTesla();
        var from = ports.EquipmentMuzzle(weapon);
        if (weapon is Rac1WeaponId.TeslaClaw or Rac1WeaponId.MorphORay && ports.WorldTick() % 3 == 0)
            ports.SpawnFlash(from, 27, weapon == Rac1WeaponId.TeslaClaw ? new(.3f, .6f, 1, .85f) : new(.4f, 1, .25f, .75f), .28f, 5);
        if (weapon == Rac1WeaponId.Taunter)
        {
            weapons.Taunter.Tick(from, ports.EquipmentForward(), ports.PlayerPosition());
            return;
        }
        if (weapon == Rac1WeaponId.TeslaClaw)
        {
            weapons.Tesla.Tick(from, ports.EquipmentForward());
            return;
        }

        weapons.Morph.Fire(from, ports.EquipmentForward());
    }

    public void Dispose()
    {
        if (_disposed) return;
        ClearHeld();
        _disposed = true;
    }
}
