namespace OBP.RAC1.Gameplay;

/// <summary>
/// Deterministic equipment accounting: native Blaster/Pyrocitor/Suck cadence,
/// Mine staging, glove ammo and captured-object capacity. Aim and world queries
/// remain the presentation host's responsibility.
/// </summary>
public sealed class Rac1AdditionalWeaponSession
{
    private readonly Rac1WeaponInventory _inventory;
    private readonly Dictionary<Rac1WeaponId, int> _cooldowns = [];
    private readonly Rac1Class4aWeaponFamilySession _mine = new();
    private readonly List<Rac1MobyRuntimeKey> _cargo = [];
    private Rac1WeaponId _previousWeapon = Rac1WeaponId.Wrench;
    private bool _previousHeld;
    private long _generation;
    private ulong _nativeTick;

    public Rac1AdditionalWeaponSession(Rac1WeaponInventory inventory) => _inventory = inventory;
    // Ordinary (non-gold) captured-Moby store. It is not item9's ammo[] slot.
    public const int SuckCapacity = 5;
    public IReadOnlyList<Rac1MobyRuntimeKey> Cargo => _cargo.AsReadOnly();
    public bool Vacuuming { get; private set; }
    public int MineRearmTicks => _mine.Probe().ProjectileRearmTicksRemaining;
    public long Generation => _generation;

    public sealed record Shot(Rac1WeaponId Weapon, long Generation, Rac1MobyRuntimeKey? Cargo = null);

    public Shot? Step(bool held, bool admitted = true)
    {
        foreach (var key in _cooldowns.Keys.ToArray())
            _cooldowns[key] = Math.Max(0, _cooldowns[key] - 1);
        var weapon = _inventory.Equipped;
        if (weapon != _previousWeapon) { _previousHeld = false; Vacuuming = false; }
        bool pressed = held && !_previousHeld;
        ulong frame = _nativeTick++;
        _previousHeld = held; _previousWeapon = weapon;
        if (!held || weapon != Rac1WeaponId.SuckCannon) Vacuuming = false;
        if (weapon == Rac1WeaponId.SuckCannon && pressed && _cargo.Count == 0) Vacuuming = true;

        bool mineAdmitted = _inventory.Owns(Rac1WeaponId.MineGlove) && _inventory.GetAmmo(17) > 0;
        var mine = _mine.Step(held && weapon == Rac1WeaponId.MineGlove && mineAdmitted, mineAdmitted);
        if (mine.Launch is not null && _inventory.TryConsumeAmmo(17, 1))
            return new(weapon, ++_generation);
        if (!admitted || !held || !Rac1WeaponProfiles.IsAdditional(weapon) || weapon == Rac1WeaponId.MineGlove ||
            _cooldowns.GetValueOrDefault(weapon) > 0 || Vacuuming) return null;
        if (Rac1ArsenalRules.PressOnly(weapon) && !pressed) return null;
        if (weapon == Rac1WeaponId.SuckCannon)
        {
            if (_cargo.Count == 0) return null;
            // Native release scans the pointer slots from index0; source retirement and
            // release geometry are separate from generic ammo consumption.
            var cargo = _cargo[0]; _cargo.RemoveAt(0);
            _cooldowns[weapon] = Rac1WeaponProfiles.SuckFireTicks;
            return new(weapon, ++_generation, cargo);
        }
        if (weapon == Rac1WeaponId.Pyrocitor && frame % Rac1WeaponProfiles.PyrocitorAmmoInterval != 0) return null;
        if (Rac1ItemAmmoDescriptors.Get((int)weapon).UsesAmmo && !_inventory.TryConsumeAmmo((int)weapon, 1)) return null;
        _cooldowns[weapon] = weapon switch
        {
            Rac1WeaponId.Blaster => Rac1WeaponProfiles.BlasterFireTicks,
            Rac1WeaponId.GloveOfDoom => 20,
            Rac1WeaponId.Pyrocitor => 0,
            _ => Rac1ArsenalRules.FireTicks(weapon),
        };
        return new(weapon, ++_generation);
    }

    public bool TryCapture(Rac1MobyRuntimeKey target)
    {
        if (target.NativeClassId < 0 || target.InstanceIndex < 0) throw new ArgumentOutOfRangeException(nameof(target));
        if (!Vacuuming || _cargo.Count >= SuckCapacity || _cargo.Contains(target)) return false;
        _cargo.Add(target);
        return true;
    }

    public void RestoreCargo(IReadOnlyList<Rac1MobyRuntimeKey> cargo)
    {
        if (cargo.Count > SuckCapacity || cargo.Distinct().Count() != cargo.Count ||
            cargo.Any(key => key.NativeClassId < 0 || key.InstanceIndex < 0))
            throw new ArgumentException("Invalid captured-Moby store.", nameof(cargo));
        _cargo.Clear(); _cargo.AddRange(cargo); Vacuuming = false;
    }
}
