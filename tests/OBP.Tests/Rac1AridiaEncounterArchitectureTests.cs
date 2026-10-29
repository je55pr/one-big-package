using Microsoft.CodeAnalysis;

namespace OBP.Tests;

public sealed class Rac1AridiaEncounterArchitectureTests
{
    [Fact]
    public void WorldScopedEncounterOwnsAllNativeFamiliesAndConstructorbotCaches()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var owner = compilation.GetTypeByMetadataName("OBP.Game.Rac1AridiaEncounter")!;
        var enemyHost = compilation.GetTypeByMetadataName("OBP.Game.Rac1EnemyHost")!;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        Assert.True(owner.IsSealed);
        Assert.Null(owner.ContainingType);
        Assert.Equal("Rac1EnemyHost", Assert.Single(owner.InstanceConstructors).Parameters.Single().Type.Name);
        var property = Assert.Single(enemyHost.GetMembers("Aridia").OfType<IPropertySymbol>());
        Assert.Equal(owner, property.Type);
        Assert.Null(property.SetMethod);
        foreach (string name in new[]
        {
            "_rac1AridiaEnemies", "_rac1AridiaController604", "_rac1AridiaSandmouse",
            "_rac1AridiaClipVolumes", "_rac1ConstructorbotStartups", "_rac1ConstructorbotNativeWaypoints",
            "_rac1ConstructorbotWalkerSpeeds", "_rac1ConstructorbotWalkerYawVelocities",
            "_rac1ConstructorbotWalkerSphere", "_rac1ConstructorbotRoamingTargets",
            "_rac1ConstructorbotRoamExitFlags", "_rac1ConstructorbotSweepProbeCounts",
            "_rac1ConstructorbotSphereProbeCounts", "_rac1ConstructorbotNativeLiftCounts",
            "_rac1ConstructorbotSphereContactCounts", "_rac1ConstructorbotStateThreeVerticalSteps"
        })
        {
            Assert.Single(owner.GetMembers(name).OfType<IFieldSymbol>());
            Assert.Empty(root.GetMembers(name));
        }
        foreach (string file in new[] { "OBPGame.Rac1AridiaEnemies.cs", "OBPGame.Rac1AridiaScripted.cs" })
            Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game", "scripts", file)));
    }

    [Fact]
    public void RegistrationTickDamageAndRetirementUseOwnedHostInsteadOfNeighborStateCallbacks()
    {
        var scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        string host = File.ReadAllText(Path.Combine(scripts, "Rac1EnemyHost.cs"));
        string aridia = File.ReadAllText(Path.Combine(scripts, "Rac1AridiaEncounter.cs"));
        string scripted = File.ReadAllText(Path.Combine(scripts, "Rac1AridiaEncounter.Scripted.cs"));
        string native = File.ReadAllText(Path.Combine(scripts, "Rac1AridiaEncounter.Native.cs"));
        string contacts = File.ReadAllText(Path.Combine(scripts, "Rac1AridiaEncounter.Contacts.cs"));
        string tick = File.ReadAllText(Path.Combine(scripts, "Rac1AridiaEncounter.Tick.cs"));
        string ports = File.ReadAllText(Path.Combine(scripts, "Rac1EnemyPorts.cs"));
        Assert.Contains("Aridia.Configure(world, result", host, StringComparison.Ordinal);
        Assert.Contains("Aridia.Tick(enemy)", host, StringComparison.Ordinal);
        Assert.Contains("Aridia.ApplyDamage(node, damage)", host, StringComparison.Ordinal);
        Assert.Contains("Aridia.Reset", host, StringComparison.Ordinal);
        Assert.Contains("Aridia.Dispose()", host, StringComparison.Ordinal);
        Assert.Contains("ConfigureScriptActors(world, result, authoredPlacements)", aridia, StringComparison.Ordinal);
        Assert.Contains("_host.Context.MobyRuntime.Register(source", scripted, StringComparison.Ordinal);
        Assert.Contains("_host.Context.MobyRuntime.SetNativeState", aridia, StringComparison.Ordinal);
        Assert.Contains("_host.Roster.TryGetValue(id, out var shark)", aridia, StringComparison.Ordinal);
        Assert.Contains("Rac1AridiaConstructorbotNative.StepWalkerState", tick, StringComparison.Ordinal);
        Assert.Contains("Rac1AridiaConstructorbotNative.StepActivation", native, StringComparison.Ordinal);
        Assert.Contains("PhysicsServer3D.BodyTestMotion", contacts, StringComparison.Ordinal);
        Assert.DoesNotContain("PhysicsServer3D.BodyTestMotion", native, StringComparison.Ordinal);
        Assert.DoesNotContain("Action<Rac1HostedEnemy> TickAridia", ports, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<Rac1AridiaEnemySession>", ports, StringComparison.Ordinal);
    }
}
