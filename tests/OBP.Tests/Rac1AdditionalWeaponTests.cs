using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1AdditionalWeaponTests
{
    private static Rac1WeaponInventory Inventory(Rac1WeaponId weapon, int ammo = 40)
    {
        var inventory = Rac1WeaponInventory.CreateOpeningVeldinWitness();
        inventory.AcquireNativeItem((int)weapon);
        if (Rac1ItemAmmoDescriptors.Get((int)weapon).UsesAmmo) inventory.GrantAmmoClamped((int)weapon, ammo);
        Assert.True(inventory.TryEquip(weapon));
        return inventory;
    }

    [Fact]
    public void HeldBlasterUsesSixTickCadenceAndSharedInventoryWithoutRenderAccounting()
    {
        var inventory = Inventory(Rac1WeaponId.Blaster);
        var session = new Rac1AdditionalWeaponSession(inventory);
        int before = inventory.GetAmmo(15);
        var shots = Enumerable.Range(0, 61).Where(_ => session.Step(true) is not null).ToArray();
        Assert.Equal(11, shots.Length);
        Assert.Equal(before - 11, inventory.GetAmmo(15));
        Assert.True(inventory.TryEquip(Rac1WeaponId.Wrench));
        Assert.Null(session.Step(true));
        Assert.Equal(before - 11, inventory.GetAmmo(15));
    }

    [Fact]
    public void MineStagesAtUpdateTailAndRearmsBeforeItsTwentyTickFireGate()
    {
        var inventory = Inventory(Rac1WeaponId.MineGlove);
        var session = new Rac1AdditionalWeaponSession(inventory);
        int ammo = inventory.GetAmmo(17);
        Assert.Null(session.Step(true));
        Assert.NotNull(session.Step(true));
        Assert.Equal(ammo - 1, inventory.GetAmmo(17));
        Assert.Equal(10, session.MineRearmTicks);
        for (int tick = 0; tick < 19; tick++) Assert.Null(session.Step(true));
        Assert.Equal(0, session.MineRearmTicks);
        Assert.NotNull(session.Step(true));
        Assert.Equal(ammo - 2, inventory.GetAmmo(17));
    }

    [Fact]
    public void PyrocitorUsesRecoveredTenTickAmmoGateAndStopsOnRelease()
    {
        var inventory = Inventory(Rac1WeaponId.Pyrocitor);
        var session = new Rac1AdditionalWeaponSession(inventory);
        int ammo = inventory.GetAmmo(16);
        for (int tick = 0; tick < 60; tick++) session.Step(true);
        Assert.Equal(ammo - 6, inventory.GetAmmo(16));
        for (int tick = 0; tick < 60; tick++) Assert.Null(session.Step(false));
        Assert.Equal(ammo - 6, inventory.GetAmmo(16));
    }

    [Fact]
    public void CannonHoldsFiveDistinctCapturedMobiesWithoutWritingOrdinaryAmmo()
    {
        var inventory = Inventory(Rac1WeaponId.SuckCannon);
        var session = new Rac1AdditionalWeaponSession(inventory);
        Assert.Null(session.Step(true));
        Assert.True(session.Vacuuming);
        for (int index = 0; index < 5; index++) Assert.True(session.TryCapture(new(749, index)));
        Assert.False(session.TryCapture(new(749, 5)));
        Assert.False(session.TryCapture(new(749, 0)));
        for (int tick = 0; tick < 60; tick++) Assert.Null(session.Step(true));
        Assert.Equal(5, session.Cargo.Count);
        Assert.Equal(0, inventory.GetAmmo(9));
        session.Step(false);
        var shot = Assert.IsType<Rac1AdditionalWeaponSession.Shot>(session.Step(true));
        Assert.Equal(new Rac1MobyRuntimeKey(749, 0), shot.Cargo);
        for (int tick = 0; tick < 24; tick++) Assert.Null(session.Step(true));
        Assert.Equal(new Rac1MobyRuntimeKey(749, 1), session.Step(true)!.Cargo);
        Assert.Equal(3, session.Cargo.Count);
        var hud = Rac1HudProjection.Capture(new(), inventory, capturedMobyCount: session.Cargo.Count);
        Assert.Equal(3, hud.CurrentWeapon!.Ammo!.Current);
        Assert.Equal(5, hud.CurrentWeapon.Ammo.Capacity);
    }

    [Fact]
    public void CannonCargoSurvivesWeaponSwitchAndRejectsMalformedRestore()
    {
        var inventory = Inventory(Rac1WeaponId.SuckCannon);
        var session = new Rac1AdditionalWeaponSession(inventory);
        session.Step(true); Assert.True(session.TryCapture(new(500, 12)));
        inventory.TryEquip(Rac1WeaponId.Wrench); session.Step(true);
        Assert.False(session.Vacuuming);
        Assert.Single(session.Cargo);
        session.RestoreCargo([new(749, 3), new(500, 12)]);
        Assert.False(session.Vacuuming);
        Assert.Equal(2, session.Cargo.Count);
        Assert.Throws<ArgumentException>(() => session.RestoreCargo([new(749, 1), new(749, 1)]));
        Assert.Throws<ArgumentException>(() => session.RestoreCargo(Enumerable.Range(0, 6).Select(i => new Rac1MobyRuntimeKey(749, i)).ToArray()));
    }

    [Fact]
    public void DoomCanisterEmitsFourEightTicksApartAndWaitsAtEightAgentLimit()
    {
        var canister = new Rac1DoomCanisterSession();
        Assert.False(canister.Step(8));
        for (int tick = 0; tick < 14; tick++) Assert.False(canister.Step(7));
        Assert.True(canister.Step(7));
        for (int agent = 1; agent < 4; agent++)
        {
            for (int tick = 0; tick < 7; tick++) Assert.False(canister.Step(agent));
            Assert.True(canister.Step(agent));
        }
        Assert.True(canister.Finished);
        Assert.Equal(4, canister.Emitted);
        Assert.False(canister.Step(0));
    }

    [Fact]
    public void NewDamageDescriptorsRetainControllerChildAndOwnerIdentity()
    {
        var target = new Rac1MobyRuntimeKey(749, 7);
        var blaster = Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Blaster, target, .25, Rac1WeaponProfiles.BlasterFlags);
        Assert.True(Rac1EnemyDamage.IsPlayerWeapon(blaster));
        Assert.Equal(168, blaster.Source.NativeClassId);
        var mine = Rac1WeaponProfiles.ChildDamage(Rac1WeaponId.MineGlove, 74, 2, target, 3, Rac1WeaponProfiles.MineFlags);
        Assert.True(Rac1EnemyDamage.IsPlayerWeapon(mine));
        Assert.Equal(190, mine.Owner!.Value.NativeClassId);
        Assert.False(Rac1EnemyDamage.IsPlayerWeapon(Rac1WeaponProfiles.ChildDamage(Rac1WeaponId.Blaster, 74, 2, target, 3, Rac1WeaponProfiles.MineFlags)));
        Assert.False(Rac1EnemyDamage.IsPlayerWeapon(Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Blaster, target, 3, 0x10000)));
    }

    [SkippableFact]
    public void RetailEquipmentRowsAndNativeWeaponGatesMatchRecoveredContracts()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay();
        int Word(int address) => BinaryPrimitives.ReadInt32LittleEndian(read(address, 4));
        foreach (var profile in Rac1WeaponProfiles.Additional)
        {
            int row = 0x179ac0 + (int)profile.Item * 0x4c;
            Assert.Equal(profile.ModelClass, Word(row + 0x10));
            Assert.Equal(profile.HandSemantic, Word(row + 0xc));
            Assert.Equal(profile.BodySequence, Word(row + 0x24));
        }
        Assert.Equal(0x24040006, Word(0x2b4e44));
        Assert.Equal(0x24040023, Word(0x2cb084));
        Assert.Equal(0x3c060001, Word(0x2b854c));
        Assert.Equal(Rac1WeaponProfiles.PyrocitorAmmoInterval, Word(0x161530));
        Assert.Equal(Rac1WeaponProfiles.PyrocitorReach, BitConverter.Int32BitsToSingle(Word(0x161520)));
        Assert.Equal(0x24040019, Word(0x2db12c));
        Assert.Equal(0x28820005, Word(0x2da8d0)); // Ordinary Suck store, not Walloper item18.
        Assert.Equal(0x28820008, Word(0x2c90b4)); // Doom population cap.
        Assert.Equal(0x24040008, Word(0x2c91a0));
    }

    [SkippableFact]
    public void GunPoseLayerKeepsFeetAndLocomotionPhaseWhileMoving()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        var avatar = Rac1PlayerAvatarProvider.Instance.Load(path!, "ratchet");
        var controller = Assert.IsType<Rac1PlayerAnimationPresentationController>(Rac1PlayerAvatarProvider.Instance.CreateAnimationController(avatar));
        for (int tick = 0; tick < 10; tick++)
            controller.StepOrdinaryAnimation(OBP.Runtime.Player.PlayerAnimationState.Run, tick / 60d, new(true, true, .1f, InputMagnitude: 1));
        var before = controller.Current.SampledPose!.LocalPositions;
        var phase = controller.OrdinaryLocomotionSample;
        var layered = controller.ApplyEquipmentPose(56).SampledPose!.LocalPositions;
        var feet = Enumerable.Range(0, avatar.VertexCount).Where(vertex => avatar.LocalAnimationFrames[0][vertex * 3 + 2] < .12).ToArray();
        Assert.NotEmpty(feet);
        foreach (int vertex in feet)
            for (int axis = 0; axis < 3; axis++) Assert.Equal(before[vertex * 3 + axis], layered[vertex * 3 + axis], 10);
        Assert.False(before.SequenceEqual(layered));
        Assert.Equal(phase, controller.OrdinaryLocomotionSample);
        Assert.NotNull(controller.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(0)));
    }

    [SkippableFact]
    public void RunningPyrocitorHoldsItsUpperBodyWithoutOverwritingLocomotionLegs()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        var avatar = Rac1PlayerAvatarProvider.Instance.Load(path!, "ratchet");
        var controller = Assert.IsType<Rac1PlayerAnimationPresentationController>(Rac1PlayerAvatarProvider.Instance.CreateAnimationController(avatar));
        int sequence = Rac1ArsenalRules.PyrocitorHoldSequence;
        Assert.Equal(51, sequence);
        var reference = Assert.IsType<Rac1PlayerAnimationPresentationController>(
            Rac1PlayerAvatarProvider.Instance.CreateAnimationController(avatar));
        for (int tick = 0; tick < 10; tick++)
            reference.ApplyEquipmentPose(sequence, Rac1ArsenalRules.PyrocitorReadyPhaseSeconds,
                loop: false, fullBody: true);
        var referenceHand = reference.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(0))!;
        for (int tick = 0; tick < 10; tick++)
        {
            controller.StepOrdinaryAnimation(OBP.Runtime.Player.PlayerAnimationState.Run, tick / 60d,
                new(true, true, .1f, InputMagnitude: 1));
            var moving = controller.Current.SampledPose!.LocalPositions;
            var phase = controller.OrdinaryLocomotionSample;
            var held = controller.ApplyEquipmentPose(sequence, Rac1ArsenalRules.PyrocitorReadyPhaseSeconds,
                loop: false, fullBody: false, stabilizeShoulders: true).SampledPose!.LocalPositions;
            var feet = Enumerable.Range(0, avatar.VertexCount)
                .Where(vertex => avatar.LocalAnimationFrames[0][vertex * 3 + 2] < .12).ToArray();
            Assert.NotEmpty(feet);
            foreach (int vertex in feet)
                for (int axis = 0; axis < 3; axis++) Assert.Equal(moving[vertex * 3 + axis], held[vertex * 3 + axis], 10);
            Assert.Equal(phase, controller.OrdinaryLocomotionSample);
            var socket = controller.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(0));
            Assert.NotNull(socket);
            if (tick == 9)
            {
                Assert.False(moving.SequenceEqual(held));
                // The two shoulder roots are retargeted into Ratchet's authored
                // forward hold, not left to pitch/roll with his running torso.
                static System.Numerics.Vector3 Unit(OBP.Runtime.Player.PlayerAvatarPoint p) =>
                    System.Numerics.Vector3.Normalize(new((float)p.X, (float)p.Y, (float)p.Z));
                Assert.True(System.Numerics.Vector3.Dot(Unit(socket!.X), Unit(referenceHand.X)) > .99f);
                Assert.True(System.Numerics.Vector3.Dot(Unit(socket.Y), Unit(referenceHand.Y)) > .99f);
                Assert.True(System.Numerics.Vector3.Dot(Unit(socket.Z), Unit(referenceHand.Z)) > .99f);
                controller.InterruptEquipmentPose();
                Assert.Equal(moving, controller.Current.SampledPose!.LocalPositions);
                Assert.Equal(moving, controller.ApplyEquipmentPose(null).SampledPose!.LocalPositions);
            }
        }
    }

    [SkippableFact]
    public void RetailSkinsTexturesAndAnimatedChildrenHaveNoProceduralStandIns()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        var assets = Rac1EquipmentAssets.Load(path!);
        var counts = new[] { (176, 1317, 1472), (168, 923, 962), (229, 840, 780), (190, 989, 826), (849, 1593, 1755),
            (74, 660, 644), (186, 1225, 1392), (230, 106, 144) };
        foreach (var (id, vertices, faces) in counts)
        {
            var model = assets.Models[id];
            Assert.Equal(vertices, model.VertexCount); Assert.Equal(faces, model.TriangleCount);
            Assert.All(model.Clips.Values, clip =>
            {
                Assert.All(clip.Frames, frame => Assert.All(frame, value => Assert.True(double.IsFinite(value))));
                Assert.All(clip.Durations, duration => Assert.True(duration > 0));
                Assert.Equal(clip.Frames.Length, clip.Origins.Length);
            });
        }
        Assert.Equal(7, assets.Models[849].Clips.Count);
        Assert.Equal(8, assets.Models[186].Clips.Count);
        Assert.Equal(6, assets.Models[176].Clips.Count);
        Assert.All(assets.ParticleTextures.Values, texture => Assert.Equal(texture.Width * texture.Height * 4, texture.Rgba.Length));
        Assert.Equal(180, Rac1WeaponProfiles.Get(Rac1WeaponId.Walloper).ModelClass);
        Assert.Equal(849, Rac1WeaponProfiles.Get(Rac1WeaponId.SuckCannon).ModelClass);
    }
}
