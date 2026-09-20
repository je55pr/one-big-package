using System.Buffers.Binary;
using OBP.Core.Math;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1Goal1PlayableSliceTests
{
    private static readonly PlayerContactFacts Grounded = new(true);

    [Theory]
    [InlineData(Rac1Class749Hostile.TerminalNativeStateFd)]
    [InlineData(Rac1Class749Hostile.TerminalNativeStateFe)]
    public void DeterministicPlayableSliceChainsRecoveredGoalOneMilestones(int terminalStatus)
    {
        // The explicit player start is the recovered Veldin class-0 placement,
        // converted from native X/Y/Z into OBP X/Y/Z. The remote ship is not Ratchet's spawn.
        var world = CreateRepresentativeVeldinWorld();
        var spawn = Assert.IsType<RuntimeSpawn>(world.PreferredPlayerStart);
        Assert.Equal("rac1", world.Game);
        Assert.Equal(0, world.LevelId);
        Assert.Equal(Rac1PlayerStartProvider.RatchetClass, 0);
        Assert.Equal(Rac1PlayerStartProvider.RatchetInstanceIndex, 0);
        Assert.Equal(132.09d, spawn.X, 2);
        Assert.Equal(31.43d, spawn.Y, 2);
        Assert.Equal(115.48d, spawn.Z, 2);
        Assert.Equal(0.6627015d, spawn.Yaw, 7);
        Assert.NotSame(world.Ship, spawn);

        var movement = new Rac1RatchetMovementController();
        var yaw = new Rac1RatchetYawController(spawn.Yaw);
        var controlBasis = new PlayerPlanarBasis(
            -Math.Sin(spawn.Yaw),
            Math.Cos(spawn.Yaw),
            Math.Cos(spawn.Yaw),
            Math.Sin(spawn.Yaw));
        var run = new PlayerControlIntent(0, 1, false, false, PlanarBasis: controlBasis);
        Rac1RatchetMovementController.StepResult move = default;
        for (int tick = 0; tick < 80; tick++)
            move = movement.Step(
                run,
                Grounded,
                mode => yaw.Step(run.PlanarX, run.PlanarY, spawn.Yaw, mode).CurrentYaw);

        Assert.Equal(Rac1RatchetMovementPhase.Grounded, move.Phase);
        Assert.Equal(Rac1RatchetMovementController.MaximumPlanarStep, move.PlanarMagnitude, 12);

        var jump = movement.Step(
            new PlayerControlIntent(0, 1, true, true, PlanarBasis: controlBasis),
            Grounded,
            mode => yaw.Step(0d, 1d, spawn.Yaw, mode).CurrentYaw);
        Assert.Equal(Rac1RatchetMovementPhase.JumpAnticipation, jump.Phase);
        for (int tick = 1; tick < Rac1RatchetMovementController.JumpAnticipationTicks; tick++)
            jump = movement.Step(
                new PlayerControlIntent(0, 1, true, false, PlanarBasis: controlBasis),
                Grounded,
                mode => yaw.Step(0d, 1d, spawn.Yaw, mode).CurrentYaw);

        Assert.Equal(Rac1RatchetMovementPhase.Rising, jump.Phase);
        Assert.True(jump.Vertical > 0d);
        Assert.Equal(Rac1RatchetMovementController.MaximumPlanarStep, jump.PlanarMagnitude, 12);

        var redPlant = RedPlant(instanceIndex: 17);
        var redPlantState = RuntimeEntityState.FromAuthored(redPlant);
        Assert.Equal(RuntimeObjectAnimationRole.Rest, redPlantState.Presentation.AnimationRole);
        Assert.Equal(RuntimeEntityPresence.Active, redPlantState.Presentation.Presence);

        var atPlantBoundary = Rac1MobyAnimationProvider.AdvanceRedPlantEntityState(
            redPlant,
            redPlantState,
            Rac1MobyAnimationProvider.RedPlantTriggerDistance,
            jump.PlanarMagnitude,
            nativeAnimationFlags: 0);
        Assert.Equal(RuntimeObjectAnimationRole.Rest, atPlantBoundary.Presentation.AnimationRole);

        redPlantState = Rac1MobyAnimationProvider.AdvanceRedPlantEntityState(
            redPlant,
            atPlantBoundary,
            Math.BitDecrement(Rac1MobyAnimationProvider.RedPlantTriggerDistance),
            jump.PlanarMagnitude,
            nativeAnimationFlags: 0);
        Assert.Equal(RuntimeObjectAnimationRole.Reaction, redPlantState.Presentation.AnimationRole);
        Assert.Equal(RuntimeEntityPresence.Active, redPlantState.Presentation.Presence);

        var wrench = new Rac1WrenchCombatController();
        var crate = Class500(uid: 121, rewardCentre: 10);
        var crateState = RuntimeEntityState.FromAuthored(crate);
        var crateTarget = new Rac1WrenchContactTarget(
            Rac1BoltCrate.NativeClassId,
            Rac1WrenchCombatController.DamageableMobyFlag,
            IsPlayerSelf: false);
        var crateSession = new Rac1BoltCrateSession();
        Assert.True(Rac1BoltCrate.RewardRange(10).Contains(12));

        var contactSphere = Assert.IsType<Rac1WrenchSphere>(wrench.GetClass500ToolTipSphere(
            Rac1WrenchCombatController.OrdinaryActionId,
            Rac1WrenchCombatController.OrdinaryProfileId,
            nativeAge: 20,
            root: new Rac1WrenchPoint(0, 0, 0),
            tip: new Rac1WrenchPoint(1, 0, 0)));
        Assert.Equal(Rac1WrenchCombatController.ToolTipSphereRadius, contactSphere.Radius, 12);

        var crateHit = Assert.IsType<Rac1WrenchDamageResult>(wrench.ApplyClass500ToolTipContact(
            Rac1WrenchCombatController.OrdinaryActionId,
            Rac1WrenchCombatController.OrdinaryProfileId,
            nativeAge: 20,
            crateTarget,
            crate,
            crateState,
            crateSession,
            selectedTotal: 12));
        Assert.Equal(Rac1WrenchContactPath.ToolTipSphere, crateHit.ContactPath);
        Assert.Equal(Rac1WrenchCombatController.NativeDamage, crateHit.NativeDamage);

        var brokenCrate = Assert.IsType<Rac1BoltCrateBreakResult>(crateHit.BoltCrateBreak);
        Assert.Equal(Rac1BoltCrate.ActiveNativeState, brokenCrate.NativeStateBefore);
        Assert.Equal(Rac1BoltCrate.BreakTransitionNativeState, brokenCrate.NativeBreakTransitionState);
        Assert.Equal(Rac1BoltCrate.DisabledNativeState, brokenCrate.NativeDisabledState);
        Assert.Equal(RuntimeEntityPresence.Inactive, brokenCrate.EntityState.Presentation.Presence);
        Assert.Equal(12, brokenCrate.PhysicalValue);

        Assert.Equal(new[] { 14, 14, 13, 13 }, brokenCrate.Pickups.Select(pickup => pickup.NativeClassId));
        foreach (var pickup in brokenCrate.Pickups)
            crateSession.CollectPickup(pickup.PickupId);
        Assert.Equal(12, crateSession.CollectedBolts);
        Assert.Equal(0, crateSession.OutstandingPickupCount);

        var hostile = Class749(instanceIndex: 149, health: 1f);
        var hostileSession = new Rac1Class749HostileSession();
        var hostileProbe = hostileSession.RegisterRepresentative(
            hostile,
            RuntimeEntityState.FromAuthored(hostile));
        Assert.Equal(Rac1Class749Hostile.TargetSearchNativeState, hostileProbe.NativeState);
        Assert.Equal(1f, hostileProbe.Health);
        Assert.Equal(RuntimeEntityPresence.Active, hostileProbe.EntityState.Presentation.Presence);

        hostileProbe = hostileSession.Step(
            hostile,
            new Rac1Class749TargetFacts(3d, 0d, StatusSentinel: 0));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, hostileProbe.NativeState);
        Assert.Null(hostileProbe.NativeSequence);

        hostileProbe = hostileSession.Step(
            hostile,
            new Rac1Class749TargetFacts(Rac1Class749Hostile.AttackDistanceExclusive, 0d, StatusSentinel: 0));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, hostileProbe.NativeState);
        Assert.Null(hostileProbe.NativeSequence);

        hostileProbe = hostileSession.Step(
            hostile,
            new Rac1Class749TargetFacts(
                1d,
                Rac1Class749Hostile.AttackFacingErrorExclusive,
                StatusSentinel: 0));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, hostileProbe.NativeState);
        Assert.Null(hostileProbe.NativeSequence);

        hostileProbe = hostileSession.Step(
            hostile,
            new Rac1Class749TargetFacts(
                Math.BitDecrement(Rac1Class749Hostile.AttackDistanceExclusive),
                Math.BitDecrement((double)Rac1Class749Hostile.AttackFacingErrorExclusive),
                StatusSentinel: 0));
        Assert.Equal(Rac1Class749Hostile.AttackNativeState, hostileProbe.NativeState);
        Assert.Equal(Rac1Class749Hostile.AttackSequenceId, hostileProbe.NativeSequence);
        Assert.Equal(0, hostileProbe.NativeSequenceUpdate);
        Assert.Null(hostileProbe.Attack);

        for (int nativeUpdate = 1; nativeUpdate < Rac1Class749Hostile.AttackMarkerNativeUpdate; nativeUpdate++)
        {
            hostileProbe = hostileSession.Step(
                hostile,
                new Rac1Class749TargetFacts(1d, 0d, StatusSentinel: 0));
            Assert.Null(hostileProbe.Attack);
        }

        hostileProbe = hostileSession.Step(
            hostile,
            new Rac1Class749TargetFacts(1d, 0d, StatusSentinel: 0));
        Assert.Equal(
            new Rac1Class749AttackEvent(
                Rac1Class749Hostile.AttackMarker,
                Rac1Class749Hostile.AttackDamage),
            hostileProbe.Attack);

        var hostileTarget = new Rac1WrenchContactTarget(
            Rac1Class749Hostile.NativeClassId,
            Rac1WrenchCombatController.DamageableMobyFlag,
            IsPlayerSelf: false);
        var hostileDamage = Assert.IsType<Rac1WrenchDamageResult>(wrench.ResolveForwardDirectRecord(
            Rac1WrenchCombatController.OrdinaryActionId,
            Rac1WrenchCombatController.OrdinaryProfileId,
            nativeAge: 20,
            hostileTarget));
        Assert.Equal(Rac1WrenchContactPath.ForwardDirectRecord, hostileDamage.ContactPath);

        var damaged = hostileSession.ApplyWrenchDamage(hostile, hostileDamage);
        Assert.Equal(0f, damaged.Health);
        Assert.Equal(Rac1Class749Hostile.DamageNativeState, damaged.NativeState);
        Assert.Equal(RuntimeEntityPresence.Active, damaged.EntityState.Presentation.Presence);

        var terminal = hostileSession.ApplyTerminalStatus(hostile, terminalStatus);
        Assert.Equal(terminalStatus, terminal.NativeState);
        Assert.Contains(terminal.NativeState, new[] { 0xfd, 0xfe });
        Assert.Equal(RuntimeEntityPresence.Inactive, terminal.EntityState.Presentation.Presence);

        var environment = Assert.IsType<RuntimeEnvironment>(world.Environment);
        Assert.Equal(27f, environment.DeathHeight);
        var nanotech = new Rac1RatchetNanotechSession();
        var death = Assert.IsType<Rac1RatchetNanotechSnapshot>(
            nanotech.TryApplyVeldinEnvironmentalDeath(new Rac1VeldinEnvironmentalDeathFacts(
                Math.BitDecrement((double)environment.DeathHeight),
                environment.DeathHeight,
                Math.BitIncrement(Rac1RatchetNanotechSession.RetailVeldinDeathContactSeparationExclusive),
                NativeSpecialPlayerState20A4: 0)));
        Assert.Equal(Rac1RatchetNanotechSession.RetailVeldinDeathNativeState, death.NativePlayerState);
        Assert.Equal(Rac1RatchetNanotechSession.RetailVeldinDeathNativeSequence, death.NativeSequence);
        Assert.Equal(0, death.NativeSequenceFrame);

        var respawned = nanotech.Respawn();
        Assert.Equal(4, respawned.Nanotech);
        var authoredRespawn = Assert.IsType<RuntimeSpawn>(world.PreferredPlayerStart);
        Assert.Equal(spawn, authoredRespawn);
    }
    [Fact]
    public void LiveHostCompositionKeepsRac1GameplayStateAuthoritative()
    {
        var inventory = new Rac1WeaponInventory(
            ownsFirstRanged: true,
            equipped: Rac1WeaponId.Wrench,
            firstRangedAmmo: 6);
        var bombGlove = new Rac1BombGloveSession(inventory);

        Assert.Null(bombGlove.Step(fireRequested: true).Shot);
        Assert.Equal(6, inventory.FirstRangedAmmo);
        Assert.True(inventory.TryEquip(Rac1WeaponId.FirstRanged));
        var shot = Assert.IsType<Rac1BombGloveShot>(bombGlove.Step(fireRequested: true).Shot);
        Assert.Equal(5, inventory.FirstRangedAmmo);
        Assert.Equal(inventory.FirstRangedAmmo, bombGlove.Probe().Ammo);

        var hostile = Class749(instanceIndex: 149, health: 1f);
        var hostiles = new Rac1Class749HostileSession();
        var hostileProbe = hostiles.RegisterRepresentative(hostile, RuntimeEntityState.FromAuthored(hostile));
        var bombDamage = Assert.IsType<Rac1BombGloveDamageResult>(
            bombGlove.ResolveGoal1Impact(shot.Projectile.ProjectileId, hostile, hostileProbe.NativeState));
        hostileProbe = hostiles.ApplyBombGloveDamage(hostile, bombDamage);
        Assert.Equal(0f, hostileProbe.Health);
        hostileProbe = hostiles.ApplyTerminalStatus(hostile, Rac1Class749Hostile.TerminalNativeStateFd);
        Assert.Equal(RuntimeEntityPresence.Inactive, hostileProbe.EntityState.Presentation.Presence);

        var nanotech = new Rac1RatchetNanotechSession();
        var afterHit = nanotech.ApplyClass749Attack(
            new Rac1Class749AttackEvent(Rac1Class749Hostile.AttackMarker, Rac1Class749Hostile.AttackDamage));
        Assert.Equal(3, afterHit.Nanotech);
        Assert.False(afterHit.IsDead);

        var dead = Assert.IsType<Rac1RatchetNanotechSnapshot>(
            nanotech.TryApplyVeldinEnvironmentalDeath(new Rac1VeldinEnvironmentalDeathFacts(
                NativeVerticalPosition: 26d,
                DeathHeight: 27d,
                ContactSeparation: 3d,
                NativeSpecialPlayerState20A4: 0)));
        Assert.Equal(0, dead.Nanotech);
        Assert.True(dead.IsDead);
        var respawned = nanotech.Respawn();
        Assert.Equal(4, respawned.Nanotech);
        Assert.False(respawned.IsDead);

        var facing = new Rac1WrenchCombatController().ResolveFirstSwingFacing(0.6627015d);
        Assert.Equal(Math.Cos(0.6627015d), facing.X, 12);
        Assert.Equal(Math.Sin(0.6627015d), facing.Y, 12);
    }

    private static RuntimeWorld CreateRepresentativeVeldinWorld()
    {
        var ship = new RuntimeSpawn(20d, 20d, 20d, 0d);
        var playerStart = new RuntimeSpawn(132.09d, 31.43d, 115.48d, 0.6627015d);
        return new RuntimeWorld(
            Game: "rac1",
            BuildId: "rac1-ntscu-original",
            LevelId: 0,
            PlanetName: "Veldin",
            LocationName: null,
            Meshes: [],
            Textures: [],
            MaterialCount: 0,
            CollisionMeshes: [],
            Bounds: new ObpBounds(Vec3.Zero, new Vec3(200, 200, 200)),
            Environment: new RuntimeEnvironment(
                DeathHeight: 27f,
                IsSphericalWorld: false,
                BackgroundColour: null,
                FogColour: null,
                FogNearDistance: 0f,
                FogFarDistance: 0f),
            Ship: ship,
            PlayerStart: playerStart);
    }

    private static RuntimeDynamicObject RedPlant(int instanceIndex) =>
        new(
            "rac1",
            Rac1MobyAnimationProvider.RedPlantClassId,
            instanceIndex,
            null,
            $"moby:{Rac1MobyAnimationProvider.RedPlantClassId}",
            $"moby:{instanceIndex}",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>());

    private static RuntimeDynamicObject Class500(int uid, int rewardCentre)
    {
        var raw = new byte[0x78];
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x0c, 2), checked((ushort)uid));
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x10, 4), rewardCentre);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x18, 4), Rac1BoltCrate.NativeClassId);
        var pvar = new byte[0x100];

        return new RuntimeDynamicObject(
            "rac1",
            Rac1BoltCrate.NativeClassId,
            89,
            uid,
            $"moby:{Rac1BoltCrate.NativeClassId}",
            "moby:89",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>(),
            [
                new RuntimeOpaquePayload(Rac1BoltCrate.InstancePayloadFormat, raw),
                new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, pvar),
            ]);
    }

    private static RuntimeDynamicObject Class749(int instanceIndex, float health)
    {
        var pvar = new byte[Rac1Class749Hostile.PVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac1Class749Hostile.HealthOffset, sizeof(int)),
            BitConverter.SingleToInt32Bits(health));

        return new RuntimeDynamicObject(
            "rac1",
            Rac1Class749Hostile.NativeClassId,
            instanceIndex,
            null,
            $"moby:{Rac1Class749Hostile.NativeClassId}",
            $"moby:{instanceIndex}",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>(),
            [new RuntimeOpaquePayload(Rac1Class749Hostile.PVarPayloadFormat, pvar)]);
    }
}
