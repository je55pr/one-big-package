using System.Text.Json;
using OBP.Core;
using OBP.Core.Math;
using OBP.Fusion;
using OBP.RAC2.Gameplay;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OneBigPackage;

namespace OBP.Tests;

public sealed class SourceIdentityWireTests
{
    [Theory]
    [InlineData("rac1", ObpSourceGame.Rac1)]
    [InlineData("rac2", ObpSourceGame.Rac2)]
    [InlineData("rac3", ObpSourceGame.Rac3)]
    [InlineData("deadlocked", ObpSourceGame.Deadlocked)]
    [InlineData("synthetic", ObpSourceGame.Synthetic)]
    public void SourceWireValuesRemainStable(string wire, ObpSourceGame source)
    {
        Assert.Equal(source, SourceIdentityWire.ParseSource(wire));
        Assert.Equal(wire, SourceIdentityWire.FormatSource(source));
        Assert.Equal((WorldContentIdentity)source, SourceIdentityWire.ParseWorld(wire));
        Assert.Equal(wire, SourceIdentityWire.FormatWorld(source));
    }

    [Theory]
    [InlineData("gc", ObpSourceGame.Rac2)]
    [InlineData("R&C1", ObpSourceGame.Rac1)]
    [InlineData("Going Commando", ObpSourceGame.Rac2)]
    [InlineData(" Up Your Arsenal ", ObpSourceGame.Rac3)]
    [InlineData("RC3", ObpSourceGame.Rac3)]
    public void LegacyViewerLabelsAreAcceptedOnlyAtTheBoundary(string label, ObpSourceGame source) =>
        Assert.Equal(source, SourceIdentityWire.ParseSource(label));

    [Theory]
    [InlineData("")]
    [InlineData("rac4")]
    [InlineData("some new game")]
    [InlineData("fusion")]
    public void UnknownOrAuthoredLabelsCannotBecomeNativeSourceGames(string value) =>
        Assert.Throws<ArgumentException>(() => SourceIdentityWire.ParseSource(value));

    [Fact]
    public void AuthoredLabIsDistinctAndRetainsItsEstablishedWorldWireValue()
    {
        var lab = FusionLabWorldFactory.Build(FusionLabDefinition.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "data", "fusion_lab.json"))));
        Assert.Equal(WorldContentIdentity.FusionLab, lab.Game);
        Assert.True(lab.Game.IsAuthored);
        Assert.Equal("obp:fusion-lab", lab.Game.AuthoredId);
        Assert.Null(lab.Game.SourceGame);
        Assert.NotEqual((WorldContentIdentity)ObpSourceGame.Rac1, lab.Game);
        Assert.NotEqual((WorldContentIdentity)ObpSourceGame.Synthetic, lab.Game);
        Assert.Equal("\"fusion\"", JsonSerializer.Serialize(lab.Game));
        Assert.Equal(lab.Game, JsonSerializer.Deserialize<WorldContentIdentity>("\"fusion\""));
        Assert.Equal(lab.Game, SourceIdentityWire.ParseWorld("obp:fusion-lab"));
        Assert.Throws<ArgumentException>(() => SourceIdentityWire.ParseWorld("unknown-world"));
        Assert.Throws<ArgumentException>(() => SourceIdentityWire.FormatWorld(default));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SourceGame\":\"unknown\",\"NativeItemId\":30}")]
    [InlineData("{\"SourceGame\":\"fusion\",\"NativeItemId\":30}")]
    [InlineData("{\"SourceGame\":2,\"NativeItemId\":30}")]
    public void MigratedKeysRejectMissingUnknownAndNumericStringReplacements(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SourceWeaponKey>(json));

    [Fact]
    public void LegacyStringFieldsRoundTripWhileAlreadyTypedEnumsKeepNumericJson()
    {
        var key = JsonSerializer.Deserialize<SourceWeaponKey>("{\"SourceGame\":\"rac2\",\"NativeItemId\":30}").Validate();
        Assert.Equal(new(ObpSourceGame.Rac2, 30), key);
        Assert.Equal("rac2/item:30", key.ToString());
        Assert.Equal("rac2", JsonSerializer.SerializeToElement(key).GetProperty("SourceGame").GetString());
        Assert.Equal("rac2", JsonSerializer.SerializeToElement(new GcLancerTarget(ObpSourceGame.Rac2, 500, 1)).GetProperty("SourceGame").GetString());
        Assert.Equal("rac3", JsonSerializer.SerializeToElement(new UyaShockCannonTarget(ObpSourceGame.Rac3, 500, 1)).GetProperty("SourceGame").GetString());
        Assert.Equal("rac1", JsonSerializer.SerializeToElement(new FusionSourceIdentity(ObpSourceGame.Rac1, "build", "moby:0")).GetProperty("Game").GetString());
        Assert.Equal(0, JsonSerializer.SerializeToElement(new ObpBuildIdentity(ObpSourceGame.Rac1, "build")).GetProperty("Game").GetInt32());
        Assert.Equal(1, JsonSerializer.SerializeToElement(new ObpSourceRef(ObpSourceGame.Rac2, "build")).GetProperty("Game").GetInt32());
        Assert.Throws<ArgumentException>(() => default(SourceWeaponKey).Validate());
        Assert.Equal(new SourceWeaponKey(ObpSourceGame.Rac1, 0), JsonSerializer.Deserialize<SourceWeaponKey>("{\"SourceGame\":\"rac1\",\"NativeItemId\":0}").Validate());
    }

    [Fact]
    public void ProfilesAndCompositionsKeepTheirExistingSourceWireShapes()
    {
        var profile = ObpProfileTests.Profile() with
        {
            Sources = new[] { ObpSourceGame.Rac1, ObpSourceGame.Rac2, ObpSourceGame.Rac3 }
                .Select(source => new ProfileSource(source, "fixture-build", new string('a', 64))).ToArray(),
        };
        using var encoded = JsonDocument.Parse(profile.Encode());
        var sources = encoded.RootElement.GetProperty("Sources").EnumerateArray().ToArray();
        Assert.Equal(3, sources.Length);
        Assert.Equal(new[] { "rac1", "rac2", "rac3" }, sources.Select(source => source.GetProperty("Game").GetString()));
        Assert.Equal(JsonValueKind.Number, encoded.RootElement.GetProperty("Player").GetProperty("Configuration").GetProperty("Avatar").GetProperty("SourceGame").ValueKind);
        var world = new RuntimeWorld(ObpSourceGame.Rac3, "build", 1, null, null, [], [], 0, [], new ObpBounds(Vec3.Zero, Vec3.Zero), null, null);
        Assert.Equal("rac3", JsonSerializer.SerializeToElement(world).GetProperty("Game").GetString());
        var composition = CompositionJson.Deserialize("{\"version\":1,\"worlds\":[{\"id\":\"old\",\"sourceGame\":\"Going Commando\",\"levelId\":1},{\"id\":\"lab\",\"sourceGame\":\"fusion\"}]}");
        Assert.Equal((WorldContentIdentity)ObpSourceGame.Rac2, composition.World("old")!.SourceGame);
        Assert.Equal(WorldContentIdentity.FusionLab, composition.World("lab")!.SourceGame);
        var saved = CompositionJson.Serialize(composition);
        Assert.Contains("\"sourceGame\": \"rac2\"", saved);
        Assert.Contains("\"sourceGame\": \"fusion\"", saved);
        Assert.Throws<JsonException>(() => CompositionJson.Deserialize("{\"version\":1,\"worlds\":[{\"id\":\"bad\",\"sourceGame\":\"typo\"}]}"));
    }
}
