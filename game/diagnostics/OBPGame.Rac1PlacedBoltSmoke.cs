using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>Real authored nodes, staged contacts; not a matched retail replay.</summary>
    private async Task RunRac1PlacedBoltSmokeAsync()
    {
        Node3D? fixture = null;
        try
        {
            Require(_player is not null && _world is { Game: "rac1" }, "placed-bolt smoke needs R&C1");
            var player = _player!;
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            fixture = new Node3D { Name = "SyntheticPlacedBoltFixture", Position = new(2000, 64, 2600) };
            AddChild(fixture);
            AddSyntheticTraversalMesh(fixture, "PlacedBoltFloor", 11,
                (new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20)),
                (new(-20, 0, -20), new(20, 0, 20), new(-20, 0, 20)));
            await PhysicsFramesAsync(3);
            Vector3 origin = fixture.GlobalPosition;
            player.ApplyDevelopmentReset(origin + new Vector3(-8, .05f, 0));
            await WaitForGroundedAsync(player, 180);
            await Rac1SmokeWaitAsync(() => _rac1PendingPlacedBolts.Count == 0, 120, "placed-bolt floor initialization");
            int expectedCount = _world!.LevelId switch { 0 => 29, 8 => 194, 11 => 40, _ => -1 };
            Require(_rac1PlacedBolts.Registrations.Count == expectedCount, "authored bolt census changed");
            Require(_rac1PlacedBoltNodes.Keys.All(id => id < 0) &&
                _rac1PlacedBoltNodes.All(pair => ReferenceEquals(pair.Value.Root, _rac1PickupNodes[pair.Key])),
                "authored bolt presentation was duplicated or aliased with emitted ids");

            if (_world.LevelId == 8)
            {
                var floating = _rac1PlacedBolts.Registrations.Where(item => item.Authored.PreserveAuthoredHeight).ToArray();
                Require(floating.Length == 127 && floating.All(item =>
                    Math.Abs(_rac1PickupMotion[item.PickupId].Position.Z - item.Authored.Position.Z - .1291563f) < .001f),
                    "Batalia's authored floating bolts were snapped to the floor");
                GD.Print("[placed-bolt-smoke] Batalia preserve-height placements PASS");
            }

            var registration = _rac1PlacedBolts.Registrations.First(item => item.Authored.NativeClassId == 13 && item.Instance.IsActive);
            var node = _rac1PlacedBoltNodes[registration.PickupId];
            StageRac1PlacedBoltForSmoke(registration, origin);
            Quaternion firstPose = node.Root.Quaternion;
            int balance = _rac1BoltWallet.Balance, soundCount = _rac1PickupSoundCount;
            await PhysicsFramesAsync(90);
            Require(_rac1PickupMotion[registration.PickupId].NativeState == 3 &&
                Math.Abs(node.Root.GlobalPosition.Y - origin.Y - .1291563f) < .001f &&
                node.Root.Quaternion != firstPose && Math.Abs(node.Root.Scale.X - .65f) < .00001f,
                "placed bolt did not rest/rotate at native height and scale");
            Require(_rac1BoltWallet.Balance == balance, "distant resting bolt credited early");
            await CaptureRac1CratesAsync("placed-rest", origin + Vector3.Up * .2f);
            player.ApplyDevelopmentReset(origin + new Vector3(-2.4f, .05f, 0));
            await Rac1SmokeWaitAsync(() => _rac1PickupMotion[registration.PickupId].NativeState == 4, 30, "placed-bolt attraction");
            Require(node.Root.Visible && _rac1BoltWallet.Balance == balance, "attraction skipped visible homing/arrival");
            await Rac1SmokeWaitAsync(() => _rac1PlacedBolts.IsCollected(registration.PickupId), 180, "placed-bolt arrival");
            Require(_rac1BoltWallet.Balance == balance + 1 && _rac1PickupSoundCount > soundCount &&
                !node.Root.Visible && node.State.Presentation.Presence == RuntimeEntityPresence.Inactive &&
                !_rac1PickupNodes.ContainsKey(registration.PickupId), "arrival failed original-node removal, sound or exact credit");
            await PhysicsFramesAsync(60);
            Require(_rac1BoltWallet.Balance == balance + 1 && !registration.Instance.IsActive,
                "collected placement resurrected or credited twice");
            GD.Print("[placed-bolt-smoke] retail root/rest/rotation/scale/proximity/homing/sound/exact credit PASS");

            player.ApplyDevelopmentReset(origin + Vector3.Up * .05f);
            await WaitForGroundedAsync(player, 180);
            var distant = _rac1PlacedBolts.Registrations.First(item => item.Instance.IsActive &&
                item.Authored.NativeClassId == (_world.LevelId == 11 ? 14 : 13));
            Vector3 forward = new(-(float)Math.Cos(player.Rac1CurrentYaw), 0, (float)Math.Sin(player.Rac1CurrentYaw));
            StageRac1PlacedBoltForSmoke(distant, player.GlobalPosition + forward * 4.5f);
            await PhysicsFramesAsync(12);
            Require(_rac1PickupMotion[distant.PickupId].NativeState == 3, "distant bolt attracted before wrench contact");
            Vector3 throwOrigin = player.GlobalPosition;
            balance = _rac1BoltWallet.Balance;
            Input.ActionPress(RawGamepadInput.Crouch);
            await PhysicsFramesAsync(3);
            await TapWrenchSmokeAsync();
            Input.ActionRelease(RawGamepadInput.Crouch);
            await Rac1SmokeWaitAsync(() => _rac1PlacedBolts.IsCollected(distant.PickupId), 240, "thrown wrench/placed bolt arrival");
            int value = Rac1BoltCrate.ValueForNativePickupClass(distant.Authored.NativeClassId);
            Require(_rac1BoltWallet.Balance == balance + value && HorizontalDistance(throwOrigin, player.GlobalPosition) < .4f &&
                Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons, _rac1BoltWallet).Bolts!.Balance == balance + value,
                "thrown-wrench collection required approaching the bolt or lost its denomination/HUD credit");
            GD.Print($"[placed-bolt-smoke] distant Comet-Strike and denomination{value} PASS");
            GD.Print($"[placed-bolt-smoke] PASS level={_world.LevelId}");
            ApplicationLifecycle.RequestQuit(this, "rac1-placed-bolt-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[placed-bolt-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-placed-bolt-smoke-fail", 1);
        }
        finally
        {
            if (fixture is not null && IsInstanceValid(fixture)) fixture.QueueFree();
            ClearMovementSmokeInput();
        }
    }

    private void StageRac1PlacedBoltForSmoke(Rac1PlacedBoltSession.Registration registration, Vector3 floor)
    {
        var authored = registration.Authored with { Position = ToEffectNative(floor + Vector3.Up), PreserveAuthoredHeight = false };
        var motion = Rac1BoltMotionSession.FromPlaced(authored, Rac1EffectRandom, new(ToEffectNative(floor), NVector3.UnitZ));
        _rac1PickupMotion[registration.PickupId] = motion;
        var root = _rac1PickupNodes[registration.PickupId];
        root.GlobalPosition = FromEffectNative(motion.Position);
        root.Quaternion = FromEffectOrientation(motion.Orientation);
        root.Scale = Vector3.One * motion.VisualScale;
    }
}
