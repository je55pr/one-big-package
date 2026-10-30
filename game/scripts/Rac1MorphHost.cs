using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// World-scoped native enemy and visual adapters for Morph-o-Ray.
/// This host does not own enemy rewards, inventory or the rendering pipeline.
/// </summary>
internal sealed record Rac1MorphPorts(
    Func<int, Rac1HostedEnemy?> Enemy,
    Func<int, Rac1EquipmentView> CreateView,
    Func<Rac1HostedEnemy, bool> IsActive,
    Func<Rac1HostedEnemy, bool> TryGround,
    Action<Rac1HostedEnemy> Extinguish,
    Func<Vector3> Forward,
    Func<float, float, float> SignedRandom,
    Func<Vector3, float, float, Vector3?> Ground,
    Action<Vector3, int, Color, float, int> Flash,
    Func<Vector3, float, float, RuntimeWorldScene.DynamicObjectNode?> FindTarget,
    Func<Vector3, Vector3, float, (Vector3 Point, RuntimeWorldScene.DynamicObjectNode? Target)?> Contact);

/// <summary>
/// R&C1 Morph-o-Ray exposure, transformed Moby identity, movement, replacement
/// collision and restoration belong to the same world lease. Consumers receive
/// read-only observations, never the live chicken dictionary.
/// </summary>
internal sealed class Rac1MorphHost(Rac1MorphPorts ports, Rac1ArsenalBeamPresenter beams) : IDisposable
{
    private sealed record Chicken(Rac1HostedEnemy Enemy, Rac1EquipmentView View, Shape3D OriginalShape,
        Vector3 OriginalShapePosition)
    {
        internal int Age;
        internal Vector3 Direction;
    }

    private readonly Dictionary<int, Chicken> _chickens = [];
    private readonly Dictionary<int, int> _exposure = [];
    private bool _disposed;

    internal bool Contains(int instanceIndex) => !_disposed && _chickens.ContainsKey(instanceIndex);

    internal Rac1EquipmentView? ViewFor(int instanceIndex) =>
        !_disposed && _chickens.TryGetValue(instanceIndex, out var chicken) ? chicken.View : null;

    internal IReadOnlyList<Rac1MobyRuntimeKey> ActiveKeys() =>
        _disposed ? [] : _chickens.Values
            .Where(chicken => ports.IsActive(chicken.Enemy) && !chicken.Enemy.RewardEmitted)
            .Select(chicken => new Rac1MobyRuntimeKey(chicken.Enemy.Node.Source.NativeClassId,
                chicken.Enemy.Node.Source.InstanceIndex)).ToArray();

    internal IReadOnlyList<T> Project<T>(Func<Rac1HostedEnemy, Rac1EquipmentView, T> projection) =>
        _disposed ? [] : _chickens.Values.Select(chicken => projection(chicken.Enemy, chicken.View)).ToArray();

    internal void TickExposure()
    {
        if (_disposed) return;
        foreach (int key in _exposure.Keys.ToArray())
            if (--_exposure[key] <= 0) _exposure.Remove(key);
    }

    internal void Fire(Vector3 from, Vector3 forward)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = ports.FindTarget(from, Rac1ArsenalHostPolicy.MorphRange, .5f);
        if (target is null)
        {
            var end = from + forward * 3;
            var hit = ports.Contact(from, end, .01f);
            beams.DrawMorph(0, from, hit?.Point ?? end);
            return;
        }
        beams.DrawMorph(0, from, target.Root.GlobalPosition + Vector3.Up * .5f);
        int progress = _exposure.GetValueOrDefault(target.Source.InstanceIndex) + 2;
        _exposure[target.Source.InstanceIndex] = progress;
        if (progress >= Rac1ArsenalHostPolicy.MorphExposureTicks) Morph(target);
    }

    internal void Morph(RuntimeWorldScene.DynamicObjectNode target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_chickens.ContainsKey(target.Source.InstanceIndex) ||
            ports.Enemy(target.Source.InstanceIndex) is not { } enemy) return;

        var view = ports.CreateView(270);
        view.Reparent(target.Root, false); view.Position = Vector3.Up * view.FootOffset;
        view.Scale = Vector3.One / target.Root.Scale;
        foreach (var mesh in target.Root.GetChildren().OfType<MeshInstance3D>()) mesh.Visible = false;
        ports.Extinguish(enemy); enemy.Reaction = null;
        var collider = enemy.Body.GetChild<CollisionShape3D>(0);
        var originalShape = collider.Shape; var originalPosition = collider.Position;
        collider.Shape = new CapsuleShape3D { Radius = .24f, Height = .65f }; collider.Position = Vector3.Up * .325f;
        _chickens.Add(target.Source.InstanceIndex, new(enemy, view, originalShape, originalPosition));
        _exposure.Remove(target.Source.InstanceIndex);
        ports.Flash(target.Root.GlobalPosition + Vector3.Up * .5f, 27, new(.4f, 1, .3f), 1, 12);
    }

    internal void TickChickens()
    {
        if (_disposed) return;
        foreach (var pair in _chickens.ToArray())
        {
            var chicken = pair.Value;
            if (!ports.IsActive(chicken.Enemy) || chicken.Enemy.RewardEmitted)
            { chicken.View.QueueFree(); _chickens.Remove(pair.Key); continue; }
            if (chicken.Enemy.NeedsGroundPlacement)
            {
                if (!ports.TryGround(chicken.Enemy)) continue;
                chicken.Enemy.NeedsGroundPlacement = false;
            }
            chicken.Age++; var body = chicken.Enemy.Body;
            if (chicken.Age >= 0 && chicken.Age % 90 == 1)
                chicken.Direction = ports.Forward().Rotated(Vector3.Up, ports.SignedRandom(0, MathF.PI));
            bool supported = ports.Ground(body.GlobalPosition + chicken.Direction * .5f, .4f, .8f) is not null;
            body.Velocity = (supported ? chicken.Direction : Vector3.Zero) * .8f + Vector3.Down * 2;
            body.MoveAndSlide(); chicken.Enemy.Node.Root.GlobalPosition = body.GlobalPosition;
            chicken.View.Play(1); Rac1ArsenalViewPose.Orient(chicken.View, chicken.Direction);
            chicken.View.Rotation -= new Vector3(0, chicken.Enemy.Node.Root.Rotation.Y, 0); chicken.View.Step();
        }
    }

    internal void Reset()
    {
        if (_disposed) return;
        foreach (var chicken in _chickens.Values)
        {
            if (GodotObject.IsInstanceValid(chicken.View)) chicken.View.QueueFree();
            if (!GodotObject.IsInstanceValid(chicken.Enemy.Node.Root) ||
                !GodotObject.IsInstanceValid(chicken.Enemy.Body)) continue;
            // Original rule: materialless source meshes stay hidden after restore.
            foreach (var mesh in chicken.Enemy.Node.Root.GetChildren().OfType<MeshInstance3D>())
                mesh.Visible = mesh.MaterialOverride is not null;
            var collider = chicken.Enemy.Body.GetChild<CollisionShape3D>(0);
            collider.Shape = chicken.OriginalShape; collider.Position = chicken.OriginalShapePosition;
        }
        _chickens.Clear();
        _exposure.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
