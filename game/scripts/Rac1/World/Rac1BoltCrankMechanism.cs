using Godot;
using OBP.Godot;
using OBP.RAC1.Camera;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using NVector2 = System.Numerics.Vector2;

namespace OBP.Game;

/// <summary>Class-280 visuals and interaction within one RAC1 scene lease; followers are separate.</summary>
internal sealed class Rac1BoltCrankMechanism(
    Rac1AuthoredMechanismPorts ports, Action<int, float, float>? progressChanged) : Rac1AuthoredMechanism
{
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _cranks = [];
    private readonly Dictionary<int, Rac1BoltCrankSession> _controllers = [];
    private readonly Dictionary<int, Transform3D> _crankRest = [];
    private IReadOnlyDictionary<int, Rac1BoltCrankCameraDefinition> _cameras =
        new Dictionary<int, Rac1BoltCrankCameraDefinition>();
    private int? _engaged;

    internal IReadOnlyDictionary<int, RuntimeWorldScene.DynamicObjectNode> CrankNodes => _cranks;
    internal float Progress(int id) => _controllers[id].Progress;

    internal override void Configure(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        _cameras = Rac1BoltCrankCameraDefinition.Load(ports.SourcePath() ??
            throw new InvalidDataException("Bolt crank camera requires its registered retail source."), world.LevelId);
        foreach (var source in world.DynamicObjects ?? [])
        {
            if (source.NativeClassId != Rac1BoltCrankSession.NativeClass) continue;
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException("Bolt crank lacks its imported visual.");
            _cranks.Add(source.InstanceIndex, node);
            byte[] pvar = source.NativePayloads?.SingleOrDefault(item => item.Format == "rac1-pvar")?.Data
                ?? throw new InvalidDataException("Bolt crank has no authored PVar.");
            // Native Z yaw equals scene Y yaw: the native/OBP Y-Z swap and
            // ToSceneTransform's X conjugation each reverse the planar angle.
            _controllers.Add(source.InstanceIndex, new(pvar, node.Root.Rotation.Y));
            _crankRest.Add(source.InstanceIndex, node.Root.Transform);
        }
        GD.Print($"[rac1-crank] LEVEL{world.LevelId} registered {_cranks.Count} cranks with authored cameras");
    }

    /// <summary>
    /// Square engages/releases; camera-relative stick drives the native crank session.
    /// </summary>
    internal bool TryTurn(bool squarePressed, Vector2 stick, PlayerHost player)
    {
        // Placement/reset or a temporary host control owner ends this world interaction.
        if (_engaged is not null && !player.Rac1CrankActive) Release();
        foreach (var (id, controller) in _controllers)
        {
            controller.ObserveSquare(squarePressed);
            float previous = controller.Progress;
            controller.TickIdle();
            PresentCrank(id, previous);
        }
        if (!player.Rac1GameplayAlive || player.Rac1SwimmingActive || player.Rac1Hanging ||
            player.Rac1SwingshotActive || player.Rac1EquipmentActionActive || !ports.WrenchAvailable())
        {
            Release();
            return false;
        }
        if (_engaged is null)
        {
            if (!player.IsOnFloor()) return false;
            var closest = _cranks.Values
                .Where(node => _controllers[node.Source.InstanceIndex] is { CanEngage: true, SquareBuffered: true })
                .OrderBy(node => node.Root.GlobalPosition.DistanceSquaredTo(player.GlobalPosition))
                .FirstOrDefault();
            if (closest is null || closest.Root.GlobalPosition.DistanceTo(player.GlobalPosition) >= Rac1BoltCrankSession.Reach)
                return false;
            Vector3 center = closest.Root.GlobalPosition;
            Vector3 playerPosition = player.GlobalPosition;
            var controller = _controllers[closest.Source.InstanceIndex];
            var nativeCenter = new System.Numerics.Vector3(-center.X, center.Z, center.Y);
            var nativePlayer = new System.Numerics.Vector3(-playerPosition.X, playerPosition.Z, playerPosition.Y);
            // OBP contact approximation: the foot capsule can settle across the
            // native .3 support offset on imported geometry. Check both endpoints
            // of that host interval; the source distance/facing/height gates stay exact.
            var supportedOrigin = nativePlayer + System.Numerics.Vector3.UnitZ * Rac1BoltCrankSession.HeroOriginAboveSupport;
            if (!controller.CanEngageFrom(nativeCenter, nativePlayer, (float)player.Rac1CurrentYaw) &&
                !controller.CanEngageFrom(nativeCenter, supportedOrigin, (float)player.Rac1CurrentYaw)) return false;
            _engaged = closest.Source.InstanceIndex;
            if (!_cameras.TryGetValue(_engaged.Value, out var camera))
                throw new InvalidDataException("Bolt crank has no admitted authored camera.");
            player.BeginRac1CrankCamera(camera);
            _controllers[_engaged.Value].TryEngage();
        }

        int crankId = _engaged.Value;
        var crank = _controllers[crankId];
        var root = _cranks[crankId].Root;
        Vector3 position = player.GlobalPosition;
        NVector2 nativeStick = Rac1BoltCrankSession.ControlRelativeStick(new(stick.X, stick.Y), (float)player.Rac1CrankControlYaw);
        float previousProgress = crank.Progress;
        var frame = crank.Step(new(-root.GlobalPosition.X, root.GlobalPosition.Z), new(-position.X, position.Z),
            (float)player.Rac1CurrentYaw, nativeStick, crank.SquareBuffered,
            player.Rac1CrankAnimationSequence == Rac1BoltCrankSession.InsertSequence && player.Rac1TraversalAnimationCycleCompleted,
            player.Rac1WrenchAnimationBlending);
        PresentCrank(crankId, previousProgress);
        if (!crank.Active)
        {
            _engaged = null;
            return false;
        }
        player.ApplyRac1CrankFrame(frame);
        return true;
    }

    private void Release()
    {
        if (_engaged is int id) _controllers[id].Release();
        _engaged = null;
    }

    private void PresentCrank(int crankId, float previous)
    {
        var crank = _controllers[crankId];
        var root = _cranks[crankId].Root;
        Vector3 rotation = root.Rotation;
        rotation.Y = crank.Yaw;
        root.Rotation = rotation;
        Vector3 position = root.Position;
        position.Y = _crankRest[crankId].Origin.Y - crank.Lowering;
        root.Position = position;
        if (crank.Progress > previous && previous <= 0)
            ports.PlaySourceSound(Rac1BoltCrankSession.NativeClass, 0, root.GlobalPosition);
        progressChanged?.Invoke(crankId, previous, crank.Progress);
    }

    public override void Dispose()
    {
        Release();
        foreach (var (id, rest) in _crankRest)
            if (GodotObject.IsInstanceValid(_cranks[id].Root)) _cranks[id].Root.Transform = rest;
        _controllers.Clear();
        _crankRest.Clear();
        _cranks.Clear();
        _cameras = new Dictionary<int, Rac1BoltCrankCameraDefinition>();
    }
}
