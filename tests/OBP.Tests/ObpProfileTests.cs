using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OBP.Fusion;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OneBigPackage;

namespace OBP.Tests;

public sealed class ObpProfileTests
{
    internal static ObpProfile Profile()
    {
        var native = new PlayerSessionNativeState(Rac1WeaponInventory.CreateOpeningLoadout(), 42, 1440);
        var owner = new ObpPlayerSession(PlayerSessionLifetimeTests.Snapshot(), native);
        native.Weapons.Select(FusionWeaponSession.Lancer); native.Weapons.LancerSession.ResetWorld(17);
        native.Held.HoldWrench(); native.Health.ApplyHostAridiaContactDamage();
        return new(1, ObpProfile.CurrentIntegration, ProfilePlayer.Capture(owner.Capture()), "obp:fusion-lab", [],
            [new("future-source/progress", 7, JsonSerializer.SerializeToElement(new { SameNativeId = 30, Mission = "untouched" }))]);
    }

    private static IPlayerSessionComponentSnapshot Decode(ProfilePayload payload) =>
        payload.Data.Deserialize<PlayerSessionNativeState.State>(ObpProfile.Options)!;

    [Fact]
    public void ProfileRoundTripsSourceQualifiedPlayerAndOpaqueCampaignWithoutNativeSaveSubstitution()
    {
        var profile = Profile();
        var decoded = ObpProfile.Decode(profile.Encode());
        var restored = new PlayerSessionNativeState(Rac1WeaponInventory.CreateOpeningLoadout(), 0, 1440);
        var session = new ObpPlayerSession(PlayerSessionLifetimeTests.Snapshot(), restored);
        session.Restore(decoded.Player.Restore(Decode));
        Assert.Equal(FusionWeaponSession.Lancer, restored.Weapons.Selected);
        Assert.Equal(17, restored.Weapons.LancerSession.Ammo);
        Assert.Equal(3, session.Capture().Health.Current);
        Assert.Equal(42, restored.Wallet.Balance); Assert.True(restored.Held.Hand.WrenchHeld);
        Assert.Equal("obp:fusion-lab", decoded.Destination);
        Assert.Equal("untouched", decoded.Campaigns[0].Data.GetProperty("Mission").GetString());
        Assert.Equal(30, decoded.Campaigns[0].Data.GetProperty("SameNativeId").GetInt32());
        Assert.False(new SourceWeaponKey("rac1", 30) == session.Capture().Loadout.SelectedWeapon);
    }

