using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1Class623ActivationSessionTests
{
    private static byte[] PVar(int controller, int volume = -1)
    {
        byte[] pvar = new byte[Rac1AmoeboidClass623Admission.PVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x2b8), controller);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x2bc), volume);
        return pvar;
    }

    [SkippableFact]
    public void RetailClass623ReceiverBranchesProveTerminalFlagWithdrawal()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var overlay = Rac1ExecutableOverlay.Read(reader, level);
        static uint Word(Rac1ExecutableOverlay o, int address) =>
            BinaryPrimitives.ReadUInt32LittleEndian(o.Read(address, 4));

        Assert.Equal(0x0c0a11b6u, Word(overlay, 0x003033cc));
        Assert.Equal(0x0c0a11ccu, Word(overlay, 0x003034e8));
        Assert.Equal(0x8fa30020u, Word(overlay, 0x003034f4));
        Assert.Equal(0x106200a0u, Word(overlay, 0x003034fc));
        Assert.Equal(0x92630020u, Word(overlay, 0x00303504));
        Assert.Equal(0x1062009cu, Word(overlay, 0x0030350c));
        Assert.Equal(0x2402000bu, Word(overlay, 0x0030364c));
        Assert.Equal(0xa2620020u, Word(overlay, 0x00303654));
        Assert.Equal(0x3042efffu, Word(overlay, 0x0030369c));
        Assert.Equal(0xa6620034u, Word(overlay, 0x003036a0));
        Assert.Equal(0x2402000eu, Word(overlay, 0x00303714));
        Assert.Equal(0xa2620020u, Word(overlay, 0x0030371c));
    }

    [Fact]
    public void ObservedNativeTerminalWithdrawsOnlyAnEligibleTarget()
    {
        byte[] eligible = new byte[Rac1AmoeboidClass623Admission.PVarSize];
        BinaryPrimitives.WriteInt64LittleEndian(eligible.AsSpan(0x2b8), -1);
        var targets = new Rac1Class623ActivationSession();
        targets.Register(1009, 623, eligible);
        Assert.Throws<ArgumentException>(() => targets.ObserveNativeTerminal(1009, 11, 0));
        Assert.Throws<ArgumentException>(() => targets.ObserveNativeTerminal(1009, 14, 0x1000));
        Assert.True(targets.ObserveNativeTerminal(1009, 14, 0));
        Assert.False(targets.ObserveNativeTerminal(1009, 14, 0));
        Assert.Equal(new Rac1Class623InitialTargetState(14, false), targets.Get(1009));
        Assert.Empty(targets.Advance(_ => null, _ => true));
        targets.Clear();
        targets.Register(1009, 623, eligible);
        Assert.Equal(new Rac1Class623InitialTargetState(1, true), targets.Get(1009));
    }

    [Fact]
    public void ControllerSignalMustBeLiveOwnerQualifiedAndNonzero()
    {
        var session = new Rac1Class623ActivationSession();
        session.Register(964, 623, PVar(1071));
        session.Register(965, 623, PVar(1072, 58));
        session.Register(1009, 623, PVar(-1));
        Assert.Equal(3, session.RegisteredCount);
        Assert.Equal(new Rac1Class623InitialTargetState(1, true), session.Get(1009));
        Assert.Throws<InvalidOperationException>(() => session.Register(964, 623, PVar(1071)));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Register(998, 572, PVar(1071)));

        static bool? Outside(int _) => false;
        static Rac1Class830CompletionSnapshot? Unknown(int _) => null;
        Assert.Empty(session.Advance(Unknown, Outside));
        Assert.Equal(15, session.Get(964).NativeState);
        Assert.Empty(session.Advance(_ => new(999, 830, 2), Outside));
        Assert.Empty(session.Advance(id => new(id, 623, 2), Outside));
        Assert.Empty(session.Advance(id => new(id, 830, 0), Outside));
        Assert.Empty(session.Advance(_ => null, _ => null));

        Assert.Equal(new[] { 965 }, session.Advance(_ => null, volume => volume == 58));
        Assert.Equal(new Rac1Class623InitialTargetState(1, true), session.Get(965));
        Assert.Equal(new[] { 964 }, session.Advance(id =>
            id == 1071 ? new(id, 830, 1) : null, Outside));
        Assert.Equal(new Rac1Class623InitialTargetState(1, true), session.Get(964));
        Assert.Empty(session.Advance(id => new(id, 830, 2), _ => true));
        session.Clear();
        Assert.Equal(0, session.RegisteredCount);
        session.Register(964, 623, PVar(1071));
        Assert.Equal(new Rac1Class623InitialTargetState(15, false), session.Get(964));
    }

    [SkippableFact]
    public void RetailRilgarControllersAdmitOnlyTheirOwnWaitingTargets()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var mobys = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level)).MobyInstances;
        var targetSources = mobys.Where(x => x.OClass == 623).ToArray();
        var controllers = mobys.Where(x => x.OClass == 830).Select(x => x.Index).ToHashSet();
        var session = new Rac1Class623ActivationSession();
        foreach (var target in targetSources)
            session.Register(target.Index, target.OClass, target.PVar!);
        Assert.Equal(56, session.RegisteredCount);
        Assert.Equal(12, targetSources.Count(x => session.Get(x.Index).HasTargetableFlag));

        var unverified = session.Advance(_ => null, _ => false);
        Assert.Empty(unverified);
        Assert.Equal(44, targetSources.Count(x => session.Get(x.Index).NativeState == 15));

        // Retail i964 references class-830 i1071. An unrelated 830
        // completion byte must not wake up another controller's targets.
        Assert.Equal(new[] { 964 }, session.Advance(
            id => id == 1071 ? new(id, 830, 1) : null, _ => false));
        Assert.Equal(43, targetSources.Count(x => session.Get(x.Index).NativeState == 15));

        var remaining = session.Advance(
            id => controllers.Contains(id) ? new(id, 830, 2) : null, _ => false);
        Assert.Equal(43, remaining.Count);
        Assert.DoesNotContain(964, remaining);
        Assert.All(targetSources, source =>
            Assert.Equal(new Rac1Class623InitialTargetState(1, true), session.Get(source.Index)));
        Assert.Empty(session.Advance(
            id => controllers.Contains(id) ? new(id, 830, 2) : null, _ => true));

        // A new world restores the exact original 12/44 split.
        session.Clear();
        foreach (var target in targetSources)
            session.Register(target.Index, target.OClass, target.PVar!);
        Assert.Equal(12, targetSources.Count(x => session.Get(x.Index).HasTargetableFlag));
        Assert.Equal(44, targetSources.Count(x => session.Get(x.Index).NativeState == 15));
    }
}
