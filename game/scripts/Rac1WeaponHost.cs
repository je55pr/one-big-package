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
    private ulong _worldTick;

    internal Rac1WeaponHost(Node3D sceneRoot, Rac1MetalDetectorPorts detector, Rac1WeaponTargetPorts targets,
        Rac1WeaponContactPhysicsPorts physics, Rac1SuckCannonPorts suckCannon,
        Rac1ArsenalMissilePorts missiles, Rac1ArsenalDeployablePorts deployables,
        Rac1WalloperPorts walloper, Rac1TaunterPorts taunter,
        Rac1TeslaPorts tesla, Rac1MorphPorts morph, Rac1ArsenalBeamPorts beams, Rac1ArsenalUsePorts arsenal,
        Rac1EquipmentProjectilePorts projectiles, Rac1PyrocitorFlamePorts flames)
    {
        GuidedCamera = new();
        ThrownAnimation = new();
        Detector = new(detector);
        Contacts = new(targets, physics);
        Projectiles = new(Contacts, projectiles);
        Flames = new(Contacts, flames);
        SuckCannon = new(suckCannon);
        Missiles = new(missiles, GuidedCamera, NextArsenalId);
        Taunter = new(taunter, Contacts);
        Beams = new(sceneRoot, beams);
        Tesla = new(tesla, Contacts, Beams);
        Morph = new(morph, Beams);
        Deployables = new(deployables, NextArsenalId, Taunter);
        Walloper = new(walloper, Contacts);
        Arsenal = new(this, arsenal);
    }

    internal Rac1MetalDetectorHost Detector { get; }
    internal Rac1WeaponContactHost Contacts { get; }
    internal Rac1EquipmentProjectileHost Projectiles { get; }
    internal Rac1PyrocitorFlameHost Flames { get; }
    internal Rac1SuckCannonHost SuckCannon { get; }
    internal Rac1ArsenalMissileHost Missiles { get; }
    internal Rac1ArsenalDeployableHost Deployables { get; }
    internal Rac1WalloperHost Walloper { get; }
    internal Rac1TaunterHost Taunter { get; }
    internal Rac1TeslaHost Tesla { get; }
    internal Rac1MorphHost Morph { get; }
    internal Rac1ArsenalBeamPresenter Beams { get; }
    internal Rac1ArsenalUseHost Arsenal { get; }
    internal Rac1GuidedWeaponCamera GuidedCamera { get; }
    internal Rac1ThrownEquipmentAnimationHost ThrownAnimation { get; }

    // Shared, world-owned presentation generation for missile launches and
    // decoy fold/landing animation. It survives preview resets in this world.
    internal long LastArsenalId => _arsenalId;

    // Clock belongs to this world lease, never to the persistent application root.
    internal ulong WorldTick => _worldTick;

    internal void ResetWorldTick()
    {
        if (_disposed) return;
        _worldTick = 0;
    }

    // Preserve fixed-frame ordering across arsenal, emissions, contacts and projectiles.
    internal void StepWorld(bool pyroEmitting)
    {
        if (_disposed) return;
        _worldTick++;
        Arsenal.Tick();
        Flames.Emit(pyroEmitting);
        Projectiles.StepMotes();
        Flames.StepContacts();
        SuckCannon.Step();
        Projectiles.StepShots();
        Projectiles.StepDoomAgents();
        Projectiles.StepFlashes();
    }

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
        ResetWorldTick();
        GuidedCamera.End();
        Arsenal.ClearHeld();
        ThrownAnimation.Reset();
        Detector.Reset();
        Contacts.Invalidate();
        Projectiles.Reset();
        Flames.Reset();
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
        Arsenal.Dispose();
        Projectiles.Dispose();
        Flames.Dispose();
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
