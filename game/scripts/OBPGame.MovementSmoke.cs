using Godot;
using OBP.Core;

namespace OneBigPackage;

public partial class OBPGame
{
    private async System.Threading.Tasks.Task RunMovementSmokeAsync(ObpDestination destination)
    {
        DebugPlayer player = _player
            ?? throw new InvalidOperationException("movement smoke requires a spawned player");

        try
        {
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            await WaitForGroundedAsync(player, 360);

            Require(_sceneResult?.CollisionBodies > 0, "world has no Godot collision body");
            Require(player.IsOnFloor(), "player did not settle on Godot collision");
            Require(
                player.MovementControllerLabel == "rac1-retail-derived-common-base",
                "unexpected ordinary movement controller");

            SetAnalogueSmokeInput(0f, 0.60f);
            await PhysicsFramesAsync(48);
            float walkSpeed = HorizontalSpeed(player);
            double walkMagnitude = player.Rac1AnalogueMagnitude;
            double walkTarget = player.Rac1TargetPlanarStep;
            Require(walkMagnitude >= 0.25d && walkMagnitude < 0.83d, "low stick did not land in the recovered walk band");
            Require(walkTarget > 0d && walkTarget < 0.03d, "low stick did not select the walk plateau");
            Require(walkSpeed > 0.1f, "low stick produced no host movement");

            SetAnalogueSmokeInput(0f, 1f);
            await PhysicsFramesAsync(48);
            float runSpeed = HorizontalSpeed(player);
            double cardinalYaw = player.Rac1MovementTargetYaw;
            Require(runSpeed > walkSpeed + 1.0f, "full stick did not progress from walk toward run");
            Require(player.Rac1TargetPlanarStep > walkTarget, "full stick target did not exceed walk target");

            SetAnalogueSmokeInput(0.65f, 0.80f);
            await PhysicsFramesAsync(24);
            double diagonalYaw = player.Rac1MovementTargetYaw;
            Require(
                Math.Abs(WrapAngle(diagonalYaw - cardinalYaw)) > 0.15d,
                "arbitrary-angle stick input did not change steering target");

            ClearMovementSmokeInput();
            Input.ActionPress(RawGamepadInput.Crouch);
            await PhysicsFramesAsync(4);
            Require(
                player.Rac1LocomotionState.ToString() is "Crouched" or "CrouchTurning",
                "crouch action did not reach the common controller");
            Input.ActionRelease(RawGamepadInput.Crouch);
            await PhysicsFramesAsync(4);

            player.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            float shortJump = await MeasureJumpAsync(player, holdFrames: 2, applyPartialAirControl: false);

            player.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            float longJump = await MeasureJumpAsync(player, holdFrames: 16, applyPartialAirControl: false);
            Require(longJump > shortJump + 0.20f, "held jump did not exceed tap jump apex");

            player.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            float partialAirTravel = await MeasureAirControlAsync(player);
            Require(partialAirTravel > 0.10f, "partial-stick air control produced no planar travel");

            player.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            SendPhysicalKey(Key.W, true);
            await PhysicsFramesAsync(30);
            float keyboardSpeed = HorizontalSpeed(player);
            SendPhysicalKey(Key.W, false);
            Require(keyboardSpeed > 0.1f, "WASD keyboard fallback produced no movement");
            await PhysicsFramesAsync(3);
            Vector3 beforeFly = player.GlobalPosition;
            await TapPhysicalKeyAsync(Key.F);
            Require(player.DevelopmentFlyEnabled, "F did not enable development fly");
            SendPhysicalKey(Key.W, true);
            await PhysicsFramesAsync(18);
            SendPhysicalKey(Key.W, false);
            float flyTravel = HorizontalDistance(beforeFly, player.GlobalPosition);
            Require(flyTravel > 0.5f, "development fly did not move with keyboard input");
            await TapPhysicalKeyAsync(Key.F);
            Require(!player.DevelopmentFlyEnabled, "F did not disable development fly");

            Vector3 spawn = player.DebugSpawnPosition;
            await TapPhysicalKeyAsync(Key.R);
            await WaitForGroundedAsync(player, 180);
            float respawnError = HorizontalDistance(spawn, player.GlobalPosition);
            Require(respawnError < 1.0f, "development respawn did not return to the host spawn");
            Require(player.IsOnFloor(), "respawned player did not re-enter Godot floor contact");

            GD.Print(
                $"[movement-smoke] PASS {destination.DestinationId} " +
                $"walk={walkSpeed:0.000} run={runSpeed:0.000} shortJump={shortJump:0.000} " +
                $"longJump={longJump:0.000} partialAir={partialAirTravel:0.000} keyboard={keyboardSpeed:0.000} " +
                $"fly={flyTravel:0.000} respawnError={respawnError:0.000}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[movement-smoke] FAIL {destination.DestinationId}: {ex.Message}");
            GetTree().Quit(1);
        }
        finally
        {
            ClearMovementSmokeInput();
            SendPhysicalKey(Key.W, false);
            SendPhysicalKey(Key.Space, false);
        }
    }

