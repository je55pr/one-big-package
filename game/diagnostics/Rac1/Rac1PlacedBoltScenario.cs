using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Real authored currency nodes with staged contacts, preserving the original native timing.</summary>
public sealed class Rac1PlacedBoltScenario(MovementDiagnosticContext context, Rac1LootScenarioPorts ports) : MovementScenarioSupport(context)
{
    private IRac1LootDiagnosticFixture Loot => ports.Loot;
    public async Task RunAsync()
    {
        IDiagnosticGeometryLease? fixture = null;
        try
        {
            Require(World.Read().Source == ObpSourceGame.Rac1, "placed-bolt smoke needs R&C1");
            using var frameRate = World.LimitFrameRate(60); ClearMovementSmokeInput();
            fixture = Geometry.Create("SyntheticPlacedBoltFixture", new(2000, 64, 2600));
            fixture.AddMesh("PlacedBoltFloor", 11,
                (new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20)), (new(-20, 0, -20), new(20, 0, 20), new(-20, 0, 20)));
            await PhysicsFramesAsync(3); Vector3 origin = fixture.GlobalPosition;
            Player.ResetAt(origin + new Vector3(-8, .05f, 0)); await WaitForGroundedAsync(Player, 180);
            await WaitAsync(() => Loot.PendingPlacedBolts == 0, 120, "placed-bolt floor initialization");
            int level = World.Read().LevelId ?? -1;
            int expectedCount = level switch { 0 => 29, 8 => 194, 11 => 40, _ => -1 };
            var placements = Loot.ReadPlacedBolts();
            Require(placements.Length == expectedCount, "authored bolt census changed");
            Require(Loot.PlacedBoltPresentationIntact && placements.All(item => item.PickupId < 0 && item.OriginalNodeAlias),
                "authored bolt presentation was duplicated or aliased with emitted ids");
            if (level == 8)
            {
                var floating = placements.Where(item => item.PreserveAuthoredHeight).ToArray();
                Require(floating.Length == 127 && floating.All(item => item.MotionNativeZ is { } z && Math.Abs(z - item.NativeAuthoredPosition.Z - .1291563f) < .001f),
                    "Batalia's authored floating bolts were snapped to the floor");
                Log("[placed-bolt-smoke] Batalia preserve-height placements PASS");
            }
            var registration = placements.First(item => item.NativeClass == 13 && item.Active);
            var node = Loot.CapturePlacedBolt(registration.Actor); Loot.StagePlacedBolt(registration.Actor, origin);
            Quaternion firstPose = node.Read().Rotation; int balance = Loot.Read().Wallet, soundCount = Loot.Read().Sounds;
            await PhysicsFramesAsync(90);
            var rest = node.Read();
            Require(rest.NativeState == 3 && Math.Abs(rest.Position.Y - origin.Y - .1291563f) < .001f &&
                rest.Rotation != firstPose && Math.Abs(rest.Scale.X - .65f) < .00001f, "placed bolt did not rest/rotate at native height and scale");
            Require(Loot.Read().Wallet == balance, "distant resting bolt credited early");
            await ports.Capture.CaptureAsync("placed-rest", origin + Vector3.UnitY * .2f, Driver.Lifetime);
            Player.ResetAt(origin + new Vector3(-2.4f, .05f, 0));
            await WaitAsync(() => node.Read().NativeState == 4, 30, "placed-bolt attraction");
            Require(node.Read().Visible && Loot.Read().Wallet == balance, "attraction skipped visible homing/arrival");
            await WaitAsync(() => node.Read().Collected, 180, "placed-bolt arrival");
            var arrival = node.Read();
            Require(Loot.Read().Wallet == balance + 1 && Loot.Read().Sounds > soundCount && !arrival.Visible &&
                arrival.Presence == RuntimeEntityPresence.Inactive && !arrival.PickupNodePresent,
                "arrival failed original-node removal, sound or exact credit");
            await PhysicsFramesAsync(60);
            Require(Loot.Read().Wallet == balance + 1 && !node.Read().Active, "collected placement resurrected or credited twice");
            Log("[placed-bolt-smoke] retail root/rest/rotation/scale/proximity/homing/sound/exact credit PASS");
            Player.ResetAt(origin + Vector3.UnitY * .05f); await WaitForGroundedAsync(Player, 180);
            var distant = Loot.ReadPlacedBolts().First(item => item.Active && item.NativeClass == (level == 11 ? 14 : 13));
            double yaw = Player.Read().Rac1CurrentYaw; Vector3 forward = new(-(float)Math.Cos(yaw), 0, (float)Math.Sin(yaw));
            Loot.StagePlacedBolt(distant.Actor, Player.Read().GlobalPosition + forward * 4.5f);
            var distantNode = Loot.CapturePlacedBolt(distant.Actor); await PhysicsFramesAsync(12);
            Require(distantNode.Read().NativeState == 3, "distant bolt attracted before wrench contact");
            Vector3 throwOrigin = Player.Read().GlobalPosition; balance = Loot.Read().Wallet;
            Driver.SetAction(MovementDiagnosticDriver.Crouch, true); await PhysicsFramesAsync(3);
            await Rac1EncounterInput.TapWrenchAsync(Context); Driver.SetAction(MovementDiagnosticDriver.Crouch, false);
            await WaitAsync(() => distantNode.Read().Collected, 240, "thrown wrench/placed bolt arrival");
            int value = Rac1BoltCrate.ValueForNativePickupClass(distant.NativeClass);
            Require(Loot.Read().Wallet == balance + value && HorizontalDistance(throwOrigin, Player.Read().GlobalPosition) < .4f && Loot.Read().HudWallet == balance + value,
                "thrown-wrench collection required approaching the bolt or lost its denomination/HUD credit");
            Log($"[placed-bolt-smoke] distant Comet-Strike and denomination{value} PASS");
            Log($"[placed-bolt-smoke] PASS level={World.Read().LevelId}");
        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime) { throw; }
        catch (Exception error) { LogError($"[placed-bolt-smoke] FAIL: {error.Message}\n{error.StackTrace}"); throw; }
        finally { ClearMovementSmokeInput(); CleanupStep(() => fixture?.Dispose()); }
    }
    private Task WaitAsync(Func<bool> ready, int frames, string label) => Driver.WaitUntilAsync(ready, frames, "Timed out waiting for RAC1 smoke phase: " + label + ".");
}
