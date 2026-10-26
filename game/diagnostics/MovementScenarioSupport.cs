using System.Numerics;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public sealed record MovementScenarioPorts(IDiagnosticMovementPlayer Player, IDiagnosticMovementAnimation Animation,
    IDiagnosticGeometryFixture Geometry, IDiagnosticMovementWorld World, IDiagnosticInputActions Input,
    IDiagnosticPhysicalInput Physical, IDiagnosticFrameClock CleanupClock, Action<string> Log, Action<string> LogError);
public sealed record MovementDiagnosticContext(MovementScenarioPorts Ports, MovementDiagnosticDriver Driver);

/// <summary>Shared movement assertions and phase waits. The context has no application, inventory or world mutation access.</summary>
public abstract class MovementScenarioSupport(MovementDiagnosticContext context)
{
    protected MovementDiagnosticContext Context { get; } = context;
    protected IDiagnosticMovementPlayer Player => Context.Ports.Player;
    protected IDiagnosticMovementAnimation Animation => Context.Ports.Animation;
    protected IDiagnosticMovementWorld World => Context.Ports.World;
    protected IDiagnosticGeometryFixture Geometry => Context.Ports.Geometry;
    protected MovementDiagnosticDriver Driver => Context.Driver;
    protected Action<string> Log => Context.Ports.Log;
    protected Action<string> LogError => Context.Ports.LogError;
    private bool _cleanup;
    protected Task PhysicsFramesAsync(int count) => WaitAsync(DiagnosticFramePhase.Physics, count);
    protected Task ProcessFrameAsync() => WaitAsync(DiagnosticFramePhase.Process, 1);
    private async Task WaitAsync(DiagnosticFramePhase phase, int count)
    {
        if (!_cleanup) { if (phase == DiagnosticFramePhase.Physics) { await Driver.PhysicsAsync(count); } else await Driver.ProcessAsync(count); return; }
        if (count > 4) { throw new InvalidOperationException("Movement cleanup is limited to four frame waits."); }
        for (int i = 0; i < count; i++) await Context.Ports.CleanupClock.NextAsync(phase, CancellationToken.None);
    }
    protected IDisposable BeginCleanup()
    {
        _cleanup = true; return new CleanupLease(() => _cleanup = false);
    }
    protected Task WaitForGroundedAsync(IDiagnosticMovementPlayer player, int maxFrames) =>
        Driver.WaitGroundedAsync(() => player.Read().Grounded, () => player.Read().Rac1MovementPhase == Rac1RatchetMovementPhase.Grounded, maxFrames);
    protected async Task WaitForAnimationStateAsync(IDiagnosticMovementPlayer player, PlayerAnimationState expected, int maxFrames, string label)
    {
        for (int frame = 0; frame < maxFrames; frame++) { await PhysicsFramesAsync(1); if (player.Read().AnimationState == expected) { return; } }
        throw new InvalidOperationException($"{label} did not reach animation state {expected}; current={player.Read().AnimationState}");
    }
    protected async Task WaitForPlayerAvatarSequenceAsync(int sequence, int maxFrames, string label)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            await ProcessFrameAsync();
            var animation = Animation.Read();
            if (animation.Sequence == sequence && animation.Synchronized) { return; }
        }
        var current = Animation.Read(); var player = Player.Read();
        throw new InvalidOperationException($"{label} did not reach synchronized player-avatar sequence {sequence}; " +
            $"current={current.Sequence?.ToString() ?? "none"}; synchronized={current.Synchronized}; " +
            $"avatarFrame={current.Frame}/{current.FrameCount}; avatarFps={current.FramesPerSecond}; loop={current.Loop}; " +
            $"semantic={player.AnimationState}; phase={player.Rac1MovementPhase}; yawMode={player.Rac1YawMode}; " +
            $"targetStep={player.Rac1TargetPlanarStep}; grounded={player.Grounded}; recoveredSupport={player.Rac1RecoveredSupportGrounded}");
    }
    protected int? CurrentPlayerAvatarSourceSequence() => Animation.Read().Sequence;
    protected bool PlayerAvatarPresentationIsSynchronized() => Animation.Read().Synchronized;
    protected void ClearMovementSmokeInput() => Driver.Clear();
    protected void ReleaseMovementActions() => Driver.ReleaseDirections();
    protected void SetAnalogueSmokeInput(float right, float forward) => Driver.Analogue(right, forward);
    protected void SetRac1SmokeConditionedInput(double right, double forward) => Driver.Conditioned(right, forward);
    protected void SendPhysicalKey(DiagnosticPhysicalKey key, bool pressed) => Driver.SetKey(key, pressed);
    protected Task TapPhysicalKeyAsync(DiagnosticPhysicalKey key) => Driver.TapKeyAsync(key);
    protected void SetSyntheticNativeHeading(IDiagnosticMovementPlayer player, double yaw, double magnitude = 1)
    {
        double stick = player.Read().ControlHeading - yaw;
        Driver.Conditioned(Math.Sin(stick) * magnitude, Math.Cos(stick) * magnitude);
    }
    protected static void AddSyntheticTraversalMesh(IDiagnosticGeometryLease fixture, string name, int material,
        params (Vector3 A, Vector3 B, Vector3 C)[] triangles) => fixture.AddMesh(name, material, triangles);
    protected async Task Rac1SmokeWaitAsync(Func<bool> condition, int maxFrames, string label)
    {
        for (int frame = 0; frame < maxFrames; frame++) { if (condition()) { return; } await ProcessFrameAsync(); }
        throw new TimeoutException($"Timed out waiting for RAC1 smoke phase: {label}.");
    }
    protected static float HorizontalSpeed(IDiagnosticMovementPlayer player) => DiagnosticMovementMath.HorizontalSpeed(player.Read().Velocity);
    protected static float HorizontalDistance(Vector3 a, Vector3 b) => DiagnosticMovementMath.HorizontalDistance(a, b);
    protected static double WrapAngle(double value) => DiagnosticMovementMath.WrapAngle(value);
    protected static void Require(bool condition, string message) => DiagnosticMovementMath.Require(condition, message);
    private sealed class CleanupLease(Action release) : IDisposable { public void Dispose() => release(); }
}

public static class DiagnosticMovementMath
{
    public static float HorizontalSpeed(Vector3 velocity) => new Vector2(velocity.X, velocity.Z).Length();
    public static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
    public static double WrapAngle(double value)
    {
        while (value > Math.PI) value -= Math.Tau;
        while (value <= -Math.PI) value += Math.Tau;
        return value;
    }
    public static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    public static float DistanceTo(this Vector3 value, Vector3 other) => Vector3.Distance(value, other);
    public static Vector3 Normalized(this Vector3 value) => Vector3.Normalize(value);
}
