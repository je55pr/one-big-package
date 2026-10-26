using OBP.Core;
using System.Numerics;
using OBP.Fusion;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class ActorRuntimeSetTests
{
    private static readonly SourceWeaponKey Lancer = new(ObpSourceGame.Rac2, 30);
    private static readonly SourceWeaponKey Cannon = new(ObpSourceGame.Rac3, 43);
    private static RuntimeObjectTransform Identity => new([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
    private static ActorRuntimeSet Create()
    {
        var set = new ActorRuntimeSet("arena", 7);
        set.RegisterWeapon(Lancer, "gc-build", () => true); set.RegisterWeapon(Cannon, "uya-build", () => true);
        return set;
    }

    private sealed class Actor(ActorInstanceKey key, List<string>? order = null) : IRegisteredActor
    {
        public ActorRegistration Registration { get; } = new(key, new(key.Target.SourceGame, "build", "original-asset"), "native-adapter", "native-context", [new("damage", CapabilityAdmission.Ready)]);
        public double Health { get; private set; } = 3;
        public int Hits { get; private set; }
        public bool Disposed { get; private set; }
        public bool ThrowOnDispose { get; init; }
        public int DisposeCount { get; private set; }
        public ActorObservation Observe() => new(Vector3.Zero, Health > 0 && !Disposed, Disposed, Health, 12, null);
        public void Tick(ActorTickFacts facts) => order?.Add(SourceIdentityWire.FormatSource(key.Target.SourceGame) + "/" + key.Target.InstanceIndex + "/" + facts.Tick);
        public ActorHitResult Apply(WeaponDamageIntent damage)
        { Hits++; Health -= damage.SourceDamage; return new(true, Health <= 0); }
        public void Dispose()
        {
            Disposed = true; DisposeCount++;
            if (ThrowOnDispose) throw new IOException("retirement fixture");
        }
    }

    [Fact]
    public void EqualNativeNumbersRemainSeparateAcrossSourcesAndDuplicateRegistrationsFail()
    {
        using var set = Create();
        var rac2 = new Actor(new("arena", 7, new(ObpSourceGame.Rac2, 500, 1)));
        var rac3 = new Actor(new("arena", 7, new(ObpSourceGame.Rac3, 500, 1)));
        set.Register(rac2); set.Register(rac3);
        Assert.Throws<ArgumentException>(() => set.Register(rac2));
        Assert.Equal(2, set.Count);
        Assert.Same(rac2, set.Find(rac2.Registration.Instance.Target));
        Assert.Same(rac3, set.Find(rac3.Registration.Instance.Target));
    }

    [Fact]
    public void NativeTickOrderIsStableAndASharedSourceGroupRunsOnce()
    {
        using var set = Create(); var order = new List<string>(); int groups = 0;
        set.RegisterUpdateGroup("rac1/native", _ => groups++);
        set.Register(new Actor(new("arena", 7, new(ObpSourceGame.Rac3, 500, 2)), order));
        set.Register(new Actor(new("arena", 7, new(ObpSourceGame.Rac1, 749, 1)), order));
        set.Register(new Actor(new("arena", 7, new(ObpSourceGame.Rac2, 2827, 3)), order));
        set.Step(Vector3.Zero, true); set.Step(Vector3.Zero, false);
        Assert.Equal(2, groups);
        Assert.Equal(new[] { "rac1/1/1", "rac2/3/1", "rac3/2/1", "rac1/1/2", "rac2/3/2", "rac3/2/2" }, order);
        var group = new ActorUpdateGroup(_ => groups++); group.Step(new(10, Vector3.Zero, true)); group.Step(new(10, Vector3.Zero, true));
        Assert.Equal(3, groups);
        Assert.Throws<InvalidOperationException>(() => group.Step(new(9, Vector3.Zero, true)));
    }

    [Fact]
    public void NativeAmountsPassThroughAndContactDeduplicationDoesNotAliasWeapons()
    {
        using var set = Create(); var actor = new Actor(set.Allocate(ObpSourceGame.Rac1, 749)); set.Register(actor);
        var key = actor.Registration.Instance;
        var shot = new WeaponDamageIntent(new(Lancer, 1, 1), key.Target, .5f, 0x1234, "projectile");
        Assert.True(set.Apply(key, shot).Applied); Assert.Equal(2.5, actor.Health);
        Assert.False(set.Apply(key, shot).Applied); Assert.Equal(1, actor.Hits);
        Assert.True(set.Apply(key, shot with { Shot = new(Cannon, 1, 1) }).Applied);
        Assert.Equal(2, actor.Health);
        Assert.False(set.Apply(key, shot with { SourceDamage = float.NaN }).Applied);
    }

    [Fact]
    public void RetiredLeaseAndActorCannotReceiveLateHitsAndResetAllocatesANewTarget()
    {
        var set = Create(); var first = new Actor(set.Allocate(ObpSourceGame.Rac3, 500)); set.Register(first);
        var key = first.Registration.Instance;
        var damage = new WeaponDamageIntent(new(Lancer, 1, 1), key.Target, .5f, 1, "hit");
        Assert.False(set.Apply(key with { Generation = 6 }, damage).Applied);
        Assert.False(set.Apply(key with { WorldInstanceId = "previous" }, damage).Applied);
        set.Retire(key); Assert.True(first.Disposed); Assert.Null(set.Find(key.Target));
        Assert.Throws<ArgumentException>(() => set.Register(new Actor(key)));
        var second = new Actor(set.Allocate(ObpSourceGame.Rac3, 500)); set.Register(second);
        Assert.NotEqual(key, second.Registration.Instance);
        Assert.False(set.Apply(key, damage).Applied); Assert.Equal(0, second.Hits);
        set.Dispose(); Assert.True(second.Disposed);
        Assert.False(set.Apply(second.Registration.Instance, damage with { Target = second.Registration.Instance.Target }).Applied);
    }

    [Fact]
    public void MissingUnregisteredWeaponsAndWrongLeaseRegistrationFailExplicitly()
    {
        using var set = Create(); var actor = new Actor(set.Allocate(ObpSourceGame.Rac2, 2827)); set.Register(actor);
        var key = actor.Registration.Instance;
        Assert.False(set.Apply(key, new(new(new(ObpSourceGame.Rac2, 999), 1, 1), key.Target, 1, 0, "hit")).Applied);
        set.RegisterWeapon(new(ObpSourceGame.Rac1, 10), "r1-build", () => false);
        Assert.False(set.Apply(key, new(new(new(ObpSourceGame.Rac1, 10), 1, 1), key.Target, 1, 0, "hit")).Applied);
        Assert.Throws<ArgumentException>(() => set.Register(new Actor(key with { Generation = 8 })));
    }

    [Fact]
    public void FailedAdmissionDisposesOnlyTheUnregisteredConstruction()
    {
        using var set = Create(); var existing = new Actor(set.Allocate(ObpSourceGame.Rac2, 2827));
        set.Register(existing);
        var duplicate = new Actor(existing.Registration.Instance);
        Assert.Throws<ArgumentException>(() => set.CreateAndRegister(() => duplicate));
        Assert.Equal(1, duplicate.DisposeCount); Assert.False(existing.Disposed);
        Assert.Equal(1, set.Count);
        Assert.Throws<IOException>(() => set.CreateAndRegister<Actor>(() => throw new IOException("construction fixture")));
        Assert.Equal(1, set.Count);
    }

    [Fact]
    public void OneBrokenRetirementCannotLeakOtherActorsOrUpdateGroups()
    {
        var set = Create(); int ticks = 0;
        set.RegisterUpdateGroup("rac1/test", _ => ticks++);
        var broken = new Actor(set.Allocate(ObpSourceGame.Rac1, 749)) { ThrowOnDispose = true };
        var other = new Actor(set.Allocate(ObpSourceGame.Rac2, 2827));
        set.Register(broken); set.Register(other); set.Step(Vector3.Zero, true);
        set.Dispose();
        Assert.IsType<IOException>(Assert.Single(set.RetirementFailures));
        Assert.Equal(1, broken.DisposeCount); Assert.Equal(1, other.DisposeCount); Assert.Equal(0, set.Count);
        set.Dispose(); Assert.Equal(1, broken.DisposeCount); Assert.Equal(1, other.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => set.Step(Vector3.Zero, true)); Assert.Equal(1, ticks);
        using var next = Create(); next.RegisterUpdateGroup("rac1/test", _ => ticks++);
        next.Step(Vector3.Zero, true); Assert.Equal(2, ticks);
    }

    [Fact]
    public void PlacementRequiresVersionSourceContextAndRigidFiniteTransform()
    {
        var placement = new ActorPlacement("toad", new(ObpSourceGame.Rac1, "build", "LEVEL0/moby749"), 0, 749, null, Identity, "native-adapter", "source-context", 5, "arena");
        placement.Validate();
        Assert.Throws<ArgumentException>(() => (placement with { Version = 2 }).Validate());
        Assert.Throws<ArgumentException>(() => (placement with { NativeContextId = "" }).Validate());
        Assert.Throws<ArgumentException>(() => (placement with { Source = placement.Source with { Game = (ObpSourceGame)(-1) } }).Validate());
        double[] roundTrip = (double[])Identity.Matrix.Clone(); roundTrip[3] = 1e-10; roundTrip[15] += 1e-10;
        (placement with { Transform = new(roundTrip) }).Validate();
        roundTrip[3] = .01;
        Assert.Throws<ArgumentException>(() => (placement with { Transform = new(roundTrip) }).Validate());
        double[] scale = (double[])Identity.Matrix.Clone(); scale[0] = 2;
        Assert.Throws<ArgumentException>(() => (placement with { Transform = new(scale) }).Validate());
        scale[0] = double.NaN;
        Assert.Throws<ArgumentException>(() => (placement with { Transform = new(scale) }).Validate());
    }
}