    [Fact]
    public void UnknownFieldsAndUnsupportedSourcePayloadRemainLossless()
    {
        var profile = Profile() with
        {
            Extensions = new() { ["futureField"] = JsonSerializer.SerializeToElement(new { Revision = 15 }) },
            Sources = [new("rac1", "fixture", new string('A', 64)) { Extensions = new() { ["futureVerification"] = JsonSerializer.SerializeToElement(7) } }]
        };
        var twice = ObpProfile.Decode(ObpProfile.Decode(profile.Encode()).Encode());
        Assert.Equal(15, twice.Extensions!["futureField"].GetProperty("Revision").GetInt32());
        Assert.Equal(7, twice.Campaigns.Single().Version);
        Assert.Equal(7, twice.Sources.Single().Extensions!["futureVerification"].GetInt32());
        twice.VerifyAvailableSources([]); // Missing sources remain remembered, without pretending they are available.
        twice.VerifyAvailableSources([twice.Sources.Single()]);
        Assert.Throws<InvalidDataException>(() => twice.VerifyAvailableSources([new("rac1", "another-build", new string('A', 64))]));
        Assert.Throws<InvalidDataException>(() => twice.VerifyAvailableSources([new("rac1", "fixture", new string('B', 64))]));
        Assert.Throws<UnsupportedProfileVersionException>(() => ObpProfile.Decode(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(profile.Encode()).Replace("\"Version\": 1,", "\"Version\": 99,", StringComparison.Ordinal))));
    }

    [Fact]
    public void FutureEnvelopeIsNeverDecodedAsTheCurrentShapeAndMalformedMembersAreRecoverable()
    {
        Assert.Throws<UnsupportedProfileVersionException>(() => ObpProfile.Decode("{\"Version\":99,\"NewShape\":true}"u8.ToArray()));
        foreach (string property in new[] { "Player", "Sources", "Campaigns" })
        {
            var json = JsonNode.Parse(Profile().Encode())!; json[property] = null;
            Assert.Throws<InvalidDataException>(() => ObpProfile.Decode(Encoding.UTF8.GetBytes(json.ToJsonString())));
        }
        var missing = JsonNode.Parse(Profile().Encode())!;
        missing["Player"]!["Configuration"]!["Movement"] = null;
        Assert.Throws<InvalidDataException>(() => ObpProfile.Decode(Encoding.UTF8.GetBytes(missing.ToJsonString())));
    }

    [Fact]
    public void UnknownNestedRecordFieldsSurviveWhilePlayerEditsAndClearedListsTakePrecedence()
    {
        var json = JsonNode.Parse(Profile().Encode())!;
        json["Player"]!["Configuration"]!["Avatar"]!["FutureSkinField"] = "preserve";
        json["Player"]!["Health"]!["FutureHealthField"] = 7;
        var profile = ObpProfile.Decode(Encoding.UTF8.GetBytes(json.ToJsonString()));
        profile = profile with { Player = profile.Player with { Preferences = new Dictionary<string, string>(), Currencies = new Dictionary<string, long>() } };
        var encoded = JsonNode.Parse(profile.Encode())!;
        Assert.Equal("preserve", encoded["Player"]!["Configuration"]!["Avatar"]!["FutureSkinField"]!.GetValue<string>());
        Assert.Equal(7, encoded["Player"]!["Health"]!["FutureHealthField"]!.GetValue<int>());
        Assert.Empty(encoded["Player"]!["Currencies"]!.AsObject());
        var old = new ProfilePayload("adapter", 1, JsonSerializer.SerializeToElement(new { Nested = new { Value = 1, Future = true }, Items = new[] { 1, 2 } }));
        var current = new ProfilePayload("adapter", 1, JsonSerializer.SerializeToElement(new { Nested = new { Value = 2 }, Items = Array.Empty<int>() })).RetainUnknownFields(old);
        Assert.True(current.Data.GetProperty("Nested").GetProperty("Future").GetBoolean());
        Assert.Equal(2, current.Data.GetProperty("Nested").GetProperty("Value").GetInt32());
        Assert.Empty(current.Data.GetProperty("Items").EnumerateArray());
    }

    [Fact]
    public void FailedAtomicReplacementLeavesPrimaryBackupAndSeparateNativeCampaignUntouched()
    {
        string directory = Path.Combine(Path.GetTempPath(), "obp-profile-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "profile.json"), native = Path.Combine(directory, "rac1-campaign.json");
        try
        {
            File.WriteAllText(native, "isolated native campaign");
            AtomicDocumentFile.Save(path, Profile().Encode(), ObpProfile.MaximumBytes, _ => true);
            byte[] before = File.ReadAllBytes(path);
            Assert.ThrowsAny<IOException>(() => AtomicDocumentFile.Save(path, (Profile() with { Destination = "rac2:LEVEL1" }).Encode(), ObpProfile.MaximumBytes,
                _ => throw new IOException("Injected existing-file read failure")));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal("isolated native campaign", File.ReadAllText(native));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("rac1:LEVEL0")]
    [InlineData("rac2:LEVEL1")]
    [InlineData("rac3:TABLE1")]
    [InlineData("obp:fusion-lab")]
    public void EveryQualifiedWorldRestoresTheSamePlayer(string destination)
    {
        var profile = Profile() with { Destination = destination };
        Assert.Equal(destination, ObpProfile.Decode(profile.Encode()).Destination);
        Assert.Equal(FusionWeaponSession.Lancer, profile.Player.Loadout.SelectedWeapon);
        Assert.False(ObpProfile.IsDestination("../../profile.json"));
    }

    [Fact]
    public void AtomicRecoveryRetainsBadPrimaryAndNeverRollsBackAnUnknownSchema()
    {
        string directory = Path.Combine(Path.GetTempPath(), "obp-profile-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "profile.json");
        bool Valid(byte[] bytes) { try { _ = ObpProfile.Decode(bytes); return true; } catch (JsonException) { return false; } }
        bool Recoverable(Exception error) => error is IOException or JsonException or ArgumentException or InvalidDataException;
        try
        {
            var first = Profile(); AtomicDocumentFile.Save(path, first.Encode(), ObpProfile.MaximumBytes, Valid);
            var second = first with { Destination = "rac3:TABLE1" }; AtomicDocumentFile.Save(path, second.Encode(), ObpProfile.MaximumBytes, Valid);
            File.WriteAllText(path, "{ interrupted");
            var backup = AtomicDocumentFile.Load(path, ObpProfile.MaximumBytes, ObpProfile.Decode, Recoverable);
            Assert.Equal(DocumentReadKind.Backup, backup.Kind); Assert.Equal(first.Destination, backup.Value!.Destination);
            File.WriteAllText(path, "{\"Version\":1,\"Player\":null}");
            Assert.Equal(DocumentReadKind.Backup, AtomicDocumentFile.Load(path, ObpProfile.MaximumBytes, ObpProfile.Decode, Recoverable).Kind);
            AtomicDocumentFile.Save(path, second.Encode(), ObpProfile.MaximumBytes, Valid);
            Assert.Single(Directory.GetFiles(directory, "*.rejected-*"));
            Assert.Equal("rac3:TABLE1", ObpProfile.Decode(File.ReadAllBytes(path)).Destination);
            File.WriteAllText(path, Encoding.UTF8.GetString(second.Encode()).Replace("\"Version\": 1,", "\"Version\": 99,", StringComparison.Ordinal));
            Assert.Throws<UnsupportedProfileVersionException>(() => AtomicDocumentFile.Load(path, ObpProfile.MaximumBytes, ObpProfile.Decode, Recoverable));
            Assert.Contains("99", File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
