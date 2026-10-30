using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// Per-R&C1-world weapon and gadget composition. Persistent inventory is
/// borrowed through player equipment ports, not owned or reset here.
/// Combat contact/arsenal family migration (#367/#368) joins this host next.
/// </summary>
internal sealed class Rac1WeaponHost(Rac1MetalDetectorPorts detector,
    Rac1WeaponTargetPorts targets, Rac1WeaponContactPhysicsPorts physics,
    Rac1SuckCannonPorts suckCannon) : IDisposable
{
    private bool _disposed;

    internal Rac1MetalDetectorHost Detector { get; } = new(detector);
    internal Rac1WeaponContactHost Contacts { get; } = new(targets, physics);
    internal Rac1SuckCannonHost SuckCannon { get; } = new(suckCannon);
    internal Rac1GuidedWeaponCamera GuidedCamera { get; } = new();

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
        Detector.Reset();
        Contacts.Invalidate();
        SuckCannon.Reset();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GuidedCamera.Dispose();
        Detector.Dispose();
        Contacts.Dispose();
        SuckCannon.Dispose();
    }
}
