using OBP.IO;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;

namespace OBP.Tests;

public sealed class Rac3TargetRegistryTests
{
    [Theory]
    [InlineData(5821, 0x5f)]
    [InlineData(5860, 0x5f)]
    [InlineData(6306, 0x5f)]
    [InlineData(6317, 0x5f)]
    [InlineData(6476, 0x2f)]
    [InlineData(6577, 0x5f)]
    [InlineData(6836, 0x5f)]
    public void RecoveredHostileFamiliesExposeExactSelectorOffsets(
        int nativeClassId,
        int expectedOffset)
    {
        Assert.True(
            Rac3TargetRegistry.TryGetAuthoredSelectorOffset(
                nativeClassId,
                out int offset));
        Assert.Equal(expectedOffset, offset);
    }

    [Fact]
    public void SelectorZeroChoosesTagThreeAndNonzeroChoosesTagFive()
    {
        byte[] pvar = new byte[0x60];

        Rac3TargetRegistryTagChoice zero =
            Rac3TargetRegistry.DecodeAuthoredTagChoice(5821, pvar);
        Assert.Equal(0, zero.AuthoredSelector);
        Assert.Equal(Rac3TargetRegistry.NativeTagThree, zero.NativeTag);
        Assert.True(zero.RequiresAdditionalNativeAdmission);

        pvar[0x5f] = 7;
        Rac3TargetRegistryTagChoice nonzero =
            Rac3TargetRegistry.DecodeAuthoredTagChoice(5821, pvar);
        Assert.Equal(7, nonzero.AuthoredSelector);
        Assert.Equal(Rac3TargetRegistry.NativeTagFive, nonzero.NativeTag);
        Assert.True(nonzero.RequiresAdditionalNativeAdmission);
    }

    [Fact]
    public void UnsupportedClassFailsClosed()
    {
        Assert.False(
            Rac3TargetRegistry.TryGetAuthoredSelectorOffset(
                7032,
                out _));
        Assert.Throws<NotSupportedException>(() =>
            Rac3TargetRegistry.DecodeAuthoredTagChoice(
                7032,
                new byte[0x100]));
    }

    [SkippableFact]
    public void RetailTable1HostileRegistrySelectorsAreAllAuthoredTagThree()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        Rac3LevelCore.OpenedLevel opened = Rac3LevelCore.Open(reader, 1);
        Rac3Gameplay.Gameplay gameplay = Rac3Gameplay.Read(opened.GameplayReader);

        var expectedCounts = new Dictionary<int, int>
        {
            [5821] = 62,
            [5860] = 23,
            [6306] = 18,
            [6317] = 3,
            [6476] = 3,
            [6577] = 3,
            [6836] = 1,
        };

        int total = 0;
        foreach ((int nativeClassId, int expectedCount) in expectedCounts)
        {
            Assert.True(
                Rac3TargetRegistry.TryGetAuthoredSelectorOffset(
                    nativeClassId,
                    out int offset));

            Rac3Gameplay.MobyInstance[] authored = gameplay.MobyInstances
                .Where(instance => instance.OClass == nativeClassId)
                .ToArray();
            Assert.Equal(expectedCount, authored.Length);

            foreach (Rac3Gameplay.MobyInstance instance in authored)
            {
                Assert.NotNull(instance.PvarData);
                Assert.True(instance.PvarData!.Length > offset);
                Rac3TargetRegistryTagChoice choice =
                    Rac3TargetRegistry.DecodeAuthoredTagChoice(
                        nativeClassId,
                        instance.PvarData);
                Assert.Equal(0, choice.AuthoredSelector);
                Assert.Equal(Rac3TargetRegistry.NativeTagThree, choice.NativeTag);
                Assert.True(choice.RequiresAdditionalNativeAdmission);
            }

            total += authored.Length;
        }

        Assert.Equal(113, total);
        Assert.DoesNotContain(
            gameplay.MobyInstances,
            instance => instance.OClass == 7072);
    }
}
