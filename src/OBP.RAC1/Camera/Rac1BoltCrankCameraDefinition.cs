using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.RAC1.Camera;

/// <summary>Class-280's authored camera point and type-5 radius interpolation parameters.</summary>
public sealed record Rac1BoltCrankCameraDefinition(Vector3 EyeNative, Vector3 RotationNative,
    float RadiusStartReference, float RadiusEndReference)
{
    public static IReadOnlyDictionary<int, Rac1BoltCrankCameraDefinition> Load(string sourcePath, int levelId)
    {
        using var reader = new FileRandomAccessReader(sourcePath);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == levelId);
        byte[] gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        return Rac1Instances.Parse(gameplay).MobyInstances
            .Where(moby => moby.OClass == Rac1NovalisCrankDoorSession.CrankClass)
            .ToDictionary(moby => moby.Index, moby => Decode(gameplay,
                moby.PVar ?? throw new InvalidDataException("Crank camera requires its authored PVar.")));
    }

    public static Rac1BoltCrankCameraDefinition Decode(byte[] gameplay, ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length != 0x40 || gameplay.Length < 0x64)
            throw new InvalidDataException("Crank camera directory or PVar is absent.");
        int block = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(0x60));
        int point = BinaryPrimitives.ReadInt32LittleEndian(pvar.Slice(0x20));
        if (block <= 0 || block > gameplay.Length - 16)
            throw new InvalidDataException("Crank camera point directory exceeds gameplay.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(block));
        if (count is < 0 or > 65_536 || (long)block + 16 + (long)count * 0x80 > gameplay.Length ||
            point < 0 || point >= count)
            throw new InvalidDataException("Crank references an unavailable camera point.");
        int at = block + 16 + point * 0x80;
        float F(int offset) => BinaryPrimitives.ReadSingleLittleEndian(gameplay.AsSpan(at + offset));
        return new Rac1BoltCrankCameraDefinition(new(F(0x30), F(0x34), F(0x38)),
            new(F(0x70), F(0x74), F(0x78)),
            BinaryPrimitives.ReadSingleLittleEndian(pvar.Slice(0x28)),
            BinaryPrimitives.ReadSingleLittleEndian(pvar.Slice(0x2c))).Validate();
    }

    public Rac1BoltCrankCameraDefinition Validate()
    {
        if (!Finite(EyeNative) || !Finite(RotationNative) ||
            !float.IsFinite(RadiusStartReference) || !float.IsFinite(RadiusEndReference))
            throw new InvalidDataException("Crank camera contains non-finite data.");
        return this;
    }

    internal static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
