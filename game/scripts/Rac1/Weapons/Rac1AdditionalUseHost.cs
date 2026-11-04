using OBP.RAC1.Gameplay;

namespace OBP.Game;

/// <summary>
/// Native source step result and player-use presentation flags. The equipped
/// inventory and native shot cadence remain owned by their existing sessions.
/// </summary>
internal readonly record struct Rac1AdditionalUseFrame(
    bool Held, bool Pyro, bool PyroChanged, Rac1AdditionalWeaponSession.Shot? Shot);

/// <summary>
/// Player-session-owned additional weapon input and use clock. This state
/// survives world-lease changes only for the lifetime of its player, and is
/// explicitly reset at the same weapon-preview/world entry boundaries as
/// the original OBPGame fields. No Godot nodes or world gameplay hosts live
/// here; native ammo and cadence remain in Rac1AdditionalWeaponSession.
/// </summary>
internal sealed class Rac1AdditionalUseHost
{
    private Rac1WeaponId _useWeapon;

    internal Rac1AdditionalWeaponSession? Session { get; private set; }
    internal Rac1WeaponInventory? BoundInventory { get; private set; }
    internal bool FireRequested { get; private set; }
    internal bool PyroEmitting { get; private set; }
    internal int UseTicks { get; private set; }
    internal int UseAge { get; private set; }
    internal int ShotsFired { get; private set; }

    internal void RequestFire() => FireRequested = true;
    internal void CancelFire() => FireRequested = false;

    internal void ClearUseTicks() => UseTicks = 0;
    internal void StopPyro() => PyroEmitting = false;

    internal void ResetSession(Rac1WeaponInventory inventory)
    {
        Session = new(inventory);
        BoundInventory = inventory;
    }

    internal void BindInventory(Rac1WeaponInventory inventory) => BoundInventory = inventory;

    internal void ResetInputState()
    {
        FireRequested = false;
        PyroEmitting = false;
        UseTicks = UseAge = 0;
        ShotsFired = 0;
    }

    internal Rac1AdditionalUseFrame BeginFrame(
        bool eligible, Func<bool> detectorEquipped, Rac1WeaponInventory inventory,
        Action<bool> setArsenalHeld, Func<bool> admitArsenalUse)
    {
        var session = Session ?? throw new InvalidOperationException("Additional weapon session has not been configured.");
        bool held = eligible && FireRequested && !detectorEquipped();
        FireRequested = false;
        // Native use admission depends on the held state being visible first.
        setArsenalHeld(held);
        if (_useWeapon != inventory.Equipped)
        {
            _useWeapon = inventory.Equipped;
            UseTicks = UseAge = 0;
        }
        if (UseTicks > 0) { UseTicks--; UseAge++; }
        var shot = session.Step(held, admitArsenalUse());
        bool pyro = held && inventory.Equipped == Rac1WeaponId.Pyrocitor &&
            (inventory.GetAmmo(16) > 0 || shot is not null);
        bool changed = pyro != PyroEmitting;
        PyroEmitting = pyro;
        return new(held, pyro, changed, shot);
    }

    internal void RecordShot(Rac1AdditionalWeaponSession.Shot shot)
    {
        if (UseTicks == 0) UseAge = 0;
        UseTicks = shot.Weapon switch
        {
            Rac1WeaponId.Blaster => 6,
            Rac1WeaponId.Devastator => 35,
            Rac1WeaponId.Ryno => Rac1ArsenalRules.FireTicks(shot.Weapon),
            Rac1WeaponId.Walloper => Rac1ArsenalRules.WalloperDurationTicks,
            _ => 2,
        };
        ShotsFired++;
    }

    internal void CompleteFrame(Rac1AdditionalUseFrame frame, Rac1WeaponInventory inventory)
    {
        bool continuous = frame.Pyro || frame.Held && (inventory.Equipped is Rac1WeaponId.Taunter or Rac1WeaponId.MorphORay ||
            inventory.Equipped == Rac1WeaponId.TeslaClaw && inventory.GetAmmo(19) > 0 ||
            inventory.Equipped == Rac1WeaponId.SuckCannon && Session?.Vacuuming == true);
        if (continuous) UseTicks = Math.Max(UseTicks, 2);
    }
}
