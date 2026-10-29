using OBP.Game;

namespace OBP.Tests;

public sealed class Rac1WeaponSoundTraceTests
{
    [Fact]
    public void WeaponSoundHistoryIsBoundedCopiedAndClearedOnRetirement()
    {
        var presenter = new Rac1WeaponSoundTrace();
        presenter.Record(168, 1, 0, trace: false, loop: true);
        Assert.Empty(presenter.Events);
        for (ulong tick = 0; tick <= 4096; tick++)
            presenter.Record(168, 1, tick, trace: true, loop: true);

        var events = presenter.Events;
        Assert.Equal(4096, events.Length);
        Assert.Equal(1UL, events[0].Tick);
        Assert.Equal(4096UL, events[^1].Tick);
        Assert.All(events, item =>
        {
            Assert.Equal(168, item.Class);
            Assert.Equal(1, item.Sound);
            Assert.True(item.Loop);
        });

        events[0] = default;
        Assert.Equal(1UL, presenter.Events[0].Tick);
        Assert.Equal(4096, presenter.Events.Length);
        presenter.Clear();
        Assert.Empty(presenter.Events);
    }
}
