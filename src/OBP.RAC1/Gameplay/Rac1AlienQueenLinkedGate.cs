using System.Buffers.Binary;
using System.Numerics;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// FUN_L06_002f4f00 + FUN_L06_002f53e8: native class1021 parent
/// and seven dynamically created class1021 children per source placement.
/// Only the two authentic Queen-linked parents are admitted.
/// The original rotation-acceleration recurrence and camera are external.
/// </summary>
public sealed class Rac1AlienQueenLinkedGate
{
    public const int ClassId = 1021;
    public const int ChildrenPerParent = 7;
    public const float ChildYawIncrement = MathF.PI / 4f;
    public const float HideRotation = MathF.PI / 3f;
    public const float ProximityAdmissionRadius = 6f;

    private sealed class Parent(int instance, int sourceIndex, Vector3 position)
    {
        public int Instance { get; } = instance;
        public int SourceIndex { get; } = sourceIndex;
        public Vector3 Position { get; } = position;
        public int NativeState { get; set; }
        public float RotationOffset { get; set; }
        public bool Visible { get; set; } = true;
        public int[] Children { get; } = new int[ChildrenPerParent];
    }

    private readonly Dictionary<int, Parent> _parents = [];
    private readonly Dictionary<int, (int Parent, int Slot)> _children = [];
    private int _nextChildId = -1;

