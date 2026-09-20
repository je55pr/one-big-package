namespace OBP.RAC1.Progression;

/// <summary>
/// Native per-level campaign state recovered from the R&C1 save/progression path.
/// Values intentionally preserve the retail byte states rather than inventing
/// a trilogy-wide campaign abstraction.
/// </summary>
public enum Rac1LevelVisitState : byte
{
    Unvisited = 0,
    Visited = 1,
    Completed = 2,
}

/// <summary>
/// Result of feeding one recovered retail progression-dispatch value into the
/// destination-discovery path. These values describe discovery only; they do
/// not imply mission completion, travel, or checkpoint activation.
/// </summary>
public enum Rac1DestinationDiscoveryResult
{
    NotDestinationDiscoveryEvent = 0,
    AlreadyDiscovered = 1,
    Discovered = 2,
}

/// <summary>
/// The four recovered persistent campaign blocks represented by
/// <see cref="Rac1CampaignState"/>. Collection values are copied on capture and
/// restore so callers cannot mutate live campaign state through a snapshot.
/// </summary>
public sealed record Rac1CampaignPersistentState(
    int CurrentLevel,
    IReadOnlyList<byte> VisitedPlanets,
    IReadOnlyList<int> GalacticMap,
    IReadOnlyList<Rac1LevelVisitState> LevelStates);

/// <summary>
/// Engine-independent model of the first evidence-backed R&C1 campaign state.
/// The fixed 20-entry tables mirror the recovered VisitedPlanets, GalacticMap,
/// and per-level persistence blocks. CurrentLevel remains a native-sized integer.
/// </summary>
public sealed class Rac1CampaignState
{
    /// <summary>Serialized VisitedPlanets, GalacticMap, and per-level record capacity.</summary>
    public const int NativeLevelCapacity = 20;

    /// <summary>Retail gameplay accepts native level/destination ids 0 through 18.</summary>
    public const int NativeDestinationCount = 19;

    /// <summary>First dispatcher value proven to discover a normal destination.</summary>
    public const int FirstDestinationDiscoveryEvent = 0x25;

    /// <summary>Last dispatcher value proven to discover a normal destination.</summary>
    public const int LastDestinationDiscoveryEvent = 0x36;

    private const int DestinationDiscoveryEventOffset = 0x24;

    private readonly byte[] _visitedPlanets = new byte[NativeLevelCapacity];
    private readonly int[] _galacticMap = new int[NativeLevelCapacity];
    private readonly Rac1LevelVisitState[] _levelStates = new Rac1LevelVisitState[NativeLevelCapacity];
    private readonly IReadOnlyList<byte> _visitedPlanetsView;
    private readonly IReadOnlyList<int> _galacticMapView;
    private readonly IReadOnlyList<Rac1LevelVisitState> _levelStatesView;

    public Rac1CampaignState()
    {
        _visitedPlanetsView = Array.AsReadOnly(_visitedPlanets);
        _galacticMapView = Array.AsReadOnly(_galacticMap);
        _levelStatesView = Array.AsReadOnly(_levelStates);

        CurrentLevel = 0;
        _levelStates[0] = Rac1LevelVisitState.Visited;
    }

    public int CurrentLevel { get; private set; }

    /// <summary>
    /// The retail admission routine derives the append slot by counting nonzero
    /// VisitedPlanets bytes, so the model does the same instead of persisting a
    /// host-only counter.
    /// </summary>
    public int AdmittedDestinationCount
    {
        get
        {
            int count = 0;
            foreach (byte value in _visitedPlanets)
            {
                if (value != 0)
                    count++;
            }

            return count;
        }
    }

    public IReadOnlyList<byte> VisitedPlanets => _visitedPlanetsView;
    public IReadOnlyList<int> GalacticMap => _galacticMapView;
    public IReadOnlyList<Rac1LevelVisitState> LevelStates => _levelStatesView;

    /// <summary>
    /// Resolve the normal retail progression-dispatch range that feeds destination
    /// admission. Values 0x25..0x36 map to destination ids 1..18 by subtracting
    /// 0x24. Other dispatcher values are unrelated to destination discovery.
    /// </summary>
    public static bool TryResolveDestinationDiscoveryEvent(int progressionEventId, out int destinationId)
    {
        if (progressionEventId < FirstDestinationDiscoveryEvent
            || progressionEventId > LastDestinationDiscoveryEvent)
        {
            destinationId = 0;
            return false;
        }

        destinationId = progressionEventId - DestinationDiscoveryEventOffset;
        return true;
    }

