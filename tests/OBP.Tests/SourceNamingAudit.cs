using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

internal static class SourceNamingAudit
{
    internal sealed record Finding(string Key, string Reason);

    internal static IEnumerable<Finding> Scan(string path, string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        foreach (var alias in root.DescendantNodes().OfType<UsingDirectiveSyntax>().Where(item => item.Alias is not null))
        {
            string name = alias.Alias!.Name.Identifier.ValueText;
            if (LegacyPrefix(name)) yield return new(path + "::using " + name, "C# aliases also use the canonical Rac2/Rac3 vocabulary.");
        }
        foreach (var declaration in root.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(item => item is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
        {
            var ancestors = declaration.Ancestors().OfType<BaseTypeDeclarationSyntax>().ToArray();
            var modifiers = declaration is BaseTypeDeclarationSyntax type ? type.Modifiers : ((DelegateDeclarationSyntax)declaration).Modifiers;
            if (!modifiers.Any(SyntaxKind.PublicKeyword) || ancestors.Any(parent => !parent.Modifiers.Any(SyntaxKind.PublicKeyword))) continue;
            string name = declaration is BaseTypeDeclarationSyntax named ? named.Identifier.ValueText : ((DelegateDeclarationSyntax)declaration).Identifier.ValueText;
            string ns = string.Join(".", declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(item => item.Name.ToString()));
            string? owner = SourceOwner(path, ns);
            string? reason = LegacyPrefix(name) ? "New C# source APIs use Rac2/Rac3, not Gc/Uya aliases." :
                LegacyNamespace(ns) ? "Source namespaces use OBP.RAC2/OBP.RAC3." :
                owner is not null && path.StartsWith("src/OBP." + owner.ToUpperInvariant() + "/", StringComparison.Ordinal) &&
                    ns != "OBP." + owner.ToUpperInvariant() && !ns.StartsWith("OBP." + owner.ToUpperInvariant() + ".", StringComparison.Ordinal)
                    ? "Source project types retain their canonical namespace root." :
                ancestors.Length == 0 && owner is not null && !Canonical(name, owner) ? "A public source-owned type needs its canonical source prefix." :
                ancestors.Length == 0 && owner is not null && !Canonical(Path.GetFileNameWithoutExtension(path), owner) ? "A source-owned file needs its canonical source prefix." : null;
            int arity = declaration switch { TypeDeclarationSyntax generic => generic.TypeParameterList?.Parameters.Count ?? 0, DelegateDeclarationSyntax generic => generic.TypeParameterList?.Parameters.Count ?? 0, _ => 0 };
            string key = path + "::" + ns + "." + string.Join(".", ancestors.AsEnumerable().Reverse().Select(parent => parent.Identifier.ValueText).Append(name)) + (arity > 0 ? "`" + arity : "");
            if (reason is not null) yield return new(key, reason);
        }
    }

    private static string? SourceOwner(string path, string ns)
    {
        foreach (string number in new[] { "1", "2", "3" })
            if (path.StartsWith("src/OBP.RAC" + number + "/", StringComparison.Ordinal) || ns == "OBP.RAC" + number || ns.StartsWith("OBP.RAC" + number + ".", StringComparison.Ordinal))
                return "Rac" + number;
        return null;
    }

    private static bool Canonical(string name, string owner) => name.StartsWith(owner, StringComparison.Ordinal) || name.StartsWith("I" + owner, StringComparison.Ordinal);
    private static bool LegacyPrefix(string name) => name.StartsWith("Gc", StringComparison.Ordinal) || name.StartsWith("IGc", StringComparison.Ordinal) ||
        name.StartsWith("Uya", StringComparison.Ordinal) || name.StartsWith("IUya", StringComparison.Ordinal);
    private static bool LegacyNamespace(string ns) => ns.Equals("OBP.GC", StringComparison.OrdinalIgnoreCase) || ns.StartsWith("OBP.GC.", StringComparison.OrdinalIgnoreCase) ||
        ns.Equals("OBP.UYA", StringComparison.OrdinalIgnoreCase) || ns.StartsWith("OBP.UYA.", StringComparison.OrdinalIgnoreCase) ||
        SourceOwner("", ns) is not null && ns.Split('.').Skip(2).Any(LegacyPrefix);

    internal static IEnumerable<string> ProjectErrors(string path, string? rootNamespace, string? assemblyName)
    {
        string project = Path.GetFileNameWithoutExtension(path);
        if (project.StartsWith("OBP.GC", StringComparison.OrdinalIgnoreCase) || project.StartsWith("OBP.UYA", StringComparison.OrdinalIgnoreCase) ||
            rootNamespace is not null && LegacyNamespace(rootNamespace) || assemblyName is not null && LegacyNamespace(assemblyName))
            yield return path + ": source projects use OBP.RAC2/OBP.RAC3.";
        foreach (string number in new[] { "1", "2", "3" })
        {
            string expected = "OBP.RAC" + number;
            if (!project.Equals(expected, StringComparison.OrdinalIgnoreCase) && !path.StartsWith("src/" + expected + "/", StringComparison.Ordinal)) continue;
            if (project != expected || rootNamespace is not null && rootNamespace != expected || assemblyName is not null && assemblyName != expected)
                yield return path + ": source project/namespace/assembly must be " + expected + ".";
        }
    }
}
