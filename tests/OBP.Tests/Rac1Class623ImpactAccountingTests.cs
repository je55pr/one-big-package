using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1Class623ImpactAccountingTests
{
    [Fact]
    public void ScalarSubtractsFromSourceHealthOnlyAfterNativeStatusAdmission()
    {
        Assert.Equal(new Rac1Class623ImpactAccountingResult(false, 1f, false),
            Rac1Class623ImpactAccounting.Evaluate(1f, 1, null, 1f));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(false, 1f, false),
            Rac1Class623ImpactAccounting.Evaluate(1f, 1, 0, null));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(false, 1f, false),
            Rac1Class623ImpactAccounting.Evaluate(1f, 1, 1, 1f));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(false, 1f, false),
            Rac1Class623ImpactAccounting.Evaluate(1f, 14, 2, 1f));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(true, 0.75f, false),
            Rac1Class623ImpactAccounting.Evaluate(1f, 1, 0, 0.25f));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(true, 0f, true),
            Rac1Class623ImpactAccounting.Evaluate(1f, 1, 0, 1f));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(true, -0.5f, true),
            Rac1Class623ImpactAccounting.Evaluate(1f, 11, 2, 1.5f));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(true, 1.25f, false),
            Rac1Class623ImpactAccounting.Evaluate(1f, 1, 0, -0.25f));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(false, 1f, false),
            Rac1Class623ImpactAccounting.Evaluate(1f, 1, 0, float.NaN));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(false, 1f, false),
            Rac1Class623ImpactAccounting.Evaluate(1f, 1, 0, float.PositiveInfinity));
    }

    [SkippableFact]
    public void RetailRilgarEveryClass623StartsWithOneSourceHealthUnit()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var targets = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level))
            .MobyInstances.Where(x => x.OClass == 623).ToArray();
        Assert.Equal(56, targets.Length);
        Assert.All(targets, target =>
        {
            Assert.NotNull(target.PVar);
            Assert.Equal(1f, Rac1Class623ImpactAccounting.ReadAuthoredHealth(target.PVar!));
            Assert.Equal(0x3f800000u,
                BinaryPrimitives.ReadUInt32LittleEndian(target.PVar!.AsSpan(0x20)));
        });
        Assert.Equal(12, targets.Count(x =>
            Rac1AmoeboidClass623Admission.Read(x.PVar!).HasTargetableFlag));
    }

    [SkippableFact]
    public void RetailClass623CallerReadsStatusAndSubtractsServiceScalarFromPVarHealth()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var overlay = Rac1ExecutableOverlay.Read(reader,
            Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5));
        static uint Word(Rac1ExecutableOverlay source, int address) =>
            BinaryPrimitives.ReadUInt32LittleEndian(source.Read(address, 4));

        // Caller passes source PVar+0x20 in a2, status out in a4,
        // scalar out in a5, followed by original native mode 4.
        Assert.Equal(0x26260020u, Word(overlay, 0x003034d4));
        Assert.Equal(0x27a80020u, Word(overlay, 0x003034dc));
        Assert.Equal(0x27a90024u, Word(overlay, 0x003034e0));
        Assert.Equal(0x0c0a11ccu, Word(overlay, 0x003034e8));
        Assert.Equal(0x240b0004u, Word(overlay, 0x003034ec));
        Assert.Equal(0x8fa30020u, Word(overlay, 0x003034f4));
        Assert.Equal(0x106200a0u, Word(overlay, 0x003034fc)); // status 1 => exit
        Assert.Equal(0x1062009cu, Word(overlay, 0x0030350c)); // state 14 => exit
        Assert.Equal(0xc7a10024u, Word(overlay, 0x00303510)); // service scalar
        Assert.Equal(0xc6200020u, Word(overlay, 0x00303514)); // source health
        Assert.Equal(0x46010001u, Word(overlay, 0x0030351c)); // sub.s
        Assert.Equal(0x46020036u, Word(overlay, 0x00303520)); // c.le.s against zero
        Assert.Equal(0x45000002u, Word(overlay, 0x00303528)); // positive branch
        Assert.Equal(0xe6200020u, Word(overlay, 0x0030352c)); // updated health
        Assert.Equal(0x24100001u, Word(overlay, 0x00303530)); // terminal reaction select

        // The native service itself initializes the status out slot
        // before evaluating the contact.
        Assert.Equal(0xae600000u, Word(overlay, 0x00284788));
        Assert.Equal(0x24020001u, Word(overlay, 0x00284c04));
        Assert.Equal(0x24020002u, Word(overlay, 0x00284af0));
        Assert.Equal(0xc680002cu, Word(overlay, 0x00284c38));
        Assert.Equal(0xe4400000u, Word(overlay, 0x00284c3c));
    }
}
