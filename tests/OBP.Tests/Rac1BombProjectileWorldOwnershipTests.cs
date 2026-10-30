using Microsoft.CodeAnalysis;

namespace OBP.Tests;

/// <summary>
/// #368: R&C1 bomb launch, native flight, impact and blast dispatch share one
/// world lease while player use admission and the source damage authority stay
/// with their existing owners.
/// </summary>
public sealed class Rac1BombProjectileWorldOwnershipTests
{
    private static string Script(string file) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", file));

    [Fact]
    public void ProjectileMotionAndImpactCountAreOwnedByTheWorldInsteadOfRoot()
    {
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (var member in new[]
        {
            "_rac1BombImpactCount", "SpawnRac1BombProjectile", "ImpactRac1Bomb",
            "ResolveRac1BombBlast", "RetireBomb",
        })
            Assert.Empty(root.GetMembers(member));

        string module = Script("Rac1WorldModule.cs");
        string host = Script("Rac1BombProjectileHost.cs");
        string entry = Script("OBPGame.Rac1Projectiles.cs");
        string application = Script("OBPGame.cs");

        Assert.Contains("Bombs = new(Context, Presentation, Weapons, Enemies, Breakables, bomb)", module, StringComparison.Ordinal);
        Assert.Contains("Bombs.Reset()", module, StringComparison.Ordinal);
        Assert.Contains("Bombs.Dispose()", module, StringComparison.Ordinal);
        Assert.Contains("internal Rac1BombProjectileHost Bombs { get; }", module, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Bombs.Spawn(shot)", entry, StringComparison.Ordinal);
        Assert.Contains("Rac1Gameplay?.Bombs.StepPhysics()", entry, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Bombs.RetireAll()", application, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Bombs.ImpactCount", application, StringComparison.Ordinal);
        Assert.Contains("internal int ImpactCount", host, StringComparison.Ordinal);
        Assert.Contains("private bool _disposed;", host, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", host, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1BombProjectileHost.cs.uid")));
    }

    [Fact]
    public void NativeLaunchAndFlightPreserveFourStagesAndSweepPriority()
    {
        string host = Script("Rac1BombProjectileHost.cs");
        Assert.Contains("Rac1BombProjectileSession.LaunchOrigin(body, yaw)", host, StringComparison.Ordinal);
        Assert.Contains("Rac1BombProjectileSession.LaunchVelocity(origin, target, yaw)", host, StringComparison.Ordinal);
        Assert.Contains("new(ports.NextAngle(), ports.NextAngle(), ports.NextAngle())", host, StringComparison.Ordinal);
        Assert.Contains("Rac1HostedProjectile(node, motion, _weapons.ThrownAnimation.Generation)", host, StringComparison.Ordinal);
        Assert.Contains("ProbeBombWorldContact(clearanceStart", host, StringComparison.Ordinal);
        Assert.Contains("Rac1LootPresentation.FromEffectNative(motion.AdvanceFlight())", host, StringComparison.Ordinal);
        Assert.Contains("float closest = contact is { } hit ? from.DistanceTo(hit) : float.PositiveInfinity", host, StringComparison.Ordinal);
        Assert.Contains("point.DistanceTo(centre) <= .55f && from.DistanceTo(point) < closest", host, StringComparison.Ordinal);
        Assert.Contains("if (contact is not null || motion.TickFlightCountdown())", host, StringComparison.Ordinal);
        Assert.Contains("if (motion.NativeState == 2)", host, StringComparison.Ordinal);
        Assert.True(host.IndexOf("ResolveBlast(pair.Key, motion, projectile.Generation)", StringComparison.Ordinal)
            < host.IndexOf("motion.AdvanceBlast()", StringComparison.Ordinal));
        Assert.Contains("if (motion.Finished) Retire(pair.Key, projectile)", host, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeDamageAndHostEffectsRemainBehindExistingAuthorities()
    {
        string host = Script("Rac1BombProjectileHost.cs");
        string root = Script("OBPGame.cs");
        Assert.Contains("_breakables.ResolveNovalisBombBreakables(origin, motion.BlastRadius)", host, StringComparison.Ordinal);
        Assert.Contains("_context.BombGlove!.ResolveExplosionVolume(id, facts)", host, StringComparison.Ordinal);
        Assert.Contains("ports.ActorBlast(generation, id, origin, motion.BlastRadius)", host, StringComparison.Ordinal);
        Assert.Contains("_context.DamageTransport.Publish(damage)", host, StringComparison.Ordinal);
        Assert.Contains("_context.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(damage)", host, StringComparison.Ordinal);
        Assert.Contains("ports.CompleteCrate(", host, StringComparison.Ordinal);
        Assert.Contains("_breakables.TryTriggerExplosiveDamage(damage)", host, StringComparison.Ordinal);
        Assert.Contains("_enemies.ApplyRac1EnemyWeaponDamage(enemy.Node, damage)", host, StringComparison.Ordinal);
        Assert.Contains("ports.ImpactFx(position, normal, push)", host, StringComparison.Ordinal);
        Assert.Contains("ports.Player()!.GetWorld3D().DirectSpaceState.IntersectRay(ray)", host, StringComparison.Ordinal);
        Assert.Contains("ActorHost.Combat.ResolveBlast(FusionWeaponSession.BombGlove", root, StringComparison.Ordinal);
        Assert.Contains("CompleteRac1CrateDamage),", root, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1BombProjectileSession _", root, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingProjectilesRetiresTheirNativeIdsAndTheirPresentation()
    {
        string host = Script("Rac1BombProjectileHost.cs");
        Assert.Contains("foreach (var (id, projectile) in _presentation.Projectiles.ToArray())", host, StringComparison.Ordinal);
        Assert.Contains("_context.BombGlove?.CompleteProjectile(id)", host, StringComparison.Ordinal);
        Assert.Contains("projectile.Node.QueueFree()", host, StringComparison.Ordinal);
        Assert.Contains("_presentation.Projectiles.Remove(id)", host, StringComparison.Ordinal);
        Assert.Contains("if (_disposed) return;", host, StringComparison.Ordinal);
        Assert.Contains("RetireAll();", host, StringComparison.Ordinal);
        Assert.Contains("_impactCount = 0;", host, StringComparison.Ordinal);
    }
}
