using Godot;
using OBP.RAC1.Gameplay;
using OBP.Godot;

namespace OneBigPackage;

/// <summary>
/// Dynamic class1021 child presentation for the original Nebula G34 Queen
/// linked parents. Uses real mounted-disc class1021 mesh/material surfaces;
/// spawned children never enter the authored World or generic enemy census.
/// The per-level native radial offset and angle constants are read from
/// the original overlay. Collision/camera event producers remain separate.
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
        if (_rac1QueenLinkedGate is null || _rac1QueenGateNative is null) return;
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
                // Native child state1 offsets original position along its
                // rotation basis row2, normalized to D_L06_00161D48=3.109.
                root.GlobalPosition += root.GlobalTransform.Basis.Z.Normalized() *
                    _rac1QueenGateNative.ChildRadialDistance;
                _rac1QueenLinkedChildVisuals.Add(member.RuntimeId, root);
            }
        }
        GD.Print($"[frog-queen] {_rac1QueenLinkedChildVisuals.Count} runtime class1021 retail-mesh child visuals from two source parent Mobies");
    }

    private void TickRac1QueenLinkedChildren()
    {
        if (_rac1QueenLinkedGate is null || _rac1QueenGateNative is null) return;
        // The original class1021 parents use Ratchet's *current* native position
        // in state2, not their own source position. The source global starts at
        // zero; no other producer of D_L06_00161D44 is hosted yet.
        bool initialGlobalAdmits = _rac1QueenGateNative.InitialProximityGate == 0f;
        bool hasPlayer = _player is not null && IsInstanceValid(_player);
        var playerNative = hasPlayer ? ToEffectNative(_player!.GlobalPosition) : default;
        foreach (int id in _rac1QueenLinkedGate.OriginalParentIds)
        {
            var parent = _rac1QueenLinkedGate.Get(id);
            if (parent.NativeState is 2 or 4 or 8)
                _rac1QueenLinkedGate.StepNative(id, playerNative,
                    hasPlayer && initialGlobalAdmits, _rac1QueenGateNative);
            foreach (var member in _rac1QueenLinkedGate.Get(id).Children)
            {
                if (!_rac1QueenLinkedChildVisuals.TryGetValue(member.RuntimeId, out var visual) ||
                    !IsInstanceValid(visual)) continue;
                if (member.NativeState == 1)
                    _rac1QueenLinkedGate.ConfirmChildInitialized(member.RuntimeId);
                else
                    _rac1QueenLinkedGate.StepChildNative(member.RuntimeId, _rac1QueenGateNative);
            }
        }
        SynchronizeRac1QueenLinkedParents();
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
                    visual.Quaternion = _rac1QueenLinkedBaseRotations[id] *
                        new Quaternion(Vector3.Up,
                            member.RotationOffset - member.RelativeYawRadians);
                }
    }
}
