namespace OBP.RAC1.Gameplay;

/// <summary>
/// Live owner-qualified class-830 completion byte. Class-623 state 15 reads
/// the byte at native Moby+0xbc from its exact PVar+0x2b8 source index.
/// Native class-830 has separately verified writes of 1 and 2.
/// </summary>
public readonly record struct Rac1Class830CompletionSnapshot(
    int InstanceIndex, int NativeClassId, byte CompletionByte);

/// <summary>
/// Bounded per-level registry for class-623 initial state and source-linked
/// activation. It never infers a controller's native +0xbc value from a
/// rendered Moby; the host must supply an actual source-owned snapshot or
/// leave it unknown. The authored volume check is a separate host predicate.
/// </summary>
public sealed class Rac1Class623ActivationSession
{
    private sealed record Target(
        Rac1Class623ActivationLinks Links,
        Rac1Class623InitialTargetState Initial,
        float InitialHealth)
    {
        public Rac1Class623InitialTargetState State { get; set; } = Initial;
        public float Health { get; set; } = InitialHealth;
    }

    private readonly Dictionary<int, Target> _targets = [];

    public int RegisteredCount => _targets.Count;

    public void Register(int targetIndex, int nativeClassId, ReadOnlySpan<byte> pvar)
    {
        if (targetIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        if (nativeClassId != Rac1AmoeboidTargetSelection.TargetClassId)
            throw new ArgumentOutOfRangeException(nameof(nativeClassId));
        var state = Rac1AmoeboidClass623Admission.Read(pvar);
        var links = Rac1AmoeboidClass623Admission.ReadActivationLinks(pvar);
        float health = Rac1Class623ImpactAccounting.ReadAuthoredHealth(pvar);
        if (!float.IsFinite(health))
            throw new InvalidDataException("Class-623 source health must be finite.");
        if (!_targets.TryAdd(targetIndex, new(links, state, health)))
            throw new InvalidOperationException($"Duplicate class-623 target i{targetIndex}.");
    }

    public Rac1Class623InitialTargetState Get(int targetIndex) =>
        _targets[targetIndex].State;

    public Rac1Class623ActivationLinks Links(int targetIndex) =>
        _targets[targetIndex].Links;

    public float Health(int targetIndex) => _targets[targetIndex].Health;

    /// <summary>
    /// Consume an *already observed* native 0x284730 service status/scalar
    /// for a target in hosted eligible state 1. On nonpositive health,
    /// Rilgar 0x303530 selects response index 1; the source jump table
    /// at 0x2186f4 routes to 0x303660, which clears the 0x1000 flag and
    /// enters state 14. The native service's independent return
    /// classification may also select response indices 1/2, both of
    /// which route to the same terminal handler at 0x303660.
    /// Unavailable return classification is null and cannot trigger
    /// that additional terminal path. No collision or service result
    /// is synthesized. Requiring hosted state 1 is the conservative
    /// host-admission policy, not complete retail receiver parity.
    /// </summary>
    public Rac1Class623ImpactAccountingResult ApplyObservedNativeImpact(
        int targetIndex, int? nativeServiceStatus, float? nativeServiceScalar,
        int? nativeResponseIndex = null)
    {
        var target = _targets[targetIndex];
        if (target.State.NativeState != Rac1AmoeboidClass623Admission.EligibleState ||
            !target.State.HasTargetableFlag)
            return new(false, target.Health, false);

        var result = Rac1Class623ImpactAccounting.Evaluate(
            target.Health, target.State.NativeState, nativeServiceStatus, nativeServiceScalar);
        if (!result.Applied)
            return result;

        target.Health = result.RemainingHealth;
        // Nonpositive health overwrites the service's return index with 1.
        // With positive health, explicitly observed indices 1/2 also
        // dispatch the same flag-clearing terminal response.
        bool terminal = result.TakesTerminalPath || nativeResponseIndex is 1 or 2;
        if (terminal)
            target.State = new(14, false);
        return result with { TakesTerminalPath = terminal };
    }

    /// <summary>
    /// Accept an independently observed *native receiver* transition to
    /// terminal state 14 after its native +0x34 target flag was cleared.
    /// Rilgar 0x303714/0x30369c–0x3036a0. This does not infer a hit,
    /// subtract health or run the unported receiver.
    /// </summary>
    public bool ObserveNativeTerminal(int targetIndex, int nativeState,
        ushort nativeFlags)
    {
        if (nativeState != 14 || (nativeFlags & Rac1AmoeboidClass623Admission.TargetableFlag) != 0)
            throw new ArgumentException("Only source-observed terminal state 14 with 0x1000 cleared is admitted.");
        var target = _targets[targetIndex];
        if (target.State.NativeState == 14) return false;
        if (target.State.NativeState != Rac1AmoeboidClass623Admission.EligibleState)
            return false;
        target.State = new(14, false);
        return true;
    }

    /// <summary>
    /// Advance only waiting targets. The lookup is keyed to each target's
    /// exact linked native controller index and must return genuine runtime
    /// +0xbc state, not a visibility proxy. Null = unknown and never admits.
    /// Returns IDs newly admitted in this step, at most once per lifetime.
    /// </summary>
    public IReadOnlyList<int> Advance(
        Func<int, Rac1Class830CompletionSnapshot?> controllerLookup,
        Func<int, bool?> volumeContainsPlayer)
    {
        ArgumentNullException.ThrowIfNull(controllerLookup);
        ArgumentNullException.ThrowIfNull(volumeContainsPlayer);
        var admitted = new List<int>();
        foreach (var (index, target) in _targets)
        {
            if (target.State.NativeState != Rac1AmoeboidClass623Admission.WaitingState)
                continue;
            var links = target.Links;
            byte? signal = null;
            if (links.LinkedMobyIndex >= 0 &&
                controllerLookup(links.LinkedMobyIndex) is { } controller &&
                controller.InstanceIndex == links.LinkedMobyIndex &&
                controller.NativeClassId == 830)
                signal = controller.CompletionByte;
            bool? inVolume = links.TriggerVolumeIndex >= 0
                ? volumeContainsPlayer(links.TriggerVolumeIndex)
                : null;
            var next = Rac1AmoeboidClass623Admission.AdvanceWaiting(
                target.State, links, signal, inVolume);
            if (next == target.State) continue;
            target.State = next;
            admitted.Add(index);
        }
        return admitted;
    }

    public void Clear() => _targets.Clear();
}
