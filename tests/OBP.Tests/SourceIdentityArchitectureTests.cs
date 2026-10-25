using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public sealed class SourceIdentityArchitectureTests
{
    [Fact]
    public void SourceContractsAndRoutesCannotReintroduceFreeStringIdentities()
    {
        var violations = new[] { "src", "game" }.SelectMany(directory => Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, directory), "*.cs", SearchOption.AllDirectories))
            .Select(path => (Path: Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/'), Source: File.ReadAllText(path)))
            .Where(item => !item.Path.Contains("/obj/", StringComparison.Ordinal) && !item.Path.Contains("/bin/", StringComparison.Ordinal) && !item.Path.Contains("/.godot/", StringComparison.Ordinal))
            .SelectMany(item => Findings(item.Path, item.Source));
        Assert.Empty(violations);
    }

    private static IEnumerable<string> Findings(string path, string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        bool IdentityName(string name) => name is "Game" or "SourceGame" or "game" or "sourceGame";
        bool Text(TypeSyntax? type) => type?.ToString().TrimEnd('?') is "string" or "String" or "System.String" or "global::System.String";
        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            if (IdentityName(property.Identifier.ValueText) && Text(property.Type)) yield return path + ": string identity property " + property.Identifier;
        foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
            if (IdentityName(parameter.Identifier.ValueText) && Text(parameter.Type)) yield return path + ": string identity parameter " + parameter.Identifier;
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            if (Text(field.Declaration.Type) && field.Declaration.Variables.Any(variable => IdentityName(variable.Identifier.ValueText))) yield return path + ": string identity field";

        // The one external vocabulary owns canonical strings and compatibility
        // labels. BuildId, AssetKind and complete destination IDs stay arbitrary.
        if (path == "src/OBP.Core/SourceIdentityWire.cs") yield break;
        bool IdentityExpression(ExpressionSyntax expression) => expression switch
        {
            IdentifierNameSyntax name => IdentityName(name.Identifier.ValueText),
            MemberAccessExpressionSyntax member => IdentityName(member.Name.Identifier.ValueText) || IdentityExpression(member.Expression),
            ConditionalAccessExpressionSyntax conditional => IdentityExpression(conditional.Expression) || conditional.WhenNotNull is MemberBindingExpressionSyntax binding && IdentityName(binding.Name.Identifier.ValueText),
            InvocationExpressionSyntax invocation => IdentityExpression(invocation.Expression) || invocation.ArgumentList.Arguments.Any(argument => IdentityExpression(argument.Expression)),
            _ => false,
        };
        bool Wire(ExpressionSyntax expression) => expression is LiteralExpressionSyntax literal && literal.Token.ValueText is "rac1" or "rac2" or "rac3" or "fusion" or "synthetic" or "deadlocked";
        foreach (var comparison in root.DescendantNodes().OfType<BinaryExpressionSyntax>().Where(binary => binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression)))
            if (IdentityExpression(comparison.Left) && Wire(comparison.Right) || IdentityExpression(comparison.Right) && Wire(comparison.Left)) yield return path + ": raw source identity comparison " + comparison;
        foreach (var pattern in root.DescendantNodes().OfType<IsPatternExpressionSyntax>())
            if (IdentityExpression(pattern.Expression) && pattern.Pattern.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().Any(Wire)) yield return path + ": raw source identity pattern";
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Equals" } &&
                IdentityExpression(invocation) && invocation.ArgumentList.Arguments.Any(argument => Wire(argument.Expression))) yield return path + ": raw source identity equality call";
        foreach (var selection in root.DescendantNodes().OfType<SwitchExpressionSyntax>())
            if (IdentityExpression(selection.GoverningExpression) && selection.Arms.Any(arm => arm.Pattern.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().Any(Wire))) yield return path + ": raw source identity switch";
    }

    [Theory]
    [InlineData("public record Target(string SourceGame);")]
    [InlineData("public class World { public string Game { get; init; } }")]
    [InlineData("class Host { bool Route(World world) => world.Game == \"rac1\"; }")]
    [InlineData("class Host { bool Route(World world) => world.Game.ToString() == \"rac2\"; }")]
    [InlineData("class Host { bool Route(World world) => world.Game.Equals(\"rac2\"); }")]
    [InlineData("class Host { bool Route(World world) => SourceIdentityWire.FormatWorld(world.Game) == \"rac1\"; }")]
    [InlineData("class Host { bool Route(World world) => world?.Game is \"rac3\"; }")]
    public void RawIdentityContractsAndComparisonsFail(string source) => Assert.NotEmpty(Findings("src/OBP.Runtime/New.cs", source));

    [Theory]
    [InlineData("public record Asset(string BuildId, string AssetKind, string DestinationId);")]
    [InlineData("class Host { bool Route(Asset asset) => asset.AssetKind == \"rac1\"; }")]
    [InlineData("public record Target(ObpSourceGame SourceGame);")]
    public void ArbitraryLabelsAndTypedIdentityContractsRemainValid(string source) => Assert.Empty(Findings("src/OBP.Runtime/New.cs", source));
}
