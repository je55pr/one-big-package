using System.Numerics;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1AridiaClass604Tests
{
    private static readonly Rac1AridiaClass604Session.NativeFacts Idle = new(0, false);

    [Fact]
    public void ScriptCommandFiveAndCompletionFlagsProduceNativeStatesZeroToFive()
    {
        var controller = new Rac1AridiaClass604Session();
        controller.Register(526);
        Assert.Equal(new Rac1AridiaClass604Session.Frame(0, 0), controller.Current(526));
        Assert.Equal(new(1, 0), controller.Step(526, Idle));
        for (int i = 0; i < 90; i++)
            Assert.Equal(new(1, 0), controller.Step(526, Idle));
        Assert.Equal(new(1, 0), controller.Step(526, new(2, true)));
        Assert.Equal(new(2, 1), controller.Step(526, new(5, false)));
        Assert.Equal(new(2, 1), controller.Step(526, new(5, false)));
        Assert.Equal(new(3, 2), controller.Step(526, new(5, true)));
        Assert.Equal(new(4, 3), controller.Step(526, new(5, false)));
        Assert.Equal(new(4, 3), controller.Step(526, new(5, false)));
        Assert.Equal(new(5, 4), controller.Step(526, new(5, true)));
        for (int i = 0; i < 120; i++)
            Assert.Equal(new(5, 4), controller.Step(526, new(5, false)));
    }

    [Fact]
    public void OriginalAnimationGatedRetreatAndRestStatesPreserveCommandSemantics()
    {
        var c = new Rac1AridiaClass604Session();
        c.Register(526);
        c.Step(526, Idle);
        Assert.Equal(new(2, 1), c.Step(526, new(3, false)));
        Assert.Equal(new(3, 2), c.Step(526, new(0, true)));
        Assert.Equal(new(7, 6), c.Step(526, new(7, false)));
        Assert.Equal(new(7, 6), c.Step(526, new(7, false)));
        Assert.Equal(new(1, 0), c.Step(526, new(0, true)));

        Assert.Equal(new(2, 1), c.Step(526, new(5, false)));
        Assert.Equal(new(3, 2), c.Step(526, new(5, true)));
        Assert.Equal(new(4, 3), c.Step(526, new(5, false)));
        Assert.Equal(new(5, 4), c.Step(526, new(5, true)));
        Assert.Equal(new(5, 4), c.Step(526, new(4, true))); // not a state-5 command
        Assert.Equal(new(6, 5), c.Step(526, new(6, false)));
        Assert.Equal(new(6, 5), c.Step(526, new(6, false)));
        Assert.Equal(new(3, 2), c.Step(526, new(6, true)));
        Assert.Equal(new(7, 6), c.Step(526, new(1, false)));
        Assert.Equal(new(1, 0), c.Step(526, new(1, true)));
    }

    [Fact]
    public void SandmouseRequiresRealClass604StateFiveBeforeItsOwnSharkUnlock()
    {
        var linked = new Rac1AridiaClass604Session();
        linked.Register(526);
        var mouse = new Rac1AridiaSandmouseSession();
        mouse.Register(756, 526);
        var sharks = new Rac1AridiaEnemySession();
        for (int i = 440; i <= 445; i++)
            sharks.Register(i, Rac1AridiaEnemies.Sandshark, linkedActivationMobyIndex: 756);
        var close = new Rac1AridiaEnemyFacts(Vector3.Zero, new(.3f, 0, 0), true, true);

        Assert.Equal(1, mouse.Step(756, default));
        Assert.Null(mouse.TakeLinkedControllerCommand(756));
        Assert.Equal(1, mouse.Step(756, new(true, 5, false)));
        Assert.Null(mouse.TakeLinkedControllerCommand(756));
        // Native scene mode 5 plus the verified cinematic/hero entry is
        // required BEFORE class604 is allowed to influence Sandmouse.
        Assert.Equal(2, mouse.Step(756, new(true, 5, true)));
        byte command = mouse.TakeLinkedControllerCommand(756) ?? throw new Exception("Missing native script command");
        Assert.Equal((byte)5, command);
        Assert.Null(mouse.TakeLinkedControllerCommand(756));
        Assert.Equal(1, linked.Step(526, Idle).NativeState);
        Assert.Equal(2, linked.Step(526, new(command, false)).NativeState);
        Assert.Equal(3, linked.Step(526, new(5, true)).NativeState);
        Assert.Equal(4, linked.Step(526, new(5, false)).NativeState);
        for (int i = 440; i <= 445; i++)
            Assert.Equal(Rac1AridiaEnemyPhase.Buried,
                sharks.Step(i, close with { LinkedMobyNativeState = mouse.NativeState(756) }).Phase);
        Assert.Equal(2, mouse.Step(756, new(LinkedControllerNativeState: 4)));
        Assert.Equal(5, linked.Step(526, new(5, true)).NativeState);
        Assert.Equal(3, mouse.Step(756, new(LinkedControllerNativeState: linked.Current(526).NativeState)));
        Assert.Equal(4, mouse.Step(756, new(NativeTravelCompleted: true)));
        for (int i = 440; i <= 445; i++)
            Assert.Equal(Rac1AridiaEnemyPhase.Buried,
                sharks.Step(i, close with { LinkedMobyNativeState = mouse.NativeState(756) }).Phase);
        Assert.Equal(5, mouse.Step(756, default));
        for (int i = 440; i <= 445; i++)
            Assert.Equal(Rac1AridiaEnemyPhase.Emerging,
                sharks.Step(i, close with { LinkedMobyNativeState = mouse.NativeState(756) }).Phase);
    }

    [Fact]
    public void InvalidControllersCannotAffectOtherInstancesOrRelaunchState()
    {
        var session = new Rac1AridiaClass604Session();
        session.Register(526);
        Assert.Throws<InvalidOperationException>(() => session.Register(526));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Register(-1));
        Assert.Throws<KeyNotFoundException>(() => session.Step(527, Idle));
        Assert.Equal(1, session.Step(526, Idle).NativeState);
        var retry = new Rac1AridiaClass604Session();
        retry.Register(526);
        Assert.Equal(0, retry.Current(526).NativeState);
    }
}
