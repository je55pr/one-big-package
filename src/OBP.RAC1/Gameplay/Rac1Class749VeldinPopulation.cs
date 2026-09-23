using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Recovered LEVEL0/Veldin population contract for class 749. This scope is
/// deliberately level-specific: LEVEL18 placements are not promoted by it.
/// </summary>
public static class Rac1Class749VeldinPopulation
{
    public const int LevelId = 0;
    public const int FirstInstanceIndex = 143;
    public const int LastInstanceIndex = 158;
    public const int AuthoredPlacementCount = 16;
    public const int SpecialLinkedInstanceIndex = 154;
    public const int SpecialLinkMode = 21;
    public const int SpecialLinkedMobyInstanceIndex = 197;

    public static readonly Rac1Class749WorldPoint AuthoredRatchetStart =
        new(132.09d, 0d, 115.48d);

    public static bool IsRecoveredPlacement(int levelId, RuntimeDynamicObject source) =>
        levelId == LevelId &&
        source.SourceGame == "rac1" &&
        source.NativeClassId == Rac1Class749Hostile.NativeClassId &&
        TryGetActivationGroup(source.InstanceIndex, out _);

    public static int GetActivationGroup(int instanceIndex) =>
        TryGetActivationGroup(instanceIndex, out int group)
            ? group
            : throw new ArgumentOutOfRangeException(nameof(instanceIndex));
    public static bool TryGetActivationGroup(int instanceIndex, out int group)
    {
        group = instanceIndex switch
        {
            143 or 144 => 0,
            145 or 146 or 147 => 1,
            148 => 2,
            149 or 150 => 3,
            151 => 4,
            152 or 153 => 16,
            154 => 20,
            155 or 156 or 157 or 158 => 23,
            _ => -1,
        };
        return group >= 0;
    }

    public static bool IsAdmitted(int activationGroup, Rac1Class749WorldPoint target)
    {
        if (!Polygons.TryGetValue(activationGroup, out var polygon))
            throw new ArgumentOutOfRangeException(nameof(activationGroup));
        return ContainsPlanarPoint(polygon, target.X, target.Z);
    }

    public static IReadOnlyList<Rac1Class749PlanarPoint> GetActivationPolygon(int activationGroup) =>
        Polygons.TryGetValue(activationGroup, out var polygon)
            ? polygon
            : throw new ArgumentOutOfRangeException(nameof(activationGroup));

    internal static void ValidateAuthoredFields(RuntimeDynamicObject source, ReadOnlySpan<byte> pvar)
    {
        int expectedGroup = GetActivationGroup(source.InstanceIndex);
        int activationGroup = Rac1Class749Hostile.ReadInt32(
            pvar, Rac1Class749Hostile.ActivationGroupOffset);
        if (activationGroup != expectedGroup)
            throw new InvalidDataException(
                $"Class-749 i{source.InstanceIndex} activation group {activationGroup} != recovered {expectedGroup}.");

        int linkMode = Rac1Class749Hostile.ReadInt32(pvar, Rac1Class749Hostile.LinkModeOffset);
        if (source.InstanceIndex == SpecialLinkedInstanceIndex)
        {
            int stateThreeGroup = Rac1Class749Hostile.ReadInt32(
                pvar, Rac1Class749Hostile.StateThreeActivationGroupOffset);
            int linkedInstance = Rac1Class749Hostile.ReadInt32(
                pvar, Rac1Class749Hostile.LinkedInstanceOffset);
            if (linkMode != SpecialLinkMode || stateThreeGroup != -1 ||
                linkedInstance != SpecialLinkedMobyInstanceIndex)
                throw new InvalidDataException(
                    "Class-749 i154 does not match recovered linked-object fields 21/-1/197.");
        }
        else if (linkMode != -1)
        {
            throw new InvalidDataException(
                $"Class-749 i{source.InstanceIndex} link mode {linkMode} != recovered -1.");
        }
    }

    internal static int InitialNativeState(RuntimeDynamicObject source, ReadOnlySpan<byte> pvar)
    {
        ValidateAuthoredFields(source, pvar);
        return source.InstanceIndex == SpecialLinkedInstanceIndex
            ? Rac1Class749Hostile.LinkedObjectNativeState
            : Rac1Class749Hostile.TargetSearchNativeState;
    }

