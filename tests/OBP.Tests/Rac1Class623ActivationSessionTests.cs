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
