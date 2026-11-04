using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OBP.IO;
using OBP.PS2;
using OBP.PS2.Elf;
using OBP.RAC1.Camera;

namespace OBP.Tests;

public sealed class Rac1CameraNativeSourceTests
{
    [SkippableFact]
    public void OriginalOverlay_PinsCameraStagesOptionsAndSpringConstants()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "research/generated/rac1-camera-native-traces.json")));
        RequireCodeStages(doc.RootElement.GetProperty("code"));
        foreach (var stage in doc.RootElement.GetProperty("code").EnumerateArray())
        {
            int start = int.Parse(stage.GetProperty("start").GetString()![2..], NumberStyles.HexNumber);
            int end = int.Parse(stage.GetProperty("end").GetString()![2..], NumberStyles.HexNumber);
            Assert.Equal(stage.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(read(start, end - start))).ToLowerInvariant());
        }
        double Scalar(int address) => BinaryPrimitives.ReadSingleLittleEndian(read(address, 4));
        Assert.Equal(Rac1OrdinaryCameraController.ManualInputAcceleration, Scalar(0x161cac));
        Assert.Equal(Rac1OrdinaryCameraController.ManualInputDamping, Scalar(0x161cb0));
        Assert.Equal(Rac1OrdinaryCameraController.VerticalActiveStep, Scalar(0x161cbc));
        Assert.Equal(Rac1OrdinaryCameraController.VerticalReleaseStep, Scalar(0x161cc0));
        Assert.Equal(Rac1OrdinaryCameraController.VerticalTurnLimit, Scalar(0x161cc4));
        Assert.Equal(Rac1OrdinaryCameraController.CollisionBaseRadius, Scalar(0x161cd8));
        Assert.Equal(Rac1OrdinaryCameraController.CollisionRadiusStep, Scalar(0x161cdc));
        Assert.Equal(Rac1OrdinaryCameraController.CollisionProbeCount, Scalar(0x161ce0));
        for (int i = 0; i < 3; ++i)
            Assert.Equal(new Rac1CameraOptions(Speed: (Rac1CameraSpeed)i).YawRadiansPerUpdate, Scalar(0x161db0 + i * 4));
        Assert.Equal(0x4fa5, BinaryPrimitives.ReadInt32LittleEndian(read(0x1b5b48, 4))); // value 0: Reversed
        Assert.Equal(0x4fa4, BinaryPrimitives.ReadInt32LittleEndian(read(0x1b5b4c, 4))); // value 1: Normal
    }

    [SkippableFact]
    public void BootProgram_InitializesBothAxesAndSpeedToNativeNormalMedium()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(path!);
        var boot = Ps2Boot.OpenDisc(disc);
        var segments = Ps2Boot.ReadBootProgramHeaders(boot);
        foreach (int address in new[] { 0x15eddc, 0x15ede0, 0x15ede4 })
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(Elf32Reader.ReadVirtualRange(boot.BootExecutable, segments, address, 4)));
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("renamed")]
    [InlineData("empty-range")]
    public void CodeWitnessRequiresEveryPreservedStageIdentity(string fault)
    {
        var witness = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "research/generated/rac1-camera-native-traces.json")))!;
        var code = witness["code"]!.AsArray();
        switch (fault)
        {
            case "missing": code.RemoveAt(0); break;
            case "duplicate": code[1] = code[0]!.DeepClone(); break;
            case "renamed": code[0]!["stage"] = "replacement"; break;
            case "empty-range": code[0]!["end"] = code[0]!["start"]!.GetValue<string>(); break;
        }
        void Check() => RequireCodeStages(JsonSerializer.SerializeToElement(code));
        if (fault == "complete") Check();
        else Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);
    }

    private static void RequireCodeStages(JsonElement code)
    {
        // Independent identities retained by tools/rac1-camera-native-trace.py.
        (string Stage, int Start, int End)[] expected =
        [
            ("manualSelector", 0x2e89b0, 0x2e8bb8), ("turn", 0x2e8bb8, 0x2e8f40),
            ("follow", 0x2e8f40, 0x2e9010), ("aim", 0x2e9010, 0x2e9450),
            ("position", 0x2e9518, 0x2e9aa0), ("update", 0x2e9c28, 0x2e9cb8),
            ("collision", 0x2e7d20, 0x2e8368), ("projection", 0x1f7848, 0x1f7b00),
        ];
        var actual = code.EnumerateArray().Select(stage => (
            Stage: stage.GetProperty("stage").GetString()!,
            Start: int.Parse(stage.GetProperty("start").GetString()![2..], NumberStyles.HexNumber),
            End: int.Parse(stage.GetProperty("end").GetString()![2..], NumberStyles.HexNumber))).ToArray();
        Assert.Equal(expected.OrderBy(stage => stage.Stage, StringComparer.Ordinal),
            actual.OrderBy(stage => stage.Stage, StringComparer.Ordinal));
    }
}
