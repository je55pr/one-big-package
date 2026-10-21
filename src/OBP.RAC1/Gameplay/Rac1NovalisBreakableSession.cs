namespace OBP.RAC1.Gameplay;

/// <summary>
/// Bounded Novalis destructible host slice. Native class identities and authored
/// placements are real; hit filtering and absence of native fragments/rewards are
/// explicitly host policy pending the class-specific retail damage consumers.
/// </summary>
public sealed class Rac1NovalisBreakableSession
{
    public const int NovalisLevelId = 1;
    public const int MushroomClass = 754;
    public const int RockFormationClass = 704;
    // daino-selvatico/rac1-native-game@3fd14c1 novalis_mobys.cpp:
    // plant_bolts_drop_l1 sets flags 2 only when hero is within
    // seven native XY units; bolts_drop(m, 4, 7, flags, -1) returns otherwise.
    public const float PlantBoltPlanarReach = 7f;
    public const int PlantBoltMinimum = 4;
    public const int PlantBoltMaximum = 7;

    public static bool PlantBoltRewardAdmitted(float planarDistance) =>
        float.IsFinite(planarDistance) && planarDistance < PlantBoltPlanarReach;

    public enum ContactKind { Wrench, Explosion }

    private readonly Dictionary<int, int> _classes = [];
    private readonly HashSet<int> _destroyed = [];

    public int RegisteredCount => _classes.Count;
    public int DestroyedCount => _destroyed.Count;

    public static bool IsAdmitted(int levelId, int classId) =>
        levelId == NovalisLevelId && classId is MushroomClass or RockFormationClass;

    public void Register(int levelId, int nativeClassId, int instanceIndex)
    {
        if (!IsAdmitted(levelId, nativeClassId))
            throw new ArgumentException("Only the bounded Novalis mushroom/rock families are admitted.");
        if (instanceIndex < 0) throw new ArgumentOutOfRangeException(nameof(instanceIndex));
        if (!_classes.TryAdd(instanceIndex, nativeClassId))
            throw new InvalidOperationException($"Duplicate Novalis breakable {instanceIndex}.");
    }

    public bool IsDestroyed(int instanceIndex) => _destroyed.Contains(instanceIndex);

    public bool TryBreak(int instanceIndex, ContactKind contact)
    {
        if (!_classes.TryGetValue(instanceIndex, out int nativeClass) ||
            _destroyed.Contains(instanceIndex))
            return false;
        if (nativeClass == RockFormationClass && contact != ContactKind.Explosion)
            return false;
        return _destroyed.Add(instanceIndex);
    }
}
