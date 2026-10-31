using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Typed scene and contact adapters borrowed by the native Suck Cannon family.
/// Source inventory/cargo admission stays in Rac1AdditionalWeaponSession.
/// </summary>
internal sealed record Rac1SuckCannonPorts(
    Func<Rac1AdditionalWeaponSession?> Session,
    Func<bool> PlayerPresent,
    Func<Vector3> Muzzle,
    Func<Vector3> Forward,
    Func<ulong> WorldTick,
    Func<float, float, float> RandomSigned,
    Func<IEnumerable<RuntimeWorldScene.DynamicObjectNode>> EligibleTargets,
    Func<IEnumerable<RuntimeWorldScene.DynamicObjectNode>> PresentedTargets,
    Func<int, Node3D?> ChickenView,
    Func<Vector3, Vector3, bool> HasLineOfSight,
    Func<int, Color, float, Node3D> CreateParticle,
    Action<Node3D, Vector3, Vector3> EmitMote,
    Action<RuntimeWorldScene.DynamicObjectNode, Vector3> RetireCapturedTarget,
    Func<Node3D> SceneRoot,
    Func<int, Rac1EquipmentView> CreateWorldView,
    Func<Vector3, float, float, Vector3> Aim,
    Action<Rac1AdditionalWeaponSession.Shot, Node3D, Vector3, Vector3> LaunchCargo,
    Action<int, int, Vector3> PlayPickupSound,
    Action RefreshHud);

/// <summary>
/// Owns one world's temporary captured Moby meshes and Morph-o-Ray chicken
/// cargo metadata. Does not own the native inventory, saved cargo, projectiles
/// or damage authority. Preview and bookmark restore may retire presentation
/// without changing native cargo until the restore operation rebinds it.
/// </summary>
internal sealed class Rac1SuckCannonHost(Rac1SuckCannonPorts ports) : IDisposable
{
    private sealed record Capture(Rac1MobyRuntimeKey Key, Node3D Visual, Vector3 Origin, Vector3 Scale)
    {
        internal int Age;
    }

    private readonly List<Capture> _captures = [];
    private readonly Dictionary<Rac1MobyRuntimeKey, Node3D> _models = [];
    private readonly HashSet<Rac1MobyRuntimeKey> _chickenCargo = [];
    private bool _disposed;

    internal IReadOnlyList<Rac1MobyRuntimeKey> ChickenCargoKeys => [.. _chickenCargo];
    internal int ChickenCargoCount => _chickenCargo.Count;

