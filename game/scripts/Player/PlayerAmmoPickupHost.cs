using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

internal sealed record PlayerAmmoPickupPorts(Func<PlayerHost?> Player, Func<bool> Alive, Func<bool> Current,
    Func<IReadOnlyList<PlayerAmmoSupply>> Supplies, Func<SourceWeaponKey, int, int> Grant,
    Func<string?> VisualSourcePath, Func<int, int> Below, Action Collected,
    Action<global::Godot.Collections.Array<Rid>> Exclusions);

/// <summary>One world's transient family pickups; the persistent player owns eligibility and ammunition.</summary>
internal sealed class PlayerAmmoPickupHost(RuntimeWorld world, RuntimeWorldScene.Result scene, PlayerAmmoPickupPorts ports) : IDisposable
{
    private sealed record Drop(AmmoPickupFamily Family, Node3D Node, PlayerAmmoPickupMotion Motion);
    private readonly List<Drop> _drops = [];
    private AmmoPickupPresentation? _visuals;
    private string? _sourcePath;
    private double _accumulator;
    private bool _disposed;

    internal void Prepare(Rac1PickupAssets.Catalogue? existing = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_visuals is not null) return;
        try
        {
            _sourcePath = ports.VisualSourcePath() ?? throw new InvalidDataException("Ammo-family presentation needs the attached, supported RAC1 source.");
            _visuals = new(world, existing ?? Rac1PickupAssets.Load(_sourcePath, 0));
        }
        catch (Exception error)
        {
            GD.PrintErr($"[ammo-family] Required pickup assets could not be prepared: {error}");
            ApplicationLifecycle.RequestQuit(scene.Root, "required-ammo-assets-unavailable", 2);
            throw;
        }
    }

    internal void Spawn(Vector3 origin, int count, int crateIndex)
    {
        if (_disposed || !ports.Current()) return;
        Prepare();
        for (int i = 0; i < count; i++)
        {
            if (PlayerAmmoSupplyPolicy.SelectDropFamily(ports.Supplies(), ports.Below) is not { } family)
            {
                GD.Print($"[ammo-family] crate={crateIndex} no eligible owned attached-source recipient");
                return;
            }
            var node = _visuals!.Create(family, scene.Root, $"AmmoFamily_{family}_{crateIndex}_{i}");
            node.GlobalPosition = origin;
            _drops.Add(new(family, node, new(ToNeutral(origin), Sample)));
            GD.Print($"[ammo-family] crate={crateIndex} family={family} recipient=pending-family-collection");
        }
    }

    internal void Tick(double delta)
    {
        if (_disposed || !ports.Current() || ports.Player() is not { } player || !GodotObject.IsInstanceValid(player)) return;
        if (_sourcePath != ports.VisualSourcePath())
        {
            GD.PrintErr("[ammo-family] Required pickup presentation source changed or became unavailable.");
            ApplicationLifecycle.RequestQuit(scene.Root, "required-ammo-source-unavailable", 2);
            return;
        }
        _accumulator = Math.Min(.2, _accumulator + Math.Clamp(delta, 0, .25));
        int ticks = (int)Math.Floor(_accumulator * 60); _accumulator -= ticks / 60d;
        var space = player.GetWorld3D().DirectSpaceState;
        var excluded = new global::Godot.Collections.Array<Rid> { player.GetRid() }; ports.Exclusions(excluded);
        for (int tick = 0; tick < ticks; tick++)
        {
            var root = ToNeutral(player.GlobalPosition);
            var socket = root + NVector3.UnitY * .7f;
            foreach (var drop in _drops.ToArray())
                TickDrop(drop, root, socket, space, excluded);
        }
    }

    private void TickDrop(Drop drop, NVector3 root, NVector3 socket, PhysicsDirectSpaceState3D space,
        global::Godot.Collections.Array<Rid> excluded)
    {
        if (ports.Alive() && drop.Motion.CanCollect(root)) TryCollect(drop, socket, space, excluded);
        drop.Motion.Step(socket, (from, to) =>
        {
            var bottom = Vector3.Up * _visuals!.GroundOffset(drop.Family);
            var ray = PhysicsRayQueryParameters3D.Create(FromNeutral(from) - bottom + Vector3.Up * .1f, FromNeutral(to) - bottom);
            ray.Exclude = excluded;
            var hit = space.IntersectRay(ray);
            return hit.TryGetValue("position", out var point) && hit.TryGetValue("normal", out var normal) && normal.AsVector3().Y > .70710677f
                ? ToNeutral(point.AsVector3() + bottom) : null;
        });
        drop.Node.GlobalPosition = FromNeutral(drop.Motion.Position); drop.Node.Rotation = new(0, drop.Motion.Spin, 0);
        if (!drop.Motion.Finished) return;
        drop.Node.QueueFree(); _drops.Remove(drop);
    }

    private void TryCollect(Drop drop, NVector3 socket, PhysicsDirectSpaceState3D space,
        global::Godot.Collections.Array<Rid> excluded)
    {
        var ray = PhysicsRayQueryParameters3D.Create(FromNeutral(drop.Motion.Position) + Vector3.Up * .1f, FromNeutral(socket));
        ray.Exclude = excluded;
        if (space.IntersectRay(ray).Count != 0 || PlayerAmmoSupplyPolicy.Select(ports.Supplies(), drop.Family, ports.Below) is not { } selected) return;
        int amount = ports.Grant(selected.Weapon, selected.Amount);
        if (amount <= 0) return;
        drop.Motion.Claim(); ports.Collected();
        GD.Print($"[ammo-family] collected family={drop.Family} recipient={selected.Weapon} delta={amount}");
    }

    private float Sample(float min, float max) => min + ports.Below(32768) * (max - min) / 32768f;
    private static NVector3 ToNeutral(Vector3 point) => new(point.X, point.Y, point.Z);
    private static Vector3 FromNeutral(NVector3 point) => new(point.X, point.Y, point.Z);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var drop in _drops.Where(drop => GodotObject.IsInstanceValid(drop.Node))) drop.Node.QueueFree();
        _drops.Clear(); _visuals = null;
    }
}
