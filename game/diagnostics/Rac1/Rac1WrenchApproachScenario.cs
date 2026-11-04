using System.Numerics;

namespace OBP.Game.Diagnostics;

/// <summary>Test-side ordinary input approach; it never stages the player.</summary>
public sealed class Rac1WrenchApproachScenario(MovementDiagnosticContext context, IRac1WrenchApproachFixture approach, Func<string>? failureEvidence = null)
    : MovementScenarioSupport(context)
{
    public async Task<float> RunAsync(Func<Vector3> targetCenterProvider, int maxFrames, string label,
        float inputScale = 1, float minimumTravel = .5f, float maximumPlanarDistance = float.PositiveInfinity)
    {
        if (!(inputScale > 0 && inputScale <= 1)) throw new ArgumentOutOfRangeException(nameof(inputScale));
        Vector3 start = Player.Read().GlobalPosition;
        Vector3 targetCenter = targetCenterProvider();
        try
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                targetCenter = targetCenterProvider();
                if (approach.Admits(targetCenter) && HorizontalDistance(Player.Read().GlobalPosition, targetCenter) <= maximumPlanarDistance)
                {
                    Vector3 travelled = Player.Read().GlobalPosition - start;
                    travelled.Y = 0;
                    float distance = travelled.Length();
                    if (distance < minimumTravel)
                        throw new InvalidOperationException($"{label} entered wrench policy after only {distance:0.000} host units; required at least {minimumTravel:0.000} for this smoke phase.");
                    return distance;
                }
                Vector3 desired = targetCenter - Player.Read().GlobalPosition;
                desired.Y = 0;
                if (desired.LengthSquared() <= 1e-5f)
                    throw new InvalidOperationException($"{label} reached the target origin without entering the wrench host policy.");
                desired = desired.Normalized();
                // These are desired conditioned axes. Raw diagonal axes would be
                // deadbanded a second time and reduce this run to walking.
                var axes = Rac1EncounterInput.ConditionedAxesToward(desired, approach.ControlYaw, inputScale);
                SetRac1SmokeConditionedInput(axes.Right, axes.Forward);
                await ProcessFrameAsync();
            }
        }
        finally { CleanupStep(ClearMovementSmokeInput); }
        throw new TimeoutException($"Timed out driving ordinary movement into wrench host policy for {label}: start={start}, current={Player.Read().GlobalPosition}, target={targetCenter}, yaw={Player.Read().Rac1CurrentYaw:R}, control={approach.ControlYaw:R}; velocity={Player.Read().Velocity}; grounded={Player.Read().Grounded}; contacts={failureEvidence?.Invoke()}.");
    }
}
