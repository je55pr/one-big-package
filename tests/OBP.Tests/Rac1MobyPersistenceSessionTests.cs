using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1MobyPersistenceSessionTests
{
    [Fact]
    public void UidStateIsKeyedByLevelAndStableAuthoredIdentity()
    {
        var source = Dynamic(nativeClassId: 500, instanceIndex: 89, nativeUid: 121);
        var session = new Rac1MobyPersistenceSession(levelId: 0);

        var key = session.RequireKey(source);
        var before = session.QueryUid(source);
        var updated = session.UpdateUid(
            source,
            Rac1MobyUidPersistenceBits.BothSet);

        Assert.Equal(new Rac1MobyPersistenceKey(0, 500, 89, 121), key);
        Assert.Equal(Rac1MobyUidPersistenceBits.Clear, before);
        Assert.Equal(Rac1MobyUidPersistenceBits.BothSet, updated);
        Assert.Equal(updated, session.QueryUid(source));
        Assert.Equal(1, session.TrackedUidCount);
    }

    [Fact]
    public void TwoRecoveredUidChannelsRemainIndependent()
    {
        var source = Dynamic(nativeClassId: 500, instanceIndex: 89, nativeUid: 121);
        var session = new Rac1MobyPersistenceSession(levelId: 0);
        var levelOnly = new Rac1MobyUidPersistenceBits(
            LevelIndexedMap: true,
            LocalSessionMap: false);

        session.UpdateUid(source, levelOnly);

        Assert.Equal(levelOnly, session.QueryUid(source));
        Assert.NotEqual(
            Rac1MobyUidPersistenceBits.BothSet,
            session.QueryUid(source));
    }

    [Fact]
    public void FullLevelBoundaryUsesIndependentStoresWithoutClaimingReloadPersistence()
    {
        var source = Dynamic(nativeClassId: 500, instanceIndex: 89, nativeUid: 121);
        var veldin = new Rac1MobyPersistenceSession(levelId: 0);
        veldin.UpdateUid(source, Rac1MobyUidPersistenceBits.BothSet);

        var freshVeldinLoad = new Rac1MobyPersistenceSession(levelId: 0);
        var levelTwo = new Rac1MobyPersistenceSession(levelId: 2);

        Assert.Equal(
            Rac1MobyUidPersistenceBits.Clear,
            freshVeldinLoad.QueryUid(source));
        Assert.Equal(
            Rac1MobyUidPersistenceBits.Clear,
            levelTwo.QueryUid(source));
    }

    [Fact]
    public void UidPersistenceRequiresAuthoredUidAndRac1Source()
    {
        var session = new Rac1MobyPersistenceSession(levelId: 0);

        Assert.Throws<InvalidOperationException>(() =>
            session.QueryUid(Dynamic(500, 89, nativeUid: null)));
        Assert.Throws<ArgumentException>(() =>
            session.QueryUid(new RuntimeDynamicObject(
                "rac2",
                500,
                89,
                121,
                "moby:500",
                "moby:89",
                new RuntimeObjectTransform(new double[16]),
                Array.Empty<RuntimeObjectMesh>())));
    }

    private static RuntimeDynamicObject Dynamic(
        int nativeClassId,
        int instanceIndex,
        int? nativeUid) =>
        new(
            "rac1",
            nativeClassId,
            instanceIndex,
            nativeUid,
            $"moby:{nativeClassId}",
            $"moby:{instanceIndex}",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>());
}