    private async System.Threading.Tasks.Task<float> MeasureJumpAsync(
        DebugPlayer player,
        int holdFrames,
        bool applyPartialAirControl)
    {
        float baseline = player.GlobalPosition.Y;
        float apex = baseline;
        bool sawAir = false;

        Input.ActionPress(RawGamepadInput.Jump);
        await PhysicsFramesAsync(holdFrames);
        Input.ActionRelease(RawGamepadInput.Jump);

        for (int frame = 0; frame < 240; frame++)
        {
            await PhysicsFramesAsync(1);
            if (!player.IsOnFloor())
            {
                sawAir = true;
                if (applyPartialAirControl && frame == 0)
                    SetAnalogueSmokeInput(0.58f, 0f);
            }

            apex = Math.Max(apex, player.GlobalPosition.Y);
            if (sawAir && player.IsOnFloor())
            {
                ClearMovementSmokeInput();
                return apex - baseline;
            }
        }

        throw new InvalidOperationException("jump did not land within smoke timeout");
    }

    private async System.Threading.Tasks.Task<float> MeasureAirControlAsync(DebugPlayer player)
    {
        Vector3 start = player.GlobalPosition;
        Input.ActionPress(RawGamepadInput.Jump);
        await PhysicsFramesAsync(10);
        Input.ActionRelease(RawGamepadInput.Jump);

        bool sawAir = false;
        for (int frame = 0; frame < 240; frame++)
        {
            await PhysicsFramesAsync(1);
            if (!player.IsOnFloor())
            {
                if (!sawAir)
                {
                    sawAir = true;
                    start = player.GlobalPosition;
                    SetAnalogueSmokeInput(0.58f, 0f);
                }
            }
            else if (sawAir)
            {
                float travel = HorizontalDistance(start, player.GlobalPosition);
                ClearMovementSmokeInput();
                return travel;
            }
        }

        throw new InvalidOperationException("partial-air-control jump did not land within smoke timeout");
    }

    private async System.Threading.Tasks.Task WaitForGroundedAsync(DebugPlayer player, int maxFrames)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            await PhysicsFramesAsync(1);
            if (player.IsOnFloor())
                return;
        }

        throw new InvalidOperationException("player did not reach Godot floor contact within smoke timeout");
    }

    private async System.Threading.Tasks.Task PhysicsFramesAsync(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
    private async System.Threading.Tasks.Task TapPhysicalKeyAsync(Key key)
    {
        SendPhysicalKey(key, true);
        await PhysicsFramesAsync(2);
        SendPhysicalKey(key, false);
        await PhysicsFramesAsync(2);
    }

    private static void SendPhysicalKey(Key key, bool pressed)
    {
        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = pressed,
            Echo = false,
        });
    }

    private static void SetAnalogueSmokeInput(float right, float forward)
    {
        ReleaseMovementActions();
        if (right > 0f) Input.ActionPress(RawGamepadInput.MoveRight, right);
        else if (right < 0f) Input.ActionPress(RawGamepadInput.MoveLeft, -right);

        if (forward > 0f) Input.ActionPress(RawGamepadInput.MoveForward, forward);
        else if (forward < 0f) Input.ActionPress(RawGamepadInput.MoveBack, -forward);
    }

    private static void ClearMovementSmokeInput()
    {
        ReleaseMovementActions();
        Input.ActionRelease(RawGamepadInput.Jump);
        Input.ActionRelease(RawGamepadInput.Crouch);
        Input.ActionRelease(RawGamepadInput.Action);
    }

    private static void ReleaseMovementActions()
    {
        Input.ActionRelease(RawGamepadInput.MoveLeft);
        Input.ActionRelease(RawGamepadInput.MoveRight);
        Input.ActionRelease(RawGamepadInput.MoveForward);
        Input.ActionRelease(RawGamepadInput.MoveBack);
    }

    private static float HorizontalSpeed(DebugPlayer player) =>
        new Vector2(player.Velocity.X, player.Velocity.Z).Length();

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        new Vector2(a.X - b.X, a.Z - b.Z).Length();

    private static double WrapAngle(double value)
    {
        while (value > Math.PI) value -= Math.Tau;
        while (value <= -Math.PI) value += Math.Tau;
        return value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
