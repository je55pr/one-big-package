using Godot;
using OBP.Godot;
using OBP.RAC1.Player;
using OBP.Runtime;

namespace OBP.Game;

public partial class OBPGame
{
    /// <summary>
    /// Synthetic Godot fixtures exercise the production host/query/animation
    /// adapters. They are integration checks, not new retail trajectory evidence.
    /// </summary>
    private async System.Threading.Tasks.Task RequireRac1TraversalContactSmokeAsync(PlayerHost player)
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
            Name = "SyntheticRac1TraversalContactFixture",
            Position = new Vector3(2000f, 64f, 2000f),
        };
        const float ledgeTop = 3.0f;
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
            Require(player.Rac1TraversalAction == 0x19,
                "synthetic ledge grab did not reach the native hanging action");
            Require(CurrentPlayerAvatarSourceSequence() == 33 && PlayerAvatarPresentationIsSynchronized(),
                "synthetic ledge hang did not publish synchronized native sequence33");
            Vector3 shimmyStart = player.GlobalPosition;
            bool sawShimmy = false;
            for (int tick = 0; tick < 70; tick++)
            {
                SetSyntheticNativeHeading(player, Math.PI / 2d);
                await PhysicsFramesAsync(1);
                sawShimmy |= player.Rac1TraversalAction is 0x1a or 0x1b;
            }
            Require(player.Rac1Hanging && sawShimmy &&
                Math.Abs(player.GlobalPosition.Z - shimmyStart.Z) > .15f,
                $"synthetic ledge shimmy did not move along the edge: action={player.Rac1TraversalAction}, " +
                $"delta={player.GlobalPosition - shimmyStart}");
            ClearMovementSmokeInput();
            for (int tick = 0; tick < 90 && player.Rac1TraversalAction != 0x19; tick++)
                await PhysicsFramesAsync(1);
            Require(player.Rac1TraversalAction == 0x19, "synthetic ledge shimmy did not settle back to hang");

            float jumpStart = player.GlobalPosition.Y;
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(2);
            Input.ActionRelease(RawGamepadInput.Jump);
            Require(!player.Rac1Hanging && player.Rac1TraversalAction == 0x1c,
                $"synthetic ledge Cross did not select native ledge jump: action={player.Rac1TraversalAction}");
            float jumpApex = jumpStart;
            for (int tick = 0; tick < 65; tick++)
            {
                SetSyntheticNativeHeading(player, 0d);
                await PhysicsFramesAsync(1);
                jumpApex = Math.Max(jumpApex, player.GlobalPosition.Y);
            }
            Require(jumpApex > jumpStart + .75f,
                $"synthetic ledge jump failed to launch: rise={jumpApex - jumpStart:F4}");
            GD.Print($"[movement-smoke] synthetic ledge grab/hang/shimmy/Cross jump passed; rise={jumpApex - jumpStart:F4}");

            await GrabSyntheticLedgeAsync();
            float dropStart = player.GlobalPosition.Y;
            Input.ActionPress(RawGamepadInput.Crouch);
            await PhysicsFramesAsync(2);
            Input.ActionRelease(RawGamepadInput.Crouch);
            await PhysicsFramesAsync(5);
            Require(!player.Rac1Hanging && player.GlobalPosition.Y < dropStart - .01f,
                "synthetic ledge drop action failed to release downward");
            GD.Print("[movement-smoke] synthetic ledge drop release passed");

            ClearMovementSmokeInput();
            player.Rac1CurrentYaw = 0d;
            player.ApplyDevelopmentReset(fixture.GlobalPosition + new Vector3(0f, .08f, 30f));
            await PhysicsFramesAsync(4);
            await WaitForGroundedAsync(player, 60);
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(11);
            Input.ActionRelease(RawGamepadInput.Jump);
            await PhysicsFramesAsync(2);
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(2);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(player.Rac1TraversalAction == 0x11 && CurrentPlayerAvatarSourceSequence() == 35,
                $"synthetic opposing-wall slot did not select native wall jump/sequence35: " +
                $"action={player.Rac1TraversalAction}, pos={player.GlobalPosition - fixture.GlobalPosition}");
            Vector3 wallJumpStart = player.GlobalPosition;
            await PhysicsFramesAsync(12);
            Require(player.GlobalPosition.X > wallJumpStart.X + .1f &&
                player.GlobalPosition.Y > wallJumpStart.Y + .15f,
                $"synthetic wall jump did not launch upward and away from the facing wall: " +
                $"delta={player.GlobalPosition - wallJumpStart}, velocity={player.Velocity}");
            GD.Print($"[movement-smoke] synthetic wall-jump slot/action11/sequence35 passed; " +
                $"delta={player.GlobalPosition - wallJumpStart}");

            ClearMovementSmokeInput();
            player.Rac1CurrentYaw = 0d;
            player.ApplyDevelopmentReset(fixture.GlobalPosition + new Vector3(20f, .08f, 80f));
            await PhysicsFramesAsync(4);
            for (int tick = 0; tick < 60 && !player.IsOnFloor(); tick++)
                await PhysicsFramesAsync(1);
            Require(player.IsOnFloor(), $"synthetic ice spawn did not settle: " +
                $"pos={player.GlobalPosition - fixture.GlobalPosition}, velocity={player.Velocity}, " +
                $"phase={player.Rac1MovementPhase}, ice={player.Rac1IceActive}");
            Vector3 iceAccelerationStart = player.GlobalPosition;
            float maximumIceSpeed = 0f;
            for (int tick = 0; tick < 210; tick++)
            {
                SetSyntheticNativeHeading(player, 0d);
                await PhysicsFramesAsync(1);
                maximumIceSpeed = Math.Max(maximumIceSpeed, HorizontalSpeed(player));
            }
            Require(player.Rac1IceActive && player.IsOnFloor(),
                "synthetic raw-class7 floor did not select ice in the actual host");
            float iceRun = HorizontalSpeed(player);
            Require(iceRun > 5.5f, $"synthetic ice did not accelerate toward its native target: {iceRun:F4}, " +
                $"max={maximumIceSpeed:F4}, travel={player.GlobalPosition - iceAccelerationStart}, " +
                $"delta={player.GetPositionDelta()}, realVelocity={player.GetRealVelocity()}, " +
                $"velocity={player.Velocity}, phase={player.Rac1MovementPhase}, ice={player.Rac1IceActive}");
            ClearMovementSmokeInput();
            await PhysicsFramesAsync(8);
            float neutralIce = HorizontalSpeed(player);
            Require(neutralIce > iceRun * .90f,
                $"synthetic neutral ice lost its retained planar contact motion: {iceRun:F4}->{neutralIce:F4}");
            Input.ActionPress(RawGamepadInput.Crouch);
            await PhysicsFramesAsync(3);
            float earlyCrouch = HorizontalSpeed(player);
            ulong earlyCrouchPhysicsFrame = Engine.GetPhysicsFrames();
            await PhysicsFramesAsync(20);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float lateCrouch = HorizontalSpeed(player);
            ulong crouchPhysicsTicks = Engine.GetPhysicsFrames() - earlyCrouchPhysicsFrame;
            float expectedLoss = (float)(Rac1SurfaceMovementSession.IceCrouchBrake * 60d * crouchPhysicsTicks);
            Require(player.Rac1IceActive && CurrentPlayerAvatarSourceSequence() == 13,
                "synthetic ice crouch lost its source surface branch or animation");
            Require(lateCrouch < earlyCrouch && lateCrouch > iceRun * .60f &&
                Math.Abs((earlyCrouch - lateCrouch) - expectedLoss) < .08f,
                $"synthetic ice crouch brake changed: {earlyCrouch:F4}->{lateCrouch:F4}, expectedLoss={expectedLoss:F4}");
            GD.Print($"[movement-smoke] synthetic class7 ice/crouch passed; run={iceRun:F4}, " +
                $"neutral={neutralIce:F4}, crouch={earlyCrouch:F4}->{lateCrouch:F4}");
        }
        finally
        {
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

        async System.Threading.Tasks.Task GrabSyntheticLedgeAsync()
        {
            ClearMovementSmokeInput();
            player.Rac1CurrentYaw = 0d;
            player.ApplyDevelopmentReset(fixture.GlobalPosition + new Vector3(2f, .08f, 0f));
            await PhysicsFramesAsync(4);
            await WaitForGroundedAsync(player, 60);
            for (int tick = 0; tick < 65; tick++)
            {
                SetSyntheticNativeHeading(player, 0d);
                await PhysicsFramesAsync(1);
            }
            Input.ActionPress(RawGamepadInput.Jump);
            bool grabbed = false;
            for (int tick = 0; tick < 140; tick++)
            {
                if (tick == 16)
                    Input.ActionRelease(RawGamepadInput.Jump);
                SetSyntheticNativeHeading(player, 0d);
                await PhysicsFramesAsync(1);
                if (player.Rac1Hanging)
                {
                    grabbed = true;
                    break;
                }
            }
            ClearMovementSmokeInput();
            Require(grabbed, $"synthetic ordinary jump did not grab the authored edge: " +
                $"position={player.GlobalPosition - fixture.GlobalPosition}, action={player.Rac1TraversalAction}");
            for (int tick = 0; tick < 90 && player.Rac1TraversalAction != 0x19; tick++)
                await PhysicsFramesAsync(1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static void SetSyntheticNativeHeading(PlayerHost player, double nativeYaw, double magnitude = 1d)
    {
        double controlHeading = player.Rac1RuntimeCameraState?.ControlHeadingRadians ?? player.Rac1ControlYaw;
        double stickAngle = controlHeading - nativeYaw;
        SetRac1SmokeConditionedInput(Math.Sin(stickAngle) * magnitude, Math.Cos(stickAngle) * magnitude);
    }

    private static void AddSyntheticTraversalMesh(
        Node3D parent,
        string name,
        int rawFaceType,
        params (Vector3 A, Vector3 B, Vector3 C)[] triangles)
    {
        var positions = new double[triangles.Length * 9];
        var indices = new int[triangles.Length * 3];
        var faceTypes = Enumerable.Repeat(rawFaceType, triangles.Length).ToArray();
        for (int triangle = 0; triangle < triangles.Length; triangle++)
        {
            var (a, b, c) = triangles[triangle];
            Vector3[] vertices = [a, b, c];
            for (int vertex = 0; vertex < 3; vertex++)
            {
                Vector3 value = vertices[vertex];
                int offset = triangle * 9 + vertex * 3;
                positions[offset] = -value.X;
                positions[offset + 1] = value.Y;
                positions[offset + 2] = value.Z;
                indices[triangle * 3 + vertex] = triangle * 3 + vertex;
            }
        }
        var body = new RuntimeWorldScene.RuntimeCollisionBody3D { Name = name };
        var blob = new RuntimeCollisionBlob(1, positions, indices, faceTypes);
        var plan = RuntimeWorldScene.BuildCollisionFacePlan(blob);
        body.Configure(blob, -1, plan.SourceTriangleIndices);
        body.AddChild(new CollisionShape3D
        {
            Shape = new ConcavePolygonShape3D { Data = plan.Faces, BackfaceCollision = true },
        });
        parent.AddChild(body);
    }
}
