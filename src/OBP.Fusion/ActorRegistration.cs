using OBP.Core;
using OBP.Runtime.Gameplay;

namespace OBP.Fusion;

/// <summary>
/// Contact identity within one world lease. Target reuses the existing source-
/// qualified weapon contact key; InstanceIndex is allocated uniquely per lease.
/// Original authored placement identity remains in the registration's Source.
/// </summary>
public readonly record struct ActorInstanceKey(string WorldInstanceId, long Generation, WeaponTargetKey Target);

public enum CapabilityAdmission { Ready, Unimplemented, MissingSource, MissingAssets, MissingContext }

/// <summary>Availability observation, not evidence that an unimplemented mechanic works.</summary>
public sealed record ActorCapabilityRegistration(string CapabilityId, CapabilityAdmission Admission, string? Reason = null);

/// <summary>
/// Registration envelope shared by world providers and authored scenarios. #256
/// owns spawn parameters and source adapters; native state/payloads stay there.
/// </summary>
public sealed record ActorRegistration(
    ActorInstanceKey Instance, FusionSourceIdentity Source, string AdapterId,
    string? NativeContextId, IReadOnlyList<ActorCapabilityRegistration> Capabilities)
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;

    public void Validate()
    {
        Source.Validate();
        var target = Instance.Target;
        if (Version != CurrentVersion || string.IsNullOrWhiteSpace(Instance.WorldInstanceId) ||
            Instance.Generation < 0 || target.SourceGame is not (ObpSourceGame.Rac1 or ObpSourceGame.Rac2 or ObpSourceGame.Rac3) ||
            Source.Game != target.SourceGame || target.NativeClassId < 0 || target.InstanceIndex < 0 ||
            string.IsNullOrWhiteSpace(AdapterId))
            throw new ArgumentException("Invalid source-qualified actor registration.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in Capabilities)
            if (string.IsNullOrWhiteSpace(capability.CapabilityId) || !ids.Add(capability.CapabilityId) ||
                !Enum.IsDefined(capability.Admission) ||
                (capability.Admission != CapabilityAdmission.Ready && string.IsNullOrWhiteSpace(capability.Reason)))
                throw new ArgumentException("An unavailable capability needs a specific reason; capability ids must be unique.");
    }
}
