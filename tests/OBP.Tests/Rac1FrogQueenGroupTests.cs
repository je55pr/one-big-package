using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1FrogQueenGroupTests
{
    private static RuntimeDynamicObject Queen(int selector = 22, int cls = 1051,
        int size = 0x180, bool withEntrance = false)
    {
        var p = new byte[size];
        if (p.Length >= 0xf4)
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0xf0), selector);
        if (withEntrance && p.Length >= 0x108)
        {
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0xec), 54);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0xf8), 59);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0xfc), 573);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0x100), 578);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0x104), 25);
        }
        return new("rac1", cls, 626, null, "queen", "queen",
            new RuntimeObjectTransform(new double[16]), [],
            NativePayloads: [new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, p)]);
    }

    private static Rac1FrogQueenGroup Setup()
    {
        var groups = new int[23][];
        for (int i = 0; i < groups.Length; i++) groups[i] = [];
        groups[22] = [.. Enumerable.Range(448, 12)];
        return new(Queen(), groups, Enumerable.Range(448, 12));
    }

    [Fact]
    public void NativeQueenEntranceHonoursRatchetTargetCountdownAndOriginalLinkedCallbacks()
    {
        var source = Queen(withEntrance: true);
        var links = new Dictionary<int, int> { [573] = 1021, [578] = 1021 };
        var entrance = new Rac1AlienQueenEntrance(source, links);
        Assert.Equal(626, entrance.QueenInstance);
        Assert.Equal(54, entrance.NativePathSelector);
        Assert.Equal(59, entrance.CameraSelector);
        Assert.Equal(25, entrance.LinkedEventSelector);
        Assert.Equal(573, entrance.FirstLinkedInstance);
        Assert.Equal(578, entrance.SecondLinkedInstance);
        Assert.Equal(1, entrance.NativeState);
        Assert.Equal(1, entrance.StepWaiting(false, false, true).NativeState);
        Assert.Equal(1, entrance.StepWaiting(true, true, true).NativeState);
        Assert.Equal(0, entrance.RemainingTicks);
        var armed = entrance.StepWaiting(true, false);
        Assert.Equal(10, armed.RemainingTicks);
        Assert.False(armed.RequiresLinkedActivation);
        for (int i = 0; i < 9; i++)
        {
            var frame = entrance.StepWaiting(true, false);
            Assert.Equal(9 - i, frame.RemainingTicks);
            Assert.Equal(1, frame.NativeState);
        }
        Assert.Equal(0, entrance.StepWaiting(true, true, true).RemainingTicks);
        // The shared native helper ticked down to zero despite the target
        // mode block. It must not activate the links until state1 can run.
        Assert.Equal(1, entrance.NativeState);
        var due = entrance.StepWaiting(true, false);
        Assert.Equal(0, due.RemainingTicks);
        Assert.True(due.RequiresLinkedActivation);
        Assert.Equal(1, due.NativeState);
        Assert.True(entrance.StepWaiting(true, false).RequiresLinkedActivation);
        var committed = entrance.StepWaiting(true, false, true);
        Assert.Equal(2, committed.NativeState);
        Assert.True(committed.LinkedCallbacksCommitted);
        Assert.Equal(2, entrance.StepWaiting(true, false, true).NativeState);
        Assert.Equal(2, entrance.ObserveLanding(false, false).NativeState);
        var landed = entrance.ObserveLanding(true, true);
        Assert.Equal(7, landed.NativeState);
        Assert.True(landed.LandingConfirmed);
        Assert.Equal(7, entrance.ObserveLanding(false, true).NativeState);
        Assert.Equal(8, entrance.ObserveLanding(false, false).NativeState);
        Assert.Equal(8, entrance.ObserveLanding(true, true).NativeState);
    }

    [Fact]
    public void QueenEntranceRefusesUnrelatedLinkedClassesOrInvalidPvar()
    {
        var linked = new Dictionary<int, int> { [573] = 1021, [578] = 1021 };
        Assert.Throws<ArgumentException>(() =>
            new Rac1AlienQueenEntrance(Queen(cls: 1021, withEntrance: true), linked));
        Assert.Throws<InvalidDataException>(() =>
            new Rac1AlienQueenEntrance(Queen(size: 0xf0, withEntrance: true), linked));
        Assert.Throws<InvalidDataException>(() =>
            new Rac1AlienQueenEntrance(Queen(withEntrance: true),
                new Dictionary<int, int> { [573] = 1021, [578] = 827 }));
        var malformed = Queen();
        Assert.Throws<InvalidDataException>(() =>
            new Rac1AlienQueenEntrance(malformed, linked));
    }

    [Fact]
    public void RealQueenAnimationGateEmitsTwelveOriginalFrogsOnlyOnNativeEvents()
    {
        var queen = Setup();
        Assert.Equal(626, queen.QueenInstance);
        Assert.Equal(22, queen.GroupIndex);
        Assert.Equal(Enumerable.Range(448, 12), queen.Members);
        var random = Rac1PickupRandom.FromSeed(1051);
        var original = Enumerable.Range(448, 12)
            .ToDictionary(id => id, id => new Vector3(id, id + 1, 100));
        Vector3 Position(int id) => original[id];
        Assert.Empty(queen.ObserveNativeUpdate(0, 6, true, Position, random));
        Assert.Empty(queen.ObserveNativeUpdate(3, 5, true, Position, random));
        Assert.Empty(queen.ObserveNativeUpdate(3, 6, false, Position, random));
        var first = queen.ObserveNativeUpdate(3, 6, true, Position, random);
        Assert.Equal(12, first.Count);
        Assert.Equal(Enumerable.Range(448, 12), first.Select(e => e.FrogInstance));
        foreach (var entry in first)
        {
            var old = Position(entry.FrogInstance);
            Assert.Equal(old.X, entry.NativePosition.X);
            Assert.Equal(old.Y, entry.NativePosition.Y);
            Assert.InRange(entry.NativePosition.Z - old.Z, 15f, 20f);
        }
        Assert.Empty(queen.ObserveNativeUpdate(3, 6, true, Position, random));
        var repeated = queen.ObserveNativeUpdate(4, 1, false, Position, random);
        Assert.Equal(12, repeated.Count);
        Assert.NotEqual(first[0].NativePosition.Z, repeated[0].NativePosition.Z);
        Assert.Empty(queen.ObserveNativeUpdate(8, 6, true, Position, random));
        Assert.Equal(12, queen.ObserveNativeUpdate(3, 6, true, Position, random).Count);
    }

    [Fact]
    public void NativeEmissionConsumesRandomPerMemberEvenWhenNoneCanBeLaunched()
    {
        var a = Setup();
        var b = Setup();
        var randomA = Rac1PickupRandom.FromSeed(1051);
        var randomB = Rac1PickupRandom.FromSeed(1051);
        Vector3 Position(int _) => Vector3.Zero;
        var all = a.ObserveNativeUpdate(4, 0, false, Position, randomA);
        Assert.Equal(12, all.Count);
        for (int i = 0; i < 12; i++) randomB.Range(15, 20);
        Assert.Equal(randomB.Range(15, 20), randomA.Range(15, 20));
        Assert.Empty(b.ObserveNativeUpdate(2, 6, true, Position, Rac1PickupRandom.FromSeed(1051)));
        Assert.Throws<InvalidDataException>(() =>
            a.ObserveNativeUpdate(4, 0, false,
                _ => new(float.NaN, 0, 0), Rac1PickupRandom.FromSeed(2)));
    }

    [Fact]
    public void QueenCanLeaveStateFourOnlyOnceAllGroupMembersReachNativeTerminalStates()
    {
        var queen = Setup();
        Assert.False(queen.IsNativeGroupFinished(_ => 15));
        Assert.False(queen.IsNativeGroupFinished(_ => 6));
        Assert.False(queen.IsNativeGroupFinished(id => id == 448 ? 8 : 0xfe));
        Assert.True(queen.IsNativeGroupFinished(id => (id % 3) switch
        {
            0 => 0xfe,
            1 => 0xfd,
            _ => 0x10
        }));
        Assert.Throws<ArgumentNullException>(() => queen.IsNativeGroupFinished(null!));
    }

    [Fact]
    public void QueenNativeThirtyTwentyTenRecoveryChoosesCorrectFrogWave()
    {
        var boss = new Rac1AlienQueenRecovery(Queen());
        Assert.Equal(626, boss.QueenInstance);
        Assert.Equal(30f, boss.Health);
        Assert.Equal(1, boss.NativeState);
        boss.AdmitNativeState(8, 3);
        var first = boss.ApplyAdmittedHit(7, 0x79); // amplified 1.5; crosses 20
        Assert.True(first.RecoveryStarted);
        Assert.Equal(20f, first.Health);
        Assert.True(boss.Recovering);
        Assert.Equal(30, boss.Timer);
        var shielded = boss.ApplyAdmittedHit(500);
        Assert.False(shielded.DeathTriggered);
        Assert.Equal(20f, boss.Health);
        Assert.Throws<InvalidOperationException>(() => boss.AdmitNativeState(3, 6));
        for (int tick = 0; tick < 29; tick++)
        {
            var frame = boss.Step();
            Assert.True(frame.Recovering);
            Assert.Equal(8, frame.NativeState);
        }
        var entry = boss.Step();
        Assert.False(entry.Recovering);
        Assert.Equal(3, entry.NativeState);
        Assert.Equal(6, entry.Sequence);
        Assert.Equal(19.9f, entry.Health);
        Assert.Equal(0, entry.RemainingTicks);
        Assert.False(boss.ApplyAdmittedHit(100).Accepted); // native state3 damage immune
        boss.AdmitNativeState(4, 1);
        Assert.Equal(19.9f, boss.Health);
        for (int i = 0; i < 10; i++) boss.Step();
        boss.AdmitNativeState(8, 3);
        var second = boss.ApplyAdmittedHit(10, 0x131); // reduced 0.666; crosses 10?
        Assert.False(second.RecoveryStarted);
        second = boss.ApplyAdmittedHit(4);
        Assert.True(second.RecoveryStarted);
        Assert.Equal(10f, second.Health);
        for (int i = 0; i < 30; i++) boss.Step();
        Assert.Equal(5, boss.NativeState);
        Assert.Equal(6, boss.Sequence);
        Assert.Equal(9.9f, boss.Health);
        Assert.False(boss.ApplyAdmittedHit(100).Accepted);
        boss.AdmitNativeState(8, 3);
        for (int i = 0; i < 10; i++) boss.Step();
        var lethal = boss.ApplyAdmittedHit(10);
        Assert.True(lethal.DeathTriggered);
        Assert.Equal(14, boss.NativeState);
        Assert.Equal(10, boss.Sequence);
        Assert.True(boss.DeathTriggered);
    }

    [Fact]
    public void RealWeaponTransportRequiresOriginalQueenTargetAndAdmittedCombatState()
    {
        var queen = new Rac1AlienQueenRecovery(Queen());
        var target = Rac1GameplayEntityRef.Moby(new(1051, 626));
        var other = Rac1GameplayEntityRef.Moby(new(1051, 627));
        Rac1GameplayDamageEvent Event(Rac1GameplayEntityRef to, double amount,
            Rac1GameplayDamageOwner? owner = null, uint? flags = 0x10000) =>
            new(Rac1GameplayEntityRef.PlayerWrench, to, amount, flags,
                owner: owner ?? Rac1GameplayDamageOwner.FromEntity(Rac1GameplayEntityRef.Player));
        Assert.False(queen.CanReceiveHostWeaponContact);
        Assert.False(queen.ApplyNativeDamageEvent(Event(target, 5)).Accepted);
        queen.AdmitNativeState(8, 3);
        Assert.True(queen.CanReceiveHostWeaponContact);
        Assert.False(queen.ApplyNativeDamageEvent(Event(other, 5)).Accepted);
        Assert.False(queen.ApplyNativeDamageEvent(Event(target, 5,
            Rac1GameplayDamageOwner.FromEntity(Rac1GameplayEntityRef.Moby(new(827, 448))))).Accepted);
        Assert.False(queen.ApplyNativeDamageEvent(Event(target, 5, flags: null)).Accepted);
        Assert.False(queen.ApplyNativeDamageEvent(new(
            Rac1GameplayEntityRef.PlayerWrench, target, 5, 0x10000)).Accepted);
        Assert.Equal(30f, queen.Health);
        var hit = queen.ApplyNativeDamageEvent(Event(target, 4));
        Assert.True(hit.Accepted);
        Assert.Equal(26f, hit.Health);
        Assert.False(hit.RecoveryStarted);
        hit = queen.ApplyNativeDamageEvent(Event(target, 6));
        Assert.True(hit.RecoveryStarted);
        Assert.Equal(20f, hit.Health);
        Assert.False(queen.CanReceiveHostWeaponContact);
        Assert.False(queen.ApplyNativeDamageEvent(Event(target, 20)).Accepted);
        Assert.Equal(20f, queen.Health);
        for (int i = 0; i < 30; i++) queen.Step();
        Assert.Equal(3, queen.NativeState);
        Assert.False(queen.CanReceiveHostWeaponContact);
    }

    [Fact]
    public void RangedWeaponProfileEmitsQueenTargetAndPlayerOwnedSource()
    {
        var queen = new Rac1AlienQueenRecovery(Queen());
        var target = new Rac1MobyRuntimeKey(1051, 626);
        var eventFromBlaster = Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Blaster,
            target, 1, Rac1WeaponProfiles.BlasterFlags);
        Assert.True(eventFromBlaster.Target.MatchesMoby(target));
        Assert.False(queen.ApplyNativeDamageEvent(eventFromBlaster).Accepted);
        queen.AdmitNativeState(8, 3);
        var hit = queen.ApplyNativeDamageEvent(eventFromBlaster);
        Assert.True(hit.Accepted);
        Assert.Equal(29f, hit.Health);
        Assert.False(hit.RecoveryStarted);
    }

    [Fact]
    public void QueenNativeDamageEventPreservesAmplifiedFrogClassAndNativeSourceIdentity()
    {
        var queen = new Rac1AlienQueenRecovery(Queen());
        queen.AdmitNativeState(8, 3);
        var frog = Rac1GameplayEntityRef.Moby(new(827, 448));
        var target = Rac1GameplayEntityRef.Moby(new(1051, 626));
        var eventFromFrog = new Rac1GameplayDamageEvent(frog, target, 7, 0x330000,
            owner: Rac1GameplayDamageOwner.FromEntity(frog));
        var hit = queen.ApplyNativeDamageEvent(eventFromFrog);
        Assert.True(hit.RecoveryStarted);
        Assert.Equal(20f, hit.Health); // class 827 ×1.5
    }

    [Fact]
    public void QueenRecoveryInputGuardsAndOneThresholdPerHit()
    {
        var boss = new Rac1AlienQueenRecovery(Queen());
        boss.AdmitNativeState(8, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => boss.ApplyAdmittedHit(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => boss.ApplyAdmittedHit(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => boss.AdmitNativeState(-1, 0));
        Assert.Throws<ArgumentException>(() => new Rac1AlienQueenRecovery(Queen(cls: 1052)));
        Assert.Throws<InvalidDataException>(() => new Rac1AlienQueenRecovery(Queen(size: 0xf0)));
        Assert.False(boss.ApplyAdmittedHit(0).Accepted);
        var massive = boss.ApplyAdmittedHit(100);
        Assert.True(massive.RecoveryStarted);
        Assert.Equal(20f, boss.Health); // first threshold wins, never leapfrog phase5
        for (int i = 0; i < 30; i++) boss.Step();
        Assert.Equal(3, boss.NativeState);
        Assert.Equal(19.9f, boss.Health);
    }

    [Fact]
    public void OnlyOriginalQueenAndAllFrogGroupMembersAccepted()
    {
        var groups = new int[23][];
        for (int i = 0; i < groups.Length; i++) groups[i] = [];
        groups[22] = [448, 449];
        Assert.Throws<ArgumentException>(() => new Rac1FrogQueenGroup(Queen(cls: 1052),
            groups, [448, 449]));
        Assert.Throws<InvalidDataException>(() => new Rac1FrogQueenGroup(Queen(size: 0xf0),
            groups, [448, 449]));
        Assert.Throws<InvalidDataException>(() => new Rac1FrogQueenGroup(Queen(selector: 23),
            groups, [448, 449]));
        Assert.Throws<InvalidDataException>(() => new Rac1FrogQueenGroup(Queen(),
            groups, [448]));
        var valid = new Rac1FrogQueenGroup(Queen(), groups, [448, 449]);
        Assert.Equal(2, valid.Members.Count);
    }
}
