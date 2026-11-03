using OBP.Core;
using OBP.Core.Math;
using OBP.Game;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using Xunit;

namespace OBP.Tests;

public sealed class Rac3Class500WorldRuntimeTests
{
    [Fact]
    public void AdmittedClass500DispatchesNativeTransitionWithoutChangingAuthoredSource()
    {
        var source = Source(311);
        using var world = new Rac3Class500WorldRuntime(World(source));
        Assert.Equal(1, world.AuthoredCount);
        Assert.Equal(1, world.AdmittedCount);
        Assert.Equal(0, world.RejectedCount);
        Assert.Same(source, Assert.Single(world.AdmittedSources));
        Assert.True(world.IsActive(source));

        Assert.True(world.TryBreak(source, 1, 1, publish: true, out var result, out long sequence));
        Assert.Equal(1, sequence);
        Assert.NotNull(result);
        Assert.Equal(Rac3Class500PostBreakRoute.Deactivate, result.PostBreakRoute);
        Assert.Equal(RuntimeEntityPresence.Inactive, result.EntityState.Presentation.Presence);
        Assert.False(world.IsActive(source));
        Assert.False(world.TryBreak(source, 1, 1, publish: true, out _, out _));
        Assert.Equal(311, source.InstanceIndex);
    }

    [Fact]
    public void SharedWeaponDispatchDoesNotPublishOrGrantUnknownDamage()
    {
        var source = Source(311);
        using var world = new Rac3Class500WorldRuntime(World(source));
        Assert.False(world.TryBreak(source, 1, 0, publish: false, out _, out long sequence));
        Assert.Equal(0, sequence);
        Assert.True(world.IsActive(source));

        Assert.True(world.TryBreak(source, 1, 1, publish: false, out var result, out sequence));
        Assert.Equal(0, sequence);
        Assert.Equal(RuntimeEntityPresence.Inactive, result!.EntityState.Presentation.Presence);
    }

    [Fact]
    public void AuthoredAdmissionRejectsUnrecoveredRoutesAndUntrustedCopies()
    {
        var valid = Source(311);
        var unsupported = Source(312, pvarB0: 1);
        var missingPayload = Source(313) with { NativePayloads = [] };
        using var world = new Rac3Class500WorldRuntime(World(valid, unsupported, missingPayload));

        Assert.Equal(3, world.AuthoredCount);
        Assert.Equal(1, world.AdmittedCount);
        Assert.Equal(2, world.RejectedCount);
        Assert.False(world.TryBreak(unsupported, 1, 1, publish: false, out _, out _));
        Assert.False(world.TryBreak(missingPayload, 1, 1, publish: false, out _, out _));
        Assert.False(world.TryBreak(valid with { }, 1, 1, publish: false, out _, out _));
        Assert.True(world.IsActive(valid));
    }

    [Fact]
    public void RetiredLeaseCannotModifyCurrentLeaseAndDisposalIsIdempotent()
    {
        var original = Source(311);
        var replacement = Source(311);
        using var older = new Rac3Class500WorldRuntime(World(original));
        using var newer = new Rac3Class500WorldRuntime(World(replacement));

        older.Dispose();
        older.Dispose();
        Assert.False(older.Active);
        Assert.Empty(older.AdmittedSources);
        Assert.False(older.IsActive(original));
        Assert.False(older.TryBreak(original, 1, 1, publish: true, out _, out _));
        Assert.True(newer.IsActive(replacement));
        Assert.True(newer.TryBreak(replacement, 1, 1, publish: true, out _, out _));
    }

    [Fact]
    public void WrongSourceOrBuildDoesNotCreateNativeOwner()
    {
        Assert.Throws<ArgumentException>(() =>
            new Rac3Class500WorldRuntime(World(Source(311)) with { Game = ObpSourceGame.Rac2 }));
        Assert.Throws<ArgumentException>(() =>
            new Rac3Class500WorldRuntime(World(Source(311)) with { BuildId = "unknown" }));
    }

    private static RuntimeDynamicObject Source(int index, uint pvarB0 = 0) =>
        Rac3Class500DestructibleTests.Class500(index, 352 + index, 44, pvarB0, 0);

    private static RuntimeWorld World(params RuntimeDynamicObject[] actors) =>
        new(ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, 1, null, null,
            [], [], 0, [], new ObpBounds(Vec3.Zero, Vec3.Zero), null, null,
            DynamicObjects: actors);
}
