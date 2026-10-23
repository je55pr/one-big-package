using Godot;

namespace OneBigPackage;

/// <summary>
/// Live acceptance-test input isolation. A real pad can be reporting stick
/// input while a synthetic controller test runs. Only the *specific* injected
/// InputEvent resources are admitted; the normal device-0 InputMap semantics
/// remain unchanged. No instance is installed in gameplay outside this test.
/// </summary>
public sealed partial class Rac1PauseSmokeInputGuard : Node
{
    private readonly HashSet<ulong> _injectedEventIds = [];

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public void Admit(InputEvent @event) => _injectedEventIds.Add(@event.GetInstanceId());

    public override void _Input(InputEvent @event)
    {
        if (!_injectedEventIds.Remove(@event.GetInstanceId()))
            GetViewport().SetInputAsHandled();
    }
}
