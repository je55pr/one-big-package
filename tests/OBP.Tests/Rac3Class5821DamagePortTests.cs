using System.Buffers.Binary;
using System.Numerics;
using OBP.Core;
using OBP.Fusion;
using OBP.Game;
using OBP.RAC1.Gameplay;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

/// <summary>Damage-only multi-instance regression. Does not claim native motion or one-eyed variant identity.</summary>
public sealed class Rac3Class5821DamagePortTests
{
    private static readonly SourceWeaponKey Pyrocitor =
        new(ObpSourceGame.Rac1, (int)Rac1WeaponId.Pyrocitor);

    private static RuntimeDynamicObject Source(int instance, short lifetime = 1)
    {
        var original = Rac3Class5821ActorTests.Class5821(instance, lifetime);
        return original with
        {
            NativePayloads = [.. original.NativePayloads!,
                new RuntimeOpaquePayload(Rac3MobyRuntimeSession.InstancePayloadFormat,
                    new byte[Rac3MobyPlacement.NativeRecordBytes])]
        };
    }

    private static ActorRuntimeSet Encounter(IReadOnlyList<RuntimeDynamicObject> originals)
    {
        var set = new ActorRuntimeSet("uya-damage-stage", 7);
        set.RegisterWeapon(Pyrocitor, "rac1-ntscu", () => true);
        foreach (var original in originals)
        {
            var key = set.Allocate(ObpSourceGame.Rac3, Rac3Class5821Actor.NativeClassId);
            var clone = original with { InstanceIndex = key.Target.InstanceIndex };
            set.Register(new DamageActor(key, new Rac3Class5821DamagePort(clone)));
        }
        return set;
    }

    [Fact]
    public void EightReceiversAreIndependentAndResetFromUnchangedFixturePayloads()
    {
        RuntimeDynamicObject[] originals = Enumerable.Range(430, 8).Select(i => Source(i)).ToArray();
        using (var actors = Encounter(originals))
        {
            Assert.Equal(8, actors.Count);
            var ordered = actors.Ordered.ToArray();
            actors.Step(Vector3.Zero, true);
            actors.Step(Vector3.Zero, false);
            Assert.Equal(2L, actors.TickCount);
            Assert.All(ordered, actor => Assert.Equal(1d, actor.Observe().Health));

            for (int i = 0; i < ordered.Length; ++i)
            {
                var actor = ordered[i];
                var key = actor.Registration.Instance;
                var shot = new WeaponDamageIntent(new(Pyrocitor, 7, i + 1),
                    key.Target, .25f, 0xFFFFFFFF, "flame");
                Assert.True(actors.Apply(key, shot).Applied);
                Assert.Equal(0d, actor.Observe().Health); // Native <=1 authored lifetime clamp.
                Assert.False(actor.Observe().Alive);
                Assert.False(actors.Apply(key, shot).Applied);
                Assert.All(ordered.Skip(i + 1), other => Assert.Equal(1d, other.Observe().Health));
            }
            Assert.All(ordered, actor => Assert.False(actor.Observe().Retired));
        }

        Assert.All(originals, source => Assert.Equal(1f,
            BinaryPrimitives.ReadSingleLittleEndian(source.NativePayloads![0].Data
                .AsSpan(Rac3Class5821Actor.RuntimeLifetimeOffset, sizeof(float)))));
        using var replay = Encounter(originals);
        Assert.Equal(8, replay.Count);
        Assert.All(replay.Ordered, actor => Assert.Equal(1d, actor.Observe().Health));
    }

    [Fact]
    public void WrongClassProfileTargetAndMissingPlacementFailClosed()
    {
        var source = Source(430);
        Assert.Throws<InvalidDataException>(() =>
            new Rac3Class5821DamagePort(source with { NativeClassId = 6306 }));
        Assert.Throws<InvalidDataException>(() => new Rac3Class5821DamagePort(Source(431, 0)));
        Assert.Throws<InvalidDataException>(() => new Rac3Class5821DamagePort(
            source with { NativePayloads = [source.NativePayloads![0]] }));

        var port = new Rac3Class5821DamagePort(source);
        var wrongTarget = new WeaponDamageIntent(new(Pyrocitor, 1, 1),
            new(ObpSourceGame.Rac2, source.NativeClassId, source.InstanceIndex),
            1f, 0, "flame");
        Assert.False(port.Apply(wrongTarget).Applied);
        Assert.Equal(1f, port.Lifetime);
        Assert.True(port.Apply(wrongTarget with
        {
            Target =
            new(ObpSourceGame.Rac3, source.NativeClassId, source.InstanceIndex)
        }).Applied);
        Assert.Equal(0f, port.Lifetime);
        Assert.False(port.Apply(wrongTarget with
        {
            Target =
            new(ObpSourceGame.Rac3, source.NativeClassId, source.InstanceIndex)
        }).Applied);
    }

    private sealed class DamageActor(ActorInstanceKey key, Rac3Class5821DamagePort native) : IRegisteredActor
    {
        public ActorRegistration Registration { get; } = new(
            key, new(ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, "table1-class5821"),
            "rac3/class5821-damage-only/v1", "uya/table1-class5821-damage-only/v1",
            [new("damage", CapabilityAdmission.Ready)]);

        public ActorObservation Observe() => new(Vector3.Zero,
            native.Lifetime > 0f, false, native.Lifetime,
            Rac3Class5821Actor.NativeDormantState, null);
        // Damage-only test actor: no simulated movement, native tick, or owned resources.
        public void Tick(ActorTickFacts facts) { }
        public ActorHitResult Apply(WeaponDamageIntent damage) => native.Apply(damage);
        public void Dispose() { }
    }
}
