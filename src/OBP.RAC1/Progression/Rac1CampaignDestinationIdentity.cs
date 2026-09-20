namespace OBP.RAC1.Progression;

/// <summary>
/// Resolves the retail-proven R&C1 campaign destination ids to their display names.
/// GalacticMap entries contain these destination ids; GalacticMap slot position is
/// only admission order and must not be treated as destination identity. The native
/// loader uses the same integer as the 19-entry retail level-table index.
/// </summary>
public static class Rac1CampaignDestinationIdentity
{
    public static string? ResolveDisplayName(int destinationId) => destinationId switch
    {
        1 => "Novalis",
        2 => "Aridia",
        3 => "Kerwan",
        4 => "Eudora",
        5 => "Rilgar",
        6 => "Nebula G34",
        7 => "Umbris",
        8 => "Batalia",
        9 => "Gaspar",
        10 => "Orxon",
        11 => "Pokitaru",
        12 => "Hoven",
        13 => "Oltanis Orbit",
        14 => "Oltanis",
        15 => "Quartu",
        16 => "Kalebo III",
        17 => "Veldin Orbit",
        18 => "Veldin",
        _ => null,
    };
}
