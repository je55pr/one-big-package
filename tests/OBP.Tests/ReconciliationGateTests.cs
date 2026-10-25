using System.Text.Json;
using OBP.Core;
using OBP.Fusion;
using OBP.Runtime.Gameplay;
using OBP.Game;

namespace OBP.Tests;

public sealed class ReconciliationGateTests
{
    internal static JsonElement Matrix => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root,
        "tests", "OBP.Tests", "reconciliation-capabilities.json"))).RootElement.Clone();

    [Fact]
    public void DeclaredCapabilitiesMatchRegisteredPlayerAndNativeEquipment()
    {
        var matrix = Matrix;
        Assert.Equal(1, matrix.GetProperty("Version").GetInt32());
        Assert.Equal(60, matrix.GetProperty("NativeHz").GetInt32());
        Assert.Equal(matrix.GetProperty("Movement").GetString(), PlayerHostProfiles.Native.Identity.ProfileId);
        Assert.Equal(ObpProfile.CurrentIntegration, matrix.GetProperty("OrdinaryProfile").GetString());
        Assert.DoesNotContain(HostOwnershipAudit.Policy(), entry => entry.Kind == "migration");
        foreach (var id in matrix.GetProperty("ImplementedWeapons").EnumerateArray())
            Assert.Equal(EquipmentImplementation.Implemented, PlayerEquipmentRegistrations.Catalog.Definitions.Single(entry => entry.Id == id.GetString()).Implementation);
        foreach (var id in matrix.GetProperty("UnavailableEquipment").EnumerateArray())
            Assert.Equal(EquipmentImplementation.Unimplemented, PlayerEquipmentRegistrations.Catalog.Definitions.Single(entry => entry.Id == id.GetString()).Implementation);
    }

    [Fact]
    public void CaptureAndProfileFlagsNeverInferParityFromTheDestination()
    {
        foreach (var world in Matrix.GetProperty("Route").EnumerateArray())
        {
            var args = CommandLineArgs.Parse(["--destination", world.GetString()!, "--record-playtest", "fixture.run.jsonl.gz", "--profile", "fixture.json"]);
            Assert.False(args.NativeParityRequested);
            Assert.True(args.PlaytestRequested);
            Assert.Equal("fixture.json", args.ProfilePath);
        }
        Assert.True(CommandLineArgs.Parse(["--retail-parity"]).NativeParityRequested);
    }
}
