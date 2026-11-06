using Godot;
using OBP.RAC1.Gameplay;
using static OBP.Game.Rac1EnemyCoordinates;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>
/// One Veldin shooting robot's recovered effects: the class-1471 plasma beam
/// while it fires and the always-drawn glow billboard at its second socket.
/// Native rules live in <see cref="Rac1BeamSlot"/>; this owns only Godot nodes.
/// </summary>
internal sealed class Rac1EnemyBeamView : IDisposable
{
    private readonly Rac1FxMaterials _materials;
    private readonly ImmediateMesh _mesh = new();
    private readonly MeshInstance3D _node;
    private Rac1BeamSlot? _slot;
    private NVector3? _glowSocket;
    private bool _disposed;

    internal Rac1EnemyBeamView(Node3D sceneRoot, Rac1FxMaterials materials, string name)
    {
        _materials = materials;
        // World-space vertices: the node ignores its parent's transform.
        _node = new MeshInstance3D { Name = name, Mesh = _mesh, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        sceneRoot.AddChild(_node);
    }

    internal bool Firing => _slot is not null;

    /// <summary>Native Z-up muzzle (socket 0) and clipped endpoint, once per native tick while the robot is in its firing state.</summary>
    internal void StepBeam(NVector3 origin, NVector3 endpoint, Rac1PickupRandom random)
    {
        _slot ??= new();
        _slot.Step(origin, endpoint, random);
    }

    internal void StopBeam() => _slot = null;

    internal void SetGlow(NVector3? socketOne) => _glowSocket = socketOne;

    /// <summary>Rebuilds the quad mesh for the camera. Call after the tick's Step/Set calls.</summary>
    internal void Present(Vector3 eye, Rac1PickupRandom random)
    {
        if (_disposed) return;
        NVector3 nativeEye = ToEffectNative(eye);
        var surfaces = new List<Rac1FxSurface>();
        if (_slot is not null) surfaces.AddRange(_slot.Build(nativeEye, random));
        if (_glowSocket is { } socket) surfaces.Add(Rac1BeamSlot.RobotGlow(socket, nativeEye));
        _materials.Write(_mesh, surfaces, FromEffectNative);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_node) && !_node.IsQueuedForDeletion()) _node.QueueFree();
        _slot = null; _glowSocket = null;
    }
}
