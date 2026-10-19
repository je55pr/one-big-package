using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OneBigPackage;

public partial class OBPGame
{
    // OBP host policy until crank engagement/turn cadence and door easing are
    // recovered. The authored link/target positions themselves are retail data.
    private const float Rac1CrankHostReach = 2.5f;
    private const double Rac1CrankHostProgressPerTick = 1d / 100d;
    private Rac1NovalisCrankDoorSession _rac1NovalisCranks = new();
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _rac1NovalisCrankNodes = [];
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _rac1NovalisDoorNodes = [];
    private readonly Dictionary<int, StaticBody3D> _rac1NovalisDoorColliders = [];

    private void ResetRac1NovalisCranks()
    {
        _rac1NovalisCranks = new();
        _rac1NovalisCrankNodes.Clear();
        _rac1NovalisDoorNodes.Clear();
        _rac1NovalisDoorColliders.Clear();
    }

    private void ConfigureRac1NovalisCranks(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        if (world.LevelId != Rac1NovalisCrankDoorSession.NativeLevelId) return;
        foreach (var source in world.DynamicObjects ?? [])
        {
            if (source.NativeClassId != Rac1NovalisCrankDoorSession.CrankClass) continue;
            _rac1NovalisCranks.RegisterCrank(source.InstanceIndex);
            var node = FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException("Novalis bolt crank lacks its imported visual.");
            _rac1NovalisCrankNodes.Add(source.InstanceIndex, node);
        }

        foreach (var source in world.DynamicObjects ?? [])
        {
            if (source.NativeClassId != Rac1NovalisCrankDoorSession.DoorClass) continue;
            var pvar = source.NativePayloads?.SingleOrDefault(item => item.Format == "rac1-pvar")?.Data
                ?? throw new InvalidDataException("Novalis crank door has no authored PVar.");
            var matrix = source.Transform.Matrix;
            var native = new NVector3((float)matrix[12], (float)matrix[14], (float)matrix[13]);
            var door = Rac1NovalisCrankDoorSession.DecodeDoor(source.InstanceIndex, pvar, native);
            _rac1NovalisCranks.RegisterDoor(door);
            var node = FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException("Novalis crank door lacks its imported visual.");
            _rac1NovalisDoorNodes.Add(source.InstanceIndex, node);
            AddRac1NovalisDoorCollider(source.InstanceIndex, node);
        }
        _rac1NovalisCranks.ValidateLinks();
        GD.Print($"[rac1-crank] Novalis registered {_rac1NovalisCranks.CrankCount} cranks, " +
                 $"{_rac1NovalisCranks.DoorCount} linked door halves");
    }

    private void AddRac1NovalisDoorCollider(int id, RuntimeWorldScene.DynamicObjectNode node)
    {
        var meshes = node.Root.GetChildren().OfType<MeshInstance3D>().ToArray();
        if (meshes.Length == 0) return;
        Aabb bounds = meshes[0].GetAabb();
        foreach (var mesh in meshes.Skip(1)) bounds = bounds.Merge(mesh.GetAabb());
        if (bounds.Size.LengthSquared() < .001f) return;
        var collider = new StaticBody3D { Name = "NovalisDoorHostCollider", CollisionLayer = 1, CollisionMask = 1 };
        collider.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = bounds.Size },
            Position = bounds.GetCenter(),
        });
        node.Root.AddChild(collider);
        _rac1NovalisDoorColliders.Add(id, collider);
    }

    /// <summary>
    /// Hold Square at a crank and move the left stick. Stick-turn progress and
    /// collision bounds are provisional Godot adapters; linkage and endpoints
    /// are decoded from the original Novalis objects.
    /// </summary>
    private bool TryTurnRac1NovalisCrank(bool actionHeld, Vector2 stick)
    {
        if (!actionHeld || _world is not { Game: "rac1", LevelId: 1 } ||
            _player is null || _rac1Nanotech.Probe().IsDead)
            return false;

        var closest = _rac1NovalisCrankNodes.Values
            .OrderBy(node => node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition))
            .FirstOrDefault();
        if (closest is null ||
            closest.Root.GlobalPosition.DistanceTo(_player.GlobalPosition) > Rac1CrankHostReach)
            return false;

        int crankId = closest.Source.InstanceIndex;
        // Direction of the native crank is not yet proven. Require stick motion
        // rather than letting a single Square tap instantaneously open the door.
        float magnitude = Mathf.Clamp(stick.Length(), 0f, 1f);
        if (magnitude > .2f)
        {
            double previous = _rac1NovalisCranks.Progress(crankId);
            bool starting = previous == 0d;
            double progress = _rac1NovalisCranks.Turn(crankId, Rac1CrankHostProgressPerTick * magnitude);
            if (starting)
                PlayRac1PickupSound(Rac1NovalisCrankDoorSession.CrankClass, 0, closest.Root.GlobalPosition);
            foreach (var door in _rac1NovalisCranks.Doors.Where(door => door.CrankInstanceIndex == crankId))
            {
                NVector3 target = _rac1NovalisCranks.DoorPosition(door.InstanceIndex);
                _rac1NovalisDoorNodes[door.InstanceIndex].Root.GlobalPosition =
                    new Vector3(-target.X, target.Z, target.Y);
                if (_rac1NovalisDoorColliders.TryGetValue(door.InstanceIndex, out var collider) &&
                    progress >= .98d)
                    collider.CollisionLayer = 0;
            }
            if (progress >= 1d)
            {
                if (previous < 1d)
                {
                    var door = _rac1NovalisCranks.Doors.First(item => item.CrankInstanceIndex == crankId);
                    PlayRac1PickupSound(Rac1NovalisCrankDoorSession.DoorClass, 0,
                        _rac1NovalisDoorNodes[door.InstanceIndex].Root.GlobalPosition);
                }
                _rac1CombatStatus = $"Novalis crank {crankId} opened its paired doors";
            }
        }
        return true;
    }
}
