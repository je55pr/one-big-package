using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Audio;

namespace OBP.Game;

/// <summary>
/// Engine-only ports for authored R&C1 objects. Source rules stay in OBP.RAC1;
/// these ports may only present a source object, play a source sound or report status.
/// </summary>
internal sealed record Rac1AuthoredMechanismPorts(
    Func<string?> SourcePath,
    Action<int, int, Vector3> PlaySourceSound,
    Func<RuntimeAudioPlaybackIntent, int> StartLevelMusic,
    Action StopLevelMusic,
    Action<string> ReportCombatStatus);

internal enum Rac1AuthoredTickPhase { Music, Visual }

/// <summary>Single owner of one authored mechanism's nodes, native session and transient state.</summary>
internal abstract class Rac1AuthoredMechanism : IDisposable
{
    internal abstract void Configure(RuntimeWorld world, RuntimeWorldScene.Result scene);
    internal virtual void Tick(double delta, Rac1AuthoredTickPhase phase, PlayerHost? player) { }
    internal virtual void OnVisualTickSkipped() { }
    internal virtual void PhysicsStep(PlayerHost? player, bool suspended, bool waitingForReplay) { }
    public virtual void Dispose() { }
}

/// <summary>
/// A scene-lease registry, never a universal moby simulator. New world-specific
/// adapters register in the source catalog, not by adding an OBPGame partial.
/// </summary>
internal sealed class Rac1AuthoredMechanismRegistry : IDisposable
{
    private readonly RuntimeWorld _world;
    private readonly RuntimeWorldScene.Result _scene;
    private readonly List<Rac1AuthoredMechanism> _registered = [];
    private PlayerHost? _player;
    private bool _disposed;
    private bool _configured;

    internal Rac1NovalisCrankMechanism? Cranks { get; private set; }
    internal Rac1NovalisRouteMechanism? Route { get; private set; }
    internal Rac1NovalisMusicMechanism? Music { get; private set; }
    internal Rac1NovalisWaterMechanism? Water { get; private set; }
    internal Rac1RedPlantMechanism? RedPlants { get; private set; }
    internal Rac1PulsingOrbMechanism? Orbs { get; private set; }

    internal Rac1AuthoredMechanismRegistry(RuntimeWorld world, RuntimeWorldScene.Result scene,
        Rac1AuthoredMechanismPorts ports)
    {
        _world = world;
        _scene = scene;
        Rac1AuthoredMechanismCatalog.Register(world, this, ports);
    }

    internal void Register(Rac1AuthoredMechanism mechanism)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_configured) throw new InvalidOperationException("Authored mechanism registration has closed.");
        _registered.Add(mechanism);
        switch (mechanism)
        {
            case Rac1NovalisCrankMechanism crank: Cranks = crank; break;
            case Rac1NovalisRouteMechanism route: Route = route; break;
            case Rac1NovalisMusicMechanism music: Music = music; break;
            case Rac1NovalisWaterMechanism water: Water = water; break;
            case Rac1RedPlantMechanism plants: RedPlants = plants; break;
            case Rac1PulsingOrbMechanism orbs: Orbs = orbs; break;
        }
    }

    internal void Configure()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_configured) throw new InvalidOperationException("Authored mechanisms already configured.");
        _configured = true;
        try
        {
            foreach (var mechanism in _registered) mechanism.Configure(_world, _scene);
        }
        catch
        {
            try { Dispose(); }
            catch (Exception cleanup) { GD.PrintErr($"[rac1-mechanisms] cleanup after failed configure: {cleanup}"); }
            throw;
        }
    }

    internal void BindPlayer(PlayerHost player) => _player = player;
    internal bool TryTurnCrank(bool held, Vector2 stick) =>
        !_disposed && _player is { } player && GodotObject.IsInstanceValid(player) &&
        Cranks?.TryTurn(held, stick, player) == true;
    internal float? SampleWaterHeight(Vector3 position) => !_disposed ? Water?.Sample(position) : null;
    internal bool HasWater => !_disposed && Water is { Configured: true };
    internal void Tick(double delta, Rac1AuthoredTickPhase phase)
    {
        if (_disposed) return;
        foreach (var mechanism in _registered) mechanism.Tick(delta, phase, _player);
    }
    internal void OnVisualTickSkipped()
    {
        if (_disposed) return;
        foreach (var mechanism in _registered) mechanism.OnVisualTickSkipped();
    }
    internal void PhysicsStep(bool suspended, bool waitingForReplay)
    {
        if (_disposed) return;
        foreach (var mechanism in _registered) mechanism.PhysicsStep(_player, suspended, waitingForReplay);
    }
    internal void SelectAuthoredMusicCrank(int index) { if (!_disposed) Music?.OnCrank(index); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _player = null;
        // Retirement is deliberately reversed so readers/audio are released after
        // scene node adapters have detached. Continue releasing if one fails.
        Exception? failure = null;
        for (int i = _registered.Count - 1; i >= 0; i--)
        {
            try { _registered[i].Dispose(); }
            catch (Exception error) { failure ??= error; }
        }
        _registered.Clear();
        if (failure is not null) throw new InvalidOperationException("Authored mechanism retirement failed.", failure);
    }
}

/// <summary>Source-qualified registration point for future authored level scripts.</summary>
internal static class Rac1AuthoredMechanismCatalog
{
    internal static void Register(RuntimeWorld world, Rac1AuthoredMechanismRegistry registry,
        Rac1AuthoredMechanismPorts ports)
    {
        if (world.Game != ObpSourceGame.Rac1) return;
        if (world.LevelId == 1)
        {
            registry.Register(new Rac1NovalisCrankMechanism(ports, registry.SelectAuthoredMusicCrank));
            registry.Register(new Rac1NovalisRouteMechanism(ports));
            registry.Register(new Rac1NovalisWaterMechanism());
            registry.Register(new Rac1NovalisMusicMechanism(ports));
        }
        if (world.LevelId == Rac1RedPlantSession.NativeLevelId)
            registry.Register(new Rac1RedPlantMechanism());
        if (Rac1PulsingOrbSession.SupportsLevel(world.LevelId))
            registry.Register(new Rac1PulsingOrbMechanism());
    }
}
