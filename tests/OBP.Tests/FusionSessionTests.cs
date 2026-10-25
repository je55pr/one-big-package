using OBP.Core.Math;
using OBP.Fusion;

namespace OBP.Tests;

public sealed class FusionSessionTests
{
    [Fact]
    public void SourceIdentityIsRequiredForEveryExhibit()
    {
        var session = new FusionSession
        {
            Id = "test",
            Exhibits = [
                new FusionExhibit
                {
                    Id = "target-1", Position = Vec3.Zero, AdapterId = "dummy",
                Source = new FusionSourceIdentity(OBP.Core.ObpSourceGame.Rac1, "retail", "moby:123"),
                },
            ],
        };
        session.Validate();
        Assert.Throws<ArgumentException>(() => (session with
        {
            Exhibits = [session.Exhibits[0], session.Exhibits[0]],
        }).Validate());
        Assert.Throws<ArgumentException>(() => (session with
        {
            Exhibits = [session.Exhibits[0] with
            {
            Source = new FusionSourceIdentity((OBP.Core.ObpSourceGame)(-1), "", "1"),
            }],
        }).Validate());
    }
}
