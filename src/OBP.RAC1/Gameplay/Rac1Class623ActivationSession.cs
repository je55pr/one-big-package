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
        Rac1Class623InitialTargetState Initial)
    {
        public Rac1Class623InitialTargetState State { get; set; } = Initial;
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
        if (!_targets.TryAdd(targetIndex, new(links, state)))
            throw new InvalidOperationException($"Duplicate class-623 target i{targetIndex}.");
    }

    public Rac1Class623InitialTargetState Get(int targetIndex) =>
        _targets[targetIndex].State;

    public Rac1Class623ActivationLinks Links(int targetIndex) =>
        _targets[targetIndex].Links;

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
