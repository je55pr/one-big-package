using OBP.Core;
using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1PulsingOrbTests
{
    [Fact]
    public void AuthoredPhaseAndSharedNativeColourEndpointsDriveThePulse()
    {
        var session = new Rac1PulsingOrbSession();
        Assert.True(session.Register(0, Source(1, 0)));
        Assert.True(session.Register(18, Source(2, 180)));
        Assert.Equal(2, session.Count);
        var initial = session.Step(1);
        Assert.InRange(initial.Red, 68, 80);
        Assert.InRange(initial.Green, 45, 49);
        var opposite = session.Step(2);
        Assert.InRange(initial.Red + opposite.Red, 142, 146);
        for (int i = 0; i < 59; i++) session.Step(1);
        var halfCycle = session.Step(1);
        Assert.True(halfCycle.Red > initial.Red);
    }

    [Fact]
    public void InvalidPvarsAndDuplicateInstancesFailClosed()
    {
        var session = new Rac1PulsingOrbSession();
        Assert.False(session.Register(0, Source(1, 0) with { NativeClassId = 500 }));
        Assert.Throws<InvalidDataException>(() => session.Register(1, Source(1, 0)));
        Assert.Throws<InvalidDataException>(() =>
            session.Register(0, Source(1, 0) with { NativePayloads = [] }));
        Assert.True(session.Register(0, Source(1, 0)));
        Assert.Throws<InvalidOperationException>(() => session.Register(0, Source(1, 0)));
        Assert.Throws<InvalidOperationException>(() => session.Step(99));
    }

    [SkippableFact]
    public void EveryRetailPulsePlacementIsBackedByItsOriginalSixteenBytePvar()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        int total = 0;
        foreach (var level in Rac1DiscIndex.Read(reader).Levels)
        {
            var session = new Rac1PulsingOrbSession();
            var mobies = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level)).MobyInstances;
            foreach (var source in mobies.Where(m => m.OClass == Rac1PulsingOrbSession.NativeClassId))
            {
                Assert.True(session.Register(level.LevelId, Source(source.Index,
                    BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source.PVar!)))));
                total++;
            }
        }
        Assert.Equal(334, total);
    }

    private static RuntimeDynamicObject Source(int id, float phaseDegrees)
    {
        var pvar = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(pvar, BitConverter.SingleToInt32Bits(phaseDegrees));
        return new(ObpSourceGame.Rac1, Rac1PulsingOrbSession.NativeClassId, id, null, "orb", "orb",
            new RuntimeObjectTransform(new double[16]), [],
            NativePayloads: [new RuntimeOpaquePayload("rac1-pvar", pvar)]);
    }
}
