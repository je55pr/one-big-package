using System.Numerics;
using System.Buffers.Binary;
using OBP.Core;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public sealed class Rac1TraversalContactRegression(MovementDiagnosticContext context) : MovementScenarioSupport(context)
{
    /// <summary>
    /// Synthetic Godot fixtures exercise the production host/query/animation
    /// adapters. They are integration checks, not new retail trajectory evidence.
    /// </summary>
    public async Task RunAsync()
    {
        var player = Player;
        var saved = player.Read();
        var fixture = Driver.Own(Geometry.Create("SyntheticRac1TraversalContactFixture", new Vector3(2000f, 64f, 2000f)));
        const float ledgeTop = 3.0f;
        try
        {
            ClearMovementSmokeInput();
            player.SetBackpack(null);
            player.SetClank(false);
            for (int cycle = 0; player.Read().Rac1DevelopmentPackPreview.HasValue && cycle < 3; cycle++)
                player.CyclePack();
            Require(!player.Read().Rac1DevelopmentPackPreview.HasValue,
                "could not clear the development pack preview for traversal-contact fixtures");
            if (player.Read().Rac1DevelopmentSwingshotPreview)
            { player.ToggleSwingshot(); }

            AddSyntheticTraversalMesh(fixture, "SyntheticOrdinaryFloor", 11,
                (new(-30f, 0f, -12f), new(30f, 0f, -12f), new(30f, 0f, 12f)),
                (new(-30f, 0f, -12f), new(30f, 0f, 12f), new(-30f, 0f, 12f)));
            AddSyntheticTraversalMesh(fixture, "SyntheticLedgeTopAndWall", 11,
                (new(-8f, ledgeTop, -8f), new(0f, ledgeTop, -8f), new(0f, ledgeTop, 8f)),
                (new(-8f, ledgeTop, -8f), new(0f, ledgeTop, 8f), new(-8f, ledgeTop, 8f)),
                (new(0f, 0f, -8f), new(0f, ledgeTop, -8f), new(0f, ledgeTop, 8f)),
                (new(0f, 0f, -8f), new(0f, ledgeTop, 8f), new(0f, 0f, 8f)));
            AddSyntheticTraversalMesh(fixture, "SyntheticClassSevenIceFloor", 7,
                (new(-80f, 0f, 65f), new(80f, 0f, 65f), new(80f, 0f, 95f)),
                (new(-80f, 0f, 65f), new(80f, 0f, 95f), new(-80f, 0f, 95f)));
            AddSyntheticTraversalMesh(fixture, "SyntheticOpposingWallJumpSlot", 11,
                (new(-4f, 0f, 20f), new(4f, 0f, 20f), new(4f, 0f, 40f)),
                (new(-4f, 0f, 20f), new(4f, 0f, 40f), new(-4f, 0f, 40f)),
                (new(-.7f, 0f, 25f), new(-.7f, 8f, 25f), new(-.7f, 8f, 35f)),
                (new(-.7f, 0f, 25f), new(-.7f, 8f, 35f), new(-.7f, 0f, 35f)),
                (new(.7f, 0f, 25f), new(.7f, 8f, 35f), new(.7f, 8f, 25f)),
                (new(.7f, 0f, 25f), new(.7f, 0f, 35f), new(.7f, 8f, 35f)));
            await PhysicsFramesAsync(3);

            await GrabSyntheticLedgeAsync();
            Require(player.Read().Rac1TraversalAction == 0x19,
                "synthetic ledge grab did not reach the native hanging action");
            Require(CurrentPlayerAvatarSourceSequence() == 33 && PlayerAvatarPresentationIsSynchronized(),
                "synthetic ledge hang did not publish synchronized native sequence33");
            Vector3 shimmyStart = player.Read().GlobalPosition;
            bool sawShimmy = false;
            for (int tick = 0; tick < 70; tick++)
            {
                SetSyntheticNativeHeading(player, Math.PI / 2d);
                await PhysicsFramesAsync(1);
                sawShimmy |= player.Read().Rac1TraversalAction is 0x1a or 0x1b;
            }
            Require(player.Read().Rac1Hanging && sawShimmy &&
                Math.Abs(player.Read().GlobalPosition.Z - shimmyStart.Z) > .15f,
                $"synthetic ledge shimmy did not move along the edge: action={player.Read().Rac1TraversalAction}, " +
                $"delta={player.Read().GlobalPosition - shimmyStart}");
            ClearMovementSmokeInput();
            for (int tick = 0; tick < 90 && player.Read().Rac1TraversalAction != 0x19; tick++)
                await PhysicsFramesAsync(1);
            Require(player.Read().Rac1TraversalAction == 0x19, "synthetic ledge shimmy did not settle back to hang");

            float jumpStart = player.Read().GlobalPosition.Y;
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            await PhysicsFramesAsync(2);
            Driver.SetAction(MovementDiagnosticDriver.Jump, false);
            Require(!player.Read().Rac1Hanging && player.Read().Rac1TraversalAction == 0x1c,
                $"synthetic ledge Cross did not select native ledge jump: action={player.Read().Rac1TraversalAction}");
            float jumpApex = jumpStart;
            for (int tick = 0; tick < 65; tick++)
            {
                SetSyntheticNativeHeading(player, 0d);
                await PhysicsFramesAsync(1);
                jumpApex = Math.Max(jumpApex, player.Read().GlobalPosition.Y);
            }
            Require(jumpApex > jumpStart + .75f,
                $"synthetic ledge jump failed to launch: rise={jumpApex - jumpStart:F4}");
            Log($"[movement-smoke] synthetic ledge grab/hang/shimmy/Cross jump passed; rise={jumpApex - jumpStart:F4}");

            await GrabSyntheticLedgeAsync();
            float dropStart = player.Read().GlobalPosition.Y;
            Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
            await PhysicsFramesAsync(2);
            Driver.SetAction(MovementDiagnosticDriver.Crouch, false);
            await PhysicsFramesAsync(5);
            Require(!player.Read().Rac1Hanging && player.Read().GlobalPosition.Y < dropStart - .01f,
                "synthetic ledge drop action failed to release downward");
            Log("[movement-smoke] synthetic ledge drop release passed");

            ClearMovementSmokeInput();
            player.SetYaw(0d);
            player.ResetAt(fixture.GlobalPosition + new Vector3(0f, .08f, 30f));
            await PhysicsFramesAsync(4);
            await WaitForGroundedAsync(player, 60);
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            await PhysicsFramesAsync(11);
            Driver.SetAction(MovementDiagnosticDriver.Jump, false);
            await PhysicsFramesAsync(2);
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            await PhysicsFramesAsync(2);
            await ProcessFrameAsync();
            Require(player.Read().Rac1TraversalAction == 0x11 && CurrentPlayerAvatarSourceSequence() == 35,
                $"synthetic opposing-wall slot did not select native wall jump/sequence35: " +
                $"action={player.Read().Rac1TraversalAction}, pos={player.Read().GlobalPosition - fixture.GlobalPosition}");
            Vector3 wallJumpStart = player.Read().GlobalPosition;
            await PhysicsFramesAsync(12);
            Require(player.Read().GlobalPosition.X > wallJumpStart.X + .1f &&
                player.Read().GlobalPosition.Y > wallJumpStart.Y + .15f,
                $"synthetic wall jump did not launch upward and away from the facing wall: " +
                $"delta={player.Read().GlobalPosition - wallJumpStart}, velocity={player.Read().Velocity}");
            Log($"[movement-smoke] synthetic wall-jump slot/action11/sequence35 passed; " +
                $"delta={player.Read().GlobalPosition - wallJumpStart}");

            ClearMovementSmokeInput();
            player.SetYaw(0d);
            player.ResetAt(fixture.GlobalPosition + new Vector3(20f, .08f, 80f));
            await PhysicsFramesAsync(4);
            for (int tick = 0; tick < 60 && !player.Read().Grounded; tick++)
                await PhysicsFramesAsync(1);
            Require(player.Read().Grounded, $"synthetic ice spawn did not settle: " +
                $"pos={player.Read().GlobalPosition - fixture.GlobalPosition}, velocity={player.Read().Velocity}, " +
                $"phase={player.Read().Rac1MovementPhase}, ice={player.Read().Rac1IceActive}");
            Vector3 iceAccelerationStart = player.Read().GlobalPosition;
            float maximumIceSpeed = 0f;
            for (int tick = 0; tick < 210; tick++)
            {
                SetSyntheticNativeHeading(player, 0d);
                await PhysicsFramesAsync(1);
                maximumIceSpeed = Math.Max(maximumIceSpeed, HorizontalSpeed(player));
            }
            Require(player.Read().Rac1IceActive && player.Read().Grounded,
                "synthetic raw-class7 floor did not select ice in the actual host");
            float iceRun = HorizontalSpeed(player);
            Require(iceRun > 5.5f, $"synthetic ice did not accelerate toward its native target: {iceRun:F4}, " +
                $"max={maximumIceSpeed:F4}, travel={player.Read().GlobalPosition - iceAccelerationStart}, " +
                $"delta={player.Read().PositionDelta}, realVelocity={player.Read().RealVelocity}, " +
                $"velocity={player.Read().Velocity}, phase={player.Read().Rac1MovementPhase}, ice={player.Read().Rac1IceActive}");
            ClearMovementSmokeInput();
            await PhysicsFramesAsync(8);
            float neutralIce = HorizontalSpeed(player);
            Require(neutralIce > iceRun * .90f,
                $"synthetic neutral ice lost its retained planar contact motion: {iceRun:F4}->{neutralIce:F4}");
            Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
            await PhysicsFramesAsync(3);
            float earlyCrouch = HorizontalSpeed(player);
            ulong earlyCrouchPhysicsFrame = Driver.PhysicsFrame;
            await PhysicsFramesAsync(20);
            await ProcessFrameAsync();
            float lateCrouch = HorizontalSpeed(player);
            ulong crouchPhysicsTicks = Driver.PhysicsFrame - earlyCrouchPhysicsFrame;
            float expectedLoss = (float)(Rac1SurfaceMovementSession.IceCrouchBrake * 60d * crouchPhysicsTicks);
            Require(player.Read().Rac1IceActive && CurrentPlayerAvatarSourceSequence() == 13,
                "synthetic ice crouch lost its source surface branch or animation");
            Require(lateCrouch < earlyCrouch && lateCrouch > iceRun * .60f &&
                Math.Abs((earlyCrouch - lateCrouch) - expectedLoss) < .08f,
                $"synthetic ice crouch brake changed: {earlyCrouch:F4}->{lateCrouch:F4}, expectedLoss={expectedLoss:F4}");
            Log($"[movement-smoke] synthetic class7 ice/crouch passed; run={iceRun:F4}, " +
                $"neutral={neutralIce:F4}, crouch={earlyCrouch:F4}->{lateCrouch:F4}");
        }
        finally
        {
            await RestoreFixturePlayerAsync(fixture, saved);
        }

        async Task GrabSyntheticLedgeAsync()
        {
            ClearMovementSmokeInput();
            player.SetYaw(0d);
            player.ResetAt(fixture.GlobalPosition + new Vector3(2f, .08f, 0f));
            await PhysicsFramesAsync(4);
            await WaitForGroundedAsync(player, 60);
            for (int tick = 0; tick < 65; tick++)
            {
                SetSyntheticNativeHeading(player, 0d);
                await PhysicsFramesAsync(1);
            }
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            bool grabbed = false;
            for (int tick = 0; tick < 140; tick++)
            {
                if (tick == 16)
                { Driver.SetAction(MovementDiagnosticDriver.Jump, false); }
                SetSyntheticNativeHeading(player, 0d);
                await PhysicsFramesAsync(1);
                if (player.Read().Rac1Hanging)
                {
                    grabbed = true;
                    break;
                }
            }
            ClearMovementSmokeInput();
            Require(grabbed, $"synthetic ordinary jump did not grab the authored edge: " +
                $"position={player.Read().GlobalPosition - fixture.GlobalPosition}, action={player.Read().Rac1TraversalAction}");
            for (int tick = 0; tick < 90 && player.Read().Rac1TraversalAction != 0x19; tick++)
                await PhysicsFramesAsync(1);
            await ProcessFrameAsync();
        }
    }

}
