using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Original R&C1 Amoeboid PVar+0x248 candidate-list search, native
/// FUN_L01_002ef478. Only live class-623 group members under 120 native
/// units compete with the current hero target. Actor liveness and Moby
/// flags are reported by the host, not inferred from authored placement.
/// </summary>
public static class Rac1AmoeboidTargetSelection
{
    public const int TargetClassId = 0x26f;
    public const float NativeCandidateRange = 120f;

    public sealed record Candidate(int InstanceIndex, int NativeClassId, int Group,
        Vector3 Position, bool SourceActive, int NativeState);

    /// <summary>
    /// Milestone host policy: do not redirect an attack away from Ratchet
    /// towards a class-623 prop until the host can actually receive that hit.
    /// Keep the original native selection algorithm separately testable.
    /// </summary>
    public static Candidate? SelectForHostedCombat(bool class623HitReceiverAvailable,
        int sourceCandidateGroup, Vector3 origin, Vector3 currentTarget,
        float searchRadius, IEnumerable<Candidate> candidates) =>
        class623HitReceiverAvailable
            ? Select(sourceCandidateGroup, origin, currentTarget, searchRadius, candidates)
            : null;

    /// <summary>
    /// Return only a native candidate that displaces the nearer current target.
    /// The caller owns whether a candidate has an admitted damage consumer.
    /// </summary>
    public static Candidate? Select(int sourceCandidateGroup, Vector3 origin,
        Vector3 currentTarget, float searchRadius, IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (sourceCandidateGroup < -1 || sourceCandidateGroup > 255 ||
            !IsFinite(origin) || !IsFinite(currentTarget) ||
            !float.IsFinite(searchRadius) || searchRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceCandidateGroup));

        if (sourceCandidateGroup is -1 or 255) return null;
        float best = NativeCandidateRange;
        Candidate? nearest = null;
        foreach (var candidate in candidates)
        {
            if (candidate.NativeClassId != TargetClassId ||
                candidate.Group != sourceCandidateGroup ||
                !candidate.SourceActive || candidate.NativeState is < 0 or >= 0x7f ||
                !IsFinite(candidate.Position)) continue;
            float distance = Vector3.Distance(origin, candidate.Position);
            if (distance >= best) continue;
            nearest = candidate;
            best = distance;
        }

        // Source 0x2eec68 admits a group candidate only within the current
        // detection radius and when closer than the existing target.
        return nearest is not null && best < searchRadius &&
               best < Vector3.Distance(origin, currentTarget) ? nearest : null;
    }

    private static bool IsFinite(Vector3 point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
