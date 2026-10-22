namespace OBP.RAC1.Gameplay;

/// <summary>
/// Aridia class 604's nine-slot native update dispatcher, L2:0x2d7190.
/// It is controlled by the live Moby byte +0xbc, which the class-1818
/// Sandmouse writes during the scripted sequence. That byte is NOT PVar.
/// </summary>
public sealed class Rac1AridiaClass604Session
{
    public const int NativeClassId = 604;
    public const int ScriptControlByteOffset = 0xbc;

    private sealed class Entry
    {
        public int State { get; set; }
        public int Sequence { get; set; }
    }

    public readonly record struct NativeFacts(byte ScriptControl, bool AnimationCompleted);
    public readonly record struct Frame(int NativeState, int Sequence);

    private readonly Dictionary<int, Entry> _entries = [];
    public int Count => _entries.Count;

    public void Register(int instanceIndex)
    {
        if (instanceIndex < 0) throw new ArgumentOutOfRangeException(nameof(instanceIndex));
        if (!_entries.TryAdd(instanceIndex, new Entry()))
            throw new InvalidOperationException("Duplicate class-604 native instance.");
    }

    public Frame Current(int instanceIndex)
    {
        var entry = _entries[instanceIndex];
        return new(entry.State, entry.Sequence);
    }

    public Frame Step(int instanceIndex, NativeFacts facts)
    {
        var e = _entries[instanceIndex];
        byte command = facts.ScriptControl;
        switch (e.State)
        {
            // L2:0x2d7244: initialize sequence 0 and wait for script command.
            case 0:
                e.State = 1;
                e.Sequence = 0;
                break;
            // L2:0x2d7280: commands 3/4/5 enter anim 1/state 2.
            case 1 when command is >= 3 and <= 5:
                e.State = 2;
                e.Sequence = 1;
                break;
            // L2:0x2d72c4: completion flag +0x70 bit 1 enters anim 2/state 3.
            case 2 when facts.AnimationCompleted:
                e.State = 3;
                e.Sequence = 2;
                break;
            // L2:0x2d7304: commands 4/5 enter anim 3/state 4.
            case 3 when command is 4 or 5:
                e.State = 4;
                e.Sequence = 3;
                break;
            // Same branch: commands 1/7 bypass state 4 and enter anim 6/state 7.
            case 3 when command is 1 or 7:
                e.State = 7;
                e.Sequence = 6;
                break;
            // L2:0x2d7368: anim 3 completion enters anim 4/state 5.
            case 4 when facts.AnimationCompleted:
                e.State = 5;
                e.Sequence = 4;
                break;
            // L2:0x2d73a8: commands 1/3/6/7 enter anim 5/state 6.
            case 5 when command is 1 or 3 or 6 or 7:
                e.State = 6;
                e.Sequence = 5;
                break;
            // L2:0x2d7404: animation 5 completed; 1/7 choose state 7,
            // all other controls lead back to anim 2/state 3.
            case 6 when facts.AnimationCompleted:
                if (command is 1 or 7)
                {
                    e.State = 7;
                    e.Sequence = 6;
                }
                else
                {
                    e.State = 3;
                    e.Sequence = 2;
                }
                break;
            // L2:0x2d748c: anim 6 completion returns to anim 0/state 1.
            case 7 when facts.AnimationCompleted:
                e.State = 1;
                e.Sequence = 0;
                break;
                // Slot 8 has no behavior in the retail dispatcher.
        }
        return new(e.State, e.Sequence);
    }
}
