using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1Class830ControllerSessionTests
{
    private static byte[] PVar(int mode = 0)
    {
        byte[] pvar = new byte[Rac1Class830NativePVar.Size];
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(8), mode);
        return pvar;
    }

    [Fact]
    public void ImmediateNativeCellOrBitSetsByteTwoOnlyWhenObserved()
    {
        var source = new Rac1Class830ControllerSession();
        source.Register(1071, 830, PVar());
        source.Register(1072, 830, PVar());
        Assert.Throws<InvalidOperationException>(() => source.Register(1071, 830, PVar()));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Register(1, 623, PVar()));
        Assert.Throws<InvalidDataException>(() => source.Register(1, 830, new byte[8]));
        Assert.Null(source.Snapshot(1071));
        Assert.Equal(new Rac1Class830ControllerState(0, null), source.Step(1071, new()));
        Assert.Equal(new Rac1Class830ControllerState(0, null), source.Step(1071, new(CellMarked: false)));
        Assert.Equal(new Rac1Class830ControllerState(2, (byte?)2), source.Step(1071, new(CellMarked: true)));
        Assert.Equal(new Rac1Class830CompletionSnapshot(1071, 830, 2), source.Snapshot(1071));
        Assert.Equal(new Rac1Class830ControllerState(2, (byte?)2), source.Step(1071, new(CellMarked: false, CellBitSet: false)));
        Assert.Null(source.Snapshot(1072));
        Assert.Equal(new Rac1Class830ControllerState(2, (byte?)2), source.Step(1072,
            new(CellMarked: false, CellBitSet: true)));
        Assert.Equal(2, source.RegisteredCount);
        source.Clear();
        Assert.Equal(0, source.RegisteredCount);
    }

    [Fact]
    public void PendingNativeOwnerAndSlotGateOnlyEmitsByteOneWhenBothPass()
    {
        var source = new Rac1Class830ControllerSession();
        source.Register(1081, 830, PVar());
        Assert.Equal(new Rac1Class830ControllerState(1, null), source.Step(1081,
            new(CellMarked: false, CellBitSet: false)));
        Assert.Null(source.Snapshot(1081));
        Assert.Equal(new Rac1Class830ControllerState(1, null), source.Step(1081, new(OwnsControlSlot: true)));
        Assert.Equal(new Rac1Class830ControllerState(1, null), source.Step(1081,
            new(OwnsControlSlot: false, ControlSlotClear: true)));
        Assert.Equal(new Rac1Class830ControllerState(1, null), source.Step(1081,
            new(OwnsControlSlot: true, ControlSlotClear: false)));
        Assert.Equal(new Rac1Class830ControllerState(2, (byte?)1), source.Step(1081,
            new(OwnsControlSlot: true, ControlSlotClear: true)));
        Assert.Equal(new Rac1Class830CompletionSnapshot(1081, 830, 1), source.Snapshot(1081));
        Assert.Equal(new Rac1Class830ControllerState(2, (byte?)1), source.Step(1081, new(CellMarked: true)));
    }

    [Fact]
    public void NonzeroPVarModeRequiresNativeAlternateLookupBeforeCompletion()
    {
        var source = new Rac1Class830ControllerSession();
        source.Register(1094, 830, PVar(mode: 1));
        source.Register(1095, 830, PVar(mode: 1));
        Assert.Equal(new Rac1Class830ControllerState(0, null), source.Step(1094,
            new(CellMarked: false, CellBitSet: false)));
        Assert.Equal(new Rac1Class830ControllerState(2, (byte?)2), source.Step(1094,
            new(CellMarked: false, CellBitSet: false, AlternateLookupByte: 0xff)));
        Assert.Equal(new Rac1Class830ControllerState(1, null), source.Step(1095,
            new(CellMarked: false, CellBitSet: false, AlternateLookupByte: 0xfe)));
        Assert.Null(source.Snapshot(1095));
    }

    [Fact]
    public void ActualClass830ProducerFeedsOnlyItsOriginalClass623Consumers()
    {
        byte[] targetPVar = new byte[Rac1AmoeboidClass623Admission.PVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(targetPVar.AsSpan(0x2b8), 1071);
        BinaryPrimitives.WriteInt32LittleEndian(targetPVar.AsSpan(0x2bc), -1);
        var targets = new Rac1Class623ActivationSession();
        targets.Register(964, 623, targetPVar);
        BinaryPrimitives.WriteInt32LittleEndian(targetPVar.AsSpan(0x2b8), 1072);
        targets.Register(965, 623, targetPVar);
        var source = new Rac1Class830ControllerSession();
        source.Register(1071, 830, PVar());
        source.Register(1072, 830, PVar());

        Assert.Empty(targets.Advance(source.Snapshot, _ => false));
        source.Step(1071, new(CellMarked: true));
        Assert.Equal(new[] { 964 }, targets.Advance(source.Snapshot, _ => false));
        Assert.Equal(15, targets.Get(965).NativeState);
        source.Step(1072, new(CellMarked: false, CellBitSet: false));
        Assert.Empty(targets.Advance(source.Snapshot, _ => false));
        source.Step(1072, new(OwnsControlSlot: true, ControlSlotClear: true));
        Assert.Equal(new[] { 965 }, targets.Advance(source.Snapshot, _ => false));
        Assert.Empty(targets.Advance(source.Snapshot, _ => true));

        source.Clear();
        targets.Clear();
        source.Register(1071, 830, PVar());
        BinaryPrimitives.WriteInt32LittleEndian(targetPVar.AsSpan(0x2b8), 1071);
        targets.Register(964, 623, targetPVar);
        Assert.Null(source.Snapshot(1071));
        Assert.False(targets.Get(964).HasTargetableFlag);
    }

    [SkippableFact]
    public void RetailRilgarClass830PVarsAndNativeBranchesMatchSource()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var settings = Rac1LevelSettings.ReadGameplay(reader, level);
        var mobys = Rac1Instances.Parse(settings).MobyInstances;
        var controllers = mobys.Where(x => x.OClass == 830).ToArray();
        Assert.Equal(25, controllers.Length);
        Assert.Equal(Enumerable.Range(1071, 25), controllers.Select(x => x.Index));
        Assert.All(controllers, x => Assert.Equal(16, x.PVar!.Length));
        Assert.Equal(new[] { 1094, 1095 }, controllers
            .Where(x => Rac1Class830NativePVar.Read(x.PVar!).StateZeroLookupSelector != 0)
            .Select(x => x.Index));
        var session = new Rac1Class830ControllerSession();
        foreach (var source in controllers)
            session.Register(source.Index, source.OClass, source.PVar!);
        Assert.Equal(25, session.RegisteredCount);
        Assert.All(controllers, source => Assert.Null(session.Snapshot(source.Index)));

        // Explicitly supplied native-table observations exercise the
        // producer/consumer chain without claiming they were observed
        // during ordinary Godot play. Each of the 44 source links must
        // resolve to one of these exact 25 producers.
        var targets = new Rac1Class623ActivationSession();
        foreach (var target in mobys.Where(x => x.OClass == 623))
            targets.Register(target.Index, target.OClass, target.PVar!);
        Assert.Empty(targets.Advance(session.Snapshot, _ => false));
        foreach (var source in controllers)
            Assert.Equal(new Rac1Class830ControllerState(2, 2),
                session.Step(source.Index, new(CellMarked: true)));
        Assert.Equal(44, targets.Advance(session.Snapshot, _ => false).Count);
        Assert.Empty(targets.Advance(session.Snapshot, _ => false));
        targets.Clear();
        session.Clear();
        Assert.Equal(0, session.RegisteredCount);

        var overlay = Rac1ExecutableOverlay.Read(reader, level);
        static uint Word(Rac1ExecutableOverlay source, int address) =>
            BinaryPrimitives.ReadUInt32LittleEndian(source.Read(address, 4));
        // State 0 evaluates cell table, bit table and +0x08 mode before
        // selecting the immediate-2 or pending-1 branch.
        Assert.Equal(0x90620454u, Word(overlay, 0x0030c620));
        Assert.Equal(0x1440001au, Word(overlay, 0x0030c624));
        Assert.Equal(0x30420001u, Word(overlay, 0x0030c654));
        Assert.Equal(0x8e220008u, Word(overlay, 0x0030c660));
        Assert.Equal(0x10400031u, Word(overlay, 0x0030c664));
        Assert.Equal(0x14650029u, Word(overlay, 0x0030c688));
        Assert.Equal(0xa2040020u, Word(overlay, 0x0030c69c));
        Assert.Equal(0xa20400bcu, Word(overlay, 0x0030c6a8));
        Assert.Equal(0xa2020020u, Word(overlay, 0x0030c734));
        // State 1 requires exact control-slot ownership and zero gate
        // before the alternative one-byte signal and final state 2.
        Assert.Equal(0x8c8302fcu, Word(overlay, 0x0030c7d4));
        Assert.Equal(0x1470004fu, Word(overlay, 0x0030c7d8));
        Assert.Equal(0x8482030eu, Word(overlay, 0x0030c7e0));
        Assert.Equal(0x5440004du, Word(overlay, 0x0030c7e4));
        Assert.Equal(0xa20400bcu, Word(overlay, 0x0030c878));
        Assert.Equal(0xa2030020u, Word(overlay, 0x0030c880));
    }
}
