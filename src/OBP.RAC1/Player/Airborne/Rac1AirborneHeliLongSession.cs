namespace OBP.RAC1.Player;

/// <summary>
/// Heli-Pack stretch request ownership: ordinary-axis anticipation at
/// 0x21ed70..0x21ef18 and retained request at 0x21468c..0x2146c4.
/// P+0x194 scalar and P+0xe0 request are separate; neither is the P+0x150
/// transport carry refreshed by the source's P+0x920 producer after launch.
/// </summary>
public sealed class Rac1AirborneHeliLongSession
{
    public const float MaximumTargetStep = 5.7f * (1f / 60f);
    public const float AccelerationPerUpdate = 20f * (1f / 3600f);
    public const float DecelerationPerUpdate = 11f * (1f / 3600f);
    public const float ForwardRampPerAge = 53f * (1f / 3600f);
    public const int LaunchAge = 5;

    public Rac1AirborneHeliLongSession(float scalar, Rac1AirbornePlanarMotion.PlanarStep entryRequest)
    {
        Validate(scalar);
        Validate(entryRequest.X);
        Validate(entryRequest.Y);
        if (scalar < 0)
            throw new ArgumentOutOfRangeException(nameof(scalar));
        Scalar = scalar;
        Request = entryRequest;
    }

    public float Scalar { get; private set; }
    public Rac1AirbornePlanarMotion.PlanarStep Request { get; private set; }

    public Rac1AirbornePlanarMotion.PlanarStep Step(int nativeActionAge, float conditionedMagnitude, float facingYaw,
        float? contactProcessedPlanarScalar = null)
    {
        Validate(conditionedMagnitude);
        Validate(facingYaw);
        if (nativeActionAge < 0 || conditionedMagnitude < 0 || conditionedMagnitude > 1)
            throw new ArgumentOutOfRangeException(nameof(nativeActionAge));
        if (contactProcessedPlanarScalar is { } contactScalar)
        {
            Validate(contactScalar);
            if (contactScalar < 0)
                throw new ArgumentOutOfRangeException(nameof(contactProcessedPlanarScalar));
        }
        if (nativeActionAge >= LaunchAge)
        {
            // 0x2146bc copies P+0x164 into P+0x194. Keep the flight request
            // independent of that prior completed contact pass.
            if (contactProcessedPlanarScalar is { } carriedScalar)
                Scalar = carriedScalar;
            return Request;
        }
        // The source builds from old P+0x194, updates that scalar separately,
        // then adds the authored forward term to the request already built.
        float requestedScalar = Scalar;
        float target = MaximumTargetStep * conditionedMagnitude;
        float amount = Scalar < target ? AccelerationPerUpdate : DecelerationPerUpdate;
        Scalar += Math.Clamp(target - Scalar, -amount, amount);
        float ramp = (1f / 3600f) * 53f;
        ramp *= nativeActionAge;
        float cosine = MathF.Cos(facingYaw);
        float sine = MathF.Sin(facingYaw);
        Request = new((cosine * requestedScalar) + (cosine * ramp),
            (sine * requestedScalar) + (sine * ramp));
        return Request;
    }

    private static void Validate(float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}