    internal void ClearChickenCargo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _chickenCargo.Clear();
    }

    internal void RememberChickenCargo(Rac1MobyRuntimeKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _chickenCargo.Add(key);
    }

    internal void Reset()
    {
        RetirePresentation();
        _chickenCargo.Clear();
    }

    internal void RetirePresentation()
    {
        foreach (var node in _captures.Select(capture => capture.Visual).Concat(_models.Values).Distinct())
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        _captures.Clear();
        _models.Clear();
    }

    // Actor retirement may invalidate a capture during the same world tick.
    // Preserve the original semantics: the stale capture is no longer animated,
    // while its model is held until the family reset/launch cleanup.
    internal void ForgetCapture(Rac1MobyRuntimeKey key) =>
        _captures.RemoveAll(capture => capture.Key == key);

    internal void Step()
    {
        var session = ports.Session();
        if (_disposed || session is null || !ports.PlayerPresent()) return;
        var muzzle = ports.Muzzle();
        if (session.Vacuuming)
        {
            if (ports.WorldTick() % 3 == 0)
            {
                var mote = ports.CreateParticle(27, new(.25f, .7f, 1, .6f), .12f);
                var position = muzzle + ports.Forward() * 3 + new Vector3(
                    ports.RandomSigned(0, .8f), ports.RandomSigned(0, .6f), ports.RandomSigned(0, .8f));
                ports.EmitMote(mote, position, (muzzle - position) / 18);
            }
            // Bounded Veldin eligibility adapter. Native capture admission,
            // capacity and cargo identities remain in the recovered session.
            foreach (var target in ports.EligibleTargets().Where(node =>
                node.Source.NativeClassId is Rac1ClassIds.VeldinToad or Rac1ClassIds.WoodenCrate ||
                ports.ChickenView(node.Source.InstanceIndex) is not null).ToArray())
            {
                var point = target.Root.GlobalPosition + Vector3.Up * .4f;
                var offset = point - muzzle;
                if (offset.Length() > Rac1WeaponReconstructionPolicy.SuckCaptureRange ||
                    offset.LengthSquared() > .01f &&
                        offset.Normalized().Dot(ports.Forward()) < Rac1WeaponReconstructionPolicy.SuckCaptureConeDot ||
                    !ports.HasLineOfSight(muzzle, point)) continue;
                var key = new Rac1MobyRuntimeKey(target.Source.NativeClassId, target.Source.InstanceIndex);
                if (!session.TryCapture(key)) break;
                if (ports.ChickenView(target.Source.InstanceIndex) is not null) _chickenCargo.Add(key);
                var visual = CloneCargo(target);
                _models.Add(key, visual);
                _captures.Add(new(key, visual, visual.GlobalPosition, visual.Scale));
                ports.RetireCapturedTarget(target, point);
                ports.PlayPickupSound(849, 5, point);
                ports.RefreshHud();
            }
        }
        foreach (var capture in _captures.ToArray())
        {
            if (!GodotObject.IsInstanceValid(capture.Visual)) { _captures.Remove(capture); continue; }
            float amount = Math.Clamp(++capture.Age / (float)Rac1WeaponReconstructionPolicy.SuckCaptureTicks, 0, 1);
            float eased = amount * amount;
            capture.Visual.GlobalPosition = capture.Origin.Lerp(muzzle, eased);
            capture.Visual.Scale = capture.Scale * MathF.Max(.02f, 1 - eased);
            capture.Visual.RotateY(.15f);
            if (amount >= 1) { capture.Visual.Visible = false; _captures.Remove(capture); }
        }
    }

    private Node3D CloneCargo(RuntimeWorldScene.DynamicObjectNode source)
    {
        var key = new Rac1MobyRuntimeKey(source.Source.NativeClassId, source.Source.InstanceIndex);
        var chickenView = ports.ChickenView(source.Source.InstanceIndex);
        if (_chickenCargo.Contains(key) && chickenView is null)
        {
            var restored = ports.CreateWorldView(270);
            restored.GlobalPosition = source.Root.GlobalPosition;
            return restored;
        }
        var root = new Node3D { Name = $"Rac1SuckCargo_{source.Source.NativeClassId}_{source.Source.InstanceIndex}" };
        var visual = chickenView ?? source.Root;
        foreach (var mesh in visual.GetChildren().OfType<MeshInstance3D>())
            root.AddChild(new MeshInstance3D
            {
                Mesh = mesh.Mesh,
                MaterialOverride = mesh.MaterialOverride,
                Transform = source.Root.GlobalTransform.AffineInverse() * mesh.GlobalTransform
            });
        ports.SceneRoot().AddChild(root);
        root.GlobalTransform = source.Root.GlobalTransform;
        return root;
    }

    internal void Fire(Rac1AdditionalWeaponSession.Shot shot)
    {
        if (_disposed || shot.Cargo is not { } key) return;
        if (!_models.Remove(key, out var visual))
        {
            var target = ports.PresentedTargets().FirstOrDefault(node =>
                node.Source.NativeClassId == key.NativeClassId && node.Source.InstanceIndex == key.InstanceIndex);
            if (target is null) throw new InvalidDataException("Captured Moby is absent from the loaded level.");
            visual = CloneCargo(target);
        }
        _chickenCargo.Remove(key);
        var pending = _captures.FirstOrDefault(capture => capture.Key == key);
        if (pending is not null) _captures.Remove(pending);
        visual.Scale = pending?.Scale ?? Vector3.One;
        visual.Visible = true;
        visual.GlobalPosition = ports.Muzzle();
        var direction = (ports.Aim(visual.GlobalPosition, 30, .94f) - visual.GlobalPosition).Normalized();
        ports.LaunchCargo(shot, visual, visual.GlobalPosition, direction);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }
}
