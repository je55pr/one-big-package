using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.ArchitectureTool;

public sealed record RootFilePolicy(string Path, string Owner, string Rationale, string[] Members);
public sealed record RootAccessPolicy(string Key, string Owner, string Rationale);
public sealed record RootCompositionException(string Key, string Owner, string Rationale, string ExpiresWith, string Hash);
public sealed record RootBoundaryPolicy(int Version, string BaselineCommit, RootFilePolicy[] LegacyFiles,
    RootAccessPolicy[] LegacyAccess, RootCompositionException[] Composition, string[] RetiredMembers, string[] RetiredAccess);
public sealed record RootMember(string Key, string Path, string Scope, string Kind, bool Direct, bool Mutable,
    bool Exposed, string Hash);
public sealed record RootReference(string From, string To, string Member, bool External, string Key);
public sealed record RootFileCensus(string Path, int Lines, int Methods, int Fields, int MutableFields, int ExposedMembers);
public sealed record RootCensus(int Files, int Lines, int Methods, int Fields, int MutableFields, int ExposedMembers,
    RootFileCensus[] ByFile, RootReference[] CrossPartialReferences);

/// <summary>Structural ownership guard. Native semantics and the width of reviewed ports still require code review.</summary>
public static class RootBoundaryAudit
{
    public const string RootName = "OBP.Game.OBPGame";
    public const string BaselineCommit = "eea806399f864f30949d665701d39d510549a7ac";
    public const string FrozenOriginHash = "f03bf71a5a63b5b3445b6a15f8493111cb3b8113f329c74aab91230948824051";

    private static readonly Lazy<PortableExecutableReference[]> Metadata = new(() =>
    {
        string[] assemblies = ["OBP.Core", "OBP.IO", "OBP.Runtime", "OBP.Fusion", "OBP.PS2", "OBP.RAC1", "OBP.RAC2", "OBP.RAC3", "OBP.Godot", "GodotSharp"];
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(assemblies.Select(name => Assembly.Load(name).Location)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        return references;
    });

    public static CSharpCompilation Compile(IEnumerable<SyntaxTree> sources)
    {
        var globals = CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Threading; global using System.Threading.Tasks;", path: "<implicit-usings>");
        return CSharpCompilation.Create("RootBoundaryAudit", sources.Append(globals), Metadata.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public static CSharpCompilation Read(string root) => Compile(Directory.EnumerateFiles(Path.Combine(root, "game"), "*.cs", SearchOption.AllDirectories)
        .Where(path => !new[] { "/obj/", "/bin/", "/.godot/" }.Any(part => path.Replace('\\', '/').Contains(part, StringComparison.Ordinal)))
        .Order(StringComparer.Ordinal).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path),
            path: Path.GetRelativePath(root, path).Replace('\\', '/'))));

    private static IEnumerable<BaseTypeDeclarationSyntax> RootScopes(SyntaxTree tree) => tree.GetRoot().DescendantNodes()
        .OfType<BaseTypeDeclarationSyntax>().Where(type => IsRoot(type) || type.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any(IsRoot));

    private static bool IsRoot(BaseTypeDeclarationSyntax type) => type.Identifier.ValueText == "OBPGame" &&
        string.Join(".", type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(ns => ns.Name.ToString())) == "OBP.Game";

    private static string Scope(BaseTypeDeclarationSyntax type) => string.Join("+", type.AncestorsAndSelf()
        .OfType<BaseTypeDeclarationSyntax>().Reverse().Select(item => item.Identifier.ValueText));

