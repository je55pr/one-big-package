using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>Novalis's existing crank followers, separate from the shared crank interaction.</summary>
internal sealed class Rac1NovalisCrankMechanism(
    Rac1AuthoredMechanismPorts ports, Action<int> completedCrank) : Rac1AuthoredMechanism
{
    private readonly Rac1NovalisCrankDoorSession _session = new();
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _doors = [];
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _caveDoors = [];
    private readonly Dictionary<int, StaticBody3D> _colliders = [];
    private readonly Dictionary<int, Vector3> _originalPositions = [];
    private readonly Dictionary<int, Vector3> _originalRotations = [];

    internal override void Configure(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        if (world.LevelId != Rac1NovalisCrankDoorSession.NativeLevelId) return;
        foreach (var source in world.DynamicObjects ?? [])
            if (source.NativeClassId == Rac1BoltCrankSession.NativeClass)
                _session.RegisterCrank(source.InstanceIndex);

        foreach (var source in world.DynamicObjects ?? [])
        {
            if (source.NativeClassId != Rac1NovalisCrankDoorSession.DoorClass) continue;
            var pvar = source.NativePayloads?.SingleOrDefault(item => item.Format == "rac1-pvar")?.Data
                ?? throw new InvalidDataException("Novalis crank door has no authored PVar.");
            var matrix = source.Transform.Matrix;
            var native = new NVector3((float)matrix[12], (float)matrix[14], (float)matrix[13]);
            var door = Rac1NovalisCrankDoorSession.DecodeDoor(source.InstanceIndex, pvar, native);
            _session.RegisterDoor(door);
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException("Novalis crank door lacks its imported visual.");
            _doors.Add(source.InstanceIndex, node);
            _originalPositions.Add(source.InstanceIndex, node.Root.Position);
            AddCollider(source.InstanceIndex, node);
        }
        foreach (var source in world.DynamicObjects ?? [])
        {
            if (source.NativeClassId != Rac1NovalisCrankDoorSession.CaveDoorClass) continue;
            byte[] pvar = source.NativePayloads?.SingleOrDefault(item => item.Format == "rac1-pvar")?.Data
                ?? throw new InvalidDataException("Novalis cave door has no authored PVar.");
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException("Novalis cave door has no imported visual.");
            var cave = Rac1NovalisCrankDoorSession.DecodeCaveDoor(source.InstanceIndex,
                pvar, node.Root.Rotation.Y);
            _session.RegisterCaveDoor(cave);
            _caveDoors.Add(source.InstanceIndex, node);
            _originalRotations.Add(source.InstanceIndex, node.Root.Rotation);
            AddCollider(source.InstanceIndex, node);
        }
        _session.ValidateLinks();
        GD.Print($"[rac1-crank] Novalis registered {_session.CrankCount} cranks, " +
                 $"{_session.DoorCount} linked door halves and {_session.CaveDoorCount} rotating cave door(s)");
    }

    private void AddCollider(int id, RuntimeWorldScene.DynamicObjectNode node)
    {
        var meshes = node.Root.GetChildren().OfType<MeshInstance3D>().ToArray();
        if (meshes.Length == 0) return;
        // Mesh AABBs are mesh-local. Account for each child mesh's transform
        // before placing a single enclosing box in the door root's space.
        Aabb bounds = default;
        bool hasCorner = false;
        foreach (var mesh in meshes)
        {
            Aabb local = mesh.GetAabb();
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        var corner = new Vector3(
                            x == 0 ? local.Position.X : local.End.X,
                            y == 0 ? local.Position.Y : local.End.Y,
                            z == 0 ? local.Position.Z : local.End.Z);
                        Vector3 rootCorner = mesh.Transform * corner;
                        if (!hasCorner) { bounds = new Aabb(rootCorner, Vector3.Zero); hasCorner = true; }
                        else bounds = bounds.Expand(rootCorner);
                    }
        }
        if (bounds.Size.LengthSquared() < .001f) return;
        var collider = new StaticBody3D { Name = "NovalisDoorHostCollider", CollisionLayer = 1, CollisionMask = 1 };
        collider.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = bounds.Size },
            Position = bounds.GetCenter(),
        });
        node.Root.AddChild(collider);
        _colliders.Add(id, collider);
    }

    internal void ApplyCrankProgress(int crankId, float previous, float progress)
    {
        _session.SetProgress(crankId, progress);
        if (progress > previous && previous <= 0)
        {
            var movingDoor = _session.Doors.FirstOrDefault(item => item.CrankInstanceIndex == crankId);
            if (movingDoor != default)
                ports.PlaySourceSound(Rac1NovalisCrankDoorSession.DoorClass, 0, _doors[movingDoor.InstanceIndex].Root.GlobalPosition);
        }
        foreach (var door in _session.Doors.Where(door => door.CrankInstanceIndex == crankId))
        {
            NVector3 target = _session.DoorPosition(door.InstanceIndex);
            _doors[door.InstanceIndex].Root.GlobalPosition = new(-target.X, target.Z, target.Y);
            if (_colliders.TryGetValue(door.InstanceIndex, out var collider))
                collider.CollisionLayer = progress >= .98f ? 0u : 1u;
        }
        foreach (var cave in _session.CaveDoors.Where(item => item.CrankInstanceIndex == crankId))
        {
            var node = _caveDoors[cave.InstanceIndex];
            Vector3 caveRotation = node.Root.Rotation;
            caveRotation.Y = _session.CaveDoorYaw(cave.InstanceIndex);
            node.Root.Rotation = caveRotation;
        }
        if (progress >= 1 && previous < 1)
        {
            completedCrank(crankId);
            var door = _session.Doors.FirstOrDefault(item => item.CrankInstanceIndex == crankId);
            if (door != default)
                ports.PlaySourceSound(Rac1NovalisCrankDoorSession.DoorClass, 1, _doors[door.InstanceIndex].Root.GlobalPosition);
            ports.ReportCombatStatus($"Novalis crank {crankId} opened its paired doors");
        }
    }

    public override void Dispose()
    {
        foreach (var (id, collider) in _colliders)
        {
            if (!GodotObject.IsInstanceValid(collider)) continue;
            collider.CollisionLayer = 0;
            if (collider.GetParent() is { } parent) parent.RemoveChild(collider);
            collider.Free();
        }
        foreach (var (id, position) in _originalPositions)
            if (GodotObject.IsInstanceValid(_doors[id].Root)) _doors[id].Root.Position = position;
        foreach (var (id, rotation) in _originalRotations)
            if (GodotObject.IsInstanceValid(_caveDoors[id].Root)) _caveDoors[id].Root.Rotation = rotation;
        _colliders.Clear();
        _doors.Clear();
        _caveDoors.Clear();
        _originalPositions.Clear();
        _originalRotations.Clear();
    }
}
