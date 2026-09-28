using OneBigPackage;

namespace OBP.Tests;

public sealed class CommandLineArgsTests
{
    [Fact]
    public void SmokeSceneIsOnlyExplicitlyRequested()
    {
        CommandLineArgs ordinary = CommandLineArgs.Parse([]);
        CommandLineArgs explicitSmoke = CommandLineArgs.Parse(["--test-scene", "smoke"]);

        Assert.Equal("smoke", ordinary.TestScene);
        Assert.False(ordinary.TestSceneExplicit);
        Assert.Equal("smoke", explicitSmoke.TestScene);
        Assert.True(explicitSmoke.TestSceneExplicit);
    }

    [Fact]
    public void SourceBootstrapOptionsShareTheParsedModel()
    {
        CommandLineArgs args = CommandLineArgs.Parse([
            "--rac1-iso", "rac1.iso",
            "--gc-iso", "gc.iso",
            "--uya-iso", "uya.iso",
            "--destination", "rac3:TABLE1",
            "--skip-title",
        ]);

        Assert.Equal("rac1.iso", args.Rac1Iso);
        Assert.Equal("gc.iso", args.GcIso);
        Assert.Equal("uya.iso", args.UyaIso);
        Assert.Equal("rac3:TABLE1", args.Destination);
        Assert.True(args.SkipTitle);
        Assert.False(args.TestSceneExplicit);
    }

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
        CommandLineArgs startup = CommandLineArgs.Parse(["--rac1-startup-visibility-smoke"]);

        Assert.True(play.Rac1VeldinPlaySmoke);
        Assert.False(play.Rac1CombatContractSmoke);

        Assert.True(legacyPlay.Rac1VeldinPlaySmoke);
        Assert.False(legacyPlay.Rac1CombatContractSmoke);

        Assert.False(contract.Rac1VeldinPlaySmoke);
        Assert.True(contract.Rac1CombatContractSmoke);

        Assert.True(startup.Rac1StartupVisibilitySmoke);
        Assert.False(startup.TestSceneExplicit);
    }
}
