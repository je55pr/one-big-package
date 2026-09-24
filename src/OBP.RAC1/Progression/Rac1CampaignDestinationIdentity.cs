namespace OBP.RAC1.Progression;

/// <summary>
/// Resolves the retail-proven R&C1 campaign destination ids to their display names.
/// GalacticMap entries contain these destination ids; GalacticMap slot position is
/// only admission order and must not be treated as destination identity. The native
/// loader uses the same integer as the 19-entry retail level-table index.
/// </summary>
public static class Rac1CampaignDestinationIdentity
{
    public static string? ResolveDisplayName(int destinationId) =>
        Rac1DestinationCatalogue.ResolveDisplayName(destinationId);
}
