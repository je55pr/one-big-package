using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Explicit parity fixture lease; ordinary equipment never constructs it.</summary>
internal sealed class Rac1FusionCampaignIsolation : IDisposable
{
    private readonly Rac1CampaignHost _campaign;
    private readonly ISet<int> _goldBolts;
    private readonly Rac1CampaignRuntimeSession _original;
    private readonly Rac1BoltWallet _wallet;
    private readonly int[] _originalGoldBolts;
    private bool _disposed;

    internal Rac1WeaponInventory OriginalInventory => _original.Weapons;

    internal Rac1FusionCampaignIsolation(Rac1CampaignHost campaign, ISet<int> goldBolts, RuntimeSpawn start)
    {
        _campaign = campaign;
        _goldBolts = goldBolts;
        _original = campaign.Session;
        _wallet = campaign.Wallet;
        _originalGoldBolts = goldBolts.ToArray();
        var isolated = new Rac1CampaignRuntimeSession(new Rac1CampaignState(), Rac1WeaponInventory.CreateOpeningLoadout());
        isolated.StartLevelCheckpointSession(0, start);
        campaign.Session = isolated;
        campaign.Wallet = new();
        goldBolts.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _campaign.Session = _original;
        _campaign.Wallet = _wallet;
        _goldBolts.Clear();
        _goldBolts.UnionWith(_originalGoldBolts);
    }
}
