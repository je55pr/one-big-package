using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

/// <summary>
/// The only owner of a mounted cab's rendered hull, collision and native
/// hover/yaw presentation. Native path/session logic lives in the RAC1 model.
/// </summary>
internal sealed class Rac1HostedTaxiCab : IDisposable
{
    private readonly Rac1TaxiNativeYawSeek _yaw;
    private readonly Rac1TaxiNativeHover? _hover;
    private bool _disposed;

    private Rac1HostedTaxiCab(Rac1MetropolisTaxiStop stop,
        RuntimeWorldScene.DynamicObjectNode node, StaticBody3D hull)
    {
        Stop = stop;
        Node = node;
        Hull = hull;
        _yaw = new(node.Root.GlobalRotation.Y);
        if (stop.NativeHoverPhases is { } phases)
            _hover = new(phases);
        if (stop.ApproachPath is { Count: > 1 })
            Node.Root.GlobalPosition = Rac1MetropolisTaxiHost.FromTaxiPoint(stop.ApproachPath[0]);
    }

    internal Rac1MetropolisTaxiStop Stop { get; }
    internal RuntimeWorldScene.DynamicObjectNode Node { get; }
    internal Node3D Root => Node.Root;
    internal StaticBody3D Hull { get; }
    internal bool HasHover => _hover is not null;
    internal Vector3 Position => Node.Root.GlobalPosition;
    internal bool Valid => !_disposed && GodotObject.IsInstanceValid(Node.Root);
    internal bool HullValid => Valid && GodotObject.IsInstanceValid(Hull) && !Hull.IsQueuedForDeletion();
    internal uint CollisionLayer { get => Hull.CollisionLayer; set => Hull.CollisionLayer = value; }

    internal static Rac1HostedTaxiCab? TryMount(Rac1MetropolisTaxiStop stop,
        RuntimeWorldScene.DynamicObjectNode node)
    {
        // Importer Mobies are deliberately visual-only. Taxi platforms need
        // real authored mesh collision, never an inferred placeholder plane.
        var hull = new StaticBody3D { Name = "KerwanTaxiHull" };
        try
        {
            foreach (var mesh in node.Root.GetChildren().OfType<MeshInstance3D>())
            {
                if (mesh.Mesh is not { } surface || surface.GetSurfaceCount() == 0)
                    continue;
                hull.AddChild(new CollisionShape3D
                {
                    Name = "TaxiHullSurface",
                    Shape = surface.CreateTrimeshShape(),
                    Transform = mesh.Transform,
                });
            }
            if (hull.GetChildCount() == 0)
            {
                hull.Free();
                return null;
            }
            node.Root.AddChild(hull);
            return new(stop, node, hull);
        }
        catch
        {
            if (GodotObject.IsInstanceValid(hull))
                hull.QueueFree();
            throw;
        }
    }

    internal void AdvanceHover(double delta) => _hover?.Advance(delta);

    internal Vector3 Present(Vector3 target, double delta)
    {
        if (!Valid) return target;
        var root = Node.Root;
        Vector3 motion = target - root.GlobalPosition;
        motion.Y = 0;
        float distanceSquared = motion.LengthSquared();
        double heading = distanceSquared is >= 0.0001f and <= 100f
            ? Rac1TaxiHostPresentationPolicy.SceneVisualYaw(motion.X, motion.Z)
            : _yaw.HeadingRadians;
        _yaw.Advance(heading, delta);
        // Preserve the native host's two-stage Godot rotation update. The
        // intermediate Euler decomposition matters when the cab is pitched.
        root.GlobalRotation = new(root.GlobalRotation.X, (float)_yaw.HeadingRadians,
            root.GlobalRotation.Z);
        if (_hover is not null)
        {
            target.Y += (float)_hover.HeightOffset;
            Vector3 rotation = root.GlobalRotation;
            root.GlobalRotation = new((float)_hover.PitchRadians,
                rotation.Y, (float)_hover.RollRadians);
        }
        root.GlobalPosition = target;
        return target;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(Hull) && !Hull.IsQueuedForDeletion())
            Hull.QueueFree();
    }
}