    /// <summary>
    /// Apply only the recovered destination-discovery branch of the retail
    /// progression dispatcher. This never changes CurrentLevel, per-level
    /// completion, or checkpoint state.
    /// </summary>
    public Rac1DestinationDiscoveryResult ApplyProgressionEvent(int progressionEventId)
    {
        if (!TryResolveDestinationDiscoveryEvent(progressionEventId, out int destinationId))
            return Rac1DestinationDiscoveryResult.NotDestinationDiscoveryEvent;

        return AdmitDestination(destinationId)
            ? Rac1DestinationDiscoveryResult.Discovered
            : Rac1DestinationDiscoveryResult.AlreadyDiscovered;
    }

    /// <summary>
    /// Append a destination to the recovered GalacticMap order exactly once and
    /// mark its VisitedPlanets byte. This does not imply travel or completion.
    /// The append slot is the retail count of nonzero VisitedPlanets entries.
    /// </summary>
    public bool AdmitDestination(int destinationId)
    {
        ValidateLevelId(destinationId);
        if (_visitedPlanets[destinationId] != 0)
            return false;

        int mapSlot = AdmittedDestinationCount;
        if ((uint)mapSlot >= NativeLevelCapacity)
            throw new InvalidOperationException("R&C1 GalacticMap has no remaining serialized slot.");

        _galacticMap[mapSlot] = destinationId;
        _visitedPlanets[destinationId] = 1;
        return true;
    }

    public bool CanSelectDestination(int destinationId)
    {
        ValidateLevelId(destinationId);
        return _visitedPlanets[destinationId] != 0;
    }

    /// <summary>
    /// Persist travel only to an admitted destination. A first visit promotes
    /// per-level state 0 to 1; completed state 2 is never demoted by travel.
    /// </summary>
    public bool BeginTravel(int destinationId)
    {
        ValidateLevelId(destinationId);
        if (!CanSelectDestination(destinationId))
            return false;

        CurrentLevel = destinationId;
        if (_levelStates[destinationId] == Rac1LevelVisitState.Unvisited)
            _levelStates[destinationId] = Rac1LevelVisitState.Visited;
        return true;
    }

    /// <summary>
    /// Promote a per-level state to the independently recovered completed value.
    /// Admission, GalacticMap ordering, and CurrentLevel are intentionally unchanged.
    /// </summary>
    public bool MarkLevelComplete(int levelId)
    {
        ValidateLevelId(levelId);
        if (_levelStates[levelId] == Rac1LevelVisitState.Completed)
            return false;

        _levelStates[levelId] = Rac1LevelVisitState.Completed;
        return true;
    }

    /// <summary>
    /// Capture exactly the recovered persistent campaign fields. The ship/map
    /// selected destination and checkpoint state are not part of these blocks.
    /// </summary>
    public Rac1CampaignPersistentState CapturePersistentState() =>
        new(
            CurrentLevel,
            Array.AsReadOnly((byte[])_visitedPlanets.Clone()),
            Array.AsReadOnly((int[])_galacticMap.Clone()),
            Array.AsReadOnly((Rac1LevelVisitState[])_levelStates.Clone()));

    /// <summary>
    /// Restore the four recovered persistent campaign blocks without inferring
    /// discovery from CurrentLevel, completion, or any checkpoint state.
    /// </summary>
    public static Rac1CampaignState RestorePersistentState(Rac1CampaignPersistentState persistent)
    {
        ArgumentNullException.ThrowIfNull(persistent);
        ValidateLevelId(persistent.CurrentLevel);
        ValidatePersistentCount(persistent.VisitedPlanets, nameof(persistent.VisitedPlanets));
        ValidatePersistentCount(persistent.GalacticMap, nameof(persistent.GalacticMap));
        ValidatePersistentCount(persistent.LevelStates, nameof(persistent.LevelStates));

        var restored = new Rac1CampaignState();
        restored.CurrentLevel = persistent.CurrentLevel;

        for (int index = 0; index < NativeLevelCapacity; index++)
        {
            Rac1LevelVisitState levelState = persistent.LevelStates[index];
            if ((byte)levelState > (byte)Rac1LevelVisitState.Completed)
                throw new ArgumentOutOfRangeException(nameof(persistent), $"Level state {index} has unsupported value {(byte)levelState}.");

            restored._visitedPlanets[index] = persistent.VisitedPlanets[index];
            restored._galacticMap[index] = persistent.GalacticMap[index];
            restored._levelStates[index] = levelState;
        }

        return restored;
    }

    public Rac1LevelVisitState GetLevelState(int levelId)
    {
        ValidateLevelId(levelId);
        return _levelStates[levelId];
    }

    private static void ValidatePersistentCount<T>(IReadOnlyList<T>? values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count != NativeLevelCapacity)
            throw new ArgumentException($"R&C1 persistent campaign table must contain exactly {NativeLevelCapacity} entries.", parameterName);
    }

    private static void ValidateLevelId(int levelId)
    {
        if ((uint)levelId >= NativeDestinationCount)
            throw new ArgumentOutOfRangeException(nameof(levelId));
    }
}
