using Godot;
using OBP.Runtime.Diagnostics;

namespace OBP.Game;

internal sealed partial class PlaytestEndDriver : Node
{
    public PlaytestDriver Driver { get; init; } = null!;
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = ProcessPhysicsPriority = 10000;
    }
    public override void _PhysicsProcess(double delta) => Driver.End(PlaytestStepKind.Physics);
    public override void _Process(double delta) => Driver.End(PlaytestStepKind.Process);
}
