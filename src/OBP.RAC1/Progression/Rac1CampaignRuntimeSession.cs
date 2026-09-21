using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.RAC1.Progression;

public enum Rac1LevelEntryKind
{
    CampaignCurrentLevel,
    CampaignTravel,
    ExternalHostLoad,
}

/// <summary>
/// Process-lifetime R&C1 campaign/player state used across ordinary world unload/load
/// handoffs. Native campaign progression and the recovered weapon inventory remain
/// persistent; level-local gameplay owners are deliberately not stored here.
/// </summary>
public sealed class Rac1CampaignRuntimeSession
{
    public Rac1CampaignRuntimeSession(
        Rac1CampaignState campaign,
        Rac1WeaponInventory weapons)
    {
        Campaign = campaign ?? throw new ArgumentNullException(nameof(campaign));
        Weapons = weapons ?? throw new ArgumentNullException(nameof(weapons));
        Travel = new Rac1PlanetTravelSession(Campaign);
    }

    public Rac1CampaignState Campaign { get; }
    public Rac1WeaponInventory Weapons { get; }
    public Rac1PlanetTravelSession Travel { get; private set; }
    public Rac1LevelCheckpointSession? LevelCheckpoint { get; private set; }

    /// <summary>
    /// Start the level-local checkpoint owner for a completed host level entry.
    /// Every full load receives a fresh inactive session: retail checkpoint
    /// serialization across reload/revisit is not yet recovered, so OBP does not
    /// carry an active checkpoint across this boundary.
    /// </summary>
    public Rac1LevelCheckpointSession StartLevelCheckpointSession(
        int nativeLevelId,
        RuntimeSpawn authoredClass0)
    {
        ValidateDestination(nativeLevelId);
        return LevelCheckpoint = new Rac1LevelCheckpointSession(nativeLevelId, authoredClass0);
    }

    /// <summary>
    /// Begin ordinary map travel. Selection always starts from CurrentLevel and only
    /// an already-admitted destination can become the pending loader target.
    /// </summary>
    public Rac1PlanetTravelStartResult BeginTravel(int destinationId)
    {
        Travel.OpenPlanetMap();
        if (!Travel.TrySelectDestination(destinationId))
            return Rac1PlanetTravelStartResult.DestinationUnavailable;

        return Travel.BeginSelectedTravel();
    }

    /// <summary>
    /// Commit a successfully loaded target at the recovered late-entry boundary.
    /// Host/debug loads that did not originate in campaign travel never rewrite
    /// campaign CurrentLevel or synthesize discovery.
    /// </summary>
    public Rac1LevelEntryKind CommitLoadedLevel(int nativeLevelId)
    {
        ValidateDestination(nativeLevelId);

        if (!Travel.TransitionActive)
        {
            return nativeLevelId == Campaign.CurrentLevel
                ? Rac1LevelEntryKind.CampaignCurrentLevel
                : Rac1LevelEntryKind.ExternalHostLoad;
        }

        if (Travel.LoaderDestination != nativeLevelId)
        {
            throw new InvalidOperationException(
                $"R&C1 loader produced level {nativeLevelId} while campaign travel targets " +
                $"{Travel.LoaderDestination}.");
        }

        Travel.CommitLevelEntry();
        return Rac1LevelEntryKind.CampaignTravel;
    }

    /// <summary>
    /// Finish only a committed campaign travel after new-world/player initialization.
    /// Current-level and external host loads have no transient transfer to clear.
    /// </summary>
    public void FinishLoadedLevel(Rac1LevelEntryKind entryKind)
    {
        if (entryKind == Rac1LevelEntryKind.CampaignTravel)
            Travel.FinishLevelEntry();
    }

    /// <summary>
    /// OBP-only recovery for a provider/import failure before CurrentLevel commit.
    /// Retail failure semantics are not claimed; campaign state is unchanged because
    /// the native-shaped transfer has not crossed its late commit boundary.
    /// </summary>
    public void AbandonUncommittedHostLoad()
    {
        if (Travel.CurrentLevelCommitted)
            throw new InvalidOperationException(
                "Cannot abandon R&C1 host load after campaign CurrentLevel commit.");

        Travel = new Rac1PlanetTravelSession(Campaign);
    }

    private static void ValidateDestination(int nativeLevelId)
    {
        if ((uint)nativeLevelId >= Rac1CampaignState.NativeDestinationCount)
            throw new ArgumentOutOfRangeException(nameof(nativeLevelId));
    }
}
