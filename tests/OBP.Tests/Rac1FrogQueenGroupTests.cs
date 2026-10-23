using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1FrogQueenGroupTests
{
    private static RuntimeDynamicObject Queen(int selector = 22, int cls = 1051,
        int size = 0x180)
    {
        var p = new byte[size];
        if (p.Length >= 0xf4)
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0xf0), selector);
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
