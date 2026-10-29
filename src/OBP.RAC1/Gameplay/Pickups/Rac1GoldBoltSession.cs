namespace OBP.RAC1.Gameplay;

/// <summary>
/// Source-game identity and one-time collection for Novalis's three authored
/// gold bolts. Object class 1134 is cross-checked against the retail level-one
/// model/instance census. Native pickup timing and save flags are not recovered.
/// </summary>
public sealed class Rac1GoldBoltSession
{
    public const int NovalisLevelId = 1;
    public const int GoldBoltNativeClassId = 1134;
    public const int ExpectedNovalisBolts = 3;

    private readonly HashSet<int> _registered = [];
    private readonly HashSet<int> _collected = [];

    public Rac1GoldBoltSession(IEnumerable<int>? restored = null)
    {
        if (restored is null) return;
        foreach (int instance in restored)
        {
            if (instance < 0 || !_collected.Add(instance))
                throw new InvalidDataException("Invalid or duplicate restored Novalis Gold Bolt instance.");
        }
    }

    public int RegisteredCount => _registered.Count;
    public int CollectedCount => _collected.Count;
    public IReadOnlyCollection<int> CollectedInstanceIndices => _collected;

    public void Register(int nativeLevelId, int nativeClassId, int instanceIndex)
    {
        if (nativeLevelId != NovalisLevelId || nativeClassId != GoldBoltNativeClassId)
            throw new ArgumentException("Only an authored Novalis class-1134 gold bolt is supported.");
        if (instanceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(instanceIndex));
        if (!_registered.Add(instanceIndex))
            throw new InvalidOperationException($"Duplicate Novalis gold bolt instance {instanceIndex}.");
    }

    /// <summary>
    /// Caller supplies a collision/contact decision. No generic coin-credit,
    /// invented retail radius or additional currency payout is attached.
    /// </summary>
    public bool TryCollect(int instanceIndex, bool contactAdmitted)
    {
        if (!_registered.Contains(instanceIndex) || !contactAdmitted)
            return false;
        return _collected.Add(instanceIndex);
    }

    public bool IsCollected(int instanceIndex) => _collected.Contains(instanceIndex);
}
