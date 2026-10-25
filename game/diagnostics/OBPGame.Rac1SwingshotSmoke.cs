using OBP.Core;
using System.Buffers.Binary;
using Godot;
using OBP.Runtime;

namespace OBP.Game;

public partial class OBPGame
{
    /// <summary>
    /// Synthetic authored PVars exercise the production target parsers, preview
    /// controls, attached motion and source animation clocks. They are not retail
    /// target placements or proof of the Swingshot projectile lifecycle.
    /// </summary>
    private async System.Threading.Tasks.Task RequireRac1SwingshotSmokeAsync(PlayerHost player)
    {
        Vector3 savedPosition = player.GlobalPosition;
        Vector3 savedRotation = player.Rotation;
        double savedYaw = player.Rac1CurrentYaw;
        bool savedPreview = player.Rac1DevelopmentSwingshotPreview;
        var fixture = new Node3D
        {
            Name = "SyntheticRac1SwingshotFixture",
            Position = new Vector3(2000f, 64f, 2200f),
        };
        AddChild(fixture);
        try
        {
            ClearMovementSmokeInput();
            AddSyntheticTraversalMesh(fixture, "SyntheticSwingshotSupportFloor", 11,
                (new(-40f, 0f, -25f), new(40f, 0f, -25f), new(40f, 0f, 25f)),
                (new(-40f, 0f, -25f), new(40f, 0f, 25f), new(-40f, 0f, 25f)));
            await PhysicsFramesAsync(3);
            if (player.Rac1DevelopmentSwingshotPreview)
                await ToggleSyntheticSwingshotPreviewAsync();
            await ToggleSyntheticSwingshotPreviewAsync();
            Require(player.Rac1DevelopmentSwingshotPreview,
                "physical F11 did not enable the development Swingshot preview");

            Vector3 pullAnchor = fixture.GlobalPosition + new Vector3(-2f, 5f, 0f);
            player.ConfigureRac1TraversalTargets([]);
            player.Rac1CurrentYaw = 0d;
            player.ApplyDevelopmentReset(fixture.GlobalPosition + new Vector3(10f, .08f, 0f));
            await PhysicsFramesAsync(4);
            await WaitForGroundedAsync(player, 60);
            Vector3 pullStart = player.GlobalPosition;
            float initialPullDistance = pullStart.DistanceTo(pullAnchor);
            SendPhysicalKey(Key.E, true);
            await PhysicsFramesAsync(3);
            Require(!player.Rac1SwingshotActive,
                "physical E attached without any configured synthetic target");
            // The player can approach an admissible target while continuing to
            // hold the use button. It must not require a second key transition.
            player.ConfigureRac1TraversalTargets([SyntheticTraversalTarget(758, 900758, pullAnchor)]);
            await PhysicsFramesAsync(3);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(player.Rac1SwingshotActive && player.Rac1SwingshotTargetIndex == 900758 &&
                player.Rac1TraversalAction == 0x25,
                $"synthetic class758 target did not enter attached pull: action={player.Rac1TraversalAction}");
            Require(CurrentPlayerAvatarSourceSequence() == 48 && PlayerAvatarPresentationIsSynchronized(),
                "synthetic class758 pull did not publish synchronized native sequence48");
            await PhysicsFramesAsync(18);
            float pullTravel = pullStart.DistanceTo(player.GlobalPosition);
            Require(player.Rac1SwingshotActive && pullTravel > 2f &&
                player.GlobalPosition.DistanceTo(pullAnchor) < initialPullDistance - 2f,
                $"synthetic class758 pull failed to approach its authored anchor: travel={pullTravel:F4}");
            SendPhysicalKey(Key.E, false);
            await PhysicsFramesAsync(3);
            Require(!player.Rac1SwingshotActive,
                "synthetic class758 pull did not release when physical E was released");
            GD.Print($"[movement-smoke] synthetic class758 pull/acquire/release passed; travel={pullTravel:F4}");

            ClearMovementSmokeInput();
            Vector3 swingAnchor = fixture.GlobalPosition + new Vector3(2f, 8f, 0f);
            player.ConfigureRac1TraversalTargets([SyntheticTraversalTarget(803, 900803, swingAnchor)]);
            player.Rac1CurrentYaw = 0d;
            player.ApplyDevelopmentReset(fixture.GlobalPosition + new Vector3(10f, 2f, 0f));
            await PhysicsFramesAsync(2);
            Vector3 swingStart = player.GlobalPosition;
            SendPhysicalKey(Key.E, true);
            await PhysicsFramesAsync(3);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(player.Rac1SwingshotActive && player.Rac1SwingshotTargetIndex == 900803 &&
                player.Rac1TraversalAction == 0x2c,
                $"synthetic class803 target did not enter attached swing: action={player.Rac1TraversalAction}");
            Require(CurrentPlayerAvatarSourceSequence() == 52 && PlayerAvatarPresentationIsSynchronized(),
                "synthetic class803 swing did not select native entry sequence52");
            bool sawLoop = false;
            float greatestTravel = 0f;
            float minimumHeight = player.GlobalPosition.Y;
            float maximumHeight = minimumHeight;
            for (int tick = 0; tick < 240; tick++)
            {
                SetSyntheticNativeHeading(player, 0d);
                await PhysicsFramesAsync(1);
                Require(player.Rac1SwingshotActive && player.Rac1TraversalAction == 0x2c,
                    "synthetic class803 swing released while physical E remained held");
                sawLoop |= CurrentPlayerAvatarSourceSequence() == 53;
                greatestTravel = Math.Max(greatestTravel, HorizontalDistance(swingStart, player.GlobalPosition));
                minimumHeight = Math.Min(minimumHeight, player.GlobalPosition.Y);
                maximumHeight = Math.Max(maximumHeight, player.GlobalPosition.Y);
            }
            Require(sawLoop && PlayerAvatarPresentationIsSynchronized(),
                "synthetic class803 entry sequence52 did not advance to synchronized loop sequence53");
            Require(greatestTravel > 4f && maximumHeight - minimumHeight > 1f,
                $"synthetic class803 swing did not describe an arc: travel={greatestTravel:F4}, " +
                $"heightRange={maximumHeight - minimumHeight:F4}");
            SendPhysicalKey(Key.E, false);
            ClearMovementSmokeInput();
            await PhysicsFramesAsync(3);
            Require(!player.Rac1SwingshotActive,
                "synthetic class803 swing did not release after physical E release");
            GD.Print($"[movement-smoke] synthetic class803 swing52/53/arc/release passed; " +
                $"travel={greatestTravel:F4}, heightRange={maximumHeight - minimumHeight:F4}");
        }
        finally
        {
            SendPhysicalKey(Key.E, false);
            SendPhysicalKey(Key.F11, false);
            ClearMovementSmokeInput();
            player.ConfigureRac1TraversalTargets(_world?.DynamicObjects ?? []);
            if (player.Rac1DevelopmentSwingshotPreview != savedPreview)
                await ToggleSyntheticSwingshotPreviewAsync();
            if (GodotObject.IsInstanceValid(fixture) && !fixture.IsQueuedForDeletion())
                fixture.QueueFree();
            player.Rotation = savedRotation;
            player.Rac1CurrentYaw = savedYaw;
            player.ApplyDevelopmentReset(savedPosition);
            await PhysicsFramesAsync(3);
        }

        async System.Threading.Tasks.Task ToggleSyntheticSwingshotPreviewAsync()
        {
            SendPhysicalKey(Key.F11, true);
            await PhysicsFramesAsync(2);
            SendPhysicalKey(Key.F11, false);
            await PhysicsFramesAsync(2);
        }
    }

    private static RuntimeDynamicObject SyntheticTraversalTarget(int nativeClass, int instance, Vector3 sceneAnchor)
    {
        byte[] pvar = new byte[0x38];
        static void Word(byte[] bytes, int offset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        static void Float(byte[] bytes, int offset, float value) =>
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset, 4), value);
        Word(pvar, 0, nativeClass == 803 ? 1 : 0);
        Float(pvar, 4, 7f);
        Float(pvar, 0xc, 1f / 3f);
        Word(pvar, 0x1c, -1);
        Float(pvar, 0x20, 30f);
        double[] transform = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0,
            -sceneAnchor.X, sceneAnchor.Y, sceneAnchor.Z, 1];
        return new(ObpSourceGame.Rac1, nativeClass, instance, null, $"synthetic-class:{nativeClass}",
            $"synthetic-instance:{instance}", new RuntimeObjectTransform(transform), [], [new("rac1-pvar", pvar)]);
    }
}
