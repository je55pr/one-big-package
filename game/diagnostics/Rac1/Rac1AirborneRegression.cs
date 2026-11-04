using System.Numerics;
using System.Buffers.Binary;
using OBP.Core;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public sealed class Rac1AirborneRegression(MovementDiagnosticContext context) : MovementScenarioSupport(context)
{
    public async Task RunAsync()
    {
        var player = Player;
        var shape = player.Read();
        Require(shape.CapsuleRadius is { } radius && Math.Abs(radius - .45f) < .000001f && shape.CapsuleHeight < 1.5f,
            "R&C1 still uses the oversized debug collision envelope");
        foreach (int pack in new[] { 2, 3 })
        {
            SelectPackPreview(pack);
            ClearMovementSmokeInput();
            Player.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
            await PhysicsFramesAsync(8);
            float startHeight = player.Read().GlobalPosition.Y;
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            await PhysicsFramesAsync(2);
            int expectedAction = pack == 2 ? 15 : 13;
            Require(player.Read().Rac1TraversalAction == expectedAction,
                $"pack {pack} high jump did not enter action {expectedAction}; actual={player.Read().Rac1TraversalAction}");
            Require(CurrentPlayerAvatarSourceSequence() == (pack == 2 ? 21 : 17), "pack jump selected the wrong native clip");
            Driver.SetAction(MovementDiagnosticDriver.Crouch, false);
            bool airborne = false, landed = false;
            float apex = startHeight;
            for (int tick = 0; tick < 240; tick++)
            {
                if (tick == 24) { Driver.SetAction(MovementDiagnosticDriver.Jump, false); }
                await PhysicsFramesAsync(1);
                apex = Math.Max(apex, player.Read().GlobalPosition.Y);
                airborne |= !player.Read().Grounded;
                if (airborne && player.Read().Grounded && tick > 12) { landed = true; break; }
            }
            Require(airborne && landed && apex - startHeight > 1f,
                $"pack {pack} high jump failed contact/height/landing: apex={apex - startHeight:0.000}");
            Log($"[traversal-smoke] pack={pack} high action={expectedAction} apex={apex - startHeight:0.000}");
        }

        SelectPackPreview(null);
        ClearMovementSmokeInput();
        Player.ResetToSpawn();
        await WaitForGroundedAsync(player, 180);
        Driver.SetAction(MovementDiagnosticDriver.Jump, true);
        await PhysicsFramesAsync(11);
        Driver.SetAction(MovementDiagnosticDriver.Jump, false);
        await PhysicsFramesAsync(4);
        Driver.SetAction(MovementDiagnosticDriver.Jump, true);
        for (int tick = 0; tick < 12 && player.Read().Rac1TraversalAction != 14; tick++)
            await PhysicsFramesAsync(1);
        Require(player.Read().Rac1TraversalAction == 14 && CurrentPlayerAvatarSourceSequence() == 22,
            "second Cross did not enter the native aerial boost/sequence 22");
        Log("[traversal-smoke] second Cross entered native aerial boost 0x0e");

        SelectPackPreview(2);
        ClearMovementSmokeInput();
        Player.ResetToSpawn();
        await WaitForGroundedAsync(player, 180);
        Driver.SetAction(MovementDiagnosticDriver.Jump, true);
        await PhysicsFramesAsync(15);
        Require(player.Read().Rac1TraversalAction is null, "first held Cross incorrectly entered a pack action");
        Driver.SetAction(MovementDiagnosticDriver.Jump, false);
        await PhysicsFramesAsync(2);
        Driver.SetAction(MovementDiagnosticDriver.Jump, true);
        for (int tick = 0; tick < 70 && player.Read().Rac1TraversalAction != 8; tick++)
            await PhysicsFramesAsync(1);
        Require(player.Read().Rac1TraversalAction == 8 && CurrentPlayerAvatarSourceSequence() == 19,
            $"Heli-Pack second Cross never reached native glide; actual={player.Read().Rac1TraversalAction}");
        Require(Math.Abs(player.Read().Velocity.Y / 60f + .036f) < .002f,
            "Heli-Pack glide did not use its source constant descent");
        Log("[traversal-smoke] Heli-Pack boost-to-glide selected sequence 19 and native descent");

        SelectPackPreview(null);
        ClearMovementSmokeInput();
        Player.ResetToSpawn();
        await WaitForGroundedAsync(player, 180);
        Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
        Driver.SetAction(MovementDiagnosticDriver.Right, true);
        Driver.SetAction(MovementDiagnosticDriver.Jump, true);
        await PhysicsFramesAsync(2);
        Require(player.Read().Rac1TraversalAction == 11 && CurrentPlayerAvatarSourceSequence() is 28 or 29 or 31,
            "directional crouch/Cross chord did not enter the native flip family");
        Log("[traversal-smoke] directional flip selected native action 0x0b");
        SelectPackPreview(3);
        ClearMovementSmokeInput();
        Player.ResetToSpawn();
        await WaitForGroundedAsync(player, 180);
        Driver.SetAction(MovementDiagnosticDriver.Jump, true);
        await PhysicsFramesAsync(16);
        Driver.SetAction(MovementDiagnosticDriver.Jump, false);
        await PhysicsFramesAsync(2);
        Driver.SetAction(MovementDiagnosticDriver.Crouch, true);
        await PhysicsFramesAsync(2);
        Require(player.Read().Rac1TraversalAction == 0x22 && CurrentPlayerAvatarSourceSequence() == 42,
            "airborne Thruster-Pack crouch did not enter Power Slam");
        Driver.SetAction(MovementDiagnosticDriver.Crouch, false);
        bool sawDive = false;
        for (int tick = 0; tick < 200 && player.Read().Rac1TraversalAction == 0x22; tick++)
        {
            await PhysicsFramesAsync(1);
            sawDive |= player.Read().Velocity.Y < -1f;
        }
        Require(sawDive && player.Read().Rac1TraversalAction is null && player.Read().Grounded,
            $"Power Slam did not dive, contact the floor and return control after animation exit: " +
            $"dive={sawDive} action={player.Read().Rac1TraversalAction} floor={player.Read().Grounded} " +
            $"vertical={player.Read().Velocity.Y:0.000} animation={player.Read().Rac1TraversalAuthoredAnimationTime:0.000} " +
            $"cycle={player.Read().Rac1TraversalAnimationCycleCompleted}");
        Log("[traversal-smoke] Thruster-Pack Power Slam rise/dive/impact/recovery passed");
        SelectPackPreview(null);
        ClearMovementSmokeInput();
        Player.ResetToSpawn();
        await WaitForGroundedAsync(player, 180);

        void SelectPackPreview(int? pack)
        {
            for (int attempt = 0; attempt < 3 && player.Read().Rac1DevelopmentPackPreview != pack; attempt++)
                player.CyclePack();
            Require(player.Read().Rac1DevelopmentPackPreview == pack, "could not select the development pack preview");
        }
    }

}
