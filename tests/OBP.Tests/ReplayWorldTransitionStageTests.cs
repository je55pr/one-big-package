using OBP.Fusion;
using OBP.Runtime.Diagnostics;

namespace OBP.Tests;

public sealed class ReplayWorldTransitionStageTests
{
    [Theory]
    [InlineData(PlaytestStepKind.Physics, PlaytestStepKind.Physics)]
    [InlineData(PlaytestStepKind.Process, PlaytestStepKind.Process)]
    [InlineData(PlaytestStepKind.Physics, PlaytestStepKind.Process)]
    public void PreStepTransitionPrecedesReadinessAndIsNotRepeatedWhileWaiting(PlaytestStepKind recorded, PlaytestStepKind callback)
    {
        var stage = new ReplayWorldTransitionStage();
        var step = new PlaytestStep(7, recorded, 100, 1d / 60, [], new Dictionary<string, float>(), [])
        { Commands = [new("quick-slot", "rac3/item/43", 7), new("destination", "rac2:LEVEL1"), new("equip", "rac2/item/30"), new("unequip", "", After: true)] };
        var applied = new List<PlaytestCommand>();
        bool ready = true;
        int consumed = 0;
        bool TryBegin(PlaytestStepKind phase)
        {
            if (stage.Stage(step, command => { applied.Add(command); if (command.Kind == "destination") ready = false; })) return false;
            if (!ready || phase != step.Kind) return false;
            consumed++; return true;
        }
        Assert.False(TryBegin(callback));
        Assert.False(ready); // Matching phases still yield before input/checkpoints consume the new world.
        Assert.Equal(step.Commands.Take(3), applied);
        for (int wait = 0; wait < 3; wait++) Assert.False(TryBegin(callback));
        ready = true;
        if (recorded != callback) Assert.False(TryBegin(callback));
        Assert.True(TryBegin(recorded));
        Assert.Equal(1, consumed); Assert.Equal(3, applied.Count);
        Assert.True(stage.Stage(step with { Index = 8 }, applied.Add));
        Assert.Equal(6, applied.Count);
    }

    [Fact]
    public void OrdinaryAndAfterStepCommandsStayInTheirRecordedPhase()
    {
        var stage = new ReplayWorldTransitionStage();
        var step = new PlaytestStep(0, PlaytestStepKind.Physics, 0, 1d / 60, [], new Dictionary<string, float>(), [])
        { Commands = [new("equip", "rac2/item/30"), new("destination", "obp:fusion-lab", After: true)] };
        Assert.False(stage.Stage(step, _ => throw new InvalidOperationException("Premature session operation.")));
    }
}
