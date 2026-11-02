using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// Persistent source campaign and its parity wallet, separate from the ordinary
/// player's session and every world lease. Native save/travel adapters remain authoritative.
/// </summary>
internal sealed class Rac1CampaignHost
{
    internal Rac1CampaignRuntimeSession Session { get; set; } = new(new Rac1CampaignState(), Rac1WeaponInventory.CreateOpeningLoadout());
    internal Rac1BoltWallet Wallet { get; set; } = new();
    internal HashSet<int> CollectedNovalisGoldBolts { get; } = [];
    internal Rac1CampaignPersistenceHost Persistence { get; }

    internal Rac1CampaignHost() => Persistence = new(this);

    /// <summary>Admit only this campaign's source world for checkpoint preparation.</summary>
    internal RuntimeSpawn? BeginWorldEntry(RuntimeWorld world) =>
        world.Game == ObpSourceGame.Rac1 ? BeginWorldEntry(world.LevelId, world.PlayerStart!) : null;

    /// <summary>Complete only this campaign's admitted source world.</summary>
    internal bool FinishWorldEntry(RuntimeWorld world) =>
        world.Game == ObpSourceGame.Rac1 && FinishWorldEntry(world.LevelId);

    /// <summary>Begin a fresh source-level checkpoint session before a world is installed.</summary>
    internal RuntimeSpawn BeginWorldEntry(int levelId, RuntimeSpawn authoredStart) =>
        Session.StartLevelCheckpointSession(levelId, authoredStart).AuthoredClass0;

    /// <summary>Complete the native campaign transfer only after the new world is active.</summary>
    /// <returns>Whether campaign travel was committed and needs a parity save.</returns>
    internal bool FinishWorldEntry(int levelId)
    {
        var entry = Session.CommitLoadedLevel(levelId);
        Session.FinishLoadedLevel(entry);
        return entry == Rac1LevelEntryKind.CampaignTravel;
    }

    /// <summary>Restore native campaign and owned collectibles as one authority.</summary>
    internal void Restore(Rac1CampaignRestoreResult restored)
    {
        Session = new Rac1CampaignRuntimeSession(
            restored.Campaign, restored.Weapons,
            Rac1BuriedBoltProgress.Restore(restored.BuriedBoltHistory));
        Wallet = new Rac1BoltWallet(restored.Bolts);
        CollectedNovalisGoldBolts.Clear();
        CollectedNovalisGoldBolts.UnionWith(restored.NovalisGoldBoltInstances);
    }
}
