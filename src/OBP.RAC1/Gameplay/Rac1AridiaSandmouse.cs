namespace OBP.RAC1.Gameplay;

/// <summary>
/// Narrow, source-ordered portion of Aridia class 1818, LEVEL2:0x2f0f88.
/// The native scene/hero and path physics conditions are not yet implemented
/// by the Godot host. They must arrive as affirmative facts, never guessed
/// from proximity or elapsed time.
/// </summary>
public sealed class Rac1AridiaSandmouseSession
{
    public const int NativeClassId = Rac1AridiaEnemies.Sandmouse;
    public const int LinkedControllerClassId = 604;
    public const int WaitForSceneState = 1;
    public const int WaitForLinkedControllerState = 2;
    public const int TravelState = 3;
    public const int ArrivedState = 4;
    public const int SharksUnlockState = 5;

    private readonly Dictionary<int, Entry> _entries = [];

    private sealed class Entry(int linkedControllerInstance)
    {
        public int LinkedControllerInstance { get; } = linkedControllerInstance;
        public int State { get; set; }
        public byte? PendingLinkedControllerCommand { get; set; }
    }

    public readonly record struct NativeFacts(
        bool NativeSceneFlag = false,
        int NativeSceneMode = 0,
        bool NativeSceneEntryCompleted = false,
        int? LinkedControllerNativeState = null,
        bool NativeTravelCompleted = false);

    public void Register(int instanceIndex, int linkedControllerInstance)
    {
        if (instanceIndex < 0 || linkedControllerInstance < 0)
            throw new ArgumentOutOfRangeException(nameof(instanceIndex));
        if (!_entries.TryAdd(instanceIndex, new(linkedControllerInstance)))
            throw new InvalidOperationException("Duplicate Aridia Sandmouse instance.");
    }

    public int LinkedController(int instanceIndex) => _entries[instanceIndex].LinkedControllerInstance;
    public int NativeState(int instanceIndex) => _entries[instanceIndex].State;

    /// <summary>
    /// Native 0x2f159c writes value 5 to linked Moby +0xbc after the hero
    /// cinematic entry succeeds. The caller may consume the command once,
    /// then supply it to the class-604 controller on its next native tick.
    /// This is a live script-control byte, not a PVar write.
    /// </summary>
    public byte? TakeLinkedControllerCommand(int instanceIndex)
    {
        var entry = _entries[instanceIndex];
        byte? command = entry.PendingLinkedControllerCommand;
        entry.PendingLinkedControllerCommand = null;
        return command;
    }

    public int Step(int instanceIndex, NativeFacts facts)
    {
        var entry = _entries[instanceIndex];
        // L2:0x2f131c: after initial transform and animation setup, state 0 -> 1.
        // L2:0x2f1330: state 1 is gated by 0x13d4c5 and 0x1404a8 == 5;
        // further cutscene/player interactions must be represented by the
        // explicitly supplied entry-completed fact.
        // L2:0x2f15f0: state 2 requires linked class-604 state EXACTLY 5.
        // L2:0x2f16b8..0x2f1878: state 3 advances upon native route/contact.
        // L2:0x2f18f8..0x2f1980: state 4 unconditionally commits state 5.
        switch (entry.State)
        {
            case 0:
                entry.State = WaitForSceneState;
                break;
            case WaitForSceneState when facts.NativeSceneFlag && facts.NativeSceneMode == 5 &&
                facts.NativeSceneEntryCompleted:
                // L2:0x2f159c sets the linked class604's +0xbc command to 5
                // before entering Sandmouse state2. Never issue on a mere
                // global scene-mode match without successful hero entry.
                entry.PendingLinkedControllerCommand = 5;
                entry.State = WaitForLinkedControllerState;
                break;
            case WaitForLinkedControllerState when facts.LinkedControllerNativeState == 5:
                entry.State = TravelState;
                break;
            case TravelState when facts.NativeTravelCompleted:
                entry.State = ArrivedState;
                break;
            case ArrivedState:
                entry.State = SharksUnlockState;
                break;
        }
        return entry.State;
    }
}