    public Rac1AlienQueenLinkedGate(RuntimeDynamicObject queen,
        IEnumerable<RuntimeDynamicObject> authoredObjects,
        IReadOnlyDictionary<int, Vector3>? nativePositions = null)
    {
        ArgumentNullException.ThrowIfNull(queen);
        ArgumentNullException.ThrowIfNull(authoredObjects);
        if (queen.SourceGame != "rac1" || queen.NativeClassId != Rac1AlienQueenEntrance.ClassId)
            throw new ArgumentException("Requires original Queen class1051.", nameof(queen));
        var p = queen.NativePayloads?.SingleOrDefault(x => x.Format == Rac1BoltCrate.PVarPayloadFormat)?.Data;
        if (p is not { Length: >= 0x104 })
            throw new InvalidDataException("Missing original Queen linked class1021 selectors.");
        var authored = authoredObjects.ToDictionary(x => x.InstanceIndex);
        for (int i = 0; i < 2; i++)
        {
            int id = BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(0xfc + i * 4));
            if (!authored.TryGetValue(id, out var source) || source.SourceGame != "rac1" ||
                source.NativeClassId != ClassId)
                throw new InvalidDataException($"Queen linked Moby {id} is not original class1021.");
            var sourcePvar = source.NativePayloads?.SingleOrDefault(x =>
                x.Format == Rac1BoltCrate.PVarPayloadFormat)?.Data;
            if (sourcePvar is not { Length: >= 0x20 })
                throw new InvalidDataException($"Class1021 {id} lacks source PVar +0x1c index.");
            int triggerIndex = BinaryPrimitives.ReadInt32LittleEndian(sourcePvar.AsSpan(0x1c));
            if (triggerIndex != -1)
                throw new InvalidDataException($"Queen linked class1021 {id} needs special referenced trigger actor.");
            var transform = source.Transform.Matrix;
            var nativePosition = nativePositions is not null && nativePositions.TryGetValue(id, out var supplied)
                ? supplied
                : new Vector3((float)transform[12], (float)transform[13], (float)transform[14]);
            if (!Finite(nativePosition))
                throw new InvalidDataException("Linked source Moby has nonfinite native position.");
            _parents.Add(id, new Parent(id, triggerIndex, nativePosition));
        }
    }

    public int OriginalParentCount => _parents.Count;
    public int RuntimeChildCount => _children.Count;
    public IEnumerable<int> OriginalParentIds => _parents.Keys.Order();

    public Rac1AlienQueenGateFrame Get(int parent) => Snapshot(_parents[parent]);

    public Rac1AlienQueenGateFrame Step(int parent, Vector3 playerNativePosition,
        bool globalProximityGateZero, float angularAdvanceRadians,
        bool nativeCommandZero = true)
    {
        if (!Finite(playerNativePosition) || !float.IsFinite(angularAdvanceRadians) ||
            angularAdvanceRadians < 0)
            throw new ArgumentOutOfRangeException(nameof(playerNativePosition));
        var p = _parents[parent];
        switch (p.NativeState)
        {
            case 0:
                // Native create_moby(0x3fd) seven times, all new Moby indices
                // rather than additional authored level placements.
                for (int slot = 0; slot < ChildrenPerParent; slot++)
                {
                    int childId = _nextChildId--;
                    p.Children[slot] = childId;
                    _children.Add(childId, (parent, slot));
                    SetChildState(childId, 1);
                }
                p.NativeState = 2;
                break;
            case 2:
                // A source idx of -1 uses native hero distance <6 and global
                // D_L06_00161D44 == 0, then checks all seven children state3.
                if (nativeCommandZero && globalProximityGateZero &&
                    Vector3.Distance(playerNativePosition, p.Position) < ProximityAdmissionRadius &&
                    p.Children.All(id => GetChildState(id) == 3))
                {
                    p.NativeState = 4;
                    foreach (int id in p.Children) SetChildState(id, 5);
                }
                break;
            case 4:
            case 5:
                p.RotationOffset = MathF.Min(HideRotation, p.RotationOffset + angularAdvanceRadians);
                if (p.RotationOffset >= HideRotation)
                {
                    p.NativeState = p.NativeState == 4 ? 6 : 7;
                    p.Visible = false;
                }
                break;
            case 8:
            case 9:
                p.RotationOffset = MathF.Max(0, p.RotationOffset - angularAdvanceRadians);
                if (p.RotationOffset <= 0)
                    p.NativeState = p.NativeState == 8 ? 2 : 3;
                break;
        }
        return Snapshot(p);
    }

    /// <summary>
    /// The seven spawned children all use the same native class1021 updater.
    /// Their state1->3 transition depends on the native parent transform,
    /// whose presentation can be supplied by the host; it is not instantaneous
    /// on the source parent state0 spawn tick.
    /// </summary>
    public void ConfirmChildInitialized(int runtimeChildId)
    {
        if (!_children.TryGetValue(runtimeChildId, out var owner))
            throw new ArgumentOutOfRangeException(nameof(runtimeChildId));
        var parent = _parents[owner.Parent];
        if (parent.Children[owner.Slot] != runtimeChildId)
            throw new InvalidOperationException("Class1021 dynamic child ownership mismatch.");
        if (GetChildState(runtimeChildId) != 1)
            throw new InvalidOperationException("Class1021 child was not waiting in state1.");
        SetChildState(runtimeChildId, 3);
    }

    public bool ActivateFromQueen(int parent)
    {
        var p = _parents[parent];
        if (p.NativeState != 6 || p.Children.Length != ChildrenPerParent ||
            p.Children.Any(id => GetChildState(id) != 5))
            return false;
        // FUN_L06_002f53e8 only admits state6, returning parent to state8
        // and moving every spawned child to state9 with collision enabled.
        p.NativeState = 8;
        p.Visible = true;
        foreach (int child in p.Children) SetChildState(child, 9);
        return true;
    }

    public int GetChildState(int runtimeChildId)
    {
        if (!_children.ContainsKey(runtimeChildId))
            throw new KeyNotFoundException($"Unknown dynamic class1021 child {runtimeChildId}");
        return _childStates[runtimeChildId];
    }

    private readonly Dictionary<int, int> _childStates = [];
    private void SetChildState(int id, int state) => _childStates[id] = state;

    public IReadOnlyList<Rac1AlienQueenLinkedChildFrame> Children(int parent)
    {
        var p = _parents[parent];
        return p.Children.Where(id => id != 0).Select(id =>
        {
            var child = _children[id];
            return new Rac1AlienQueenLinkedChildFrame(id, parent, child.Slot,
                (child.Slot + 1) * ChildYawIncrement, GetChildState(id));
        }).ToArray();
    }

    private Rac1AlienQueenGateFrame Snapshot(Parent p) => new(p.Instance,
        p.NativeState, p.Visible, p.RotationOffset, p.Position, Children(p.Instance));

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) &&
        float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

public sealed record Rac1AlienQueenGateFrame(int AuthoredInstance, int NativeState,
    bool Visible, float RotationOffset, Vector3 NativePosition,
    IReadOnlyList<Rac1AlienQueenLinkedChildFrame> Children);

public sealed record Rac1AlienQueenLinkedChildFrame(int RuntimeId, int Parent,
    int Slot, float RelativeYawRadians, int NativeState);
