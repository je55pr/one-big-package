using OneBigPackage;

namespace OBP.Tests;

public sealed class CommandLineArgsTests
{
    [Fact]
    public void Rac1CampaignPersistenceIsOptIn()
    {
        CommandLineArgs ordinary = CommandLineArgs.Parse([]);
        CommandLineArgs defaultPersistence = CommandLineArgs.Parse(["--rac1-campaign-persist"]);
        CommandLineArgs isolatedPersistence = CommandLineArgs.Parse([
            "--rac1-campaign-save",
            "campaign.json",
        ]);

        Assert.False(ordinary.Rac1CampaignPersist);
        Assert.Null(ordinary.Rac1CampaignSavePath);
        Assert.False(ordinary.Rac1CampaignPersistenceRequested);

        Assert.True(defaultPersistence.Rac1CampaignPersist);
        Assert.True(defaultPersistence.Rac1CampaignPersistenceRequested);

        Assert.False(isolatedPersistence.Rac1CampaignPersist);
        Assert.Equal("campaign.json", isolatedPersistence.Rac1CampaignSavePath);
        Assert.True(isolatedPersistence.Rac1CampaignPersistenceRequested);
    }

    [Fact]
    public void Rac1PlayabilityAndSyntheticCombatSmokesAreDistinct()
    {
        CommandLineArgs play = CommandLineArgs.Parse(["--rac1-veldin-play-smoke"]);
        CommandLineArgs legacyPlay = CommandLineArgs.Parse(["--rac1-combat-smoke"]);
        CommandLineArgs contract = CommandLineArgs.Parse(["--rac1-combat-contract-smoke"]);

        Assert.True(play.Rac1VeldinPlaySmoke);
        Assert.False(play.Rac1CombatContractSmoke);

        Assert.True(legacyPlay.Rac1VeldinPlaySmoke);
        Assert.False(legacyPlay.Rac1CombatContractSmoke);

        Assert.False(contract.Rac1VeldinPlaySmoke);
        Assert.True(contract.Rac1CombatContractSmoke);
    }
}
