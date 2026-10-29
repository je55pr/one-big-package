using System.Buffers.Binary;
using OBP.RAC1.Level;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Retail Nebula G34 class1021 gate tuning read from original executable .lit
/// values, referenced by FUN_L06_002f4f00; do not substitute global defaults
/// from another level, as overlay SDA addresses are level-specific.
/// </summary>
public sealed record Rac1AlienQueenGateNative(
    float AngularApproachDegrees,
    float AngularBrakeDegrees,
    float AngularMaximumDegrees,
    float InitialProximityGate,
    float ChildRadialDistance)
{
    public const int ApproachAddress = 0x161d38;
    public const int BrakeAddress = 0x161d3c;
    public const int MaximumAddress = 0x161d40;
    public const int ProximityAddress = 0x161d44;
    public const int RadiusAddress = 0x161d48;
    public const float NativeFrameRate = 60f;

    public static Rac1AlienQueenGateNative Read(Rac1ExecutableOverlay original)
    {
        ArgumentNullException.ThrowIfNull(original);
        float At(int address) => BinaryPrimitives.ReadSingleLittleEndian(original.Read(address, 4));
        var value = new Rac1AlienQueenGateNative(
            At(ApproachAddress), At(BrakeAddress), At(MaximumAddress),
            At(ProximityAddress), At(RadiusAddress));
        if (!float.IsFinite(value.AngularApproachDegrees) || value.AngularApproachDegrees <= 0 ||
            !float.IsFinite(value.AngularBrakeDegrees) || value.AngularBrakeDegrees <= 0 ||
            !float.IsFinite(value.AngularMaximumDegrees) || value.AngularMaximumDegrees <= 0 ||
            !float.IsFinite(value.InitialProximityGate) ||
            !float.IsFinite(value.ChildRadialDistance) || value.ChildRadialDistance <= 0)
            throw new InvalidDataException("Original Queen-linked gate overlay constants invalid.");
        return value;
    }

    public float PerTickApproach => AngularApproachDegrees * (MathF.PI / 180f) /
        (NativeFrameRate * NativeFrameRate);
    public float PerTickBrake => AngularBrakeDegrees * (MathF.PI / 180f) /
        (NativeFrameRate * NativeFrameRate);
    public float PerTickMaximum => AngularMaximumDegrees * (MathF.PI / 180f) /
        NativeFrameRate;
}
