using Microsoft.CodeAnalysis;

namespace OBP.Tests;

public sealed class Rac1EnemyHostArchitectureTests
{
    [Fact]
    public void EnemyStateAndFamiliesCannotReturnToRootPartials()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string field in new[] { "_rac1Enemies", "_rac1Class1440", "_rac1Peckbots", "_rac1Birdbots", "_rac1Seekers", "_rac1EnemyTicks", "_rac1AridiaNativeFrameCounter", "_rac1BirdbotShots" })
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
                     "Rac1ToadEnemyAdapter", "Rac1Class1440EnemyAdapter", "Rac1BirdbotEnemyAdapter", "Rac1PeckbotEnemyAdapter", "Rac1SeekerEnemyAdapter" })
        {
            var type = compilation.GetTypeByMetadataName("OBP.Game." + name)!;
            Assert.Null(type.ContainingType);
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>()) Check(field.Type);
            foreach (var constructor in type.InstanceConstructors)
                foreach (var parameter in constructor.Parameters) Check(parameter.Type);
        }
    }

    private static void Check(ITypeSymbol type)
    {
        Assert.NotEqual("OBP.Game.OBPGame", type.ToDisplayString());
        if (type is INamedTypeSymbol named)
            foreach (var argument in named.TypeArguments) Check(argument);
    }
}
