using System.Numerics;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1AridiaSandmouseTests
{
    [Fact]
    public void Class1818RequiresTheNativeSceneLinked604AndTravelChainBeforeUnlock()
    {
        var controller = new Rac1AridiaSandmouseSession();
        controller.Register(756, 526);
        Assert.Equal(526, controller.LinkedController(756));
        Assert.Equal(0, controller.NativeState(756));
        Assert.Equal(1, controller.Step(756, default));

        // The near-start sharks must not unlock from proximity, time or a
        // staged cutscene mode in isolation.
        for (int i = 0; i < 240; i++)
            Assert.Equal(1, controller.Step(756, new(
                NativeSceneFlag: true, NativeSceneMode: 5)));
        Assert.Equal(1, controller.Step(756, new(
            NativeSceneFlag: true, NativeSceneMode: 4,
            NativeSceneEntryCompleted: true)));
        Assert.Equal(2, controller.Step(756, new(
            NativeSceneFlag: true, NativeSceneMode: 5,
            NativeSceneEntryCompleted: true)));
        Assert.Equal(2, controller.Step(756, new(LinkedControllerNativeState: 4)));
        Assert.Equal(2, controller.Step(756, new(LinkedControllerNativeState: 6)));
        Assert.Equal(3, controller.Step(756, new(LinkedControllerNativeState: 5)));
        Assert.Equal(3, controller.Step(756, default));
        Assert.Equal(4, controller.Step(756, new(NativeTravelCompleted: true)));
        Assert.Equal(5, controller.Step(756, default));
        Assert.Equal(5, controller.Step(756, default));
    }

    [Fact]
    public void OnlySixRetailLinkedSharksUnlockWhenSandmouseCompletesTheFullNativeChain()
    {
        var sandmouse = new Rac1AridiaSandmouseSession();
        sandmouse.Register(756, 526);
        var sharks = new Rac1AridiaEnemySession();
        for (int id = 440; id <= 445; id++)
            sharks.Register(id, Rac1AridiaEnemies.Sandshark, linkedActivationMobyIndex: 756);
        sharks.Register(446, Rac1AridiaEnemies.Sandshark);
        var close = new Rac1AridiaEnemyFacts(Vector3.Zero, new(.1f, 0, 0), true, true);
        for (int i = 0; i < 120; i++)
            for (int id = 440; id <= 445; id++)
                Assert.Equal(Rac1AridiaEnemyPhase.Buried,
                    sharks.Step(id, close with { LinkedMobyNativeState = sandmouse.NativeState(756) }).Phase);

        sandmouse.Step(756, default);
        sandmouse.Step(756, new(true, 5, true));
        sandmouse.Step(756, new(LinkedControllerNativeState: 5));
        sandmouse.Step(756, new(NativeTravelCompleted: true));
        Assert.Equal(4, sandmouse.NativeState(756));
        for (int id = 440; id <= 445; id++)
            Assert.Equal(Rac1AridiaEnemyPhase.Buried,
                sharks.Step(id, close with { LinkedMobyNativeState = sandmouse.NativeState(756) }).Phase);
        sandmouse.Step(756, default);
        int released = 0;
        for (int id = 440; id <= 445; id++)
            if (sharks.Step(id, close with
            { LinkedMobyNativeState = sandmouse.NativeState(756) }).Phase ==
                Rac1AridiaEnemyPhase.Emerging) released++;
        Assert.Equal(6, released);
        Assert.False(sharks.LinkedActivationSatisfied(446));
    }

    [Fact]
    public void NewSandmouseSessionReturnsToLockedNativeStartupOnRetry()
    {
        var first = new Rac1AridiaSandmouseSession();
        first.Register(756, 526);
        first.Step(756, default);
        first.Step(756, new(true, 5, true));
        first.Step(756, new(LinkedControllerNativeState: 5));
        first.Step(756, new(NativeTravelCompleted: true));
        Assert.Equal(5, first.Step(756, default));
        var retry = new Rac1AridiaSandmouseSession();
        retry.Register(756, 526);
        Assert.Equal(0, retry.NativeState(756));
        Assert.Equal(1, retry.Step(756, default));
    }
}
