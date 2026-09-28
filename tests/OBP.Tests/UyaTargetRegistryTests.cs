using OBP.IO;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;

namespace OBP.Tests;

public sealed class UyaTargetRegistryTests
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
            UyaTargetRegistry.TryGetAuthoredSelectorOffset(
                nativeClassId,
                out int offset));
        Assert.Equal(expectedOffset, offset);
    }

    [Fact]
    public void SelectorZeroChoosesTagThreeAndNonzeroChoosesTagFive()
    {
        byte[] pvar = new byte[0x60];

        UyaTargetRegistryTagChoice zero =
            UyaTargetRegistry.DecodeAuthoredTagChoice(5821, pvar);
        Assert.Equal(0, zero.AuthoredSelector);
        Assert.Equal(UyaTargetRegistry.NativeTagThree, zero.NativeTag);
        Assert.True(zero.RequiresAdditionalNativeAdmission);

        pvar[0x5f] = 7;
        UyaTargetRegistryTagChoice nonzero =
            UyaTargetRegistry.DecodeAuthoredTagChoice(5821, pvar);
        Assert.Equal(7, nonzero.AuthoredSelector);
        Assert.Equal(UyaTargetRegistry.NativeTagFive, nonzero.NativeTag);
        Assert.True(nonzero.RequiresAdditionalNativeAdmission);
    }

    [Fact]
    public void UnsupportedClassFailsClosed()
    {
        Assert.False(
            UyaTargetRegistry.TryGetAuthoredSelectorOffset(
                7032,
                out _));
        Assert.Throws<NotSupportedException>(() =>
            UyaTargetRegistry.DecodeAuthoredTagChoice(
                7032,
                new byte[0x100]));
    }

    [SkippableFact]
    public void RetailTable1HostileRegistrySelectorsAreAllAuthoredTagThree()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        UyaLevelCore.OpenedLevel opened = UyaLevelCore.Open(reader, 1);
        UyaGameplay.Gameplay gameplay = UyaGameplay.Read(opened.GameplayReader);

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
                UyaTargetRegistry.TryGetAuthoredSelectorOffset(
                    nativeClassId,
                    out int offset));

            UyaGameplay.MobyInstance[] authored = gameplay.MobyInstances
                .Where(instance => instance.OClass == nativeClassId)
                .ToArray();
            Assert.Equal(expectedCount, authored.Length);

            foreach (UyaGameplay.MobyInstance instance in authored)
            {
                Assert.NotNull(instance.PvarData);
                Assert.True(instance.PvarData!.Length > offset);
                UyaTargetRegistryTagChoice choice =
                    UyaTargetRegistry.DecodeAuthoredTagChoice(
                        nativeClassId,
                        instance.PvarData);
                Assert.Equal(0, choice.AuthoredSelector);
                Assert.Equal(UyaTargetRegistry.NativeTagThree, choice.NativeTag);
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
