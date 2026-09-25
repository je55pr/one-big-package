using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC3.Level;

namespace OBP.Tests;

public sealed class UyaClass7032RegistryTests
{
    [SkippableFact]
    public void RetailTable1Class7032AuthoredRegistryLinksMatchRecoveredInitializer()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        UyaLevelCore.OpenedLevel opened = UyaLevelCore.Open(reader, 1);
        UyaGameplay.Gameplay gameplay = UyaGameplay.Read(opened.GameplayReader);

        UyaGameplay.MobyInstance[] controllers = gameplay.MobyInstances
            .Where(instance => instance.OClass == 7032)
            .OrderBy(instance => instance.Index)
            .ToArray();
        Assert.Equal(27, controllers.Length);

        int enabled = 0;
        int disabled = 0;
        foreach (UyaGameplay.MobyInstance controller in controllers)
        {
            Assert.NotNull(controller.PvarData);
            ReadOnlySpan<byte> pvar = controller.PvarData!;
            Assert.True(pvar.Length >= 0x54);

            int requestedClass = BinaryPrimitives.ReadInt32LittleEndian(pvar[0x40..]);
            int partnerIndex = BinaryPrimitives.ReadInt32LittleEndian(pvar[0x44..]);
            int runtimeChildIndex = BinaryPrimitives.ReadInt32LittleEndian(pvar[0x50..]);

            Assert.Equal(-1, runtimeChildIndex);

            if (requestedClass == -1)
            {
                Assert.Equal(-1, partnerIndex);
                disabled++;
                continue;
            }

            Assert.Equal(6886, requestedClass);
            Assert.InRange(partnerIndex, 0, gameplay.MobyInstances.Count - 1);
            Assert.Equal(7031, gameplay.MobyInstances[partnerIndex].OClass);
            enabled++;
        }

        Assert.Equal(17, enabled);
        Assert.Equal(10, disabled);
    }
}
