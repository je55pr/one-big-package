using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>
/// Focused ownership audit, not a general C# taint analyzer. Binding symbols
/// distinguishes world origin from an actor/asset's legitimate source identity,
/// and follows aliases, null access and property patterns without text bans.
/// </summary>
internal static class HostOwnershipAudit
{
    internal sealed record Usage(string Key, string Hash, int WorldReads, int LegacyReads, bool PlayerSelection);
    internal sealed record Exception(string Key, string Kind, string Reason, string? Owner = null, string? Hash = null);
    internal static readonly Lazy<CSharpCompilation> Host = new(CreateHostCompilation);

    internal static IReadOnlyList<Exception> Policy() => JsonSerializer.Deserialize<Exception[]>(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "tests", "OBP.Tests", "host-ownership-policy.json")))!;

    internal static CSharpCompilation Compile(IEnumerable<SyntaxTree> trees) => CSharpCompilation.Create(
        "OwnershipAudit", trees, References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Concat(new[] { "OBP.Core", "OBP.Runtime", "OBP.Fusion", "OBP.Godot", "OBP.IO", "OBP.PS2",
            "OBP.PS2.Presentation", "OBP.RAC1", "OBP.RAC2", "OBP.RAC3", "GodotSharp" }
            .Select(name => Assembly.Load(name).Location))
        .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)).ToArray();

    private static CSharpCompilation CreateHostCompilation()
    {
        string[] directories = ["game/scripts", "game/diagnostics", "src/OBP.Godot", "src/OBP.Fusion"];
        return Compile(directories.SelectMany(directory => Directory.EnumerateFiles(
                Path.Combine(RepoPaths.Root, directory), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Replace('\\', '/').Contains("/obj/") && !path.Replace('\\', '/').Contains("/bin/"))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/'))));
    }

    internal static IReadOnlyList<Usage> Scan(CSharpCompilation compilation)
    {
        var result = new List<Usage>();
        foreach (var tree in compilation.SyntaxTrees.Where(tree => !tree.FilePath.StartsWith("game/diagnostics/", StringComparison.Ordinal)))
        {
            var model = compilation.GetSemanticModel(tree);
            var nodes = tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(node => node.Identifier.ValueText == "UseRac1Gameplay" || IsWorldOrigin(node, model));
            foreach (var group in nodes.GroupBy(node => node.Ancestors().OfType<MemberDeclarationSyntax>()
                         .First(member => member is not TypeDeclarationSyntax)))
            {
                var member = group.Key;
                var type = member.Ancestors().OfType<TypeDeclarationSyntax>().First().Identifier.ValueText;
                string name = member switch
                {
                    MethodDeclarationSyntax method => method.Identifier.ValueText + Signature(method.ParameterList),
                    ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText + Signature(constructor.ParameterList),
                    PropertyDeclarationSyntax property => property.Identifier.ValueText,
                    FieldDeclarationSyntax field => string.Join(",", field.Declaration.Variables.Select(v => v.Identifier.ValueText)),
                    _ => member.Kind().ToString(),
                };
                string tokens = string.Join(" ", member.DescendantTokens().Select(token => token.Text.Replace("\r\n", "\n", StringComparison.Ordinal)));
                int legacy = group.Count(node => node.Identifier.ValueText == "UseRac1Gameplay");
                result.Add(new(tree.FilePath + "::" + type + "." + name,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokens))).ToLowerInvariant(),
                    group.Count() - legacy, legacy, HasPlayerSelection(member, model)));
            }
        }
        return result.OrderBy(usage => usage.Key, StringComparer.Ordinal).ToArray();
    }

    private static string Signature(ParameterListSyntax parameters) =>
        "(" + string.Join(",", parameters.Parameters.Select(parameter => parameter.Type?.WithoutTrivia().ToString())) + ")";

    private static bool IsWorldOrigin(IdentifierNameSyntax node, SemanticModel model) =>
        node.Identifier.ValueText == "Game" && model.GetSymbolInfo(node).Symbol is IPropertySymbol property &&
        property.ContainingType.ToDisplayString() is "OBP.Runtime.RuntimeWorld" or "OBP.Core.ObpDestination";

    private static bool HasPlayerSelection(MemberDeclarationSyntax member, SemanticModel model) =>
        member.DescendantNodes().Any(node => node is ObjectCreationExpressionSyntax creation &&
            model.GetTypeInfo(creation).Type?.Name is "PlayerHost" or "PauseMenuUi" or "WeaponQuickSelectUi" or
                "PlayerAvatarView" or "PlayerSessionConfiguration" ||
            node is InvocationExpressionSyntax invocation && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
            (method.ContainingType.ToDisplayString() == "OBP.Runtime.Player.PlayerAvatarProviderRegistry" && method.Name == "Get" ||
             method.Name is "SpawnPlayer" or "AttachPlayerAvatarVisual" or "TryOpenPauseMenu" or "TryOpenWeaponQuickSelect" &&
             invocation.Ancestors().OfType<IfStatementSyntax>().Any(branch =>
                 branch.Condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(name => IsWorldOrigin(name, model)))));

    internal static IReadOnlyList<string> Violations(IReadOnlyList<Usage> usages, IReadOnlyList<Exception> policy, bool checkStale = true)
    {
        var exceptions = policy.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var usage in usages)
        {
            if (!exceptions.TryGetValue(usage.Key, out var entry))
            {
                errors.Add($"OBP001 {usage.Key}: unapproved world-origin / UseRac1Gameplay dependency (hash {usage.Hash}). Select player services from the session; keep source-world facts in a narrow adapter.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(entry.Reason)) errors.Add($"OBP001 {entry.Key}: exception lacks an ownership rationale.");
            if (entry.Kind == "migration")
            {
                if (string.IsNullOrWhiteSpace(entry.Owner) || entry.Hash != usage.Hash)
                    errors.Add($"OBP001 {entry.Key}: frozen player coupling changed; migration owner {entry.Owner} (hash {usage.Hash}). Remove the dependency and its baseline, do not extend it.");
            }
            else if (entry.Kind != "adapter" || usage.LegacyReads > 0 || usage.PlayerSelection)
                errors.Add($"OBP001 {entry.Key}: source-world adapter selects a player service or uses the legacy player-mode flag.");
            else if (entry.Hash != usage.Hash)
                errors.Add($"OBP001 {entry.Key}: approved source-world adapter changed (hash {usage.Hash}); review the source-owned change and its regression before updating its fingerprint. Player-global mutations do not belong in this adapter.");
        }
        if (checkStale)
            foreach (var entry in policy.Where(entry => usages.All(usage => usage.Key != entry.Key)))
                errors.Add($"OBP001 {entry.Key}: stale exception; remove it after migration.");
        return errors;
    }
}
