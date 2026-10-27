using System.Numerics;
using OBP.Core;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

/// <summary>Staged authored mechanism fixtures, not a claim of completing the human route.</summary>
public sealed class Rac1NovalisRouteScenario(MovementDiagnosticContext context, IRac1NovalisRouteFixture route,
    IRac1EncounterMenuChecks menus) : MovementScenarioSupport(context)
{
    private Rac1NovalisPistonDiagnostic Piston() => route.ReadPiston(715);
    public async Task RunAsync()
    {
        try
        {
            Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 1 }, "Novalis route smoke needs LEVEL1");
            Require(route.MechanismCount == 18, "Missing authored Novalis mechanisms");
            ClearMovementSmokeInput(); await WaitForGroundedAsync(Player, 360);
            await menus.CheckRetailNavigationAsync("Novalis", Driver.Lifetime);
            ClearMovementSmokeInput(); await PhysicsFramesAsync(5);
            Player.StageWater(new(-191f, 65.2f, 128.5f)); await PhysicsFramesAsync(210);
            foreach (var rock in route.ReadRocks())
                Require(rock.State == 4 && Math.Abs(rock.Position.Z - (rock.Home.Z - 20)) < .001f,
                    $"Staircase rock {rock.Actor.Target.InstanceIndex} did not land: state={rock.State}, pos={rock.Position}");
            Log("[novalis-route-smoke] authored approach dropped all three staircase rocks");
            for (int i = 0; Piston().End != 1 && i < 400; i++) await PhysicsFramesAsync(1);
            var piston = Piston(); Require(piston.End == 1, "Piston never returned to lower rest");
            var hit = route.ProbePiston(piston.Actor);
            Require(hit.Hit && hit.InstanceIndex == 715, $"Piston top collider absent/occluded: {hit}");
            Player.StageWater(hit.Position + Vector3.UnitY * .03f); await PhysicsFramesAsync(8);
            float start = Player.Read().GlobalPosition.Y, offset = start - Piston().Position.Y, maximumRise = 0;
            for (int i = 0; i < 180; i++)
            {
                await PhysicsFramesAsync(1); var player = Player.Read(); piston = Piston();
                maximumRise = Math.Max(maximumRise, player.GlobalPosition.Y - start);
                Require(player.Rac1MovementPhase == Rac1RatchetMovementPhase.Grounded && player.AnimationState == PlayerAnimationState.Idle,
                    $"Standing rider entered airborne animation: tick={i}, end={piston.End}, timer={piston.Timer}, phase={player.Rac1MovementPhase}, animation={player.AnimationState}, player={player.GlobalPosition}, platform={piston.Position}, floor={player.Grounded}");
                Require(Math.Abs(player.GlobalPosition.Y - piston.Position.Y - offset) < .08f,
                    $"Piston lost its rider: player={player.GlobalPosition}, platform={piston.Position}, offset={offset}");
                if (piston.End == 0) break;
            }
            Require(maximumRise > 3.8f && Player.Read().Rac1RecoveredSupportGrounded, $"Piston carry failed: rise={maximumRise}, player={Player.Read().GlobalPosition}");
            Driver.SetAction(MovementDiagnosticDriver.Jump, true); await PhysicsFramesAsync(16); Driver.SetAction(MovementDiagnosticDriver.Jump, false);
            Require(Player.Read().GlobalPosition.Y > start + maximumRise + .5f, "Cross did not jump from moving support");
            await PhysicsFramesAsync(65);
            Require(Player.Read().Rac1RecoveredSupportGrounded, "Jump did not land on original piston geometry");
            for (int i = 0; Piston().End != 1 && i < 250; i++)
            {
                await PhysicsFramesAsync(1); var player = Player.Read();
                Require(Math.Abs(player.GlobalPosition.Y - Piston().Position.Y - offset) < .08f, "Descending piston lost its rider");
                Require(player.Rac1MovementPhase == Rac1RatchetMovementPhase.Grounded, "Descending piston incorrectly entered falling");
            }
            Require(Piston().End == 1, "Piston failed to complete its return with rider");
            Log($"[novalis-route-smoke] original piston carried its rider {maximumRise:F3} units; Cross and landing PASS");
            var door = route.ReadDoor(893); Vector3 centre = door.Centre;
            var closed = route.ProbeDoor(door.Actor);
            Require(closed.Hit && closed.NativeClass is 768 or 769, "Closed doorway does not block its authored corridor");
            Player.StageWater(centre + Vector3.UnitY * .2f); await PhysicsFramesAsync(30); door = route.ReadDoor(893);
            Require(door.State == 3 && door.Progress == 1f && Vector3.Distance(door.Position, centre) > 1.99f,
                $"Door did not open from player proximity: state={door.State}, player={Player.Read().GlobalPosition}, door={door.Position}");
            var opened = route.ProbeDoor(door.Actor);
            Require(!opened.Hit || opened.NativeClass is not (768 or 769), "Opened door collider still blocks the corridor");
            Log("[novalis-route-smoke] authored approach door opened from proximity");
            // Stage the reported collision edge; ordinary stick and Cross still produce grab, hang and jump.
            ClearMovementSmokeInput(); const double ledgeHeading = .9827937067159747;
            Player.SetYaw(ledgeHeading); Player.StageWater(new(-239.42148f, 75.65f, 117.96786f)); await PhysicsFramesAsync(8);
            Require(Player.Read().Rac1RecoveredSupportGrounded, "Reported ledge approach has no floor");
            Driver.SetAction(MovementDiagnosticDriver.Jump, true); bool grabbedLedge = false;
            for (int i = 0; i < 120; i++)
            {
                if (i == 16) Driver.SetAction(MovementDiagnosticDriver.Jump, false);
                SetSyntheticNativeHeading(Player, ledgeHeading); await PhysicsFramesAsync(1);
                if (Player.Read().Rac1Hanging) { grabbedLedge = true; break; }
            }
            ClearMovementSmokeInput(); Require(grabbedLedge, $"Ordinary jump did not grab reported static ledge: {Player.Read().GlobalPosition}");
            for (int i = 0; i < 90 && Player.Read().Rac1TraversalAction != 0x19; i++) await PhysicsFramesAsync(1);
            Require(Player.Read().Rac1Hanging && Player.Read().Rac1TraversalAction == 0x19, "Reported ledge did not retain the native hanging action");
            Driver.SetAction(MovementDiagnosticDriver.Jump, true); await PhysicsFramesAsync(2); Driver.SetAction(MovementDiagnosticDriver.Jump, false);
            Require(!Player.Read().Rac1Hanging && Player.Read().Rac1TraversalAction == 0x1c, "Cross did not jump from reported ledge");
            for (int i = 0; i < 75; i++)
            {
                SetSyntheticNativeHeading(Player, ledgeHeading, .4); await PhysicsFramesAsync(1);
                if (Player.Read().Rac1RecoveredSupportGrounded && Player.Read().GlobalPosition.Y > 78.45f) break;
            }
            ClearMovementSmokeInput();
            Require(Player.Read().Rac1RecoveredSupportGrounded && Player.Read().GlobalPosition.Y > 78.45f,
                $"Ledge jump did not land above the reported edge: {Player.Read().GlobalPosition}");
            Log($"[novalis-route-smoke] reported unclassified static ledge grabbed, held and jumped onto: {Player.Read().GlobalPosition}");
        }
        finally { ClearMovementSmokeInput(); }
    }
}
