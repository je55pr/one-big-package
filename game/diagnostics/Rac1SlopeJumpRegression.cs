using System.Numerics;
using System.Buffers.Binary;
using OBP.Core;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public sealed class Rac1SlopeJumpRegression(MovementDiagnosticContext context) : MovementScenarioSupport(context)
{
    /// <summary>
    /// Synthetic ordinary-material ramps check the production support adapter
    /// throughout the prelaunch interval. Fixture geometry is host integration
    /// evidence, not a new recovered retail slope or jump coefficient.
    /// </summary>
    public async Task RunAsync()
    {
        var player = Player;
        var saved = player.Read();
        var fixture = Driver.Own(Geometry.Create("SyntheticRac1SlopeJumpFixture", new Vector3(2000f, 64f, 1800f)));
        (string Name, float Degrees, float Lane, bool Seam)[] ramps =
        [
            ("descending-five", 5f, 0f, false),
            ("descending-twenty", 20f, 30f, false),
            ("ascending-five", -5f, 60f, false),
            ("ascending-twenty", -20f, 90f, false),
            ("flat-to-descending-seam", 5f, 120f, true),
        ];
        try
        {
            ClearMovementSmokeInput();
            player.SetBackpack(null);
            player.SetClank(false);
            for (int cycle = 0; player.Read().Rac1DevelopmentPackPreview.HasValue && cycle < 3; cycle++)
                player.CyclePack();
            if (player.Read().Rac1DevelopmentSwingshotPreview)
            { player.ToggleSwingshot(); }

            foreach (var ramp in ramps)
            {
                float gradient = MathF.Tan((MathF.PI / 180f * ramp.Degrees));
                float positiveHeight = ramp.Seam ? 0f : gradient * 40f;
                AddSyntheticTraversalMesh(fixture, $"SyntheticRamp-{ramp.Name}", 11,
                    (new(-40f, -gradient * 40f, ramp.Lane - 8f),
                        new(0f, 0f, ramp.Lane - 8f), new(0f, 0f, ramp.Lane + 8f)),
                    (new(-40f, -gradient * 40f, ramp.Lane - 8f),
                        new(0f, 0f, ramp.Lane + 8f), new(-40f, -gradient * 40f, ramp.Lane + 8f)),
                    (new(0f, 0f, ramp.Lane - 8f),
                        new(40f, positiveHeight, ramp.Lane - 8f), new(40f, positiveHeight, ramp.Lane + 8f)),
                    (new(0f, 0f, ramp.Lane - 8f),
                        new(40f, positiveHeight, ramp.Lane + 8f), new(0f, 0f, ramp.Lane + 8f)));
            }
            await PhysicsFramesAsync(3);

            foreach (var ramp in ramps)
            {
                ClearMovementSmokeInput();
                player.SetYaw(0d);
                float gradient = MathF.Tan((MathF.PI / 180f * ramp.Degrees));
                float startX = ramp.Seam ? 8f : 12f;
                float startHeight = ramp.Seam ? 0f : gradient * startX;
                player.ResetAt(fixture.GlobalPosition + new Vector3(startX, startHeight + .08f, ramp.Lane));
                await PhysicsFramesAsync(4);
                await WaitForGroundedAsync(player, 90);
                for (int tick = 0; tick < 180; tick++)
                {
                    SetSyntheticNativeHeading(player, 0d);
                    await PhysicsFramesAsync(1);
                    if ((!ramp.Seam && tick >= 71) ||
                        ramp.Seam && player.Read().GlobalPosition.X - fixture.GlobalPosition.X <= .30f)
                    { break; }
                }
                Require(player.Read().Rac1RecoveredSupportGrounded &&
                    player.Read().Rac1MovementPhase == Rac1RatchetMovementPhase.Grounded &&
                    HorizontalSpeed(player) > 5f,
                    $"synthetic {ramp.Name} did not establish a supported run before Cross: " +
                    $"pos={player.Read().GlobalPosition - fixture.GlobalPosition}, velocity={player.Read().Velocity}, " +
                    $"phase={player.Read().Rac1MovementPhase}, support={player.Read().Rac1RecoveredSupportGrounded}");

                Require(!ramp.Seam || player.Read().GlobalPosition.X - fixture.GlobalPosition.X <= .30f,
                    $"synthetic {ramp.Name} run never reached the seam before Cross");
                var samples = new List<string>();
                bool sawAnticipation = false;
                bool sawRising = false;
                float maximumClearance = 0f;
                Driver.SetAction(MovementDiagnosticDriver.Jump, true);
                for (int tick = 0; tick < 50; tick++)
                {
                    if (tick == 16)
                    { Driver.SetAction(MovementDiagnosticDriver.Jump, false); }
                    SetSyntheticNativeHeading(player, 0d);
                    await PhysicsFramesAsync(1);
                    await ProcessFrameAsync();
                    float localX = player.Read().GlobalPosition.X - fixture.GlobalPosition.X;
                    float supportHeight = fixture.GlobalPosition.Y + gradient * (ramp.Seam ? Math.Min(0f, localX) : localX);
                    float clearance = player.Read().GlobalPosition.Y - supportHeight;
                    maximumClearance = Math.Max(maximumClearance, clearance);
                    sawAnticipation |= player.Read().Rac1MovementPhase == Rac1RatchetMovementPhase.JumpAnticipation;
                    sawRising |= player.Read().Rac1MovementPhase == Rac1RatchetMovementPhase.Rising && player.Read().Velocity.Y > 1f;
                    if (tick < 16)
                    {
                        samples.Add($"{tick}:{player.Read().Rac1MovementPhase}/floor={player.Read().Grounded}/" +
                        $"support={player.Read().Rac1RecoveredSupportGrounded}/clear={clearance:F4}/vy={player.Read().Velocity.Y:F4}");
                    }
                }
                ClearMovementSmokeInput();
                Require(sawAnticipation && sawRising && maximumClearance > .6f,
                    $"synthetic {ramp.Name} run-to-Cross failed to preserve support and launch: " +
                    $"anticipation={sawAnticipation}, rising={sawRising}, clearance={maximumClearance:F4}; " +
                    string.Join("; ", samples));
                Log($"[movement-smoke] synthetic {ramp.Name} running jump passed; clearance={maximumClearance:F4}");
            }
        }
        finally
        {
            await RestoreFixturePlayerAsync(fixture, saved);
        }
    }

}
