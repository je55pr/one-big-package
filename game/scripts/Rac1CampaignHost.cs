using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;

namespace OBP.Game;

/// <summary>
/// Persistent source campaign and its parity wallet, separate from the ordinary
/// player's session and every world lease. Native save/travel adapters remain authoritative.
/// </summary>
internal sealed class Rac1CampaignHost
{
    internal Rac1CampaignRuntimeSession Session { get; set; } = new(new Rac1CampaignState(), Rac1WeaponInventory.CreateOpeningLoadout());
    internal Rac1BoltWallet Wallet { get; set; } = new();
}
