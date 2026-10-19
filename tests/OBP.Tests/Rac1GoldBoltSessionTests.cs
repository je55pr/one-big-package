using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1GoldBoltSessionTests
{
    [Fact]
    public void NovalisRegistersNativeGoldBoltsAndCollectsEachOnlyOnce()
    {
        var session = new Rac1GoldBoltSession();
        session.Register(1, 1134, 12);
        session.Register(1, 1134, 19);
        session.Register(1, 1134, 31);

        Assert.Equal(3, session.RegisteredCount);
        Assert.False(session.TryCollect(12, contactAdmitted: false));
        Assert.True(session.TryCollect(12, contactAdmitted: true));
        Assert.False(session.TryCollect(12, contactAdmitted: true));
        Assert.True(session.TryCollect(19, contactAdmitted: true));
        Assert.Equal(2, session.CollectedCount);
        Assert.True(session.IsCollected(12));
        Assert.False(session.IsCollected(31));
    }

    [Fact]
    public void RevisitedNovalisRestoresEarlierCollections()
    {
        var session = new Rac1GoldBoltSession([958, 960]);
        session.Register(1, 1134, 958);
        session.Register(1, 1134, 959);
        session.Register(1, 1134, 960);
        Assert.Equal(2, session.CollectedCount);
        Assert.False(session.TryCollect(958, contactAdmitted: true));
        Assert.True(session.TryCollect(959, contactAdmitted: true));
        Assert.Equal(3, session.CollectedCount);
    }

    [Fact]
    public void RejectsWrongGameplayClassLevelAndDuplicateIdentity()
    {
        var session = new Rac1GoldBoltSession();
        Assert.Throws<ArgumentException>(() => session.Register(0, 1134, 12));
        Assert.Throws<ArgumentException>(() => session.Register(1, 13, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Register(1, 1134, -1));
        session.Register(1, 1134, 12);
        Assert.Throws<InvalidOperationException>(() => session.Register(1, 1134, 12));
        Assert.False(session.TryCollect(999, contactAdmitted: true));
    }
}
