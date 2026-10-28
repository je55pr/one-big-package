using System.Numerics;
using System.Text.Json;
using OBP.Core;
using OBP.Game;

namespace OBP.Tests;

public sealed class CaptureFrameMetadataTests
{
    [Fact]
    public void FrameMetadataRetainsWireNamesNumericTypesRoundingAndAbsentFacts()
    {
        var snapshot = new CaptureFrameSnapshot(42, "fixture-renderer", "fixture-engine", "fixture-build", ObpSourceGame.Rac2,
            "planet", "room", 1, 2, new(1.25f, 2, 3), null, 1.23456, [new("idle", 2, 30, true)],
            1, 2, 3, 4, 5, 6, 7, 8, 9, null, null, JsonSerializer.SerializeToElement(new { Health = 4 }),
            new(new(4, 5, 6), true, "movement", "camera", true, .123456789, 8, 7.5, .25, .5, .75, 2, "Ground", "Native", "Idle"));
        var metadata = CaptureFrameMetadata.Build(snapshot);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(metadata));
        var root = json.RootElement;
        Assert.Equal(26, root.EnumerateObject().Count());
        Assert.Equal(42, root.GetProperty("renderedFrames").GetInt64());
        Assert.Equal("rac2", root.GetProperty("game").GetString());
        Assert.Equal(1.235, root.GetProperty("animClockSeconds").GetDouble());
        Assert.Equal("{\"Name\":\"idle\",\"CurrentFrame\":2,\"FrameCount\":30,\"Playing\":true}", root.GetProperty("animations")[0].GetRawText());
        Assert.Equal("[1.25,2,3]", root.GetProperty("camera").GetRawText());
        var player = root.GetProperty("player");
        Assert.Equal(15, player.EnumerateObject().Count());
        Assert.Equal(.123456789, player.GetProperty("cameraControlHeading").GetDouble());
        Assert.Equal(2, player.GetProperty("cameraObstructionReleaseTicks").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("crateDebug").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("rac1Gameplay").ValueKind);
        var absent = CaptureFrameMetadata.Build(snapshot with { Game = null, Player = null, Level = null });
        Assert.Null(absent["game"]); Assert.Null(absent["player"]); Assert.Null(absent["levelId"]);
    }
}