    private static string Tokens(SyntaxNode node) => string.Join(" ", node.DescendantTokens().Select(token => token.Text.Replace("\r\n", "\n", StringComparison.Ordinal)));
    private static string Hash(SyntaxNode node) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Tokens(node)))).ToLowerInvariant();

    public static RootMember[] Members(CSharpCompilation compilation)
    {
        var result = new List<RootMember>();
        foreach (var tree in compilation.SyntaxTrees)
            foreach (var type in RootScopes(tree))
            {
                string scope = Scope(type);
                // The declaration itself prevents a new nested diagnostic from inheriting privileged access.
                string header = type.Kind() + ":" + string.Join(",", type.Modifiers.Select(token => token.Text)) + ":" +
                    (type.BaseList is { } bases ? Tokens(bases) : "") + ":" +
                    (type is TypeDeclarationSyntax { TypeParameterList: { } parameters } ? Tokens(parameters) : "") + ":" +
                    (type is TypeDeclarationSyntax { ParameterList: { } constructor } ? Tokens(constructor) : "");
                result.Add(new(tree.FilePath + "::" + scope + "::declaration:" + header, tree.FilePath, scope, "type", false, false, false, Hash(type)));
                IEnumerable<MemberDeclarationSyntax> declared = type switch
                {
                    TypeDeclarationSyntax declaration => declaration.Members,
                    EnumDeclarationSyntax declaration => declaration.Members,
                    _ => [],
                };
                foreach (var member in declared.Where(member => member is not BaseTypeDeclarationSyntax))
                {
                    string modifiers = string.Join(",", member.Modifiers.Select(token => token.ValueText).Order(StringComparer.Ordinal));
                    bool exposed = member.Modifiers.Any(SyntaxKind.PublicKeyword) || member.Modifiers.Any(SyntaxKind.InternalKeyword) || member.Modifiers.Any(SyntaxKind.ProtectedKeyword);
                    string[] signatures = member switch
                    {
                        FieldDeclarationSyntax field => field.Declaration.Variables.Select(variable => "field:" + variable.Identifier.ValueText + ":" + Tokens(field.Declaration.Type)).ToArray(),
                        EventFieldDeclarationSyntax field => field.Declaration.Variables.Select(variable => "event:" + variable.Identifier.ValueText + ":" + Tokens(field.Declaration.Type)).ToArray(),
                        MethodDeclarationSyntax method => ["method:" + method.Identifier.ValueText + (method.TypeParameterList is { } generic ? Tokens(generic) : "") + "(" + string.Join(",", method.ParameterList.Parameters.Select(Parameter)) + "):" + Tokens(method.ReturnType)],
                        PropertyDeclarationSyntax property => ["property:" + property.Identifier.ValueText + ":" + Tokens(property.Type) + ":" +
                            (property.AccessorList is { } accessors ? string.Join(",", accessors.Accessors.Select(accessor => accessor.Keyword.Text + "/" + string.Join("/", accessor.Modifiers.Select(token => token.Text)))) : "get/")],
                        ConstructorDeclarationSyntax ctor => ["constructor:(" + string.Join(",", ctor.ParameterList.Parameters.Select(Parameter)) + ")"],
                        IndexerDeclarationSyntax indexer => ["indexer:(" + string.Join(",", indexer.ParameterList.Parameters.Select(Parameter)) + "):" + Tokens(indexer.Type)],
                        _ => [member.Kind() + ":" + Tokens(member)],
                    };
                    foreach (string signature in signatures)
                        result.Add(new(tree.FilePath + "::" + scope + "::" + signature + ":" + modifiers, tree.FilePath, scope,
                            signature.Split(':')[0], IsRoot(type), member is FieldDeclarationSyntax && !member.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) && !member.Modifiers.Any(SyntaxKind.ConstKeyword), exposed, Hash(member)));
                }
            }
        return result.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
    }

    private static string Parameter(ParameterSyntax parameter) => string.Join(" ", parameter.Modifiers.Select(token => token.Text)) + ":" + (parameter.Type is { } type ? Tokens(type) : "");

    public static RootReference[] References(CSharpCompilation compilation)
    {
        var root = compilation.GetTypeByMetadataName(RootName);
        if (root is null) return [];
        var references = new Dictionary<string, RootReference>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees.Where(tree => tree.FilePath != "<implicit-usings>"))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var symbol = model.GetSymbolInfo(name).Symbol;
                bool rootType = SymbolEqualityComparer.Default.Equals(symbol, root);
                if (!rootType && !SymbolEqualityComparer.Default.Equals(symbol?.ContainingType, root)) continue;
                string owner = symbol!.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree.FilePath ?? "";
                var enclosing = name.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                bool privileged = enclosing is not null && (IsRoot(enclosing) || enclosing.Ancestors().OfType<TypeDeclarationSyntax>().Any(IsRoot));
                string scope = enclosing is null ? "<top-level>" : Scope(enclosing);
                string member = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                string key = tree.FilePath + "::" + scope + "::" + member;
                if (!privileged || owner != tree.FilePath)
                    references.TryAdd(key, new(tree.FilePath, owner, member, !privileged, key));
            }
        }
        return references.Values.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
    }

    public static RootCensus Census(CSharpCompilation compilation)
    {
        var members = Members(compilation);
        var files = compilation.SyntaxTrees.Where(tree => RootScopes(tree).Any(IsRoot)).Select(tree =>
        {
            var direct = members.Where(member => member.Path == tree.FilePath && member.Direct).ToArray();
            string text = tree.GetText().ToString();
            int lines = text.Count(c => c == '\n') + (text.Length > 0 && text[^1] != '\n' ? 1 : 0);
            return new RootFileCensus(tree.FilePath, lines, direct.Count(item => item.Kind == "method"), direct.Count(item => item.Kind == "field"),
                direct.Count(item => item.Mutable), direct.Count(item => item.Exposed));
        }).OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
        return new(files.Length, files.Sum(item => item.Lines), files.Sum(item => item.Methods), files.Sum(item => item.Fields),
            files.Sum(item => item.MutableFields), files.Sum(item => item.ExposedMembers), files, References(compilation));
    }

    public static string[] Violations(CSharpCompilation compilation, RootBoundaryPolicy policy)
    {
        var errors = BoundaryViolations(compilation, policy).ToList();
        if (policy.Version != 1 || policy.BaselineCommit != BaselineCommit) errors.Add("OBP002 Invalid frozen root boundary policy version/commit.");
        if (OriginHash(policy) != FrozenOriginHash) errors.Add("OBP002 Frozen origin ledger changed (" + OriginHash(policy) + "); use named composition exceptions and retire old entries.");
        return errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Structural rules, independently testable on small compilations; CI also validates the pinned origin.</summary>
    public static string[] BoundaryViolations(CSharpCompilation compilation, RootBoundaryPolicy policy)
    {
        var errors = new List<string>();
        var original = policy.LegacyFiles.SelectMany(file => file.Members).ToHashSet(StringComparer.Ordinal);
        var legacy = original.Except(policy.RetiredMembers).ToHashSet(StringComparer.Ordinal);
        if (policy.RetiredMembers.Distinct(StringComparer.Ordinal).Count() != policy.RetiredMembers.Length || policy.RetiredMembers.Any(key => !original.Contains(key)))
            errors.Add("OBP002 Invalid/duplicate root member retirement.");
        var allowed = policy.Composition.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var files = policy.LegacyFiles.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var file in policy.LegacyFiles)
            if (string.IsNullOrWhiteSpace(file.Owner) || string.IsNullOrWhiteSpace(file.Rationale)) errors.Add("OBP002 Missing migration owner/rationale: " + file.Path);
        var members = Members(compilation);
        foreach (var member in members)
        {
            if (!files.Contains(member.Path)) errors.Add("OBP002 New root partial/scope: " + member.Path);
            if (legacy.Contains(member.Key)) continue;
            if (!allowed.TryGetValue(member.Key, out var exception)) errors.Add("OBP002 New privileged root member: " + member.Key);
            else if (!IssueId(exception.Owner) || string.IsNullOrWhiteSpace(exception.Rationale) || !IssueId(exception.ExpiresWith) || exception.Hash != member.Hash)
                errors.Add("OBP002 Composition exception changed or lacks owner/rationale/expiry: " + member.Key);
        }
        foreach (var exception in policy.Composition.Where(item => members.All(member => member.Key != item.Key)))
            errors.Add("OBP002 Stale composition exception: " + exception.Key);
        foreach (string key in legacy.Where(key => members.All(member => member.Key != key)))
            errors.Add("OBP002 Removed root member must be marked retired: " + key);
        var access = policy.LegacyAccess.Where(item => !policy.RetiredAccess.Contains(item.Key, StringComparer.Ordinal)).ToDictionary(item => item.Key, StringComparer.Ordinal);
        if (policy.RetiredAccess.Distinct(StringComparer.Ordinal).Count() != policy.RetiredAccess.Length || policy.RetiredAccess.Any(key => policy.LegacyAccess.All(item => item.Key != key)))
            errors.Add("OBP002 Invalid/duplicate root access retirement.");
        var external = References(compilation).Where(reference => reference.External).ToArray();
        foreach (var reference in external)
            if (!access.TryGetValue(reference.Key, out var entry) || string.IsNullOrWhiteSpace(entry.Owner) || string.IsNullOrWhiteSpace(entry.Rationale))
                errors.Add("OBP002 Unapproved external root access: " + reference.Key);
        foreach (string key in access.Keys.Where(key => external.All(reference => reference.Key != key)))
            errors.Add("OBP002 Removed root access must be marked retired: " + key);
        return errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public static string OriginHash(RootBoundaryPolicy policy) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { policy.LegacyFiles, policy.LegacyAccess })))).ToLowerInvariant();

    private static bool IssueId(string value) => !string.IsNullOrEmpty(value) && value.Length > 1 && value[0] == '#' && value.AsSpan(1).ToString().All(char.IsAsciiDigit);
}
