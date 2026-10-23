using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1Class623ObservedImpactSessionTests
{
    private static byte[] PVar(long links = -1, float health = 1f)
    {
        var bytes = new byte[Rac1AmoeboidClass623Admission.PVarSize];
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(0x2b8), links);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0x20), health);
        return bytes;
    }

    [Fact]
    public void TrustedServiceImpactAccumulatesThenWithdrawsTerminalTargetExactlyOnce()
    {
        var targets = new Rac1Class623ActivationSession();
        targets.Register(1009, 623, PVar());
        targets.Register(964, 623, PVar(1071));
        Assert.Equal(1f, targets.Health(1009));
        Assert.Equal(1f, targets.Health(964));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(false, 1f, false),
            targets.ApplyObservedNativeImpact(1009, null, 1f));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(false, 1f, false),
            targets.ApplyObservedNativeImpact(1009, 1, 1f));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(false, 1f, false),
            targets.ApplyObservedNativeImpact(964, 0, 1f));
        Assert.Equal(new Rac1Class623InitialTargetState(15, false), targets.Get(964));

        Assert.Equal(new Rac1Class623ImpactAccountingResult(true, 0.75f, false),
            targets.ApplyObservedNativeImpact(1009, 0, 0.25f));
        Assert.Equal(new Rac1Class623InitialTargetState(1, true), targets.Get(1009));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(true, 0f, true),
            targets.ApplyObservedNativeImpact(1009, 2, 0.75f));
        Assert.Equal(new Rac1Class623InitialTargetState(14, false), targets.Get(1009));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(false, 0f, false),
            targets.ApplyObservedNativeImpact(1009, 0, 1f));

        Assert.Equal(new[] { 964 }, targets.Advance(
            id => id == 1071 ? new(id, 830, 1) : null, _ => false));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(true, -0.5f, true),
            targets.ApplyObservedNativeImpact(964, 0, 1.5f));
        Assert.Equal(new Rac1Class623InitialTargetState(14, false), targets.Get(964));
        Assert.Empty(targets.Advance(id => new(id, 830, 2), _ => true));
        Assert.Equal(new Rac1Class623InitialTargetState(14, false), targets.Get(1009));

        targets.Clear();
        targets.Register(1009, 623, PVar());
        targets.Register(964, 623, PVar(1071));
        Assert.Equal(1f, targets.Health(1009));
        Assert.Equal(new Rac1Class623InitialTargetState(1, true), targets.Get(1009));
        Assert.Equal(new Rac1Class623InitialTargetState(15, false), targets.Get(964));
        Assert.Throws<InvalidDataException>(() =>
            targets.Register(200, 623, PVar(health: float.NaN)));
    }

    [Fact]
    public void TrustedPositiveHealthWithNativeResponseTwoStillDispatchesTerminalHandler()
    {
        var targets = new Rac1Class623ActivationSession();
        targets.Register(1009, 623, PVar());
        targets.Register(1010, 623, PVar());
        targets.Register(1011, 623, PVar());

        // Class-623 caller retains the native service's v0 response
        // when health remains positive, and native indices 1/2
        // both jump to 0x303660 independently of lethal subtraction.
        Assert.Equal(new Rac1Class623ImpactAccountingResult(true, 0.875f, true),
            targets.ApplyObservedNativeImpact(1009, 0, 0.125f, nativeResponseIndex: 2));
        Assert.Equal(0.875f, targets.Health(1009));
        Assert.Equal(new Rac1Class623InitialTargetState(14, false), targets.Get(1009));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(false, 0.875f, false),
            targets.ApplyObservedNativeImpact(1009, 0, 1f, nativeResponseIndex: 2));

        // Unresolved response and index 3 (alternate reaction arm)
        // must not manufacture a terminal notice.
        Assert.Equal(new Rac1Class623ImpactAccountingResult(true, 0.875f, false),
            targets.ApplyObservedNativeImpact(1010, 0, 0.125f));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(true, 0.75f, false),
            targets.ApplyObservedNativeImpact(1010, 0, 0.125f, nativeResponseIndex: 3));
        Assert.Equal(new Rac1Class623InitialTargetState(1, true), targets.Get(1010));
        Assert.Equal(new Rac1Class623ImpactAccountingResult(false, 1f, false),
            targets.ApplyObservedNativeImpact(1011, 1, 0.125f, nativeResponseIndex: 2));
        Assert.Equal(new Rac1Class623InitialTargetState(1, true), targets.Get(1011));

        targets.Clear();
        targets.Register(1009, 623, PVar());
        Assert.Equal(1f, targets.Health(1009));
        Assert.Equal(new Rac1Class623InitialTargetState(1, true), targets.Get(1009));
    }

    [SkippableFact]
    public void RetailRilgarReplaysAll56NativeHealthAndTerminalOutcomesWithoutSyntheticHostHits()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var mobys = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level))
            .MobyInstances;
        var targets = new Rac1Class623ActivationSession();
        var targetSources = mobys.Where(x => x.OClass == 623).ToArray();
        foreach (var source in targetSources)
            targets.Register(source.Index, source.OClass, source.PVar!);

        Assert.Equal(56, targets.RegisteredCount);
        Assert.All(targetSources, source => Assert.Equal(1f, targets.Health(source.Index)));
        Assert.DoesNotContain(targetSources, x => targets.Get(x.Index).NativeState == 14);

        // These are explicitly injected *retail-shaped* service outcomes
        // for a bounded replay, not a claim that Godot observed 56 hits.
        foreach (var source in targetSources)
        {
            if (targets.Get(source.Index).NativeState == 15)
            {
                Assert.False(targets.ApplyObservedNativeImpact(source.Index, 0, 1f).Applied);
                var links = targets.Links(source.Index);
                Assert.Equal(830, mobys[links.LinkedMobyIndex].OClass);
            }
        }

        var admissible = targetSources.Where(x => targets.Get(x.Index).NativeState == 1)
            .Select(x => x.Index).ToArray();
        Assert.Equal(12, admissible.Length);
        Assert.All(admissible, index =>
        {
            Assert.True(targets.ApplyObservedNativeImpact(index, 0, 1f).TakesTerminalPath);
            Assert.Equal(new Rac1Class623InitialTargetState(14, false), targets.Get(index));
        });
        Assert.Equal(44, targets.Advance(
            id => new(id, 830, 2), _ => false).Count);

        Assert.All(targetSources.Where(x => targets.Get(x.Index).NativeState == 1), source =>
        {
            Assert.True(targets.ApplyObservedNativeImpact(source.Index, 0, 1f).TakesTerminalPath);
        });
        Assert.Equal(56, targetSources.Count(x => targets.Get(x.Index).NativeState == 14));
        Assert.Empty(targets.Advance(id => new(id, 830, 2), _ => true));

        targets.Clear();
        foreach (var source in targetSources)
            targets.Register(source.Index, source.OClass, source.PVar!);
        Assert.Equal(12, targetSources.Count(x => targets.Get(x.Index).NativeState == 1));
        Assert.Equal(44, targetSources.Count(x => targets.Get(x.Index).NativeState == 15));
        Assert.All(targetSources, source => Assert.Equal(1f, targets.Health(source.Index)));
    }

    [SkippableFact]
    public void RetailClass623LethalResponseIndexOneDispatchesToFlagClearAndStateFourteen()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var overlay = Rac1ExecutableOverlay.Read(reader, level);
        static uint Word(Rac1ExecutableOverlay o, int addr) =>
            BinaryPrimitives.ReadUInt32LittleEndian(o.Read(addr, 4));

        Assert.Equal(0x24100001u, Word(overlay, 0x00303530));
        Assert.Equal(0x2e04000cu, Word(overlay, 0x00303558));
        Assert.Equal(0x3c020022u, Word(overlay, 0x00303590));
        Assert.Equal(0x244286f0u, Word(overlay, 0x00303598));
        Assert.Equal(0x8c640000u, Word(overlay, 0x003035a0));
        Assert.Equal(0x0030376cu, Word(overlay, 0x002186f0)); // index 0
        Assert.Equal(0x00303660u, Word(overlay, 0x002186f4)); // index 1, lethal
        Assert.Equal(0x00303660u, Word(overlay, 0x002186f8)); // index 2
        Assert.Equal(0x3042efffu, Word(overlay, 0x0030369c));
        Assert.Equal(0xa6620034u, Word(overlay, 0x003036a0));
        Assert.Equal(0x2402000eu, Word(overlay, 0x00303714));
        Assert.Equal(0xa2620020u, Word(overlay, 0x0030371c));
    }
}
