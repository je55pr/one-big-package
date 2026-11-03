using Microsoft.CodeAnalysis;

namespace OBP.Tests;

/// <summary>
/// #367/#368: native Pyrocitor particle lifetime remains in presentation,
/// while the per-world flame contact lease owns tracked slots and victim
/// repeat admission. No new player ammo or source damage controller exists.
/// </summary>
public sealed class Rac1PyrocitorFlameWorldOwnershipTests
{
    private static string Script(string file) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", file));

    [Fact]
    public void WorldOwnerRetiresRootFlameCollectionsAndContactMethod()
    {
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (var member in new[]
        {
            "_rac1NativeFlames", "_rac1FlameHitTicks", "HostedNativeFlame",
            "StepRac1NativeFlameContacts",
        })
            Assert.Empty(root.GetMembers(member));

        var host = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.Rac1WeaponHost")!;
        Assert.Equal("Rac1PyrocitorFlameHost",
            Assert.Single(host.GetMembers("Flames").OfType<IPropertySymbol>()).Type.Name);

        string weapons = Script("Rac1WeaponHost.cs");
        string module = Script("Rac1WorldModule.cs");
        string composition = Script("OBPGame.cs");
        string flame = Script("Rac1PyrocitorFlameHost.cs");

        Assert.Contains("Flames = new(Contacts, contacts.Flames)", weapons, StringComparison.Ordinal);
        Assert.Contains("Flames.Reset()", weapons, StringComparison.Ordinal);
        Assert.Contains("Flames.Dispose()", weapons, StringComparison.Ordinal);
        Assert.Contains("new(weaponTargets, contactPhysics, suckCannon, projectiles, flames)", module, StringComparison.Ordinal);
        Assert.Contains("new Rac1PyrocitorFlamePorts(", composition, StringComparison.Ordinal);
        Assert.Contains("() => Rac1World.Effects.EnsureParticles(),", composition, StringComparison.Ordinal);
        Assert.Contains("() => _equipmentPresentation.Explosions.Particles,", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", flame, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1ParticleSession(", flame, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1PyrocitorFlameHost.cs.uid")));
    }

    [Fact]
    public void EmissionRetainsNativeReachRayAndOneInFourTrackedSlotSelection()
    {
        string flame = Script("Rac1PyrocitorFlameHost.cs");
        string frame = Script("Rac1WeaponEffectsHost.cs");

        Assert.Contains("if (_disposed || !emitting || ports.EnsureParticles() is not { } particles) return", flame, StringComparison.Ordinal);
        Assert.Contains("Vector3 nozzle = ports.Muzzle(Rac1WeaponId.Pyrocitor)", flame, StringComparison.Ordinal);
        Assert.Contains("float reach = Rac1WeaponProfiles.PyrocitorReach", flame, StringComparison.Ordinal);
        Assert.Contains("nozzle + forward * (reach + .75f)", flame, StringComparison.Ordinal);
        Assert.Contains("ray.Exclude = contacts.RayExclusions()", flame, StringComparison.Ordinal);
        Assert.Contains("Math.Max(0, nozzle.DistanceTo(contact.AsVector3()) - .75f)", flame, StringComparison.Ordinal);
        Assert.Contains("particles.Pyrocitor(", flame, StringComparison.Ordinal);
        Assert.Contains("Rac1LootPresentation.ToEffectNative(nozzle)", flame, StringComparison.Ordinal);
        Assert.Contains("Rac1LootPresentation.ToEffectNative(forward), reach", flame, StringComparison.Ordinal);
        Assert.Contains("if ((ports.WorldTick() & 3) == 0 && created.Count > 2)", flame, StringComparison.Ordinal);
        Assert.Contains("if (_flames.Count == 15) _flames.RemoveAt(0)", flame, StringComparison.Ordinal);
        Assert.Contains("_flames.Add(new(created[2]))", flame, StringComparison.Ordinal);
        Assert.Contains("world.Weapons.StepWorld(ports.PyroEmitting())", frame, StringComparison.Ordinal);
        Assert.Contains("Flames.Emit(pyroEmitting)", Script("Rac1WeaponHost.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("particles.Pyrocitor(", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void ContactGateAndRepeatCooldownPreserveExistingAdmissionSequence()
    {
        string flame = Script("Rac1PyrocitorFlameHost.cs");

        string[] ordered =
        [
            "ports.CurrentParticles() is not { } particles",
            "_flames.ToArray()",
            "particles.IsLive(p) || p.Remaining < 2",
            "particles.FlameSecondary(p)",
            "Rac1LootPresentation.FromEffectNative(p.Position)",
            "float radius = p.Size * .5f",
            "contacts.Geometry",
            "ports.IsActive(victim) || flame.Victims.Contains(key)",
            "_victimHitTicks.GetValueOrDefault(key) > ports.WorldTick()",
            "bounds.Grow(radius).HasPoint(point)",
            "contacts.HasLineOfSight(point, victim.Root.GlobalPosition + Vector3.Up * .4f)",
            "ports.ApplyDamage(victim, Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Pyrocitor, key,",
            "flame.Victims.Add(key)",
            "_victimHitTicks[key] = ports.WorldTick() + Rac1WeaponReconstructionPolicy.FlameVictimRepeatTicks",
        ];
        int previous = -1;
        foreach (var piece in ordered)
        {
            int next = flame.IndexOf(piece, StringComparison.Ordinal);
            Assert.True(next > previous, "Pyrocitor contact order changed: " + piece);
            previous = next;
        }
        Assert.Contains("Rac1WeaponProfiles.PyrocitorDamage, Rac1WeaponProfiles.PyrocitorFlags", flame, StringComparison.Ordinal);
        Assert.Contains("if (_disposed || ports.CurrentParticles() is not { } particles) return", flame, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetPreservesPreviewCleanupAndWorldRetirementBoundaries()
    {
        string reset = Script("Rac1WeaponUsePresentationHost.cs");
        string frame = Script("Rac1WeaponEffectsHost.cs");
        string flame = Script("Rac1PyrocitorFlameHost.cs");
        string composition = Script("OBPGame.cs");

        string[] ordered =
        [
            "world.Weapons.Projectiles.RetireShotsAgentsMotes()",
            "world.Weapons.SuckCannon.Reset()",
            "world.Weapons.Flames.ClearCooldowns()",
            "world.Weapons.Flames.ClearTrackedFlames()",
            "Equipment.Explosions.Particles?.RemoveType(12)",
            "Equipment.Explosions.Particles?.RemoveType(2)",
            "world.Weapons.Projectiles.ResetFlashes()",
        ];
        int previous = -1;
        foreach (var piece in ordered)
        {
            int next = reset.IndexOf(piece, StringComparison.Ordinal);
            Assert.True(next > previous, "Weapon reset order changed: " + piece);
            previous = next;
        }

        Assert.Contains("world.Weapons.StepWorld(ports.PyroEmitting())", frame, StringComparison.Ordinal);
        string owner = Script("Rac1WeaponHost.cs");
        int emit = owner.IndexOf("Flames.Emit(pyroEmitting)", StringComparison.Ordinal);
        int motes = owner.IndexOf("Projectiles.StepMotes()", StringComparison.Ordinal);
        int contacts = owner.IndexOf("Flames.StepContacts()", StringComparison.Ordinal);
        int suck = owner.IndexOf("SuckCannon.Step()", StringComparison.Ordinal);
        Assert.True(emit >= 0 && emit < motes && motes < contacts && contacts < suck);
        Assert.Contains("ClearCooldowns()", flame, StringComparison.Ordinal);
        Assert.Contains("ClearTrackedFlames()", flame, StringComparison.Ordinal);
        Assert.Contains("Reset();", flame, StringComparison.Ordinal);
        Assert.Contains("_disposed = true", flame, StringComparison.Ordinal);
        Assert.Contains("node => Rac1World.Enemies.IsRac1GameplayActive(node)", composition, StringComparison.Ordinal);
        Assert.Contains("(node, damage) => Rac1World.WeaponDamage.Apply(node, damage)", composition, StringComparison.Ordinal);
    }
}
