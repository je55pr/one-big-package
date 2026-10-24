using System.Buffers.Binary;
using System.Numerics;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Only the Frog wave emission in native class1051 Alien Queen:
/// FUN_L06_002fb090, called on state3/sequence6 completion and every
/// state4 update. Not a substitute Alien Queen AI or boss state machine.
/// </summary>
public sealed class Rac1FrogQueenGroup
{
    public const int QueenClassId = 1051;
    public const int NativeWaveState = 4;
    public const int NativeWaveEntryState = 3;
    public const int NativeWaveEntrySequence = 6;
    public const float MinLaunchHeight = 15f;
    public const float MaxLaunchHeight = 20f;
    private readonly int[] _members;
    private bool _state3EventConsumed;

    public Rac1FrogQueenGroup(RuntimeDynamicObject queen,
        IReadOnlyList<int[]> nativeGroups, IEnumerable<int> originalFrogIndices)
    {
        if (queen.SourceGame != "rac1" || queen.NativeClassId != QueenClassId)
            throw new ArgumentException("Original R&C1 class1051 Queen required.", nameof(queen));
        var pvar = queen.NativePayloads?.SingleOrDefault(p =>
            p.Format == Rac1BoltCrate.PVarPayloadFormat)?.Data;
        if (pvar is null || pvar.Length < 0xf4)
            throw new InvalidDataException("Queen lacks source group selector at PVar +0xf0.");
        int groupIndex = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0xf0));
        if (groupIndex < 0 || groupIndex >= nativeGroups.Count)
            throw new InvalidDataException("Queen native Frog group selector out of range.");
        var group = nativeGroups[groupIndex];
        if (group.Length == 0 || group.Any(i => !originalFrogIndices.Contains(i)))
            throw new InvalidDataException("Queen group contains non-Frog source Moby IDs.");
        QueenInstance = queen.InstanceIndex;
        GroupIndex = groupIndex;
        _members = [.. group];
    }

    public int QueenInstance { get; }
    public int GroupIndex { get; }
    public IReadOnlyList<int> Members => _members;

    /// <summary>FUN_L06_002e9ea0, used by native Queen state4:
    /// group is finished only if every listed class827 Moby has entered
    /// 0xFE, 0xFD or 0x10. Neither state15 nor a launched state6 qualifies.</summary>
    public bool IsNativeGroupFinished(Func<int, int> readFrogNativeState)
    {
        ArgumentNullException.ThrowIfNull(readFrogNativeState);
        return _members.All(id => readFrogNativeState(id) is 0xfe or 0xfd or 0x10);
    }

    /// <summary>
    /// Call only with a real observed Queen native state and sequence completion.
    /// Per native source, all entries consume an independent 15..20 Z random
    /// height *even if* a Frog cannot be launched from its current state.
    /// The caller performs the state15 check via the normal Frog session.
    /// </summary>
    public IReadOnlyList<Rac1FrogQueenEmission> ObserveNativeUpdate(
        int nativeState, int nativeSequence, bool animationCompleted,
        Func<int, Vector3> readMobyPosition, Rac1PickupRandom random)
    {
        ArgumentNullException.ThrowIfNull(readMobyPosition);
        ArgumentNullException.ThrowIfNull(random);
        bool emit = nativeState == NativeWaveState;
        if (nativeState == NativeWaveEntryState)
        {
            emit = nativeSequence == NativeWaveEntrySequence &&
                animationCompleted && !_state3EventConsumed;
            if (emit) _state3EventConsumed = true;
        }
        else _state3EventConsumed = false;
        if (!emit) return [];
        var result = new Rac1FrogQueenEmission[_members.Length];
        for (int index = 0; index < _members.Length; index++)
        {
            int id = _members[index];
            var position = readMobyPosition(id);
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
                !float.IsFinite(position.Z))
                throw new InvalidDataException($"Queen group member {id} has invalid source position.");
            result[index] = new(id,
                position + Vector3.UnitZ * random.Range(MinLaunchHeight, MaxLaunchHeight));
        }
        return result;
    }
}

public sealed record Rac1FrogQueenEmission(int FrogInstance, Vector3 NativePosition);
