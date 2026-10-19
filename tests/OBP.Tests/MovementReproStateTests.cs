using OBP.Core.Math;
using OBP.RAC1.Camera;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Camera;
using OneBigPackage;

namespace OBP.Tests;

public sealed class MovementReproStateTests
{
    private static MovementReproState Sample()
    {
        var camera = new Rac1OrdinaryCameraController();
        camera.Reset(new Vec3(130, 112, 31), -1.23456789);
        var visible = camera.Step(new(new Vec3(130, 112, 31), .6, -.7, new(true, -.25)));
        return new(1, "rac1:LEVEL0", "rac1-ntscu-original", DateTimeOffset.UtcNow, Guid.NewGuid(),
            new(new(-130.125, 31.019999, 112.5), new(0, 1.2, 0), -.25,
                new(0, .5, 0), new(-.3, 0, 0), new(0, 0, 5), 6, false, true,
                camera.CaptureSnapshot(), visible.ToRuntimeState(),
                new(true, true, false, false, true, false, false), Rac1TraversalEquipmentId.ThrusterPack,
                true, 2, true));
    }

    [Fact]
    public void PersistentRoundtrip_PreservesExactPoseCameraAndEquipment()
    {
        string path = Path.Combine(Path.GetTempPath(), $"obp-movement-{Guid.NewGuid():N}.json");
        try
        {
            MovementReproState saved = Sample();
            MovementReproStateFile.Save(path, saved);
            Assert.Equal(saved, MovementReproStateFile.Load(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidOverwrite_DoesNotReplaceTheExistingSave()
    {
        string path = Path.Combine(Path.GetTempPath(), $"obp-movement-{Guid.NewGuid():N}.json");
        try
        {
            MovementReproState saved = Sample();
            MovementReproStateFile.Save(path, saved);
            Assert.Throws<InvalidDataException>(() => MovementReproStateFile.Save(path, saved with { Version = 99 }));
            Assert.Equal(saved, MovementReproStateFile.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WorldRoundtrip_RetainsDefeatedObjectsInventoryAndBoltsWithoutNativePayloads()
    {
        var inventory = new Rac1WeaponInventory(true, Rac1WeaponId.FirstRanged, 3);
        var world = new MovementReproWorld(
            [new(new(749, 143), 12, true), new(new(1440, 197), 8, true), new(new(500, 104), 0xfe, true)],
            inventory.CapturePersistentState(), inventory.CurrentItemId, 1234);
        MovementReproState saved = Sample() with { Version = MovementReproState.CurrentVersion, World = world };
        string path = Path.Combine(Path.GetTempPath(), $"obp-world-repro-{Guid.NewGuid():N}.json");
        try
        {
            MovementReproStateFile.Save(path, saved);
            MovementReproState restored = MovementReproStateFile.Load(path);
            Assert.Equal(saved.Player, restored.Player);
            Assert.Equal(world.InactiveObjects.ToArray(), restored.World!.InactiveObjects.ToArray());
            var restoredInventory = new Rac1WeaponInventory(restored.World.Inventory, restored.World.CurrentItemId);
            Assert.Equal(Rac1WeaponId.FirstRanged, restoredInventory.Equipped);
            Assert.Equal(3, restoredInventory.FirstRangedAmmo);
            Assert.Equal(1234, restored.World.Bolts);
            string json = File.ReadAllText(path);
            Assert.DoesNotContain("PVar", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NativePayload", json, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WorldValidation_RejectsDuplicateIdentitiesBadStatesAndInvalidInventory()
    {
        var inventory = Rac1WeaponInventory.CreateOpeningVeldinWitness().CapturePersistentState();
        var world = new MovementReproWorld([], inventory, (int)Rac1WeaponId.Wrench, 0);
        var defeated = new MovementReproInactiveObject(new(749, 143), 12, true);
        Assert.Throws<InvalidDataException>(() => (Sample() with { Version = 2 }).Validate());
        Assert.Throws<InvalidDataException>(() => (world with { InactiveObjects = [defeated, defeated] }).Validate());
        Assert.Throws<InvalidDataException>(() => (world with { InactiveObjects = [defeated with { NativeState = 256 }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (world with { InactiveObjects = [new(new(500, 104), 0xfe, false)] }).Validate());
        Assert.Throws<InvalidDataException>(() => (world with { Inventory = inventory with { Ammo = [-1] } }).Validate());
        Assert.Throws<InvalidDataException>(() => (world with { Bolts = -1 }).Validate());
    }

    [Fact]
    public void InvalidCoordinatesOrIncompleteCamera_AreRejectedBeforePlacement()
    {
        MovementReproState saved = Sample();
        Assert.Throws<InvalidDataException>(() => (saved with { Destination = null! }).Validate());
        Assert.Throws<InvalidDataException>(() => (saved with { Player = saved.Player with { Position = new(double.NaN, 0, 0) } }).Validate());
        Assert.Throws<InvalidDataException>(() => (saved with { Player = saved.Player with { CameraController = null! } }).Validate());
        Assert.Throws<InvalidDataException>(() => (saved with { Destination = "rac2:LEVEL1" }).Validate());
    }

    [Fact]
    public void CameraSnapshot_ResumesEveryRecurrenceWithoutAReset()
    {
        var first = new Rac1OrdinaryCameraController();
        var second = new Rac1OrdinaryCameraController();
        first.Reset(new Vec3(10, 20, 30), .25);
        for (int tick = 0; tick < 40; tick++)
            first.Step(new(new Vec3(10 + tick * .1, 20, 30 + tick * .02), .7, -.8, new(true, .5)));
        second.RestoreSnapshot(first.CaptureSnapshot());
        for (int tick = 0; tick < 80; tick++)
        {
            var input = new Rac1OrdinaryCameraController.Input(new Vec3(15, 21 + tick * .1, 31), 0, 0, default);
            Assert.Equal(first.Step(input), second.Step(input));
        }
    }

    [Fact]
    public void ReplayOptions_DoNotEnableCampaignPersistence()
    {
        var args = CommandLineArgs.Parse(["--load-movement-state", "case.json", "--replay-forward", "180", "--replay-out", "trace.json", "--replay-profile"]);
        Assert.Equal("case.json", args.LoadMovementState);
        Assert.Equal(180, args.ReplayForwardFrames);
        Assert.Equal("trace.json", args.ReplayOut);
        Assert.True(args.ReplayProfile);
        Assert.False(args.Rac1CampaignPersistenceRequested);
    }
}
