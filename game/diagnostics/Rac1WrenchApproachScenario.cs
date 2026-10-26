using System.Numerics;

namespace OBP.Game.Diagnostics;

/// <summary>Test-side ordinary input approach; it never stages the player.</summary>
public sealed class Rac1WrenchApproachScenario(MovementDiagnosticContext context, IRac1WrenchApproachFixture approach)
    : MovementScenarioSupport(context)
{
    public async Task<float> RunAsync(Func<Vector3> targetCenterProvider, int maxFrames, string label,
        float inputScale = 1, float minimumTravel = .5f, float maximumPlanarDistance = float.PositiveInfinity)
    {
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
                double controlYaw = approach.ControlYaw;
                Vector3 controlForward = new(-(float)Math.Cos(controlYaw), 0, (float)Math.Sin(controlYaw));
                Vector3 controlRight = new(-(float)Math.Sin(controlYaw), 0, -(float)Math.Cos(controlYaw));
                SetAnalogueSmokeInput(inputScale * Math.Clamp(Vector3.Dot(desired, controlRight), -1, 1),
                    inputScale * Math.Clamp(Vector3.Dot(desired, controlForward), -1, 1));
                await ProcessFrameAsync();
            }
        }
        finally { CleanupStep(ClearMovementSmokeInput); }
        throw new TimeoutException($"Timed out driving ordinary movement into wrench host policy for {label}: start={start}, current={Player.Read().GlobalPosition}, target={targetCenter}, yaw={Player.Read().Rac1CurrentYaw:R}, control={approach.ControlYaw:R}.");
    }
}
