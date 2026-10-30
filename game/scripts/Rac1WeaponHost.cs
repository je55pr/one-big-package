using OBP.Runtime;
using Godot;

namespace OBP.Game;

/// <summary>
/// Per-R&C1-world weapon and gadget composition. Persistent inventory is
/// borrowed through player equipment ports, not owned or reset here.
/// Combat contact/arsenal family migration (#367/#368) joins this host next.
/// </summary>
internal sealed class Rac1WeaponHost : IDisposable
{
    private bool _disposed;
    private long _arsenalId;

    internal Rac1WeaponHost(Node3D sceneRoot, Rac1MetalDetectorPorts detector, Rac1WeaponTargetPorts targets,
        Rac1WeaponContactPhysicsPorts physics, Rac1SuckCannonPorts suckCannon,
        Rac1ArsenalMissilePorts missiles, Rac1ArsenalDeployablePorts deployables,
        Rac1WalloperPorts walloper, Rac1TaunterPorts taunter,
        Rac1TeslaPorts tesla, Rac1MorphPorts morph, Rac1ArsenalBeamPorts beams)
    {
        GuidedCamera = new();
        ThrownAnimation = new();
        Detector = new(detector);
        Contacts = new(targets, physics);
        SuckCannon = new(suckCannon);
        Missiles = new(missiles, GuidedCamera, NextArsenalId);
        Taunter = new(taunter, Contacts);
        Beams = new(sceneRoot, beams);
        Tesla = new(tesla, Contacts, Beams);
        Morph = new(morph, Beams);
        Deployables = new(deployables, NextArsenalId, Taunter);
        Walloper = new(walloper, Contacts);
    }

    internal Rac1MetalDetectorHost Detector { get; }
    internal Rac1WeaponContactHost Contacts { get; }
    internal Rac1SuckCannonHost SuckCannon { get; }
    internal Rac1ArsenalMissileHost Missiles { get; }
    internal Rac1ArsenalDeployableHost Deployables { get; }
    internal Rac1WalloperHost Walloper { get; }
    internal Rac1TaunterHost Taunter { get; }
    internal Rac1TeslaHost Tesla { get; }
    internal Rac1MorphHost Morph { get; }
    internal Rac1ArsenalBeamPresenter Beams { get; }
    internal Rac1GuidedWeaponCamera GuidedCamera { get; }
    internal Rac1ThrownEquipmentAnimationHost ThrownAnimation { get; }

    // Shared, world-owned presentation generation for missile launches and
    // decoy fold/landing animation. It survives preview resets in this world.
    internal long LastArsenalId => _arsenalId;

    internal long NextArsenalId()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ++_arsenalId;
    }

    internal void Configure(RuntimeWorld world)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Detector.Configure(world);
    }

    internal void Tick(double delta)
    {
        if (_disposed) return;
        Detector.Tick(delta);
    }

    internal void ResetWorld()
    {
        if (_disposed) return;
        // Release camera/input before the player/world lease is retired.
        GuidedCamera.End();
        ThrownAnimation.Reset();
        Detector.Reset();
        Contacts.Invalidate();
        SuckCannon.Reset();
        Missiles.Reset();
        Deployables.Reset();
        Walloper.Reset();
        Taunter.Reset();
        Tesla.Reset();
        Morph.Reset();
        Beams.Reset();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Walloper.Dispose();
        Deployables.Dispose();
        Beams.Dispose();
        Morph.Dispose();
        Tesla.Dispose();
        Taunter.Dispose();
        Missiles.Dispose();
        GuidedCamera.Dispose();
        ThrownAnimation.Dispose();
        Detector.Dispose();
        Contacts.Dispose();
        SuckCannon.Dispose();
    }
}
