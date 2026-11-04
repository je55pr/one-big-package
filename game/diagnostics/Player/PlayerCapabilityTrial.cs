using System.Security.Cryptography;

namespace OBP.Game.Diagnostics;

internal sealed record DiagnosticCapabilitySample(int Tick, string? Sequence, int? Frame, string Pose, string Attachment,
        double Facing, double[] Position, double[] Camera, double Clock);

internal static class PlayerCapabilityTrial
{
    internal static async Task<DiagnosticCapabilitySample[]> CaptureAsync(DiagnosticRunContext run, IDiagnosticCapabilitySamples samples, IDiagnosticPlayerInputLease driver)
    {
        var trace = new List<DiagnosticCapabilitySample>(); var done = new TaskCompletionSource(); int inputTick = 0;
        long start = samples.Read().ServiceTick;
        driver.SetFilter(tick =>
        {
            inputTick = tick + 1;
            return new(Move: tick is >= 20 and < 70 ? new(0, -.6f) : tick is >= 70 and < 110 or >= 160 and < 190 ? new(0, -1) :
                tick is >= 110 and < 140 ? new(.65f, -.8f) : default,
                JumpHeld: tick is >= 120 and < 130, CrouchHeld: tick is >= 175 and < 190,
                ActionJustPressed: tick == 210, ActionHeld: tick is >= 210 and < 215,
                CameraIntent: tick is >= 90 and < 105 ? new(.3f, .1f) : default);
        });
        using var canceled = run.CancellationToken.Register(() => done.TrySetCanceled(run.CancellationToken));
        using var subscription = samples.SubscribeAfterPhysics(() =>
        {
            try
            {
                var frame = samples.Read();
                Require(frame.PhysicsTick == inputTick && frame.ServiceTick - start == inputTick, "duplicate/missing player physics subscription");
                if (inputTick % 6 == 0) trace.Add(new(inputTick, frame.Sequence, frame.Frame, PoseDigest(frame.LocalPositions.ToArray()), frame.AttachmentJson,
                    frame.Facing, Coordinates(frame.Position), Coordinates(frame.Camera), frame.Seconds));
                if (inputTick >= 240) done.TrySetResult();
            }
            catch (Exception error) { done.TrySetException(error); }
        });
        try { await done.Task; }
        finally { driver.SetFilter(_ => new()); }
        return trace.ToArray();
    }
    private static double[] Coordinates(System.Numerics.Vector3 value) => [value.X, value.Y, value.Z];
    private static string PoseDigest(double[] values) { var bytes = new byte[values.Length * sizeof(double)]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length); return Convert.ToHexString(SHA256.HashData(bytes)); }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
}

internal sealed class StandalonePlayerCapabilityTrial(DiagnosticScenarioDefinition definition, IDiagnosticCapabilitySamples samples, IDiagnosticPlayerInputFixture input) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = definition;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        using var driver = input.Acquire();
        await PlayerCapabilityTrial.CaptureAsync(run, samples, driver);
    }
}