    private static bool ContainsPlanarPoint(
        IReadOnlyList<Rac1Class749PlanarPoint> polygon,
        double x,
        double y)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            bool crosses = (a.Y > y) != (b.Y > y);
            if (!crosses) continue;
            double crossingX = (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X;
            if (x < crossingX) inside = !inside;
        }
        return inside;
    }

    private static readonly IReadOnlyDictionary<int, Rac1Class749PlanarPoint[]> Polygons =
        new Dictionary<int, Rac1Class749PlanarPoint[]>
        {
            [0] =
            [
                new(147.094879, 114.100311), new(144.217102, 114.283966),
                new(145.198273, 116.927879), new(139.334808, 127.206123),
                new(145.373245, 131.144363), new(147.926498, 134.156525),
                new(152.917770, 133.419296), new(159.348801, 129.751892),
                new(161.983765, 129.812454), new(164.569046, 123.624985),
                new(164.906113, 117.102013), new(160.863434, 115.308304),
                new(147.111404, 114.101761),
            ],
            [1] =
            [
                new(171.028107, 140.243958), new(167.976669, 141.496002),
                new(166.164352, 144.291077), new(165.030670, 147.107681),
                new(164.015167, 148.424362), new(164.131393, 150.981842),
                new(167.995087, 156.372894), new(170.570389, 157.205948),
                new(173.119431, 154.536789), new(176.303497, 153.081589),
                new(180.019669, 148.212875), new(180.619110, 142.443283),
                new(178.373657, 140.215988), new(174.704300, 139.996323),
                new(171.033249, 140.243607),
            ],
            [2] =
            [
                new(104.998184, 167.771072), new(103.742569, 168.385880),
                new(103.035637, 169.650970), new(102.772377, 170.976166),
                new(103.329056, 172.116516), new(105.082001, 172.827103),
                new(106.961861, 173.058167), new(107.898888, 172.760010),
                new(108.345261, 172.018829), new(108.753952, 170.920914),
                new(108.487968, 169.652100), new(106.909538, 168.398331),
                new(105.000481, 167.771820),
            ],
            [3] =
            [
                new(102.883224, 185.381714), new(100.887810, 186.223679),
                new(100.349770, 188.989883), new(100.668633, 192.534958),
                new(101.243980, 195.713577), new(103.209404, 197.714584),
                new(107.591309, 196.599655), new(108.665627, 192.961227),
                new(107.429390, 189.967545), new(106.331245, 187.994858),
                new(105.108429, 186.096588), new(102.885666, 185.382507),
            ],
            [4] =
            [
                new(107.375511, 201.682312), new(113.464653, 201.593323),
                new(114.682907, 202.095306), new(115.493469, 203.570999),
                new(116.671875, 207.633957), new(115.033493, 210.177048),
                new(108.273270, 210.757370), new(106.832611, 208.420013),
                new(106.272072, 203.998459), new(106.313354, 202.367828),
                new(107.374443, 201.682983),
            ],
            [16] =
            [
                new(131.449005, 157.924332), new(135.245712, 174.111542),
                new(128.757965, 177.135223), new(126.472206, 177.600403),
                new(120.671806, 176.887497), new(113.621277, 159.842377),
                new(131.438309, 157.925476),
            ],
            [20] =
            [
                new(94.064392, 260.115417), new(94.015320, 258.703979),
                new(94.252686, 257.483398), new(94.786598, 256.362640),
                new(95.627182, 255.250717), new(96.834549, 254.154633),
                new(98.468864, 253.081421), new(100.183563, 252.041183),
                new(101.631927, 251.043884), new(103.001511, 250.039719),
                new(104.480217, 248.978760), new(105.410973, 247.698380),
                new(105.135994, 246.035782), new(115.006798, 246.282837),
                new(114.676636, 248.798523), new(113.467957, 250.753723),
                new(111.791939, 252.224548), new(110.048103, 253.290878),
                new(108.455101, 254.080215), new(103.293007, 256.691528),
                new(101.904602, 257.508972), new(100.858292, 258.267120),
                new(100.424606, 258.826355), new(100.508087, 259.526947),
                new(101.012596, 260.710022), new(101.920975, 262.240814),
                new(97.920738, 262.140320), new(94.074776, 260.120911),
            ],
            [23] =
            [
                new(122.126259, 223.732422), new(126.219215, 220.423920),
                new(126.702339, 222.594009), new(127.635475, 224.306595),
                new(129.261154, 226.158783), new(131.821625, 228.748230),
                new(132.084641, 231.628754), new(132.084793, 232.629776),
                new(131.574585, 235.853058), new(131.574173, 236.854736),
                new(131.585571, 239.270813), new(129.532425, 241.110214),
                new(126.806129, 241.137466), new(125.450661, 241.545364),
                new(123.785217, 241.143921), new(124.340836, 239.138138),
                new(123.064400, 235.705536), new(120.970604, 230.502213),
                new(122.124176, 223.744583),
            ],
        };
}

public readonly record struct Rac1Class749PlanarPoint(double X, double Y);
