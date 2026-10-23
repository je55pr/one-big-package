using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1WaterNativeSourceTests
{
    [SkippableFact]
    public void NovalisWaterFloorQueryAndLedgeGateShareCurrentSolidHeight()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay(1);
        foreach (var (start, end, hash) in new[]
        {
            (0x232dc0, 0x233338, "3288e92678198c29ac7ad7e3048b15a88019f6f2375ab1bbdae1fa4c172acf5e"),
            (0x22d090, 0x22d838, "2300c2b3b70fc296badf839d61d521bf890d87cd326e94ebe582a7a37c9acee3"),
            (0x2151d8, 0x215208, "bcd0054808f70a13edcb79367704eb07f1d31ef9e121459f2f7750e0715cd307"),
        })
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(read(start, end - start))).ToLowerInvariant());
        uint Word(int address) => BinaryPrimitives.ReadUInt32LittleEndian(read(address, 4));
        Assert.Equal(0xae2002d8u, Word(0x232e3c)); // reset floor each query
        Assert.Equal(0x24060024u, Word(0x232f50)); // solid query below water
        Assert.Equal(0xe6a002d8u, Word(0x233090)); // write hit height
        Assert.Equal(0xc62202d8u, Word(0x22d5d8)); // ledge reads that height
        Assert.Equal(0x2002ffffu, Word(0x215204)); // low class31 returns -1
    }

    [SkippableFact]
    public void NovalisCodeAndTablesMatchReproducibleOriginalInstructionWitnesses()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay(1);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root,
            "research/generated/rac1-water-native-witness.json")));
        foreach (var stage in doc.RootElement.GetProperty("code").EnumerateArray())
        {
            int start = int.Parse(stage.GetProperty("start").GetString()![2..], NumberStyles.HexNumber);
            int end = int.Parse(stage.GetProperty("end").GetString()![2..], NumberStyles.HexNumber);
            Assert.Equal(stage.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(read(start, end - start))).ToLowerInvariant());
        }
        for (int i = 0; i < 30; i++)
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(read(0x17c440 + i * 4, 4));
            // At integer time and unit advance the native sampler reads i-1.
            float sampled = Rac1WaterMovementSession.SampleStroke(new(101, (i + 1) % 30, 1), 101);
            Assert.Equal(value, sampled);
        }
        var script = new (float Value, int Ticks)[]
            { (-27, 9), (40, 1), (55, 1), (75, 1), (90.5f, 9), (75, 4), (40, 3), (0, 100) };
        for (int i = 0; i < script.Length; i++)
        {
            Assert.Equal(script[i].Value, BinaryPrimitives.ReadSingleLittleEndian(read(0x17c3e0 + i * 12, 4)));
            Assert.Equal(0f, BinaryPrimitives.ReadSingleLittleEndian(read(0x17c3e4 + i * 12, 4)));
            Assert.Equal(script[i].Ticks, BinaryPrimitives.ReadInt32LittleEndian(read(0x17c3e8 + i * 12, 4)));
        }
    }

    [Fact]
    public void OriginalSurfaceEntryCasesAgreeWithTheControllerAdmission()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root,
            "research/generated/rac1-water-native-witness.json")));
        foreach (var witness in doc.RootElement.GetProperty("entry").EnumerateArray().Take(4))
        {
            float[] f = witness.GetProperty("input").EnumerateArray().Select(n => n.GetSingle()).ToArray();
            var result = new Rac1WaterMovementSession().Step(new(new(true, f[7], f[7] - f[8]),
                f[6], new(0, 0, f[10]), f[9], 0, 0, 0,
                MovementDomain: (int)f[0], MovementAction: (int)f[1], HasApexed: f[2] != 0));
            Assert.Equal(witness.GetProperty("output")[0].GetInt32() == 0x37, result.Active);
        }
    }

    [Fact]
    public void OriginalPeriodicWaterQueryEndsAboveFeetSoItCannotCancelSurfaceLedgeJumps()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root,
            "research/generated/rac1-water-native-witness.json")));
        foreach (var witness in doc.RootElement.GetProperty("periodicQuery").EnumerateArray())
        {
            float feet = witness.GetProperty("input")[6].GetSingle();
            var output = witness.GetProperty("output");
            Assert.Equal(-1f, output[0].GetSingle()); // segment execution supplied clear
            Assert.InRange(output[2].GetSingle() - feet, 3.99999f, 4.00001f);
            Assert.InRange(output[3].GetSingle() - feet, 1.29999f, 1.30001f);
            Assert.Equal(1f, output[4].GetSingle());
        }
    }
}
