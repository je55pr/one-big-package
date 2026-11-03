namespace OBP.Tests;

/// <summary>Guard the lease boundary for the #365 loot migration without needing retail discs.</summary>
public class LootPresentationOwnershipTests
{
    private static string Script(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", name));

    [Fact]
    public void LootAndCollectiblePresentationNoLongerLiveInApplicationRoot()
    {
        foreach (string retired in new[]
        {
            "OBPGame.Rac1Pickups.cs", "OBPGame.Rac1CrateSupply.cs",
            "OBPGame.Rac1PlacedBolts.cs", "OBPGame.Rac1GoldBolts.cs",
            "OBPGame.CrateBolts.cs",
        })
            Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game", "scripts", retired)), retired);

        string module = Script("Rac1WorldModule.cs");
        string loot = Script("Rac1LootPresentation.cs");
        Assert.Contains("Loot = new(scene, Context, Presentation, loot);", module, StringComparison.Ordinal);
        Assert.Contains("Loot.Dispose();", module, StringComparison.Ordinal);
        Assert.Contains("ResetRac1PickupPresentation();", loot, StringComparison.Ordinal);
        Assert.Contains("ResetRac1GoldBolts();", loot, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OBPGame", loot, StringComparison.Ordinal);
    }

    [Fact]
    public void RetailRewardIdentityAndLifecycleRemainSourceSpecific()
    {
        string rac1 = Script("Rac1LootPresentation.PlacedBolts.cs");
        string rac2 = Script("Rac2CrateBoltPresentation.cs");
        string rac2Module = Script("Rac2WorldModule.cs");
        string context = Script("Rac1WorldContext.cs");

        Assert.Contains("_context.PlacedBolts.TryCollect", rac1, StringComparison.Ordinal);
        Assert.Contains("_context.BoltCrates.CollectPickup", rac1, StringComparison.Ordinal);
        Assert.Contains("Rac1BoltCrateSession BoltCrates", context, StringComparison.Ordinal);
        Assert.Contains("Rac2CrateBolts", rac2, StringComparison.Ordinal);
        Assert.Contains("ports.Credit(receipt.Actor, pickup.PickupId, denomination)", rac2, StringComparison.Ordinal);
        Assert.Contains("!ports.Current()", rac2, StringComparison.Ordinal);
        Assert.Contains("CrateBolts.Dispose();", rac2Module, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1BoltCrateSession", rac2, StringComparison.Ordinal);
    }
}
