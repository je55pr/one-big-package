using Microsoft.CodeAnalysis;

namespace OBP.Tests;

/// <summary>
/// #367/#368: arsenal enemy selection and all additional-weapon damage use the
/// already world-scoped contact, Moby runtime, enemy and breakable authorities.
/// </summary>
public sealed class Rac1WeaponDamageWorldOwnershipTests
{
    private static string Script(string file) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", file));

    [Fact]
    public void RootTargetBlastAndDamageMethodsAreRetiredIntoWorldOwners()
    {
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (var member in new[] { "FindArsenalTarget", "ApplyArsenalBlast", "ApplyRac1AdditionalDamage" })
            Assert.Empty(root.GetMembers(member));

        string module = Script("Rac1WorldModule.cs");
        string contact = Script("Rac1WeaponContactHost.cs");
        string damage = Script("Rac1WeaponDamageHost.cs");
        string application = Script("OBPGame.cs");
        string flames = Script("Rac1PyrocitorFlameHost.cs");

        Assert.Contains("WeaponDamage = new(Context, Enemies, Breakables, WrenchDamage, Weapons.Contacts)", module, StringComparison.Ordinal);
        Assert.Contains("internal Rac1WeaponDamageHost WeaponDamage { get; }", module, StringComparison.Ordinal);
        Assert.Contains("if (retiring) WeaponDamage.Dispose()", module, StringComparison.Ordinal);
        Assert.Contains("WeaponDamage.Dispose()", module, StringComparison.Ordinal);
        Assert.Contains("internal RuntimeWorldScene.DynamicObjectNode? FindArsenalTarget(", contact, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Weapons.Contacts.FindArsenalTarget(", application, StringComparison.Ordinal);
        Assert.Contains("Rac1World.WeaponDamage.ApplyBlast(weapon, position, radius, damage, flags)", application, StringComparison.Ordinal);
        Assert.Contains("Rac1World.WeaponDamage.Apply(node, damage)", application, StringComparison.Ordinal);
        Assert.Contains("ports.ApplyDamage(victim, Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Pyrocitor", flames, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", damage, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1WeaponDamageHost.cs.uid")));
    }

    [Fact]
    public void ArsenalTargetSearchPreservesEnemyOnlyCandidatesAndExactFilterSemantics()
    {
        string contact = Script("Rac1WeaponContactHost.cs");
        string application = Script("OBPGame.cs");
        Assert.Contains("Func<IEnumerable<RuntimeWorldScene.DynamicObjectNode>> ArsenalTargets", contact, StringComparison.Ordinal);
        Assert.Contains("Func<int, bool> IsMorphed", contact, StringComparison.Ordinal);
        Assert.Contains("() => Rac1World.Enemies.Targets,", application, StringComparison.Ordinal);
        Assert.Contains("id => Rac1World.Weapons.Morph.Contains(id)", application, StringComparison.Ordinal);
        Assert.Contains("var skip = excluded?.ToHashSet()", contact, StringComparison.Ordinal);
        Assert.Contains("targets.ArsenalTargets().Where(node => IsAimTarget(node) && !targets.IsMorphed(node.Source.InstanceIndex)", contact, StringComparison.Ordinal);
        Assert.Contains("skip?.Contains(node.Source.InstanceIndex) != true", contact, StringComparison.Ordinal);
        Assert.Contains("node.Root.GlobalPosition + Vector3.Up * .5f - origin", contact, StringComparison.Ordinal);
        Assert.Contains("delta.LengthSquared() < range * range && delta.LengthSquared() > .00001f", contact, StringComparison.Ordinal);
        Assert.Contains("delta.Normalized().Dot(forward) > cone", contact, StringComparison.Ordinal);
        Assert.Contains("HasLineOfSight(origin, node.Root.GlobalPosition + Vector3.Up * .5f)", contact, StringComparison.Ordinal);
        Assert.Contains("OrderBy(node => node.Root.GlobalPosition.DistanceSquaredTo(origin)).FirstOrDefault()", contact, StringComparison.Ordinal);
        Assert.Contains("if (_disposed) return null;", contact, StringComparison.Ordinal);
        Assert.DoesNotContain("targets.Targets().Where(node => IsAimTarget(node) && !targets.IsMorphed", contact, StringComparison.Ordinal);
    }

    [Fact]
    public void AdditionalDamagePreservesNativeAdmissionDispatchAndPublishOrder()
    {
        string host = Script("Rac1WeaponDamageHost.cs");
        Assert.Contains("enemies.Frogs.ApplyRac1QueenWeaponDamage(target, damage)", host, StringComparison.Ordinal);
        Assert.Contains("if (!context.MobyRuntime.CanDispatchDamage(damage)) return false", host, StringComparison.Ordinal);
        Assert.Contains("Rac1BoltCrate.IsCrateClass(target.Source.NativeClassId)", host, StringComparison.Ordinal);
        Assert.Contains("context.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(damage)", host, StringComparison.Ordinal);
        Assert.Contains("wrenchDamage.CompleteCrateDamage(target,", host, StringComparison.Ordinal);
        Assert.Contains("breakables.TryTriggerExplosiveDamage(target, damage)", host, StringComparison.Ordinal);
        Assert.Contains("enemies.ApplyRac1EnemyWeaponDamage(target, damage)", host, StringComparison.Ordinal);
        Assert.Contains("if (applied) context.DamageTransport.Publish(damage)", host, StringComparison.Ordinal);
        int gate = host.IndexOf("context.MobyRuntime.CanDispatchDamage(damage)", StringComparison.Ordinal);
        int dispatch = host.IndexOf("context.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>", StringComparison.Ordinal);
        int publish = host.IndexOf("context.DamageTransport.Publish(damage)", StringComparison.Ordinal);
        Assert.True(gate >= 0 && dispatch > gate && publish > dispatch);
    }

    [Fact]
    public void BlastSelectionPreservesGeometryBoundsOcclusionAndSourceDamageDescriptor()
    {
        string host = Script("Rac1WeaponDamageHost.cs");
        Assert.Contains("contacts.Geometry.ToArray()", host, StringComparison.Ordinal);
        Assert.Contains("position.Clamp(bounds.Position, bounds.End)", host, StringComparison.Ordinal);
        Assert.Contains("!enemies.IsRac1GameplayActive(node) || position.DistanceTo(closest) > radius", host, StringComparison.Ordinal);
        Assert.Contains("contacts.HasLineOfSight(position + Vector3.Up * .1f, bounds.GetCenter())", host, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponProfiles.DirectDamage(weapon,", host, StringComparison.Ordinal);
        Assert.Contains("new(node.Source.NativeClassId, node.Source.InstanceIndex), damage, flags", host, StringComparison.Ordinal);
        Assert.Contains("Apply(node, Rac1WeaponProfiles.DirectDamage(weapon,", host, StringComparison.Ordinal);
        Assert.Contains("if (_disposed) return;", host, StringComparison.Ordinal);
        Assert.Contains("if (_disposed) return false;", host, StringComparison.Ordinal);
        Assert.Contains("public void Dispose() => _disposed = true;", host, StringComparison.Ordinal);
    }
}
