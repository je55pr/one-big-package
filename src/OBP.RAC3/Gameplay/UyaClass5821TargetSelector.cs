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
/// Horizontal distance and shortest heading error are supplied as facts. This
/// deliberately does not substitute .NET geometry/atan2 for the native VU0
/// distance helper or fast angle helper.
/// </summary>
public static class UyaClass5821TargetSelector
{
    public const float NativeRatchetScoreReduction = 20f;

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
