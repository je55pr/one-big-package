using Microsoft.CodeAnalysis;

namespace OBP.Tests;

public sealed class Rac1EnemyHostArchitectureTests
{
    [Fact]
    public void EnemyStateAndFamiliesCannotReturnToRootPartials()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string field in new[] { "_rac1Enemies", "_rac1Class1440", "_rac1Peckbots", "_rac1Birdbots", "_rac1Seekers", "_rac1EnemyTicks", "_rac1AridiaNativeFrameCounter", "_rac1BirdbotShots", "_rac1Amoeboids", "_rac1AmoeboidVolumes", "_rac1AmoeboidTargetGroups", "_rac1Class623Activation", "_rac1Class830Controllers", "_rac1AmoeboidLungeTargets" })
            Assert.Empty(root.GetMembers(field));
        foreach (string name in new[] { "Rac1Enemies", "Rac1Hostiles", "Rac1NovalisBirdbots", "Rac1NovalisPeckbots", "Rac1Seekers" })
            Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame." + name + ".cs")));
        var module = compilation.GetTypeByMetadataName("OBP.Game.Rac1WorldModule")!;
        var owner = Assert.Single(module.GetMembers("Enemies").OfType<IPropertySymbol>());
        Assert.Equal("Rac1EnemyHost", owner.Type.Name);
        Assert.Null(owner.SetMethod);
    }

    [Fact]
    public void NativeFamilyAndNeighborAdaptersReceiveNoPrivilegedRoot()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        foreach (string name in new[] { "Rac1EnemyHost", "Rac1EnemyPorts", "Rac1EnemyPlayerPorts", "Rac1EnemyCombatPorts", "Rac1EnemyNeighborPorts",
                     "Rac1ToadEnemyAdapter", "Rac1Class1440EnemyAdapter", "Rac1BirdbotEnemyAdapter", "Rac1PeckbotEnemyAdapter", "Rac1SeekerEnemyAdapter", "Rac1AmoeboidEnemyAdapter" })
        {
            var type = compilation.GetTypeByMetadataName("OBP.Game." + name)!;
            Assert.Null(type.ContainingType);
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>()) Check(field.Type);
            foreach (var constructor in type.InstanceConstructors)
                foreach (var parameter in constructor.Parameters) Check(parameter.Type);
        }
    }

    [Fact]
    public void AmoeboidFamilyIsWorldScopedAndUsesExistingEnemyHostBoundaries()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var adapter = compilation.GetTypeByMetadataName("OBP.Game.Rac1AmoeboidEnemyAdapter")!;
        var enemyHost = compilation.GetTypeByMetadataName("OBP.Game.Rac1EnemyHost")!;
        var neighbors = compilation.GetTypeByMetadataName("OBP.Game.Rac1EnemyNeighborPorts")!;

        Assert.True(adapter.IsSealed);
        var constructor = Assert.Single(adapter.InstanceConstructors);
        Assert.Equal("Rac1EnemyHost", Assert.Single(constructor.Parameters).Type.Name);
        Assert.Equal("Rac1AmoeboidEnemyAdapter",
            Assert.Single(enemyHost.GetMembers("_amoeboids").OfType<IFieldSymbol>()).Type.Name);
        foreach (string field in new[] { "_rac1Amoeboids", "_rac1AmoeboidVolumes",
                     "_rac1AmoeboidTargetGroups", "_rac1Class623Activation",
                     "_rac1Class830Controllers", "_rac1AmoeboidLungeTargets" })
        {
            var owned = Assert.Single(adapter.GetMembers(field).OfType<IFieldSymbol>());
            Assert.False(owned.IsStatic);
        }
        foreach (string method in new[] { "Register", "Reset", "TickGates", "Initialize", "Tick", "ApplyDamage" })
            Assert.Single(adapter.GetMembers(method).OfType<IMethodSymbol>());
        Assert.DoesNotContain(neighbors.GetMembers(), member => member.Name.Contains("Amoeboid", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1Amoeboids.cs")));

        var hostTree = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/Rac1EnemyHost.cs");
        var methods = hostTree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax>()
            .Where(access => access.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax id &&
                id.Identifier.ValueText == "_amoeboids")
            .Select(access => access.Name.Identifier.ValueText).ToArray();
        foreach (string routed in new[] { "Register", "Reset", "TickGates", "Initialize", "Tick", "ApplyDamage" })
            Assert.Contains(routed, methods);
        Assert.Equal(1, methods.Count(method => method == "TickGates"));
    }

    private static void Check(ITypeSymbol type)
    {
        Assert.NotEqual("OBP.Game.OBPGame", type.ToDisplayString());
        if (type is INamedTypeSymbol named)
            foreach (var argument in named.TypeArguments) Check(argument);
    }
}
