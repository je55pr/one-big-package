using OBP.Core;
using System.Buffers.Binary;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Source-owned class-1060 Moby colour animation on levels 0, 2, 5 and 18.
/// The four matching native overlay routines use the same low/high RGB words
/// and phase rate; their per-object phase comes from authored PVar +0x00.
/// </summary>
public sealed class Rac1PulsingOrbSession
{
    public const int NativeClassId = 1060;
    public const double NativeUpdateHz = 60d;
    public const double PhaseDegreesPerSecond = 180d;
    public const uint ColourA = 0x00404070;
    public const uint ColourB = 0x00202020;
    private readonly Dictionary<int, double> _phases = [];

    public int Count => _phases.Count;
    public static bool SupportsLevel(int levelId) => levelId is 0 or 2 or 5 or 18;

    public bool Register(int levelId, RuntimeDynamicObject source)
    {
        if (source.SourceGame != ObpSourceGame.Rac1 || source.NativeClassId != NativeClassId) return false;
        if (!SupportsLevel(levelId))
            throw new InvalidDataException($"R&C1 class-1060 updater is not admitted for level {levelId}.");
        byte[]? raw = source.NativePayloads?.SingleOrDefault(p => p.Format == "rac1-pvar")?.Data;
        if (raw is not { Length: 16 })
            throw new InvalidDataException("R&C1 class-1060 requires its original 16-byte PVar.");
        float degrees = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(raw));
        if (!float.IsFinite(degrees))
            throw new InvalidDataException("Invalid native class-1060 authored phase.");
        if (!_phases.TryAdd(source.InstanceIndex, degrees * Math.PI / 180d))
            throw new InvalidOperationException($"Duplicate pulse source {source.InstanceIndex}.");
        return true;
    }

    public Rac1PulseColour Step(int instanceIndex)
    {
        if (!_phases.TryGetValue(instanceIndex, out double phase))
            throw new InvalidOperationException($"Unregistered class-1060 instance {instanceIndex}.");
        phase += (PhaseDegreesPerSecond * Math.PI / 180d) / NativeUpdateHz;
        phase = Math.IEEERemainder(phase, Math.Tau);
        _phases[instanceIndex] = phase;
        // Source uses sin(phase) mapped from [-1,+1] into [0,1], and the
        // original packed 0xBBGGRR channel order for its two colour endpoints.
        double mix = (Math.Sin(phase) + 1d) / 2d;
        static byte Channel(uint packed, int shift) => (byte)(packed >> shift);
        static byte Tween(byte a, byte b, double weight) =>
            (byte)Math.Clamp((int)Math.Round(a + (b - a) * weight), 0, 255);
        return new Rac1PulseColour(
            Tween(Channel(ColourA, 0), Channel(ColourB, 0), mix),
            Tween(Channel(ColourA, 8), Channel(ColourB, 8), mix),
            Tween(Channel(ColourA, 16), Channel(ColourB, 16), mix));
    }
}

public readonly record struct Rac1PulseColour(byte Red, byte Green, byte Blue);
