namespace OBP.RAC3.Gameplay;

/// <summary>
/// Facts-based execution of the ordinary TABLE1 class-5821 target selector.
///
/// Native helper 0x00453850 admits the index==-1 path only when horizontal
/// distance is strictly below the request radius and absolute vertical
/// separation is strictly below f13. Shared score helper 0x00453408 then uses
/// the ordinary TABLE1 score mode: horizontal distance plus
/// heading-error * horizontal-distance * f14. Ratchet receives a 20-unit
/// score reduction clamped at zero for the authored selector-byte-zero profile.
///
/// The retail horizontal-distance and fast atan2-style helpers are recovered
/// below from TABLE1/GC-identical code and UYA's coefficient/quadrant tables.
/// The selector can therefore build its scalar facts directly from native XYZ
/// positions and native source heading without falling back to MathF.Atan2.
/// </summary>
public static class UyaClass5821TargetSelector
{
    public const float NativeRatchetScoreReduction = 20f;

    private static readonly float NativeAtanCoefficient0 =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3F7FFFF5));
    private static readonly float NativeAtanCoefficient1 =
        BitConverter.Int32BitsToSingle(unchecked((int)0xBEAAA61C));
    private static readonly float NativeAtanCoefficient2 =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3E4C40A6));
    private static readonly float NativeAtanCoefficient3 =
        BitConverter.Int32BitsToSingle(unchecked((int)0xBE0E6C63));
    private static readonly float NativeAtanCoefficient4 =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3DC577DF));
    private static readonly float NativeAtanCoefficient5 =
        BitConverter.Int32BitsToSingle(unchecked((int)0xBD6501C4));
    private static readonly float NativeAtanCoefficient6 =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3CB31652));
    private static readonly float NativeAtanCoefficient7 =
        BitConverter.Int32BitsToSingle(unchecked((int)0xBB84D7E7));

    public static readonly float NativePiOverFour =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3F490FDB));
    public static readonly float NativePiOverTwo =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3FC90FDB));
    public static readonly float NativePi =
        BitConverter.Int32BitsToSingle(unchecked((int)0x40490FDB));

    public static float NativeHorizontalDistance(
        float sourceX,
        float sourceY,
        float targetX,
        float targetY)
    {
        ValidateFinite(sourceX, nameof(sourceX));
        ValidateFinite(sourceY, nameof(sourceY));
        ValidateFinite(targetX, nameof(targetX));
        ValidateFinite(targetY, nameof(targetY));

        float dx = targetX - sourceX;
        float dy = targetY - sourceY;
        float xx = dx * dx;
        float yy = dy * dy;
        return MathF.Sqrt(xx + yy);
    }

    public static float NativeFastAtan2(float deltaY, float deltaX)
    {
        ValidateFinite(deltaY, nameof(deltaY));
        ValidateFinite(deltaX, nameof(deltaX));

        float absX = MathF.Abs(deltaX);
        float absY = MathF.Abs(deltaY);
        bool yDominant = absX < absY;

        float smaller = yDominant ? absX : absY;
        float larger = yDominant ? absY : absX;
        if (larger <= 0f)
            return 0f;

        float ratio = (smaller - larger) / (smaller + larger);
        float ratioSquared = ratio * ratio;

        float polynomial = NativeAtanCoefficient7;
        polynomial = polynomial * ratioSquared + NativeAtanCoefficient6;
        polynomial = polynomial * ratioSquared + NativeAtanCoefficient5;
        polynomial = polynomial * ratioSquared + NativeAtanCoefficient4;
        polynomial = polynomial * ratioSquared + NativeAtanCoefficient3;
        polynomial = polynomial * ratioSquared + NativeAtanCoefficient2;
        polynomial = polynomial * ratioSquared + NativeAtanCoefficient1;
        polynomial = polynomial * ratioSquared + NativeAtanCoefficient0;
        polynomial *= ratio;

        float baseAngle = NativePiOverFour + polynomial;

        bool xNegative = BitConverter.SingleToInt32Bits(deltaX) < 0;
        bool yNegative = BitConverter.SingleToInt32Bits(deltaY) < 0;
        int quadrant = (yDominant ? 1 : 0) |
                       (yNegative ? 2 : 0) |
                       (xNegative ? 4 : 0);

        (float scale, float offset) = quadrant switch
        {
            0 => (1f, 0f),
            1 => (-1f, NativePiOverTwo),
            2 => (-1f, 0f),
            3 => (1f, -NativePiOverTwo),
            4 => (-1f, NativePi),
            5 => (1f, NativePiOverTwo),
            6 => (1f, -NativePi),
            7 => (-1f, -NativePiOverTwo),
            _ => throw new InvalidOperationException(),
        };

        return baseAngle * scale + offset;
    }

    public static float NativeShortestHeadingError(
        float candidateHeading,
        float sourceHeading)
    {
        ValidateFinite(candidateHeading, nameof(candidateHeading));
        ValidateFinite(sourceHeading, nameof(sourceHeading));

        float delta = MathF.Abs(candidateHeading - sourceHeading);
        if (delta < NativePi)
            return delta;

        return (NativePi + NativePi) - delta;
    }

    public static UyaClass5821RadiusHeightFacts BuildNativeRadiusHeightFacts(
        float sourceX,
        float sourceY,
        float sourceZ,
        float targetX,
        float targetY,
        float targetZ)
    {
        ValidateFinite(sourceZ, nameof(sourceZ));
        ValidateFinite(targetZ, nameof(targetZ));
        return new UyaClass5821RadiusHeightFacts(
            NativeHorizontalDistance(sourceX, sourceY, targetX, targetY),
            MathF.Abs(targetZ - sourceZ));
    }

    public static UyaClass5821TargetScoreFacts BuildNativeScoreFacts(
        float sourceX,
        float sourceY,
        float sourceHeading,
        float targetX,
        float targetY,
        bool isRatchet)
    {
        float horizontalDistance =
            NativeHorizontalDistance(sourceX, sourceY, targetX, targetY);
        float targetHeading =
            NativeFastAtan2(targetY - sourceY, targetX - sourceX);
        float headingError =
            NativeShortestHeadingError(targetHeading, sourceHeading);
        return new UyaClass5821TargetScoreFacts(
            horizontalDistance,
            headingError,
            isRatchet);
    }

    public static bool IsNativeRadiusHeightEligible(
        UyaClass5821RadiusHeightFacts facts,
        UyaClass5821TargetSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(request);
        ValidateFiniteNonNegative(facts.HorizontalDistance, nameof(facts.HorizontalDistance));
        ValidateFiniteNonNegative(facts.AbsoluteVerticalSeparation, nameof(facts.AbsoluteVerticalSeparation));

        if (!float.IsFinite(request.Radius) || request.Radius < 0f)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (!float.IsFinite(request.NativeF13) || request.NativeF13 < 0f)
            throw new ArgumentOutOfRangeException(nameof(request));

        return facts.HorizontalDistance < request.Radius &&
               facts.AbsoluteVerticalSeparation < request.NativeF13;
    }

    public static float ScoreOrdinaryCandidate(
        UyaClass5821TargetScoreFacts facts,
        UyaClass5821TargetSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(request);
        ValidateOrdinaryScoreRequest(request);
        ValidateFiniteNonNegative(facts.HorizontalDistance, nameof(facts.HorizontalDistance));
        ValidateFiniteNonNegative(
            facts.ShortestHeadingErrorRadians,
            nameof(facts.ShortestHeadingErrorRadians));
        if (!float.IsFinite(request.NativeF14) || request.NativeF14 < 0f)
            throw new ArgumentOutOfRangeException(nameof(request));

        float angularPenalty =
            facts.ShortestHeadingErrorRadians *
            facts.HorizontalDistance;
        angularPenalty *= request.NativeF14;
        float score = facts.HorizontalDistance + angularPenalty;

        if (facts.IsRatchet && UsesNativeRatchetScoreReduction(request))
        {
            score -= NativeRatchetScoreReduction;
            if (score < 0f)
                score = 0f;
        }

        return score;
    }

    public static bool UsesNativeRatchetScoreReduction(
        UyaClass5821TargetSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.NativeSelectorIndex < 0 || !request.NativeAuxEnabled;
    }

    public static bool AdmitsRegistryTag(
        UyaClass5821TargetSelectionRequest request,
        int nativeRegistryTag)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.NativeCandidateTag == 0 ||
               nativeRegistryTag == request.NativeCandidateTag;
    }

    public static UyaClass5821TargetSelectionResult? SelectOrdinaryTarget(
        UyaClass5821TargetSelectionRequest request,
        UyaClass5821TargetCandidateFacts? ratchet,
        IReadOnlyList<UyaClass5821TargetCandidateFacts> registryCandidates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(registryCandidates);
        ValidateOrdinaryScoreRequest(request);

        UyaClass5821TargetSelectionResult? selected = null;
        if (request.SeedRatchetBeforeCandidateReplacement &&
            ratchet is not null &&
            ratchet.IsRatchet &&
            ratchet.IsEligible)
        {
            selected = new UyaClass5821TargetSelectionResult(
                ratchet.Entity,
                ScoreOrdinaryCandidate(ratchet.ScoreFacts, request),
                IsRatchet: true);
        }

        foreach (UyaClass5821TargetCandidateFacts candidate in registryCandidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (candidate.IsRatchet)
                throw new ArgumentException(
                    "Registry candidates must not duplicate the Ratchet seed.",
                    nameof(registryCandidates));
            if (!candidate.IsEligible ||
                candidate.NativeRegistryTag is not int tag ||
                !AdmitsRegistryTag(request, tag))
                continue;

            float score = ScoreOrdinaryCandidate(candidate.ScoreFacts, request);
            if (selected is null || score < selected.Score)
            {
                selected = new UyaClass5821TargetSelectionResult(
                    candidate.Entity,
                    score,
                    IsRatchet: false);
            }
        }

        return selected;
    }

    private static void ValidateOrdinaryScoreRequest(
        UyaClass5821TargetSelectionRequest request)
    {
        if (request.NativeCandidateTag is not
            UyaClass5821Actor.NativeTargetSelectorCandidateTagOne and not
            UyaClass5821Actor.NativeTargetSelectorCandidateTagTwo ||
            request.NativeF13 != UyaClass5821Actor.NativeTargetSelectorF13 ||
            request.NativeF14 != UyaClass5821Actor.NativeTargetSelectorF14 ||
            !request.SeedRatchetBeforeCandidateReplacement)
        {
            throw new NotSupportedException(
                "UYA class-5821 score request is not the recovered authored-zero TABLE1 profile.");
        }
    }

    private static void ValidateFinite(float value, string paramName)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(paramName);
    }

    private static void ValidateFiniteNonNegative(float value, string paramName)
    {
        if (!float.IsFinite(value) || value < 0f)
            throw new ArgumentOutOfRangeException(paramName);
    }
}

public sealed record UyaClass5821RadiusHeightFacts(
    float HorizontalDistance,
    float AbsoluteVerticalSeparation);

public sealed record UyaClass5821TargetScoreFacts(
    float HorizontalDistance,
    float ShortestHeadingErrorRadians,
    bool IsRatchet);

public sealed record UyaClass5821TargetCandidateFacts(
    UyaGameplayEntityRef Entity,
    bool IsRatchet,
    int? NativeRegistryTag,
    bool IsEligible,
    float HorizontalDistance,
    float ShortestHeadingErrorRadians)
{
    public UyaClass5821TargetScoreFacts ScoreFacts =>
        new(HorizontalDistance, ShortestHeadingErrorRadians, IsRatchet);
}

public sealed record UyaClass5821TargetSelectionResult(
    UyaGameplayEntityRef Entity,
    float Score,
    bool IsRatchet);
