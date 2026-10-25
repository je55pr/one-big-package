using Godot;
using OBP.RAC1.Player;

namespace OBP.Game;

public partial class OBPGame
{
    private async System.Threading.Tasks.Task RequireRac1AirborneSmokeAsync(PlayerHost player)
    {
        var shape = player.GetChildren().OfType<CollisionShape3D>().Single().Shape as CapsuleShape3D;
        Require(shape is not null && Math.Abs(shape.Radius - .45f) < .000001f && shape.Height < 1.5f,
            "R&C1 still uses the oversized debug collision envelope");
        foreach (int pack in new[] { 2, 3 })
        {
            SelectPackPreview(pack);
            ClearMovementSmokeInput();
            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            Input.ActionPress(RawGamepadInput.Crouch);
            await PhysicsFramesAsync(8);
            float startHeight = player.GlobalPosition.Y;
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(2);
            int expectedAction = pack == 2 ? 15 : 13;
            Require(player.Rac1TraversalAction == expectedAction,
                $"pack {pack} high jump did not enter action {expectedAction}; actual={player.Rac1TraversalAction}");
            Require(CurrentPlayerAvatarSourceSequence() == (pack == 2 ? 21 : 17), "pack jump selected the wrong native clip");
            Input.ActionRelease(RawGamepadInput.Crouch);
            bool airborne = false, landed = false;
            float apex = startHeight;
            for (int tick = 0; tick < 240; tick++)
            {
                if (tick == 24) Input.ActionRelease(RawGamepadInput.Jump);
                await PhysicsFramesAsync(1);
                apex = Math.Max(apex, player.GlobalPosition.Y);
                airborne |= !player.IsOnFloor();
                if (airborne && player.IsOnFloor() && tick > 12) { landed = true; break; }
            }
            Require(airborne && landed && apex - startHeight > 1f,
                $"pack {pack} high jump failed contact/height/landing: apex={apex - startHeight:0.000}");
            GD.Print($"[traversal-smoke] pack={pack} high action={expectedAction} apex={apex - startHeight:0.000}");
        }

        SelectPackPreview(null);
        ClearMovementSmokeInput();
        player.DevelopmentControls.ResetToSpawn();
        await WaitForGroundedAsync(player, 180);
        Input.ActionPress(RawGamepadInput.Jump);
        await PhysicsFramesAsync(11);
        Input.ActionRelease(RawGamepadInput.Jump);
        await PhysicsFramesAsync(4);
        Input.ActionPress(RawGamepadInput.Jump);
        for (int tick = 0; tick < 12 && player.Rac1TraversalAction != 14; tick++)
            await PhysicsFramesAsync(1);
        Require(player.Rac1TraversalAction == 14 && CurrentPlayerAvatarSourceSequence() == 22,
            "second Cross did not enter the native aerial boost/sequence 22");
        GD.Print("[traversal-smoke] second Cross entered native aerial boost 0x0e");

        SelectPackPreview(2);
        ClearMovementSmokeInput();
        player.DevelopmentControls.ResetToSpawn();
        await WaitForGroundedAsync(player, 180);
        Input.ActionPress(RawGamepadInput.Jump);
        await PhysicsFramesAsync(15);
        Require(player.Rac1TraversalAction is null, "first held Cross incorrectly entered a pack action");
        Input.ActionRelease(RawGamepadInput.Jump);
        await PhysicsFramesAsync(2);
        Input.ActionPress(RawGamepadInput.Jump);
        for (int tick = 0; tick < 70 && player.Rac1TraversalAction != 8; tick++)
            await PhysicsFramesAsync(1);
        Require(player.Rac1TraversalAction == 8 && CurrentPlayerAvatarSourceSequence() == 19,
            $"Heli-Pack second Cross never reached native glide; actual={player.Rac1TraversalAction}");
        Require(Math.Abs(player.Velocity.Y / 60f + .036f) < .002f,
            "Heli-Pack glide did not use its source constant descent");
        GD.Print("[traversal-smoke] Heli-Pack boost-to-glide selected sequence 19 and native descent");

        SelectPackPreview(null);
        ClearMovementSmokeInput();
        player.DevelopmentControls.ResetToSpawn();
        await WaitForGroundedAsync(player, 180);
        Input.ActionPress(RawGamepadInput.Crouch);
        Input.ActionPress(RawGamepadInput.MoveRight);
        Input.ActionPress(RawGamepadInput.Jump);
        await PhysicsFramesAsync(2);
        Require(player.Rac1TraversalAction == 11 && CurrentPlayerAvatarSourceSequence() is 28 or 29 or 31,
            "directional crouch/Cross chord did not enter the native flip family");
        GD.Print("[traversal-smoke] directional flip selected native action 0x0b");
        SelectPackPreview(3);
        ClearMovementSmokeInput();
        player.DevelopmentControls.ResetToSpawn();
        await WaitForGroundedAsync(player, 180);
        Input.ActionPress(RawGamepadInput.Jump);
        await PhysicsFramesAsync(16);
        Input.ActionRelease(RawGamepadInput.Jump);
        await PhysicsFramesAsync(2);
        Input.ActionPress(RawGamepadInput.Crouch);
        await PhysicsFramesAsync(2);
        Require(player.Rac1TraversalAction == 0x22 && CurrentPlayerAvatarSourceSequence() == 42,
            "airborne Thruster-Pack crouch did not enter Power Slam");
        Input.ActionRelease(RawGamepadInput.Crouch);
        bool sawDive = false;
        for (int tick = 0; tick < 200 && player.Rac1TraversalAction == 0x22; tick++)
        {
            await PhysicsFramesAsync(1);
            sawDive |= player.Velocity.Y < -1f;
        }
        Require(sawDive && player.Rac1TraversalAction is null && player.IsOnFloor(),
            $"Power Slam did not dive, contact the floor and return control after animation exit: " +
            $"dive={sawDive} action={player.Rac1TraversalAction} floor={player.IsOnFloor()} " +
            $"vertical={player.Velocity.Y:0.000} animation={player.Rac1TraversalAuthoredAnimationTime:0.000} " +
            $"cycle={player.Rac1TraversalAnimationCycleCompleted}");
        GD.Print("[traversal-smoke] Thruster-Pack Power Slam rise/dive/impact/recovery passed");
        SelectPackPreview(null);
        ClearMovementSmokeInput();
        player.DevelopmentControls.ResetToSpawn();
        await WaitForGroundedAsync(player, 180);

        void SelectPackPreview(int? pack)
        {
            for (int attempt = 0; attempt < 3 && player.Rac1DevelopmentPackPreview != pack; attempt++)
                player.CycleRac1DevelopmentPack();
            Require(player.Rac1DevelopmentPackPreview == pack, "could not select the development pack preview");
        }
    }
}
