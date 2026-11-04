using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Narrow world-owned Walloper presentation and native damage adapters.
/// Equipment inventory, native damage transport and player lifecycle stay
/// with the established player and combat hosts.
/// </summary>
internal sealed record Rac1WalloperPorts(
    Func<PlayerHost?> Player,
    Func<Rac1WeaponId> Equipped,
    Func<bool> Alive,
    Func<Vector3> Forward,
    Func<Vector3> Muzzle,
    Func<RuntimeWorldScene.DynamicObjectNode, Rac1GameplayDamageEvent, bool> Damage,
    Action<int, int, Vector3> Sound,
    Action<Vector3, int, Color, float, int> Flash);

/// <summary>
/// Per-world Walloper action state. The native 60 Hz lunge/contact window,
/// per-swing victim admission and source audio cues are preserved exactly.
/// No projectile state or persistent equipment inventory is stored here.
/// </summary>
internal sealed class Rac1WalloperHost(Rac1WalloperPorts ports, Rac1WeaponContactHost contacts) : IDisposable
{
    private int _age;
    private readonly HashSet<int> _victims = [];
    private bool _disposed;

    internal bool Active => !_disposed && _age > 0;
    // Read-only animation clock shared with the player presentation adapter.
    internal int Age => _disposed ? 0 : _age;

    internal void Begin()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _age = 1;
        _victims.Clear();
    }

    internal void Tick()
    {
        if (_disposed || ports.Player() is not { } player) return;
        player.Rac1EquipmentLungeVelocity = Vector3.Zero;
        player.Rac1EquipmentActionActive = false;
        if (_age <= 0) return;

        int age = _age++;
        if (age >= Rac1ArsenalRules.WalloperDurationTicks ||
            ports.Equipped() != Rac1WeaponId.Walloper || !ports.Alive())
        {
            _age = 0;
            return;
        }
        if (age == 10) ports.Sound(0, 53, player.GlobalPosition); // Ratchet seq103 cue2.5
        if (age == 16) ports.Sound(0, 30, player.GlobalPosition); // Ratchet seq103 cue4

        player.Rac1EquipmentActionActive = true;
        if (age > Rac1ArsenalRules.WalloperLungeFrom && age < Rac1ArsenalRules.WalloperLungeTo)
            player.Rac1EquipmentLungeVelocity = ports.Forward() * Rac1ArsenalRules.WalloperLungeSpeed;
        if (age <= Rac1ArsenalRules.WalloperLungeFrom || age >= Rac1ArsenalRules.WalloperContactTo) return;

        var centre = player.GlobalPosition + ports.Forward() * .8f + Vector3.Up * .55f;
        foreach (var (node, bounds) in contacts.Geometry)
        {
            if (_victims.Contains(node.Source.InstanceIndex) ||
                centre.DistanceTo(centre.Clamp(bounds.Position, bounds.End)) > .8f ||
                !contacts.HasLineOfSight(centre, bounds.GetCenter())) continue;

            if (ports.Damage(node, Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Walloper,
                new(node.Source.NativeClassId, node.Source.InstanceIndex), 3, 0x30000)))
                _victims.Add(node.Source.InstanceIndex);
        }

        ports.Flash(ports.Muzzle(), 27, new(.5f, .7f, 1, .7f), .55f, 3);
    }

    internal void Reset()
    {
        _age = 0;
        _victims.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
