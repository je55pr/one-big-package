using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        RequireWitnessCohorts(doc.RootElement);
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
        RequireWitnessCohorts(doc.RootElement);
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
        RequireWitnessCohorts(doc.RootElement);
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

    [Theory]
    [InlineData("complete", "")]
    [InlineData("code", "missing")]
    [InlineData("code", "duplicate")]
    [InlineData("code", "replacement")]
    [InlineData("code", "empty-range")]
    [InlineData("entry", "missing")]
    [InlineData("entry", "duplicate")]
    [InlineData("entry", "replacement")]
    [InlineData("entry", "null-output")]
    [InlineData("entry", "fractional-output")]
    [InlineData("periodicQuery", "missing")]
    [InlineData("periodicQuery", "duplicate")]
    [InlineData("periodicQuery", "replacement")]
    [InlineData("periodicQuery", "null-output")]
    public void WaterWitnessRequiresThePreservedCodeAndInputCohorts(string cohort, string fault)
    {
        var witness = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "research/generated/rac1-water-native-witness.json")))!;
        if (cohort != "complete")
        {
            var rows = witness[cohort]!.AsArray();
            switch (fault)
            {
                case "missing": rows.RemoveAt(0); break;
                case "duplicate": rows[1] = rows[0]!.DeepClone(); break;
                case "replacement" when cohort == "code": rows[0]!["stage"] = "replacement"; break;
                case "empty-range": rows[0]!["end"] = rows[0]!["start"]!.GetValue<string>(); break;
                case "replacement": rows[0]!["input"]![0] = 999; break;
                case "null-output": rows[^1]!["output"]![0] = null; break;
                case "fractional-output": rows[^1]!["output"]![0] = 1.5; break;
                default: throw new ArgumentOutOfRangeException(nameof(fault), fault, "Unknown witness fault.");
            }
        }
        void Check() => RequireWitnessCohorts(JsonSerializer.SerializeToElement(witness));
        if (cohort == "complete") Check();
        else Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);
    }

    private static void RequireWitnessCohorts(JsonElement witness)
    {
        // Independent stage ranges and input plans in tools/rac1-water-native-trace.py.
        (string Stage, int Start, int End)[] expected =
        [
            ("underwaterMotion", 0x2378bc, 0x23880c), ("surfaceMotion", 0x238a24, 0x238d20),
            ("oxygen", 0x2406b0, 0x2408e8), ("waterEntry", 0x2408e8, 0x240c78),
            ("entryQueryEndpoints", 0x232cc0, 0x232dc0), ("waterline", 0x240c78, 0x240ed8),
            ("inputDispatch", 0x2435b8, 0x243c08), ("exitImpulse", 0x17c3e0, 0x17c440),
            ("strokeCurve", 0x17c440, 0x17c4b8),
        ];
        var actual = witness.GetProperty("code").EnumerateArray().Select(stage => (
            Stage: stage.GetProperty("stage").GetString()!,
            Start: int.Parse(stage.GetProperty("start").GetString()![2..], NumberStyles.HexNumber),
            End: int.Parse(stage.GetProperty("end").GetString()![2..], NumberStyles.HexNumber))).ToArray();
        Assert.Equal(expected.OrderBy(stage => stage.Stage, StringComparer.Ordinal),
            actual.OrderBy(stage => stage.Stage, StringComparer.Ordinal));
        RequireInputs("entry", 2,
        [
            [4, 7, 1, 0, 10000, 0, 55.65f, 56, 4, -.1f, -.1f, 0],
            [4, 7, 0, 0, 10000, 0, 55.65f, 56, 4, -.1f, -.1f, 0],
            [2, 0, 1, 0, 10000, 0, 55.65f, 56, .1875f, -.1f, -.1f, 0],
            [4, 7, 1, 0, 10000, 0, 56.12f, 56, 4, -.1f, -.1f, 0],
            [17, 51, 1, 0, 5000, 0, 55.7f, 56, 4, .04f, .04f, -.9f],
            [17, 51, 1, 1, 5000, 0, 55.7f, 56, 4, .04f, .04f, -.9f],
        ]);
        RequireInputs("periodicQuery", 5,
        [
            [4, 0x1c, 0, 0, 10000, 0, 82.07f, 82.25f, 3.75f, 0, 0, 0],
            [4, 7, 0, 0, 10000, 0, 80.8f, 82.25f, 3.75f, 0, 0, 0],
        ]);

        void RequireInputs(string name, int outputColumns, float[][] inputs)
        {
            var rows = witness.GetProperty(name).EnumerateArray().ToArray();
            Assert.Equal(inputs.Length, rows.Length);
            for (int index = 0; index < rows.Length; index++)
            {
                Assert.Equal(inputs[index], rows[index].GetProperty("input").EnumerateArray().Select(value => value.GetSingle()));
                Assert.Equal(outputColumns, rows[index].GetProperty("output").GetArrayLength());
                Assert.All(rows[index].GetProperty("output").EnumerateArray(), value =>
                {
                    Assert.True(value.ValueKind == JsonValueKind.Number &&
                        value.TryGetDouble(out double number) && double.IsFinite(number));
                    if (name == "entry") Assert.True(value.TryGetInt32(out _));
                });
            }
        }
    }
}
