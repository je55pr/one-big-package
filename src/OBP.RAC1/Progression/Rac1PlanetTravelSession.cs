namespace OBP.RAC1.Progression;

public enum Rac1PlanetTravelStartResult
{
    Started,
    AlreadyCurrentLevel,
    DestinationUnavailable,
}

/// <summary>
/// Native campaign facts exposed to the host planet-map presentation.
/// UnlockedDestinations preserves the recovered GalacticMap admission order;
/// CurrentLevel remains separate because opening level 0 is not auto-admitted.
/// </summary>
public sealed record Rac1PlanetMapSnapshot(
    int CurrentLevel,
    int SelectedDestination,
    IReadOnlyList<int> UnlockedDestinations);

/// <summary>
/// Native-shaped R&C1 planet-travel session state. Persistent campaign state stays
/// in <see cref="Rac1CampaignState"/>; this object models the ship/map selection
/// and the two-phase source-to-target handoff recovered from SCUS-97199.
/// </summary>
public sealed class Rac1PlanetTravelSession
{
    private readonly Rac1CampaignState _campaign;

    public Rac1PlanetTravelSession(Rac1CampaignState campaign)
    {
        _campaign = campaign ?? throw new ArgumentNullException(nameof(campaign));
        SelectedDestination = campaign.CurrentLevel;
    }

    public int SelectedDestination { get; private set; }
    public int? SourceLevel { get; private set; }
    public int? PendingDestinationStorage { get; private set; }
    public int? LoaderDestination { get; private set; }
    public bool TransitionActive { get; private set; }
    public bool CurrentLevelCommitted { get; private set; }

    /// <summary>
    /// The pending slot remains stale after retail clears the transfer-active flag.
    /// Consumers must use this projection rather than treating the raw slot as live.
    /// </summary>
    public int? ActiveTarget => TransitionActive ? PendingDestinationStorage : null;

    public Rac1PlanetMapSnapshot OpenPlanetMap()
    {
        if (TransitionActive)
            throw new InvalidOperationException("Cannot reopen the planet map during an active travel handoff.");

        SelectedDestination = _campaign.CurrentLevel;
        return CapturePlanetMap();
    }

    private Rac1PlanetMapSnapshot CapturePlanetMap()
    {
        int count = _campaign.AdmittedDestinationCount;
        var unlocked = new int[count];
        for (int slot = 0; slot < count; slot++)
        {
            int destinationId = _campaign.GalacticMap[slot];
            if (!_campaign.CanSelectDestination(destinationId))
                throw new InvalidOperationException(
                    $"R&C1 GalacticMap slot {slot} points to destination {destinationId}, which is not admitted.");

            unlocked[slot] = destinationId;
        }

        return new Rac1PlanetMapSnapshot(
            _campaign.CurrentLevel,
            SelectedDestination,
            Array.AsReadOnly(unlocked));
    }

    public bool TrySelectDestination(int destinationId)
    {
        ValidateDestination(destinationId);
        if (destinationId != _campaign.CurrentLevel &&
            !_campaign.CanSelectDestination(destinationId))
            return false;

        SelectedDestination = destinationId;
        return true;
    }

    public Rac1PlanetTravelStartResult BeginSelectedTravel()
    {
        if (TransitionActive)
            throw new InvalidOperationException("R&C1 planet travel is already active.");

        if (SelectedDestination == _campaign.CurrentLevel)
            return Rac1PlanetTravelStartResult.AlreadyCurrentLevel;
        if (!_campaign.CanSelectDestination(SelectedDestination))
            return Rac1PlanetTravelStartResult.DestinationUnavailable;

        SourceLevel = _campaign.CurrentLevel;
        PendingDestinationStorage = SelectedDestination;
        LoaderDestination = SelectedDestination;
        TransitionActive = true;
        CurrentLevelCommitted = false;
        return Rac1PlanetTravelStartResult.Started;
    }

    /// <summary>
    /// Mirrors the late retail commit where CurrentLevel takes the pending target.
    /// The transition remains active until new-level initialization completes.
    /// </summary>
    public void CommitLevelEntry()
    {
        if (!TransitionActive || SourceLevel is null)
            throw new InvalidOperationException("No active R&C1 planet travel can be committed.");
        if (CurrentLevelCommitted)
            throw new InvalidOperationException("R&C1 planet travel CurrentLevel was already committed.");

        int target = PendingDestinationStorage
            ?? throw new InvalidOperationException("Active R&C1 travel has no pending target.");
        if (!_campaign.BeginTravel(target))
            throw new InvalidOperationException("Pending R&C1 destination became unavailable before commit.");

        CurrentLevelCommitted = true;
    }

    public void FinishLevelEntry()
    {
        if (!TransitionActive || !CurrentLevelCommitted)
            throw new InvalidOperationException("R&C1 level-entry cleanup requires a committed active transfer.");
        if (_campaign.CurrentLevel != PendingDestinationStorage)
            throw new InvalidOperationException("R&C1 campaign CurrentLevel no longer matches the committed target.");

        // Retail clears the active flag but does not need to zero the pending-target
        // storage. Preserve that distinction so stale target bytes cannot masquerade
        // as an in-flight transfer.
        TransitionActive = false;
        CurrentLevelCommitted = false;
        SourceLevel = null;
    }

    private static void ValidateDestination(int destinationId)
    {
        if ((uint)destinationId >= Rac1CampaignState.NativeDestinationCount)
            throw new ArgumentOutOfRangeException(nameof(destinationId));
    }
}
