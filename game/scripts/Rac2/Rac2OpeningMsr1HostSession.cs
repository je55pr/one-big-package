using Godot;
using OBP.Godot;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

internal enum Rac2OpeningMsr1Phase
{
    Dormant,
    State3Delay,
    State5,
    State4,
    State8,
    Chase,
    Attack,
    Recovery,
}

internal sealed class Rac2OpeningMsr1HostSession(
    RuntimeWorldScene.DynamicObjectNode node,
    Rac2Class2827HostileSession hostile)
{
    public RuntimeWorldScene.DynamicObjectNode Node { get; } = node;
    public Rac2Class2827HostileSession Hostile { get; } = hostile;
    public Rac2Class2827ApproachSession Approach { get; } = new();
    public Rac2Class2827AttackCycleSession AttackCycle { get; } = new();
    public Rac2OpeningMsr1Phase Phase { get; set; }
    public int PhaseTicks { get; set; }
    public int AttackSequence { get; set; }
    public int AttackCount { get; set; }
}
