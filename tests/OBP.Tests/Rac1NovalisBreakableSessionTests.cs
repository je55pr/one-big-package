using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1NovalisBreakableSessionTests
{
    [Fact]
    public void MushroomAcceptsWrenchWhileRockNeedsExplosion()
    {
        var session = new Rac1NovalisBreakableSession();
        session.Register(1, 754, 876);
        session.Register(1, 704, 718);
        Assert.True(session.TryBreak(876, Rac1NovalisBreakableSession.ContactKind.Wrench));
        Assert.False(session.TryBreak(876, Rac1NovalisBreakableSession.ContactKind.Explosion));
        Assert.False(session.TryBreak(718, Rac1NovalisBreakableSession.ContactKind.Wrench));
        Assert.True(session.TryBreak(718, Rac1NovalisBreakableSession.ContactKind.Explosion));
        Assert.Equal(2, session.DestroyedCount);
    }

    [Fact]
    public void RejectsOtherLevelsClassesAndUnregisteredContacts()
    {
        var session = new Rac1NovalisBreakableSession();
        Assert.Throws<ArgumentException>(() => session.Register(0, 754, 5));
        Assert.Throws<ArgumentException>(() => session.Register(1, 500, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Register(1, 754, -1));
        session.Register(1, 754, 5);
        Assert.Throws<InvalidOperationException>(() => session.Register(1, 754, 5));
        Assert.False(session.TryBreak(99, Rac1NovalisBreakableSession.ContactKind.Explosion));
    }
}
