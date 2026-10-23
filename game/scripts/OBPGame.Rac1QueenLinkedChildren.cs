using Godot;
using OBP.RAC1.Gameplay;
using OBP.Godot;

namespace OneBigPackage;

/// <summary>
/// Dynamic class1021 child presentation for the original Nebula G34 Queen
/// linked parents. Uses real mounted-disc class1021 mesh/material surfaces;
/// spawned children never enter the authored World or generic enemy census.
/// The native global radial-offset and angular acceleration are not recovered:
/// initial source transforms and state/visibility are hosted, no invented
/// outward motion or collision is applied.
/// </summary>
public partial class OBPGame
{
    private readonly Dictionary<int, Node3D> _rac1QueenLinkedChildVisuals = [];

    private void ResetRac1QueenLinkedChildren()
    {
        foreach (var child in _rac1QueenLinkedChildVisuals.Values)
            if (IsInstanceValid(child)) child.QueueFree();
        _rac1QueenLinkedChildVisuals.Clear();
    }

    private void ConfigureRac1QueenLinkedChildren(Node3D sceneRoot)
    {
        if (_rac1QueenLinkedGate is null) return;
        foreach (int id in _rac1QueenLinkedGate.OriginalParentIds)
        {
            var donor = _rac1QueenLinkedNodes[id].Root;
            var sourceSurfaces = donor.GetChildren().OfType<MeshInstance3D>().ToArray();
            if (sourceSurfaces.Length == 0)
                throw new InvalidDataException($"Original class1021 parent {id} has no retail mesh.");
            var native = _rac1QueenLinkedGate.Get(id).NativePosition;
            // One initial native tick spawns seven new class1021 Mobies.
            _rac1QueenLinkedGate.Step(id, native, false, 0);
            foreach (var member in _rac1QueenLinkedGate.Get(id).Children)
            {
                var root = new Node3D
                {
                    Name = $"QueenGate_class1021_runtime{member.RuntimeId}_owner{id}_slot{member.Slot}"
                };
                foreach (var mesh in sourceSurfaces)
                {
                    var copy = new MeshInstance3D
                    {
                        Name = $"RetailClass1021_{mesh.Name}",
                        Mesh = mesh.Mesh,
                        MaterialOverride = mesh.MaterialOverride,
                        Transform = mesh.Transform,
                        CastShadow = mesh.CastShadow
                    };
                    if (mesh.Mesh is { } data)
                        for (int surface = 0; surface < data.GetSurfaceCount(); surface++)
                            copy.SetSurfaceOverrideMaterial(surface,
                                mesh.GetSurfaceOverrideMaterial(surface));
                    root.AddChild(copy);
                }
                sceneRoot.AddChild(root);
                root.GlobalTransform = donor.GlobalTransform;
                // Godot scene X is mirrored against retail native X.
                root.RotateObjectLocal(Vector3.Up, -member.RelativeYawRadians);
                _rac1QueenLinkedChildVisuals.Add(member.RuntimeId, root);
            }
        }
        GD.Print($"[frog-queen] {_rac1QueenLinkedChildVisuals.Count} runtime class1021 retail-mesh child visuals from two source parent Mobies");
    }

    private void TickRac1QueenLinkedChildren()
    {
        if (_rac1QueenLinkedGate is null) return;
        foreach (int id in _rac1QueenLinkedGate.OriginalParentIds)
        {
            foreach (var member in _rac1QueenLinkedGate.Get(id).Children)
            {
                if (member.NativeState == 1 &&
                    _rac1QueenLinkedChildVisuals.TryGetValue(member.RuntimeId, out var visual) &&
                    IsInstanceValid(visual))
                    _rac1QueenLinkedGate.StepChild(member.RuntimeId, 0);
            }
        }
        SynchronizeRac1QueenLinkedChildVisibility();
    }

    private void SynchronizeRac1QueenLinkedChildVisibility()
    {
        if (_rac1QueenLinkedGate is null) return;
        foreach (int id in _rac1QueenLinkedGate.OriginalParentIds)
            foreach (var member in _rac1QueenLinkedGate.Get(id).Children)
                if (_rac1QueenLinkedChildVisuals.TryGetValue(member.RuntimeId, out var visual) &&
                    IsInstanceValid(visual))
                {
                    visual.Visible = member.Visible;
                    // Shared native updater subtracts the rotating offset
                    // from each child's original yaw, mirrored in scene X.
                    visual.Quaternion = _rac1QueenLinkedNodes[id].Root.Quaternion *
                        new Quaternion(Vector3.Up,
                            member.RotationOffset - member.RelativeYawRadians);
                }
    }
}
