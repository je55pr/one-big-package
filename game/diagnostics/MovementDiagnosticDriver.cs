using System.Numerics;

namespace OBP.Game.Diagnostics;

public enum DiagnosticPhysicalKey { W, Space, F, R, E, F11 }
public enum DiagnosticPhysicalButton { Wrench, EquippedUse }
public interface IDiagnosticPhysicalInput
{
    IDiagnosticPhysicalInputLease Acquire();
}
public interface IDiagnosticPhysicalInputLease : IDisposable
{
    void SetKey(DiagnosticPhysicalKey key, bool pressed);
    void SetButton(DiagnosticPhysicalButton button, bool pressed);
}

/// <summary>One scoped input owner and the exact frame-start phases used by the original movement regressions.</summary>
public sealed class MovementDiagnosticDriver : IDisposable
{
    public const string Left = "obp_move_left", Right = "obp_move_right", Forward = "obp_move_forward", Back = "obp_move_back";
    public const string Jump = "obp_jump", Crouch = "obp_crouch", Action = "obp_action", Traversal = "obp_traversal";
    private readonly DiagnosticRunContext _run;
    private readonly IDiagnosticInputLease _actions;
    private readonly IDiagnosticPhysicalInputLease _physical;
    private bool _disposed;
    public MovementDiagnosticDriver(DiagnosticRunContext run, IDiagnosticInputActions actions, IDiagnosticPhysicalInput physical)
    {
        _run = run;
        _actions = actions.Acquire(CancellationToken.None); // Cleanup must release presses after the run token is canceled.
        try { _physical = physical.Acquire(); }
        catch { _actions.Dispose(); throw; }
    }
    public ulong PhysicsFrame => _run.Frames.Physics;
    public CancellationToken Lifetime => _run.CancellationToken;
    public Task PhysicsAsync(int count = 1) => _run.WaitAsync(DiagnosticFramePhase.Physics, count);
    public Task ProcessAsync(int count = 1) => _run.WaitAsync(DiagnosticFramePhase.Process, count);
    public void SetAction(string action, bool pressed, float strength = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pressed) { _run.CancellationToken.ThrowIfCancellationRequested(); }
        _actions.SetAction(action, pressed ? strength : 0);
    }
    public void SetKey(DiagnosticPhysicalKey key, bool pressed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pressed) { _run.CancellationToken.ThrowIfCancellationRequested(); }
        _physical.SetKey(key, pressed);
    }
    public async Task SetButtonAsync(DiagnosticPhysicalButton button, bool pressed)
    {
        // Hardware arrives between physics updates. Device 15 remains separate from connected controllers.
        await ProcessAsync();
        _physical.SetButton(button, pressed);
    }
    public async Task TapButtonAsync(DiagnosticPhysicalButton button)
    {
        await SetButtonAsync(button, true); await PhysicsAsync(3);
        await SetButtonAsync(button, false); await PhysicsAsync(3);
    }
    public async Task TapKeyAsync(DiagnosticPhysicalKey key)
    {
        SetKey(key, true); await PhysicsAsync(2);
        SetKey(key, false); await PhysicsAsync(2);
    }
    public void Analogue(float right, float forward)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _run.CancellationToken.ThrowIfCancellationRequested();
        ReleaseDirections();
        if (right > 0) { _actions.SetAction(Right, right); }
        else if (right < 0) { _actions.SetAction(Left, -right); }
        if (forward > 0) { _actions.SetAction(Forward, forward); }
        else if (forward < 0) { _actions.SetAction(Back, -forward); }
    }
    public static Vector2 RawConditionedAxes(double right, double forward)
    {
        static float Raw(double axis) => Math.Abs(axis) < 1e-8d ? 0 :
            (float)Math.CopySign((48d + 76d * Math.Abs(axis)) / 128d, axis);
        return new(Raw(right), Raw(forward));
    }
    public void Conditioned(double right, double forward)
    {
        var raw = RawConditionedAxes(right, forward); Analogue(raw.X, raw.Y);
    }
    public void ReleaseDirections()
    {
        foreach (string action in new[] { Left, Right, Forward, Back }) _actions.SetAction(action, 0);
    }
    public void Clear()
    {
        ReleaseDirections();
        foreach (string action in new[] { Jump, Crouch, Action, Traversal }) _actions.SetAction(action, 0);
    }
    public async Task WaitGroundedAsync(Func<bool> grounded, Func<bool> nativeGrounded, int maximumFrames)
    {
        int consecutive = 0;
        for (int frame = 0; frame < maximumFrames; frame++)
        {
            await PhysicsAsync();
            await ProcessAsync(); // Observe the completed physics callback, not the pre-update floor bit.
            consecutive = grounded() && nativeGrounded() ? consecutive + 1 : 0;
            if (consecutive >= 3) { return; }
        }
        throw new InvalidOperationException("player did not reach Godot floor contact within smoke timeout");
    }
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        try { _physical.Dispose(); }
        finally { _actions.Dispose(); }
    }
}
