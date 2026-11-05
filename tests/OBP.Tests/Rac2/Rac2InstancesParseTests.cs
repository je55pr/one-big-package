using System.Buffers.Binary;
using OBP.RAC2.Level;

namespace OBP.Tests;

/// <summary>
/// Characterization of the Going Commando gameplay-lump block readers using
/// small synthetic buffers (no retail bytes). The reader is deliberately
/// lenient: malformed blocks yield empty or truncated lists, never exceptions.
/// </summary>
public sealed class Rac2InstancesParseTests
{
    private const int BufferSize = 0x1400;
    private const int DirLightsPtr = 0x04, TiePtr = 0x34, ShrubPtr = 0x40, MobyPtr = 0x4c;
    private const int PVarTablePtr = 0x5c, PVarDataPtr = 0x60, PointLightsPtr = 0x80, EnvTransitionsPtr = 0x84, EnvSamplesPtr = 0x8c;

    private static void I32(byte[] b, int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), v);
    private static void F32(byte[] b, int at, float v) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(at), v);
    private static void U16(byte[] b, int at, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), v);
    private static void S16(byte[] b, int at, short v) => BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(at), v);

    private static byte[] Populated()
    {
        var d = new byte[BufferSize];

        // Directional light block: one 0x40 light.
        I32(d, DirLightsPtr, 0x200); I32(d, 0x200, 1);
        for (int i = 0; i < 16; i++) F32(d, 0x210 + i * 4, i + 1);

        // Tie block: two 0x60 structs; shrub block: one 0x70 struct.
        I32(d, TiePtr, 0x300); I32(d, 0x300, 2);
        for (int t = 0; t < 2; t++)
        {
            int at = 0x310 + t * 0x60;
            I32(d, at, 100 + t);
            for (int m = 0; m < 16; m++) F32(d, at + 0x10 + m * 4, t * 100 + m);
        }
        I32(d, ShrubPtr, 0x400); I32(d, 0x400, 1);
        I32(d, 0x410, 77);
        F32(d, 0x410 + 0x10 + 15 * 4, 9f); // remaining matrix entries are zero

        // Moby block: valid, non-finite position (skipped), valid with PVar 0, bad size (stops the walk).
        I32(d, MobyPtr, 0x500); I32(d, 0x500, 4);
        for (int m = 0; m < 4; m++)
        {
            int at = 0x510 + m * 0x88;
            I32(d, at, m == 3 ? 0x80 : 0x88);
            I32(d, at + 0x10, 1000 + m); I32(d, at + 0x14, 2000 + m);
            I32(d, at + 0x28, 500 + m); F32(d, at + 0x2c, 1.5f);
            F32(d, at + 0x40, m == 1 ? float.NaN : 10 + m); F32(d, at + 0x44, 20 + m); F32(d, at + 0x48, 30 + m);
            F32(d, at + 0x4c, 0.1f); F32(d, at + 0x50, 0.2f); F32(d, at + 0x54, 0.3f);
            I32(d, at + 0x68, m == 2 ? 0 : -1);
            I32(d, at + 0x70, 0x55);
            I32(d, at + 0x74, 255); I32(d, at + 0x78, 51); I32(d, at + 0x7c, 0);
            I32(d, at + 0x80, 3);
        }

        // PVar table (relative offset 4, size 3) over a data block.
        I32(d, PVarTablePtr, 0x800); I32(d, 0x800, 4); I32(d, 0x804, 3);
        I32(d, PVarDataPtr, 0x900);
        d[0x904] = 0xaa; d[0x905] = 0xbb; d[0x906] = 0xcc;

        // Point lights: entries start 0x800 past the block header; radius 0 is skipped.
        I32(d, PointLightsPtr, 0xa00); I32(d, 0xa00, 3);
        int pl = 0xa00 + 0x10 + 0x800;
        foreach ((int i, ushort radius) in new[] { (0, (ushort)128), (1, (ushort)0), (2, (ushort)64) })
        {
            int at = pl + i * 0x10;
            U16(d, at, (ushort)(64 * (i + 1))); U16(d, at + 2, 32); U16(d, at + 4, 16); U16(d, at + 6, radius);
            U16(d, at + 8, 65535); U16(d, at + 10, 0); U16(d, at + 12, 65535);
        }

        // Environment transition: sphere table then 0x80 struct.
        I32(d, EnvTransitionsPtr, 0xc00); I32(d, 0xc00, 1);
        F32(d, 0xc10, 1); F32(d, 0xc14, 2); F32(d, 0xc18, 3); F32(d, 0xc1c, 4);
        int tr = 0xc20;
        for (int j = 0; j < 16; j++) F32(d, tr + j * 4, j);
        d[tr + 0x40] = 255; d[tr + 0x41] = 0; d[tr + 0x42] = 51;
        d[tr + 0x44] = 51; d[tr + 0x45] = 153; d[tr + 0x46] = 255;
        I32(d, tr + 0x48, 7); I32(d, tr + 0x4c, 8);
        d[tr + 0x54] = 13; d[tr + 0x55] = 26; d[tr + 0x56] = 39;
        d[tr + 0x58] = 52; d[tr + 0x59] = 65; d[tr + 0x5a] = 78;
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(tr + 0x50), 2);
        F32(d, tr + 0x5c, 11); F32(d, tr + 0x60, 12); F32(d, tr + 0x64, 13); F32(d, tr + 0x68, 14);
        F32(d, tr + 0x6c, 21); F32(d, tr + 0x70, 22); F32(d, tr + 0x74, 23); F32(d, tr + 0x78, 24);

        // Environment samples: one with fog (far > near), one without.
        I32(d, EnvSamplesPtr, 0xd00); I32(d, 0xd00, 2);
        for (int s = 0; s < 2; s++)
        {
            int at = 0xd10 + s * 0x20;
            I32(d, at, 4 + s);
            S16(d, at + 4, 8); S16(d, at + 6, -8); S16(d, at + 8, 4);
            S16(d, at + 0x0c, 12);
            d[at + 0x0e] = 3; d[at + 0x0f] = 4;
            d[at + 0x10] = 255; d[at + 0x11] = 0; d[at + 0x12] = 51;
            d[at + 0x17] = 51; d[at + 0x18] = 255; d[at + 0x19] = 0;
            S16(d, at + 0x1a, 100); S16(d, at + 0x1c, (short)(s == 0 ? 400 : 100));
        }
        return d;
    }

    [Fact]
    public void ParsesEveryBlockKindFromPackedStructs()
    {
        byte[] data = Populated();
        Rac2Instances.Gameplay g = Rac2Instances.Parse(data);

        Assert.Equal(BufferSize, g.DecompressedSize);

        var light = Assert.Single(g.DirLights);
        Assert.Equal((1f, 2f, 3f), light.ColourA);
        Assert.Equal((5f, 6f, 7f), light.DirectionA);
        Assert.Equal((9f, 10f, 11f), light.ColourB);
        Assert.Equal((13f, 14f, 15f), light.DirectionB);

        Assert.Equal(2, g.TieInstances.Count);
        Assert.Equal((0, 100), (g.TieInstances[0].Index, g.TieInstances[0].OClass));
        Assert.Equal((1, 101), (g.TieInstances[1].Index, g.TieInstances[1].OClass));
        Assert.Equal(100f + 15f, g.TieInstances[1].Matrix[15]);
        Assert.Equal(16, g.TieInstances[0].Matrix.Length);

        var shrub = Assert.Single(g.ShrubInstances);
        Assert.Equal(77, shrub.OClass);
        Assert.Equal(9f, shrub.Matrix[15]);
        Assert.All(shrub.Matrix.Take(15), value => Assert.Equal(0f, value));
    }

    [Fact]
    public void MobyWalkSkipsNonFinitePositionsAndStopsAtBadSizeField()
    {
        Rac2Instances.Gameplay g = Rac2Instances.Parse(Populated());

        Assert.Equal([0, 2], g.MobyInstances.Select(m => m.Index));
        Rac2Instances.MobyInstance moby = g.MobyInstances[1];
        Assert.Equal(502, moby.OClass);
        Assert.Equal(1.5f, moby.Scale);
        Assert.Equal((12f, 22f, 32f), moby.Position);
        Assert.Equal((0.1f, 0.2f, 0.3f), moby.Rotation);
        Assert.Equal((1f, 51f / 255f, 0f), moby.LightColour);
        Assert.Equal(3, moby.LightIndex);
        Assert.Equal((1002, 2002, 0, 0x55), (moby.Uid, moby.Bolts, moby.PVarIndex, moby.ModeBits));
        Assert.Equal(0x88, moby.RawInstance.Length);
        Assert.Equal([0xaa, 0xbb, 0xcc], moby.PVarData);
        Assert.Null(g.MobyInstances[0].PVarData);
        Assert.Equal(-1, g.MobyInstances[0].PVarIndex);
    }

    [Fact]
    public void PointLightsAreOffsetPastTheHeaderTableAndSkipZeroRadius()
    {
        Rac2Instances.Gameplay g = Rac2Instances.Parse(Populated());

        Assert.Equal(2, g.PointLights.Count);
        Assert.Equal((1f, 0.5f, 0.25f), g.PointLights[0].Position);
        Assert.Equal(2f, g.PointLights[0].Radius);
        Assert.Equal((1f, 0f, 1f), g.PointLights[0].Colour);
        Assert.Equal((3f, 0.5f, 0.25f), g.PointLights[1].Position);
        Assert.Equal(1f, g.PointLights[1].Radius);
    }

    [Fact]
    public void EnvironmentSamplesCarryFogOnlyWhenFarExceedsNear()
    {
        Rac2Instances.Gameplay g = Rac2Instances.Parse(Populated());

        Assert.Equal(2, g.EnvSamples.Count);
        Rac2Instances.EnvSample withFog = g.EnvSamples[0];
        Assert.Equal((2f, -2f, 1f), withFog.Position);
        Assert.Equal(4, withFog.HeroLightIndex);
        Assert.Equal((short)12, withFog.MusicTrack);
        Assert.Equal((1f, 0f, 51f / 255f), withFog.HeroColour);
        Assert.NotNull(withFog.Fog);
        Assert.Equal((51f / 255f, 1f, 0f), withFog.Fog!.Colour);
        Assert.Equal((100f, 400f, 3f, 4f), (withFog.Fog.NearDistance, withFog.Fog.FarDistance, withFog.Fog.NearIntensity, withFog.Fog.FarIntensity));
        Assert.Null(g.EnvSamples[1].Fog);
        Assert.Equal(5, g.EnvSamples[1].HeroLightIndex);
    }

    [Fact]
    public void EnvironmentTransitionsReadSpheresStatesAndFlagBits()
    {
        Rac2Instances.Gameplay g = Rac2Instances.Parse(Populated());

        Rac2Instances.EnvTransition t = Assert.Single(g.EnvTransitions);
        Assert.Equal((1f, 2f, 3f, 4f), t.BoundingSphere);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (float)i), t.InverseMatrix);
        Assert.False(t.EnableHero);
        Assert.True(t.EnableFog);
        Assert.Equal((1f, 0f, 51f / 255f), t.StateA.HeroColour);
        Assert.Equal((51f / 255f, 153f / 255f, 1f), t.StateB.HeroColour);
        Assert.Equal((13f / 255f, 26f / 255f, 39f / 255f), t.StateA.FogColour);
        Assert.Equal((52f / 255f, 65f / 255f, 78f / 255f), t.StateB.FogColour);
        Assert.Equal(7, t.StateA.HeroLightIndex);
        Assert.Equal(8, t.StateB.HeroLightIndex);
        Assert.Equal((11f, 13f, 12f, 14f), (t.StateA.FogNearDistance, t.StateA.FogFarDistance, t.StateA.FogNearIntensity, t.StateA.FogFarIntensity));
        Assert.Equal((21f, 23f, 22f, 24f), (t.StateB.FogNearDistance, t.StateB.FogFarDistance, t.StateB.FogNearIntensity, t.StateB.FogFarIntensity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x10)]
    [InlineData(0x90)]
    public void ShortOrEmptyBuffersYieldEmptyListsWithoutThrowing(int length)
    {
        Rac2Instances.Gameplay g = Rac2Instances.Parse(new byte[length]);

        Assert.Empty(g.TieInstances);
        Assert.Empty(g.ShrubInstances);
        Assert.Empty(g.MobyInstances);
        Assert.Empty(g.DirLights);
        Assert.Empty(g.PointLights);
        Assert.Empty(g.EnvSamples);
        Assert.Empty(g.EnvTransitions);
        Assert.Equal(length, g.DecompressedSize);
    }

    [Fact]
    public void ImplausibleCountsAndOutOfRangePointersAreIgnored()
    {
        byte[] data = Populated();
        I32(data, 0x300, 200_001);   // tie count above the 200k matrix cap
        I32(data, 0x500, -1);        // negative moby count
        I32(data, 0x200, 4097);      // directional lights above the 4096 cap
        I32(data, 0xd00, 4097);      // env samples above the cap
        I32(data, PointLightsPtr, data.Length); // pointer at the end of the buffer
        I32(data, EnvTransitionsPtr, -4);       // negative pointer

        Rac2Instances.Gameplay g = Rac2Instances.Parse(data);

        Assert.Empty(g.TieInstances);
        Assert.Empty(g.MobyInstances);
        Assert.Empty(g.DirLights);
        Assert.Empty(g.EnvSamples);
        Assert.Empty(g.PointLights);
        Assert.Empty(g.EnvTransitions);
        Assert.Single(g.ShrubInstances);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MaxValue - 8)]
    [InlineData(-1)]
    public void HostileBlockPointersAreIgnoredWithoutOffsetOverflow(int hostilePointer)
    {
        foreach (int pointer in new[] { TiePtr, ShrubPtr, MobyPtr, DirLightsPtr,
                     PointLightsPtr, EnvSamplesPtr, EnvTransitionsPtr })
        {
            byte[] data = Populated();
            I32(data, pointer, hostilePointer);
            Rac2Instances.Gameplay result = Rac2Instances.Parse(data);
            switch (pointer)
            {
                case TiePtr: Assert.Empty(result.TieInstances); break;
                case ShrubPtr: Assert.Empty(result.ShrubInstances); break;
                case MobyPtr: Assert.Empty(result.MobyInstances); break;
                case DirLightsPtr: Assert.Empty(result.DirLights); break;
                case PointLightsPtr: Assert.Empty(result.PointLights); break;
                case EnvSamplesPtr: Assert.Empty(result.EnvSamples); break;
                case EnvTransitionsPtr: Assert.Empty(result.EnvTransitions); break;
            }
        }
    }

    [Fact]
    public void TruncatedTrailingStructsAreDroppedRatherThanThrown()
    {
        byte[] data = Populated();
        // Claim many ties but leave only the first one fully inside the buffer.
        int block = BufferSize - 0x10 - 0x60 - 0x20;
        I32(data, TiePtr, block);
        I32(data, block, 1000);
        I32(data, block + 0x10, 321);

        Rac2Instances.Gameplay g = Rac2Instances.Parse(data);

        Assert.Equal(321, Assert.Single(g.TieInstances).OClass);
    }

    [Fact]
    public void MissingPVarBlocksYieldNullPayloads()
    {
        byte[] data = Populated();
        I32(data, PVarTablePtr, 0);

        Rac2Instances.Gameplay g = Rac2Instances.Parse(data);

        Assert.Equal(new[] { 0, 2 }, g.MobyInstances.Select(m => m.Index));
        Assert.All(g.MobyInstances, m => Assert.Null(m.PVarData));
    }
}
