using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    /// <summary>Staged authored mechanism fixtures; never a claim of completing the human route.</summary>
    private async Task RunRac1NovalisRouteSmokeAsync()
    {
        try
        {
            Require(_world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 1 } && _player is not null, "Novalis route smoke needs LEVEL1");
            Require(_rac1NovalisMechanisms.Count == 18, "Missing authored Novalis mechanisms");
            ClearMovementSmokeInput();
            await WaitForGroundedAsync(_player!, 360);
            await RunRac1WorldRetailNavigationSmokeAsync("Novalis");
            ClearMovementSmokeInput();
            await PhysicsFramesAsync(5);
            var player = _player!;
            player.StageDevelopmentWaterPosition(new(-191f, 65.2f, 128.5f));
            await PhysicsFramesAsync(210);
            foreach (var (rock, _) in _rac1NovalisMechanisms.Where(m => m.Mechanism is Rac1NovalisFallingRock))
                Require(rock.State == 4 && Math.Abs(rock.Position.Z - (rock.Home.Z - 20)) < .001f,
                    $"Staircase rock {rock.InstanceIndex} did not land: state={rock.State}, pos={rock.Position}");
            GD.Print("[novalis-route-smoke] authored approach dropped all three staircase rocks");

            var (mechanism, node) = _rac1NovalisMechanisms.Single(m => m.Mechanism.InstanceIndex == 715);
            var piston = (Rac1NovalisPiston)mechanism;
            for (int i = 0; piston.End != 1 && i < 400; i++) await PhysicsFramesAsync(1);
            Require(piston.End == 1, "Piston never returned to lower rest");
            var ray = PhysicsRayQueryParameters3D.Create(node.Root.GlobalPosition + Vector3.Up * 8,
                node.Root.GlobalPosition - Vector3.Up * 2);
            ray.Exclude = new global::Godot.Collections.Array<Rid> { player.GetRid() };
            var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            Require(hit.Count > 0 && RuntimeWorldScene.FindDynamicObjectOwner(hit["collider"].As<Node>())?.InstanceIndex == 715,
                $"Piston top collider absent/occluded: {hit}");
            player.StageDevelopmentWaterPosition((Vector3)hit["position"] + Vector3.Up * .03f);
            await PhysicsFramesAsync(8);
            float start = player.GlobalPosition.Y, offset = start - node.Root.GlobalPosition.Y;
            float maximumRise = 0;
            for (int i = 0; i < 180; i++)
            {
                await PhysicsFramesAsync(1);
                maximumRise = Math.Max(maximumRise, player.GlobalPosition.Y - start);
                Require(player.Rac1MovementPhase == OBP.RAC1.Player.Rac1RatchetMovementPhase.Grounded &&
                    player.AnimationState == OBP.Runtime.Player.PlayerAnimationState.Idle,
                    $"Standing rider entered airborne animation: tick={i}, end={piston.End}, timer={piston.Timer}, phase={player.Rac1MovementPhase}, animation={player.AnimationState}, player={player.GlobalPosition}, platform={node.Root.GlobalPosition}, floor={player.IsOnFloor()}");
                Require(Math.Abs(player.GlobalPosition.Y - node.Root.GlobalPosition.Y - offset) < .08f,
                    $"Piston lost its rider: player={player.GlobalPosition}, platform={node.Root.GlobalPosition}, offset={offset}");
                if (piston.End == 0) break;
            }
            Require(maximumRise > 3.8f && player.Rac1RecoveredSupportGrounded, $"Piston carry failed: rise={maximumRise}, player={player.GlobalPosition}");
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(16);
            Input.ActionRelease(RawGamepadInput.Jump);
            Require(player.GlobalPosition.Y > start + maximumRise + .5f, "Cross did not jump from moving support");
            await PhysicsFramesAsync(65);
            Require(player.Rac1RecoveredSupportGrounded, "Jump did not land on original piston geometry");
            for (int i = 0; piston.End != 1 && i < 250; i++)
            {
                await PhysicsFramesAsync(1);
                Require(Math.Abs(player.GlobalPosition.Y - node.Root.GlobalPosition.Y - offset) < .08f,
                    "Descending piston lost its rider");
                Require(player.Rac1MovementPhase == OBP.RAC1.Player.Rac1RatchetMovementPhase.Grounded,
                    "Descending piston incorrectly entered falling");
            }
            Require(piston.End == 1, "Piston failed to complete its return with rider");
            GD.Print($"[novalis-route-smoke] original piston carried its rider {maximumRise:F3} units; Cross and landing PASS");

            var (doorMechanism, doorNode) = _rac1NovalisMechanisms.Single(m => m.Mechanism.InstanceIndex == 893);
            var door = (Rac1NovalisProximityDoor)doorMechanism;
            Vector3 centre = new(-door.Home.X, door.Home.Z, door.Home.Y);
            float yaw = door.NativeYaw;
            Vector3 across = new(MathF.Sin(yaw), 0, MathF.Cos(yaw));
            var doorwayRay = PhysicsRayQueryParameters3D.Create(centre + Vector3.Up * .8f - across * 3f,
                centre + Vector3.Up * .8f + across * 3f);
            doorwayRay.Exclude = new global::Godot.Collections.Array<Rid> { player.GetRid() };
            var closed = player.GetWorld3D().DirectSpaceState.IntersectRay(doorwayRay);
            Require(closed.Count > 0 && RuntimeWorldScene.FindDynamicObjectOwner(closed["collider"].As<Node>())?.NativeClassId is 768 or 769,
                "Closed doorway does not block its authored corridor");
            player.StageDevelopmentWaterPosition(centre + Vector3.Up * .2f);
            await PhysicsFramesAsync(30);
            Require(door.State == 3 && door.Progress == 1f && doorNode.Root.GlobalPosition.DistanceTo(centre) > 1.99f,
                $"Door did not open from player proximity: state={door.State}, player={player.GlobalPosition}, door={doorNode.Root.GlobalPosition}");
            var opened = player.GetWorld3D().DirectSpaceState.IntersectRay(doorwayRay);
            Require(opened.Count == 0 || RuntimeWorldScene.FindDynamicObjectOwner(opened["collider"].As<Node>())?.NativeClassId is not (768 or 769),
                "Opened door collider still blocks the corridor");
            GD.Print("[novalis-route-smoke] authored approach door opened from proximity");
            // This source collision edge blocked the human run after the first
            // two pistons. Stage only the approach; stick and Cross input must
            // produce grab, hang and the jump onto it.
            ClearMovementSmokeInput();
            const double ledgeHeading = .9827937067159747;
            player.Rac1CurrentYaw = ledgeHeading;
            player.StageDevelopmentWaterPosition(new(-239.42148f, 75.65f, 117.96786f));
            await PhysicsFramesAsync(8);
            Require(player.Rac1RecoveredSupportGrounded, "Reported ledge approach has no floor");
            Input.ActionPress(RawGamepadInput.Jump);
            bool grabbedLedge = false;
            for (int i = 0; i < 120; i++)
            {
                if (i == 16) Input.ActionRelease(RawGamepadInput.Jump);
                SetSyntheticNativeHeading(player, ledgeHeading);
                await PhysicsFramesAsync(1);
                if (player.Rac1Hanging)
                {
                    grabbedLedge = true;
                    break;
                }
            }
            ClearMovementSmokeInput();
            Require(grabbedLedge, $"Ordinary jump did not grab reported static ledge: {player.GlobalPosition}");
            for (int i = 0; i < 90 && player.Rac1TraversalAction != 0x19; i++)
                await PhysicsFramesAsync(1);
            Require(player.Rac1Hanging && player.Rac1TraversalAction == 0x19,
                "Reported ledge did not retain the native hanging action");
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(2);
            Input.ActionRelease(RawGamepadInput.Jump);
            Require(!player.Rac1Hanging && player.Rac1TraversalAction == 0x1c,
                "Cross did not jump from reported ledge");
            for (int i = 0; i < 75; i++)
            {
                SetSyntheticNativeHeading(player, ledgeHeading, .4);
                await PhysicsFramesAsync(1);
                if (player.Rac1RecoveredSupportGrounded && player.GlobalPosition.Y > 78.45f) break;
            }
            ClearMovementSmokeInput();
            Require(player.Rac1RecoveredSupportGrounded && player.GlobalPosition.Y > 78.45f,
                $"Ledge jump did not land above the reported edge: {player.GlobalPosition}");
            GD.Print($"[novalis-route-smoke] reported unclassified static ledge grabbed, held and jumped onto: {player.GlobalPosition}");
            ApplicationLifecycle.RequestQuit(this, "rac1-novalis-route-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[novalis-route-smoke] FAIL: {ex}");
            ApplicationLifecycle.RequestQuit(this, "rac1-novalis-route-smoke-fail", 1);
        }
        finally
        {
            ClearMovementSmokeInput();
        }
    }
}
