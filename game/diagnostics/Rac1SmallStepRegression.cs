using System.Numerics;
using System.Buffers.Binary;
using OBP.Core;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public sealed class Rac1SmallStepRegression(MovementDiagnosticContext context) : MovementScenarioSupport(context)
{
    public async Task RunAsync()
    {
        var player = Player;
        Vector3 savedPosition = player.Read().GlobalPosition;
        double savedYaw = player.Read().Rac1CurrentYaw;
        float savedSnapReach = player.Read().InitialGroundSnapUpwardReach;
        var fixture = Driver.Own(Geometry.Create("SyntheticRac1SmallStepFixture", new Vector3(2200f, 64f, 1800f)));
        try
        {
            // Ground placement must not select the top of the low-ceiling
            // fixture instead of the floor beneath it.
            player.SetSnapReach(.1f);
            // These routes use the actual Veldin collision, including the
            // shallow rim beside the two looped rocks south of Ratchet's ship.
            await CheckSmallStepRouteAsync(player, "Veldin-ship-rock-east-rim",
                new Vector3(-134f, 31.32f, 113.5f), Math.PI,
                p => p.X > -130f, expectedCrossing: true);
            await CheckSmallStepRouteAsync(player, "Veldin-ship-rock-south-rim",
                new Vector3(-128.5f, 31.32f, 107.8f), Math.PI / 2d,
                p => p.Z > 111f, expectedCrossing: true);
            // Exact stationary pose and effective forward heading from the
            // user-provided movement bookmark. Earlier image-derived routes
            // missed this taller lip on the adjacent looped rocks.
            await CheckSmallStepRouteAsync(player, "Veldin-saved-ship-snag",
                new Vector3(-131.647232f, 31.240614f, 108.697662f), 1.9442879091202951d,
                p => p.Z > 110f, expectedCrossing: true, exactStart: true);
            // The subsequent performance report saved this dense contact
            // corner. It must still block normally after shape consolidation.
            await CheckSmallStepRouteAsync(player, "Veldin-saved-dense-contact",
                new Vector3(-139.876053f, 31.972511f, 111.315048f), -.821097047425359d,
                p => p.X < -141f, expectedCrossing: false, exactStart: true);

            (string Name, float Height, float Lane, bool Ceiling, bool Cross)[] steps =
            [
                ("shallow-step", .12f, 0f, false, true),
                ("small-step", .22f, 20f, false, true),
                ("tall-wall", .65f, 40f, false, false),
                ("step-under-low-ceiling", .22f, 60f, true, false),
                ("point-three-step", .30f, 80f, false, true),
                ("higher-small-step", .38f, 100f, false, true),
                ("above-step-limit", .43f, 120f, false, false),
            ];
            foreach (var step in steps)
            {
                float z = step.Lane;
                float h = step.Height;
                AddSyntheticTraversalMesh(fixture, step.Name, 11,
                    (new(-12f, 0f, z - 4f), new(0f, 0f, z - 4f), new(0f, 0f, z + 4f)),
                    (new(-12f, 0f, z - 4f), new(0f, 0f, z + 4f), new(-12f, 0f, z + 4f)),
                    (new(0f, 0f, z - 4f), new(0f, h, z - 4f), new(0f, h, z + 4f)),
                    (new(0f, 0f, z - 4f), new(0f, h, z + 4f), new(0f, 0f, z + 4f)),
                    (new(0f, h, z - 4f), new(12f, h, z - 4f), new(12f, h, z + 4f)),
                    (new(0f, h, z - 4f), new(12f, h, z + 4f), new(0f, h, z + 4f)));
                if (step.Ceiling)
                {
                    AddSyntheticTraversalMesh(fixture, "LowCeiling", 11,
                    (new(-12f, 1.34f, z - 4f), new(12f, 1.34f, z - 4f), new(12f, 1.34f, z + 4f)),
                    (new(-12f, 1.34f, z - 4f), new(12f, 1.34f, z + 4f), new(-12f, 1.34f, z + 4f)));
                }
            }
            await PhysicsFramesAsync(3);
            foreach (var step in steps.Where(s => s.Cross))
                await CheckSmallStepRouteAsync(player, step.Name,
                    fixture.GlobalPosition + new Vector3(-4f, .08f, step.Lane), Math.PI,
                    p => p.X > fixture.GlobalPosition.X + 1f, step.Cross);
            await CheckSmallStepRouteAsync(player, "diagonal-small-step",
                fixture.GlobalPosition + new Vector3(-4f, .08f, 17f), 3d * Math.PI / 4d,
                p => p.X > fixture.GlobalPosition.X + 1f, expectedCrossing: true);
            await CheckSmallStepRouteAsync(player, "walking-small-step",
                fixture.GlobalPosition + new Vector3(-2f, .08f, 20f), Math.PI,
                p => p.X > fixture.GlobalPosition.X + 1f, expectedCrossing: true, magnitude: .5d, maxTicks: 360);
            foreach (var step in steps.Where(s => !s.Cross))
                await CheckSmallStepRouteAsync(player, step.Name,
                    fixture.GlobalPosition + new Vector3(-4f, .08f, step.Lane), Math.PI,
                    p => p.X > fixture.GlobalPosition.X + 1f, step.Cross);
        }
        finally
        {
            using var cleanup = BeginCleanup();
            CleanupStep(() => ClearMovementSmokeInput());
            CleanupStep(() => fixture.Dispose());
            CleanupStep(() => player.SetSnapReach(savedSnapReach));
            CleanupStep(() => player.SetYaw(savedYaw));
            CleanupStep(() => player.ResetAt(savedPosition));
            await CleanupPhysicsAsync(3);
        }
    }


    private async Task CheckSmallStepRouteAsync(
        IDiagnosticMovementPlayer player, string name, Vector3 start, double yaw,
        Func<Vector3, bool> reached, bool expectedCrossing, double magnitude = 1d, int maxTicks = 150,
        bool exactStart = false)
    {
        ClearMovementSmokeInput();
        player.SetYaw(yaw);
        player.ResetAt(start);
        await PhysicsFramesAsync(4);
        await WaitForGroundedAsync(player, 90);
        if (exactStart) { player.SetPosition(start); }
        bool crossed = false;
        int unsupportedUpdates = 0;
        int airborneUpdates = 0;
        float maximumHeight = player.Read().GlobalPosition.Y;
        var samples = new List<string>();
        for (int tick = 0; tick < maxTicks; tick++)
        {
            SetSyntheticNativeHeading(player, yaw, magnitude);
            await PhysicsFramesAsync(1);
            var state = player.Read();
            maximumHeight = Math.Max(maximumHeight, state.GlobalPosition.Y);
            if (!state.Grounded) { unsupportedUpdates++; }
            if (state.Rac1MovementPhase != Rac1RatchetMovementPhase.Grounded) { airborneUpdates++; }
            if (samples.Count < 60 &&
                (tick % 20 == 0 || !state.Grounded || state.Rac1MovementPhase != Rac1RatchetMovementPhase.Grounded))
            {
                samples.Add($"{tick}:{state.GlobalPosition}/floor={state.Grounded}/" +
                $"phase={state.Rac1MovementPhase}/velocity={state.Velocity}/step={state.Rac1SmallStepDecision}");
            }
            if (reached(state.GlobalPosition))
            {
                crossed = true;
                break;
            }
        }
        ClearMovementSmokeInput();
        Require(crossed == expectedCrossing,
            $"small-step {name}: crossed={crossed}, expected={expectedCrossing}, " +
            $"maximum-height={maximumHeight:F4}; " + string.Join("; ", samples));
        Require(player.Read().Rac1MovementPhase == Rac1RatchetMovementPhase.Grounded,
            $"small-step {name} left the ordinary grounded phase: {player.Read().Rac1MovementPhase}");
        Require(airborneUpdates == 0,
            $"small-step {name} spuriously entered an airborne action for {airborneUpdates} updates; " +
            string.Join("; ", samples));
        Require(Math.Abs(player.Read().FloorMaxAngle - Rac1OrdinaryGroundContactMotion.OrdinarySupportMaxAngleRadians) < .000001d,
            $"small-step {name} changed the ordinary support angle");
        Log($"[movement-smoke] small-step {name} passed; crossed={crossed}, position={player.Read().GlobalPosition}, " +
            $"unsupported={unsupportedUpdates}, airborne={airborneUpdates}");
    }

}
