using Godot;
using OBP.RAC1.Player;

namespace OBP.Game;

public partial class OBPGame
{
    /// <summary>
    /// Synthetic ordinary-material ramps check the production support adapter
    /// throughout the prelaunch interval. Fixture geometry is host integration
    /// evidence, not a new recovered retail slope or jump coefficient.
    /// </summary>
    private async System.Threading.Tasks.Task RequireRac1SlopeJumpSmokeAsync(PlayerHost player)
    {
        Vector3 savedPosition = player.GlobalPosition;
        Vector3 savedRotation = player.Rotation;
        double savedYaw = player.Rac1CurrentYaw;
        var savedPack = player.Rac1ActiveBackpack;
        bool savedClank = player.Rac1ClankAvailable;
        int? savedPreview = player.Rac1DevelopmentPackPreview;
        bool savedSwingshotPreview = player.Rac1DevelopmentSwingshotPreview;
        var fixture = new Node3D
        {
            Name = "SyntheticRac1SlopeJumpFixture",
            Position = new Vector3(2000f, 64f, 1800f),
        };
        (string Name, float Degrees, float Lane, bool Seam)[] ramps =
        [
            ("descending-five", 5f, 0f, false),
            ("descending-twenty", 20f, 30f, false),
            ("ascending-five", -5f, 60f, false),
            ("ascending-twenty", -20f, 90f, false),
            ("flat-to-descending-seam", 5f, 120f, true),
        ];
        AddChild(fixture);
        try
        {
            ClearMovementSmokeInput();
            player.Rac1ActiveBackpack = null;
            player.Rac1ClankAvailable = false;
            for (int cycle = 0; player.Rac1DevelopmentPackPreview.HasValue && cycle < 3; cycle++)
                player.CycleRac1DevelopmentPack();
            if (player.Rac1DevelopmentSwingshotPreview)
                player.ToggleRac1DevelopmentSwingshot();

            foreach (var ramp in ramps)
            {
                float gradient = MathF.Tan(Mathf.DegToRad(ramp.Degrees));
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
                player.Rac1CurrentYaw = 0d;
                float gradient = MathF.Tan(Mathf.DegToRad(ramp.Degrees));
                float startX = ramp.Seam ? 8f : 12f;
                float startHeight = ramp.Seam ? 0f : gradient * startX;
                player.ApplyDevelopmentReset(fixture.GlobalPosition + new Vector3(startX, startHeight + .08f, ramp.Lane));
                await PhysicsFramesAsync(4);
                await WaitForGroundedAsync(player, 90);
                for (int tick = 0; tick < 180; tick++)
                {
                    SetSyntheticNativeHeading(player, 0d);
                    await PhysicsFramesAsync(1);
                    if ((!ramp.Seam && tick >= 71) ||
                        ramp.Seam && player.GlobalPosition.X - fixture.GlobalPosition.X <= .30f)
                        break;
                }
                Require(player.Rac1RecoveredSupportGrounded &&
                    player.Rac1MovementPhase == Rac1RatchetMovementPhase.Grounded &&
                    HorizontalSpeed(player) > 5f,
                    $"synthetic {ramp.Name} did not establish a supported run before Cross: " +
                    $"pos={player.GlobalPosition - fixture.GlobalPosition}, velocity={player.Velocity}, " +
                    $"phase={player.Rac1MovementPhase}, support={player.Rac1RecoveredSupportGrounded}");

                var samples = new List<string>();
                bool sawAnticipation = false;
                bool sawRising = false;
                float maximumClearance = 0f;
                Input.ActionPress(RawGamepadInput.Jump);
                for (int tick = 0; tick < 50; tick++)
                {
                    if (tick == 16)
                        Input.ActionRelease(RawGamepadInput.Jump);
                    SetSyntheticNativeHeading(player, 0d);
                    await PhysicsFramesAsync(1);
                    await Diagnostics.LegacyDiagnosticWait.SignalAsync(GetTree(), SceneTree.SignalName.ProcessFrame);
                    float localX = player.GlobalPosition.X - fixture.GlobalPosition.X;
                    float supportHeight = fixture.GlobalPosition.Y + gradient * (ramp.Seam ? Math.Min(0f, localX) : localX);
                    float clearance = player.GlobalPosition.Y - supportHeight;
                    maximumClearance = Math.Max(maximumClearance, clearance);
                    sawAnticipation |= player.Rac1MovementPhase == Rac1RatchetMovementPhase.JumpAnticipation;
                    sawRising |= player.Rac1MovementPhase == Rac1RatchetMovementPhase.Rising && player.Velocity.Y > 1f;
                    if (tick < 16)
                        samples.Add($"{tick}:{player.Rac1MovementPhase}/floor={player.IsOnFloor()}/" +
                            $"support={player.Rac1RecoveredSupportGrounded}/clear={clearance:F4}/vy={player.Velocity.Y:F4}");
                }
                ClearMovementSmokeInput();
                Require(sawAnticipation && sawRising && maximumClearance > .6f,
                    $"synthetic {ramp.Name} run-to-Cross failed to preserve support and launch: " +
                    $"anticipation={sawAnticipation}, rising={sawRising}, clearance={maximumClearance:F4}; " +
                    string.Join("; ", samples));
                GD.Print($"[movement-smoke] synthetic {ramp.Name} running jump passed; clearance={maximumClearance:F4}");
            }
        }
        finally
        {
            using var legacyCleanupWaits = Diagnostics.LegacyDiagnosticCancellation.Bind(CancellationToken.None);
            ClearMovementSmokeInput();
            if (GodotObject.IsInstanceValid(fixture) && !fixture.IsQueuedForDeletion())
                fixture.QueueFree();
            player.Rac1ActiveBackpack = savedPack;
            player.Rac1ClankAvailable = savedClank;
            for (int cycle = 0; player.Rac1DevelopmentPackPreview != savedPreview && cycle < 3; cycle++)
                player.CycleRac1DevelopmentPack();
            if (player.Rac1DevelopmentSwingshotPreview != savedSwingshotPreview)
                player.ToggleRac1DevelopmentSwingshot();
            player.Rotation = savedRotation;
            player.Rac1CurrentYaw = savedYaw;
            player.ApplyDevelopmentReset(savedPosition);
            await PhysicsFramesAsync(3);
        }
    }
}
