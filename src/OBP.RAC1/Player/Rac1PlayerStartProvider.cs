using OBP.IO;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Player;

/// <summary>Retail-backed R&amp;C1 Ratchet start recovered from the authored class-0 Moby placement.</summary>
public sealed record Rac1PlayerStart(
    int NativeLevelId,
    int InstanceIndex,
    float Scale,
    (float X, float Y, float Z) NativePosition,
    (float X, float Y, float Z) NativeRotation,
    RuntimeObjectTransform Transform);

/// <summary>
/// Resolves the dedicated R&amp;C1 player start without using the level-settings ship tuple.
/// Retail Veldin overlay archaeology proves that authored Moby instance 0 / class 0 is
/// populated into live Ratchet and that its authored transform seeds the live transform.
/// </summary>
public sealed class Rac1PlayerStartProvider
{
    public const int RatchetClass = 0;
    public const int RatchetInstanceIndex = 0;

    public static readonly Rac1PlayerStartProvider Instance = new();

    private Rac1PlayerStartProvider() { }

    public Rac1PlayerStart Load(string sourcePath, int nativeLevelId)
    {
        using var reader = new FileRandomAccessReader(sourcePath);
        return Load(reader, nativeLevelId);
    }

    public Rac1PlayerStart Load(IRandomAccessReader source, int nativeLevelId)
    {
        var level = Rac1DiscIndex.Read(source).Levels
            .SingleOrDefault(level => level.LevelId == nativeLevelId)
            ?? throw new ArgumentOutOfRangeException(
                nameof(nativeLevelId), nativeLevelId, "R&C1 native level is absent from the source.");
        byte[] gameplay = Rac1LevelSettings.ReadGameplay(source, level);
        var placements = Rac1Instances.Parse(gameplay).MobyInstances
            .Where(instance => instance.OClass == RatchetClass)
            .ToArray();

        if (placements.Length != 1 || placements[0].Index != RatchetInstanceIndex)
        {
            throw new InvalidDataException(
                "R&C1 Ratchet start is no longer the single class-0 placement at authored instance 0.");
        }

        var ratchet = placements[0];
        return new Rac1PlayerStart(
            nativeLevelId,
            ratchet.Index,
            ratchet.Scale,
            ratchet.Position,
            ratchet.Rotation,
            new RuntimeObjectTransform(Rac1Instances.MobyTransform(ratchet)));
    }

}
