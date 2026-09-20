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

    private readonly byte[] _visitedPlanets = new byte[NativeLevelCapacity];
    private readonly int[] _galacticMap = new int[NativeLevelCapacity];
    private readonly Rac1LevelVisitState[] _levelStates = new Rac1LevelVisitState[NativeLevelCapacity];
    private readonly IReadOnlyList<byte> _visitedPlanetsView;
    private readonly IReadOnlyList<int> _galacticMapView;
    private readonly IReadOnlyList<Rac1LevelVisitState> _levelStatesView;
    private int _admittedDestinationCount;

    public Rac1CampaignState()
    {
        _visitedPlanetsView = Array.AsReadOnly(_visitedPlanets);
        _galacticMapView = Array.AsReadOnly(_galacticMap);
        _levelStatesView = Array.AsReadOnly(_levelStates);

        CurrentLevel = 0;
        _levelStates[0] = Rac1LevelVisitState.Visited;
    }

    public int CurrentLevel { get; private set; }
    public int AdmittedDestinationCount => _admittedDestinationCount;
    public IReadOnlyList<byte> VisitedPlanets => _visitedPlanetsView;
    public IReadOnlyList<int> GalacticMap => _galacticMapView;
    public IReadOnlyList<Rac1LevelVisitState> LevelStates => _levelStatesView;

    /// <summary>
    /// Append a destination to the recovered GalacticMap order exactly once and
    /// mark its VisitedPlanets byte. This does not imply travel or completion.
    /// </summary>
    public bool AdmitDestination(int destinationId)
    {
        ValidateLevelId(destinationId);
        if (_visitedPlanets[destinationId] != 0)
            return false;

        _galacticMap[_admittedDestinationCount++] = destinationId;
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

    public Rac1LevelVisitState GetLevelState(int levelId)
    {
        ValidateLevelId(levelId);
        return _levelStates[levelId];
    }

    private static void ValidateLevelId(int levelId)
    {
        if ((uint)levelId >= NativeDestinationCount)
            throw new ArgumentOutOfRangeException(nameof(levelId));
    }
}
