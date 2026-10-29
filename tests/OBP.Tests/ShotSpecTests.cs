using System.Text.Json;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>The engine-independent shot-list model used by the <c>--shots</c> capture runner.</summary>
public class ShotSpecTests
{
    [Fact]
    public void Parse_ReadsShots_AndAppliesDefaults()
    {
        var list = ShotList.Parse("""
            { "world": "oozla",
              "shots": [
                { "name": "establishing" },
                { "name": "top", "camera": "topDown", "settleFrames": 45 },
                { "name": "wire", "camera": "orbit", "overlay": "collisionwire", "azimuthDegrees": 50 }
              ] }
            """);

        Assert.Equal("oozla", list.World);
        Assert.Equal(3, list.Shots.Count);

        var first = list.Shots[0];
        Assert.Equal("establishing", first.Name);
        Assert.Equal(ShotCamera.Showcase, first.Camera); // default
        Assert.Null(first.Overlay);
        Assert.Equal(120, first.SettleFrames);          // default

        Assert.Equal(ShotCamera.TopDown, list.Shots[1].Camera);
        Assert.Equal(45, list.Shots[1].SettleFrames);

        Assert.Equal("collisionwire", list.Shots[2].Overlay);
        Assert.Equal(50.0, list.Shots[2].AzimuthDegrees);
    }

    [Fact]
    public void RoundTrips_ThroughJson()
    {
        var original = new ShotList("siberius",
        [
            new ShotSpec("a", ShotCamera.Orbit, "kindtint", 90, 40, 30),
            new ShotSpec("b"),
        ]);

        var back = ShotList.Parse(original.ToJson());
        Assert.Equal(original.World, back.World);
        Assert.Equal(original.Shots, back.Shots); // ShotSpec is a record — element-wise
    }

    [Fact]
    public void ToJson_UsesCamelCaseEnums_AndOmitsNullOverlay()
    {
        string json = new ShotList(null, [new ShotSpec("x", ShotCamera.TopDown)]).ToJson();
        Assert.Contains("\"topDown\"", json);
        Assert.DoesNotContain("overlay", json);
        Assert.DoesNotContain("\"world\"", json);
    }

    [Theory]
    [InlineData("{ }")]                                                  // no shots
    [InlineData("{ \"shots\": [] }")]                                    // empty
    [InlineData("{ \"shots\": [ { } ] }")]                               // no name
    [InlineData("{ \"shots\": [ { \"name\": \"a\" }, { \"name\": \"a\" } ] }")]  // dup
    [InlineData("{ \"shots\": [ { \"name\": \"a\", \"settleFrames\": 0 } ] }")]  // bad settle
    public void Parse_RejectsMalformedLists(string json)
    {
        Assert.Throws<JsonException>(() => ShotList.Parse(json));
    }

    [Theory]
    [InlineData("""{"shots":[{"name":"x","camera":"orbit","distance":20}]}""")]
    [InlineData("""{"shots":[{"name":"x","camera":"orbit","focus":[1,2],"distance":20}]}""")]
    [InlineData("""{"shots":[{"name":"x","camera":"orbit","focus":[1,2,3],"distance":0}]}""")]
    [InlineData("""{"shots":[{"name":"x","camera":"showcase","focus":[1,2,3],"distance":20}]}""")]
    [InlineData("""{"shots":[{"name":"x","camera":"orbit","focus":[1,2,3],"distance":20,"elevationDegrees":90}]}""")]
    public void FocusedOrbit_RejectsInvalidCaptureParameters(string json)
    {
        Assert.Throws<JsonException>(() => ShotList.Parse(json));
    }

    [Fact]
    public void FocusedOrbit_RoundTripsSourceIndependentCoordinates()
    {
        var shot = ShotList.Parse("""
            { "world": "maktar", "shots": [
                { "name": "arena", "camera": "orbit", "focus": [500, 500, 600],
                  "distance": 68, "azimuthDegrees": 30, "elevationDegrees": 24, "hideHud": true }
            ] }
            """).Shots.Single();

        Assert.Equal(ShotCamera.Orbit, shot.Camera);
        Assert.Equal(new[] { 500d, 500d, 600d }, shot.Focus);
        Assert.Equal(68, shot.Distance);
        Assert.True(shot.HideHud);
        var roundTrip = ShotList.Parse(new ShotList("maktar", [shot]).ToJson()).Shots.Single();
        Assert.Equal(shot.Focus, roundTrip.Focus);
        Assert.Equal(shot.Distance, roundTrip.Distance);
    }
}
