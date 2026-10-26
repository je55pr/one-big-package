using Godot;
using OBP.Godot;

namespace OBP.Game.Diagnostics;

/// <summary>Engine-thread bindings. The scenario sees copied facts, never the scene node.</summary>
internal sealed class GodotDiagnosticSceneObservations(Node scene) : IDiagnosticSceneObservations
{
    public DiagnosticSceneSnapshot ReadScene()
    {
        if (!GodotObject.IsInstanceValid(scene) || !scene.IsInsideTree())
            throw new DiagnosticUnavailableException("The diagnostic scene was retired.");
        return new(scene is Node3D, scene.GetNodeOrNull<Node>("SourceManagerBootstrap") is not null,
            scene.GetNodeOrNull<Node>("UI") is not null, scene.GetNodeOrNull<Node>("SourceManager") is { } manager && manager.IsInsideTree());
    }
}

internal sealed class GodotDiagnosticFrameClock(Node owner) : IDiagnosticFrameClock
{
    public DiagnosticFrameSnapshot ReadFrames() => new(Engine.GetProcessFrames(), Engine.GetPhysicsFrames());
    public Task NextAsync(DiagnosticFramePhase phase, CancellationToken cancellationToken)
    {
        if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree())
            throw new DiagnosticUnavailableException("The diagnostic clock owner was retired.");
        return phase switch
        {
            DiagnosticFramePhase.Process => GodotAsyncWait.SignalAsync(owner.GetTree(), SceneTree.SignalName.ProcessFrame, cancellationToken),
            DiagnosticFramePhase.Physics => GodotAsyncWait.SignalAsync(owner.GetTree(), SceneTree.SignalName.PhysicsFrame, cancellationToken),
            DiagnosticFramePhase.PostDraw when DisplayServer.GetName() != "headless" =>
                GodotAsyncWait.SignalAsync(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw, cancellationToken),
            DiagnosticFramePhase.PostDraw => throw new DiagnosticUnavailableException("Post-draw evidence is unavailable in the headless renderer."),
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };
    }
}

/// <summary>Logical Godot actions only. No OS input, pointer position or mouse-mode changes.</summary>
internal sealed class GodotDiagnosticInputActions : IDiagnosticInputActions
{
    private readonly DiagnosticInputActions _actions = new(action => InputMap.HasAction(action),
        action => Input.GetActionStrength(action), (action, strength) =>
        {
            if (strength == 0) Input.ActionRelease(action); else Input.ActionPress(action, strength);
        });
    public IDiagnosticInputLease Acquire(CancellationToken lifetime) => _actions.Acquire(lifetime);
}
