using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1AmoeboidFamilySessionTests
{
    [Fact]
    public void SplitUsesTwoFreeChildrenFromSameOriginalGroupWithStableIdsAndInheritedHealth()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var family = new Rac1AmoeboidFamilySession(runtime);
        Register(family, 10, 572, 6, direct: true);
        Register(family, 11, 572, 7, direct: true);
        Register(family, 20, 865, 6);
        Register(family, 21, 865, 6);
        Register(family, 22, 865, 7);
        Register(family, 30, 866, 6);
        Register(family, 31, 866, 6);
        Register(family, 32, 866, 6);
        Register(family, 33, 866, 6);
        Assert.Equal(9, family.RegisteredCount);
        Assert.Equal(2, family.ActiveCount);
        Assert.Equal(7, family.DormantCount);
        Assert.False(runtime.CanDispatchDamage(Wrench(20, 865)));

        var first = runtime.DispatchDamage<Rac1AmoeboidDamageResult>(Wrench(10, 572));
        Assert.Equal(new[] { 20, 21 }, first.Children.Select(c => c.InstanceIndex).ToArray());
        Assert.All(first.Children, child =>
        {
            Assert.Equal(865, child.ClassId);
            Assert.Equal(6, child.Group);
            Assert.Equal(2, child.Health);
            Assert.Equal(Rac1AmoeboidFamilySession.KnockbackState, child.NativeState);
            Assert.Equal(family.Get(10).Position, child.Position);
        });
        Assert.False(runtime.CanDispatchDamage(Wrench(10, 572)));
        Assert.Equal(3, family.ActiveCount);
        Assert.Equal(5, family.DormantCount);
        Assert.Equal(12, family.Get(22).NativeState); // Group 7 untouched

        // Damage both medium children. They must exhaust separate source slots.
        var second = runtime.DispatchDamage<Rac1AmoeboidDamageResult>(Wrench(20, 865));
        var third = runtime.DispatchDamage<Rac1AmoeboidDamageResult>(Wrench(21, 865));
        Assert.Equal(new[] { 30, 31 }, second.Children.Select(c => c.InstanceIndex).ToArray());
        Assert.Equal(new[] { 32, 33 }, third.Children.Select(c => c.InstanceIndex).ToArray());
        Assert.Equal(5, family.ActiveCount); // original group-7 large + four small
        Assert.Equal(1, family.DormantCount);
        Assert.All(second.Children.Concat(third.Children), x => Assert.Equal(1, x.Health));

        var last = runtime.DispatchDamage<Rac1AmoeboidDamageResult>(Wrench(30, 866));
        Assert.True(last.Depleted);
        Assert.Empty(last.Children);
        Assert.False(runtime.CanDispatchDamage(Wrench(30, 866)));
        Assert.Equal(4, family.ActiveCount);
        Assert.Equal(9, runtime.RegisteredCount); // zero fake cloned Mobys
    }

    [Fact]
    public void GatedActorsRemainNonInteractiveUntilOriginalVolumeAdmitsHero()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var family = new Rac1AmoeboidFamilySession(runtime);
        Register(family, 20, 866, 4, volume: 44);
        Assert.Equal(0, family.ActiveCount);
        Assert.Equal(1, family.GatedCount);
        Assert.False(runtime.CanDispatchDamage(Wrench(20, 866)));
        Assert.Empty(family.ActivateGates(Vector3.Zero, (volume, _) => false));
        var admitted = Assert.Single(family.ActivateGates(Vector3.Zero, (volume, _) => volume == 44));
        Assert.Equal(1, admitted.NativeState);
        Assert.Equal(20, admitted.Position.Z);
        Assert.True(runtime.CanDispatchDamage(Wrench(20, 866)));
        Assert.Empty(family.ActivateGates(Vector3.Zero, (volume, _) => true));
    }

    [Fact]
    public void RecoveredLungeProducesOneOwnerBackedPlayerHitAndReturnsToPursuit()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var family = new Rac1AmoeboidFamilySession(runtime);
        Register(family, 10, 572, 6, direct: true);
        var target = new Vector3(1.2f, 0, 0);
        Assert.Equal(2, family.Step(10, Vector3.Zero, target).NativeState);
        Assert.Equal(15, family.Step(10, Vector3.Zero, target).NativeState);
        Assert.Equal(6, family.Step(10, Vector3.Zero, target).NativeState);
        Assert.False(family.Step(10, Vector3.Zero, target, targetInContact: true,
            animationPosition: 15.4f).Attack);
        var attack = family.Step(10, Vector3.Zero, target, targetInContact: true,
            animationPosition: 15.6f);
        Assert.True(attack.Attack);
        Assert.False(family.Step(10, Vector3.Zero, target, targetInContact: true,
            animationPosition: 15.7f).Attack);
        var envelope = Rac1AmoeboidAttack.ToPlayer(572, 10);
        Assert.True(Rac1AmoeboidAttack.IsNativePlayerAttack(envelope));
        Assert.Equal(0x10001u, envelope.NativeDamageFlags);
        Assert.Equal(Rac1GameplayEntityRef.Moby(new(572, 10)), envelope.Owner!.Value.RuntimeEntity);
        var ratchet = new OBP.RAC1.Player.Rac1RatchetNanotechSession();
        Assert.Equal(3, ratchet.ApplyDamage(envelope).Nanotech);
        Assert.Equal(7, family.Step(10, Vector3.Zero, target,
            animationFinished: true).NativeState);
        Assert.Equal(4, family.Step(10, Vector3.Zero, target,
            animationFinished: true).NativeState);

        Assert.False(Rac1AmoeboidAttack.IsNativePlayerAttack(new Rac1GameplayDamageEvent(
            envelope.Source, envelope.Target, 1, envelope.NativeDamageFlags)));
    }

    [Fact]
    public void ReloadResetsNativeReservePoolAndPreservesAuthoredGroup()
    {
        Rac1AmoeboidFamilySession Fresh()
        {
            var next = new Rac1AmoeboidFamilySession(new Rac1MobyRuntimeSession());
            Register(next, 10, 572, 42, direct: true);
            Register(next, 20, 865, 42);
            Register(next, 21, 865, 42);
            return next;
        }
        var first = Fresh();
        var runtimeSource = first.Get(10);
        Assert.True(runtimeSource.Active);
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1AmoeboidFamilySession(runtime);
        Register(session, 10, 572, 42, direct: true);
        Register(session, 20, 865, 42);
        Register(session, 21, 865, 42);
        Assert.Equal(2, runtime.DispatchDamage<Rac1AmoeboidDamageResult>(Wrench(10, 572)).Children.Count);
        var reload = Fresh();
        Assert.Equal(1, reload.ActiveCount);
        Assert.Equal(2, reload.DormantCount);
        Assert.Equal(42, reload.Get(20).Group);
    }

    [SkippableFact]
    public void EveryRetailFamilyGroupAndPopulationResolvesWithoutCrossGroupCloning()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        int total = 0, active = 0, gated = 0, dormant = 0;
        foreach (int levelId in new[] { 1, 5, 11 })
        {
            var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == levelId);
            var instances = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level)).MobyInstances;
            var session = new Rac1AmoeboidFamilySession(new Rac1MobyRuntimeSession());
            foreach (var obj in instances.Where(x => Rac1AmoeboidAuthored.IsFamily(x.OClass)))
            {
                int group = Rac1AmoeboidAuthored.ReadGroup(obj.RawRecord);
                var facts = Rac1AmoeboidAuthored.Read(obj.OClass, obj.PVar!);
                var source = Source(obj.Index, obj.OClass, facts, group, obj.PVar);
                var current = session.Register(source, RuntimeEntityState.FromAuthored(source), facts, group,
                    new(obj.Position.X, obj.Position.Y, obj.Position.Z), obj.Rotation.Z);
                if (current.Active) active++;
                else if (current.NativeState == 12) dormant++;
                else if (current.NativeState == 13) gated++;
                total++;
            }
            int expected = levelId switch { 1 => 35, 5 => 108, 11 => 84, _ => 0 };
            Assert.Equal(expected, session.RegisteredCount);
            var expectedActive = levelId switch { 1 => 5, 5 => 20, 11 => 12, _ => 0 };
            var expectedGated = levelId == 5 ? 4 : 0;
            Assert.Equal(expectedActive, session.ActiveCount);
            Assert.Equal(expectedGated, session.GatedCount);
            Assert.Equal(expected - expectedActive - expectedGated, session.DormantCount);
        }
        Assert.Equal(227, total);
        Assert.Equal(37, active);
        Assert.Equal(4, gated);
        Assert.Equal(186, dormant);
    }

    private static Rac1AmoeboidFrame Register(Rac1AmoeboidFamilySession family, int index, int cls,
        int group, bool direct = false, int volume = -1)
    {
        var pvar = new byte[Rac1AmoeboidAuthored.PVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x230), direct ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x25c), volume);
        var facts = Rac1AmoeboidAuthored.Read(cls, pvar);
        var source = Source(index, cls, facts, group, pvar);
        return family.Register(source, RuntimeEntityState.FromAuthored(source), facts, group, Vector3.Zero, 0);
    }

    private static RuntimeDynamicObject Source(int index, int cls, Rac1AmoeboidSpawnFacts facts,
        int group, byte[]? pvar)
    {
        pvar ??= new byte[Rac1AmoeboidAuthored.PVarSize];
        return new("rac1", cls, index, null, "amoeboid", "amoeboid",
            new RuntimeObjectTransform(new double[16]), [],
            NativePayloads: [new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, pvar)]);
    }

    private static Rac1GameplayDamageEvent Wrench(int index, int cls) =>
        new(Rac1GameplayEntityRef.PlayerWrench, Rac1GameplayEntityRef.Moby(new(cls, index)),
            1, Rac1WrenchAttackProfile.NativeDamageFlags,
            owner: Rac1GameplayDamageOwner.FromEntity(Rac1GameplayEntityRef.Player));
}
