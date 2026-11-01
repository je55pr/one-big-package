using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class GameplayTickSchedulerTests
{
    [Fact]
    public void OrderedWorldFramePreservesPresentationNativeThenHudAndOriginalDeltas()
    {
        using var lifetime = new CancellationTokenSource();
        using var schedule = new GameplayTickScheduler<string>(lifetime.Token);
        var calls = new List<string>();
        schedule.Register(GameplayTickPhase.Hud, "hud", "ui", 0, value => calls.Add($"hud:{value}"));
        schedule.Register(GameplayTickPhase.NativeWorld, "second-source", "source-b", 20,
            value => calls.Add($"second:{value}"));
        schedule.Register(GameplayTickPhase.Presentation, "effects", "effects", 20,
            value => calls.Add($"effects:{value}"));
        schedule.Register(GameplayTickPhase.NativeWorld, "first-source", "source-a", 10,
            value => calls.Add($"first:{value}"));
        schedule.Register(GameplayTickPhase.Presentation, "scene", "scene", 10,
            value => calls.Add($"scene:{value}"));
        schedule.Register(GameplayTickPhase.Prepare, "prepare", "effects", 0,
            value => calls.Add($"prepare:{value}"));
        schedule.Activate();

        schedule.Run(GameplayTickPhase.Prepare, .017);
        schedule.RunWorldFrame(.017);

        Assert.Equal(["prepare:0.017", "scene:0.017", "effects:0.017",
            "first:0.017", "second:0.017", "hud:0.017"], calls);
        Assert.Equal(6, schedule.Registrations.Count);
    }

    [Fact]
    public void UnavailableSourcesNeverReceiveTicksOrInputAndInputStopsAtFirstHandler()
    {
        using var schedule = new GameplayTickScheduler<string>(CancellationToken.None);
        bool active = false;
        int dormant = 0;
        int activeCalls = 0;
        schedule.Register(GameplayTickPhase.NativeWorld, "inactive", "source", 0,
            _ => dormant++, () => active);
        schedule.Register(GameplayTickPhase.NativeWorld, "active", "source", 1,
            _ => activeCalls++);
        schedule.RegisterInput("dormant-input", "source", 0, _ => { dormant++; return true; },
            () => active);
        schedule.RegisterInput("first", "world", 1, value => value == "accept");
        schedule.RegisterInput("later", "world", 2, _ => { activeCalls += 10; return true; });
        schedule.Activate();

        schedule.RunWorldFrame(1);
        Assert.True(schedule.TryHandleInput("accept"));
        Assert.Equal(0, dormant);
        Assert.Equal(1, activeCalls);
        Assert.True(schedule.TryHandleInput("other"));
        Assert.Equal(11, activeCalls);

        active = true;
        schedule.RunWorldFrame(1);
        Assert.True(schedule.TryHandleInput("accept"));
        Assert.Equal(2, dormant);
        Assert.Equal(12, activeCalls);
    }

    [Fact]
    public void RegistrationRejectsDuplicatesLateRegistrationAndInvalidCallbacksWithoutPoisoningKey()
    {
        using var schedule = new GameplayTickScheduler<int>(CancellationToken.None);
        Assert.Throws<ArgumentNullException>(() =>
            schedule.Register(GameplayTickPhase.NativeWorld, "valid", "host", 0, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            schedule.Register((GameplayTickPhase)999, "valid", "host", 0, _ => { }));
        schedule.Register(GameplayTickPhase.NativeWorld, "valid", "host", 0, _ => { });
        Assert.Throws<InvalidOperationException>(() =>
            schedule.RegisterInput("valid", "host", 0, _ => true));
        Assert.Throws<ArgumentException>(() =>
            schedule.RegisterInput("", "host", 0, _ => true));
        Assert.Throws<ArgumentException>(() =>
            schedule.Register(GameplayTickPhase.Physics, "other", "", 0, _ => { }));
        Assert.Throws<InvalidOperationException>(() => schedule.RunWorldFrame(0));
        schedule.Activate();
        Assert.Throws<InvalidOperationException>(() => schedule.Activate());
        Assert.Throws<InvalidOperationException>(() =>
            schedule.Register(GameplayTickPhase.Physics, "late", "host", 0, _ => { }));
    }

    [Fact]
    public void CancellationDuringCallbackAbortsRemainingTicksAndLateInput()
    {
        using var lifetime = new CancellationTokenSource();
        using var schedule = new GameplayTickScheduler<int>(lifetime.Token);
        int executed = 0;
        schedule.Register(GameplayTickPhase.Physics, "first", "world", 0, _ =>
        {
            executed++;
            lifetime.Cancel();
        });
        schedule.Register(GameplayTickPhase.Physics, "second", "world", 1, _ => executed += 100);
        schedule.RegisterInput("input", "world", 0, _ => { executed += 100; return true; });
        schedule.Activate();

        schedule.Run(GameplayTickPhase.Physics, 1);
        schedule.RunWorldFrame(1);
        Assert.False(schedule.TryHandleInput(0));
        Assert.Equal(1, executed);
        Assert.Throws<ObjectDisposedException>(() =>
            schedule.RegisterInput("late", "world", 1, _ => true));
    }

    [Fact]
    public void RetirementInsideCallbackAbortsRemainingStepsWithoutCollectionMutationErrors()
    {
        using var schedule = new GameplayTickScheduler<string>(CancellationToken.None);
        int invoked = 0;
        schedule.Register(GameplayTickPhase.NativeWorld, "retire", "world", 0, _ =>
        {
            invoked++;
            schedule.Dispose();
        });
        schedule.Register(GameplayTickPhase.NativeWorld, "stale", "world", 1, _ => invoked += 100);
        schedule.Activate();

        schedule.RunWorldFrame(1);
        schedule.RunWorldFrame(1);
        Assert.Equal(1, invoked);
        Assert.False(schedule.TryHandleInput("stale"));
    }

    [Fact]
    public void ReentrantDispatchAndRegistrationFailClosedButThrownCallbacksDoNotLatchDispatcher()
    {
        using var schedule = new GameplayTickScheduler<string>(CancellationToken.None);
        int attempts = 0;
        schedule.Register(GameplayTickPhase.NativeWorld, "throw-once", "source", 0, _ =>
        {
            attempts++;
            Assert.Throws<InvalidOperationException>(() => schedule.RunWorldFrame(0));
            Assert.Throws<InvalidOperationException>(() =>
                schedule.Register(GameplayTickPhase.NativeWorld, "injected", "source", 1, _ => { }));
            if (attempts == 1) throw new FormatException("native failure");
        });
        schedule.Activate();

        Assert.Throws<FormatException>(() => schedule.RunWorldFrame(0));
        schedule.RunWorldFrame(0);
        Assert.Equal(2, attempts);
        Assert.Single(schedule.Registrations);
    }

    [Fact]
    public void CancellationBeforeActivationAndRegistrationIsRejected()
    {
        using var lifetime = new CancellationTokenSource();
        using var schedule = new GameplayTickScheduler<int>(lifetime.Token);
        lifetime.Cancel();
        Assert.Throws<ObjectDisposedException>(() => schedule.Activate());
        Assert.Throws<ObjectDisposedException>(() =>
            schedule.Register(GameplayTickPhase.Physics, "late", "owner", 0, _ => { }));
    }
}
