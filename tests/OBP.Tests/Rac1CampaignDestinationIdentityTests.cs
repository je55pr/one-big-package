using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1CampaignDestinationIdentityTests
{
    [Theory]
    [InlineData(1, "Novalis")]
    [InlineData(2, "Aridia")]
    [InlineData(3, "Kerwan")]
    [InlineData(4, "Eudora")]
    [InlineData(5, "Rilgar")]
    [InlineData(6, "Nebula G34")]
    [InlineData(7, "Umbris")]
    [InlineData(8, "Batalia")]
    [InlineData(9, "Gaspar")]
    [InlineData(10, "Orxon")]
    [InlineData(11, "Pokitaru")]
    [InlineData(12, "Hoven")]
    [InlineData(13, "Oltanis Orbit")]
    [InlineData(14, "Oltanis")]
    [InlineData(15, "Quartu")]
    [InlineData(16, "Kalebo III")]
    [InlineData(17, "Veldin Orbit")]
    [InlineData(18, "Veldin")]
    public void ResolvesEveryProvenRetailDestination(int destinationId, string expectedName)
    {
        Assert.Equal(expectedName, Rac1CampaignDestinationIdentity.ResolveDisplayName(destinationId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(19)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void UnprovenOrInvalidIdsRemainUnresolved(int destinationId)
    {
        Assert.Null(Rac1CampaignDestinationIdentity.ResolveDisplayName(destinationId));
    }

    [Fact]
    public void GalacticMapSlotOrderDoesNotReplaceStoredDestinationIdentity()
    {
        var campaign = new Rac1CampaignState();
        campaign.AdmitDestination(3);
        campaign.AdmitDestination(1);

        Assert.Equal("Kerwan", Rac1CampaignDestinationIdentity.ResolveDisplayName(campaign.GalacticMap[0]));
        Assert.Equal("Novalis", Rac1CampaignDestinationIdentity.ResolveDisplayName(campaign.GalacticMap[1]));
    }
}
