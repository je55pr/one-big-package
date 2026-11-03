using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1AmoeboidKnockbackProfileTests
{
    [Fact]
    public void NativePvarLaunchUsesOwnGravityDragAndSpeedWithoutClassWideGuessing()
    {
        var a = new byte[Rac1AmoeboidAuthored.PVarSize];
        BinaryPrimitives.WriteSingleLittleEndian(a.AsSpan(0x70), 0.008f);
        BinaryPrimitives.WriteSingleLittleEndian(a.AsSpan(0x74), 0.006944444f);
        BinaryPrimitives.WriteSingleLittleEndian(a.AsSpan(0x78), 0.2f);
        BinaryPrimitives.WriteSingleLittleEndian(a.AsSpan(0x7c), 0.125f);
        var profile = Rac1AmoeboidKnockbackProfile.Read(865, a);
        Assert.Equal(0.008f, profile.Gravity);
        Assert.Equal(0.006944444f, profile.Drag);
        Assert.Equal(0.2f, profile.PlanarLaunch);
        Assert.Equal(0.125f, profile.VerticalLaunch);
        var reaction = new Rac1MobyReaction(profile, 0);
        Assert.InRange(reaction.Velocity.X, 0.1999f, 0.2001f);
        Assert.InRange(reaction.Velocity.Z, 0.1249f, 0.1251f);
        var next = reaction.Step();
        Assert.InRange(next.X, 0.193f, 0.194f);
        Assert.InRange(next.Z, 0.116f, 0.118f);

        // Different native PVars must produce different launches, even
        // when both source Mobys are the same authored class.
        BinaryPrimitives.WriteSingleLittleEndian(a.AsSpan(0x78), 0.4f);
        var faster = new Rac1MobyReaction(Rac1AmoeboidKnockbackProfile.Read(865, a), 0);
        Assert.InRange(faster.Step().X, 0.393f, 0.394f);

        Assert.Throws<InvalidDataException>(() => Rac1AmoeboidKnockbackProfile.Read(865, []));
        BinaryPrimitives.WriteSingleLittleEndian(a.AsSpan(0x70), float.NaN);
        Assert.Throws<InvalidDataException>(() => Rac1AmoeboidKnockbackProfile.Read(865, a));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac1AmoeboidKnockbackProfile.Read(193, a));
    }

    [SkippableFact]
    public void All227RetailAmoeboidsPreserveNativePerInstanceKnockbackParameters()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable.");
        using var reader = new FileRandomAccessReader(iso!);
        int count = 0;
        foreach (var level in Rac1DiscIndex.Read(reader).Levels.Where(lv => lv.LevelId is 1 or 5 or 11))
        {
            var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            foreach (var actor in Rac1Instances.Parse(gameplay).MobyInstances
                .Where(moby => Rac1AmoeboidAuthored.IsFamily(moby.OClass)))
            {
                var profile = Rac1AmoeboidKnockbackProfile.Read(actor.OClass, actor.PVar!);
                Assert.InRange(profile.Gravity, 0.00799f, 0.00801f);
                Assert.InRange(profile.Drag, 0.00693f, 0.00696f);
                Assert.InRange(profile.PlanarLaunch, 0.1999f, 0.2001f);
                Assert.InRange(profile.VerticalLaunch, 0.1249f, 0.1251f);
                count++;
            }
        }
        Assert.Equal(227, count);
    }
}
