using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using OBP.ArchitectureTool;

namespace OBP.Tests;

public sealed class RootBoundaryArchitectureTests
{
    private const string PathName = "game/scripts/OBPGame.cs";
    private const string Fixture = "namespace OBP.Game; public partial class OBPGame { private int _old; public int Read() => _old; }";
    private static readonly Lazy<RootBuild[]> Host = new(() => RootBoundaryAudit.ReadBuilds(RepoPaths.Root));
    private static RootBoundaryPolicy Policy => JsonSerializer.Deserialize<RootBoundaryPolicy>(File.ReadAllText(
        Path.Combine(RepoPaths.Root, "tests/OBP.Tests/root-boundary-policy.json")))!;

    private static Microsoft.CodeAnalysis.CSharp.CSharpCompilation Compile(params (string Path, string Source)[] files) =>
        RootBoundaryAudit.Compile(files.Select(file => CSharpSyntaxTree.ParseText(file.Source, path: file.Path)));

    private static RootBoundaryPolicy FixturePolicy(Microsoft.CodeAnalysis.CSharp.CSharpCompilation original)
    {
        var members = RootBoundaryAudit.Members(original);
        return new(1, RootBoundaryAudit.BaselineCommit,
            members.GroupBy(member => member.Path).Select(group => new RootFilePolicy(group.Key, "#376", "Existing fixture scope", group.Select(member => member.Key).ToArray())).ToArray(),
            [], [], [], []);
    }

    [Fact]
    public void ActualRootAndExternalAccessMatchTheFrozenMigrationLedger()
    {
        var errors = RootBoundaryAudit.Violations(Host.Value.Select(build => build.Compilation), Policy);
        Assert.True(errors.Length == 0, string.Join("\n", errors));
    }

    [Fact]
    public void FrozenOriginIncludesTheControllerFragmentOutsideTheFilenameCensus()
    {
        var policy = Policy;
        Assert.Equal(RootBoundaryAudit.FrozenOriginHash, RootBoundaryAudit.OriginHash(policy));
        Assert.Equal(122, policy.LegacyFiles.Length);
        var extra = Assert.Single(policy.LegacyFiles, file => file.Path == "game/diagnostics/ControllerSmoke.cs");
        Assert.Equal("#339", extra.Owner);
        Assert.Contains(extra.Members, key => key.Contains("CheckControllerWeaponInputAsync", StringComparison.Ordinal));
        Assert.Equal(121, policy.LegacyFiles.Count(file => System.IO.Path.GetFileName(file.Path).StartsWith("OBPGame", StringComparison.Ordinal)));
        var expanded = policy with { LegacyFiles = [.. policy.LegacyFiles, new("game/scripts/Unreviewed.cs", "#376", "cannot expand origin", [])] };
        Assert.NotEqual(RootBoundaryAudit.FrozenOriginHash, RootBoundaryAudit.OriginHash(expanded));
    }

    [Theory]
    [InlineData("private int _newState;")]
    [InlineData("private readonly System.Collections.Generic.List<int> _newEnemies = new();")]
    [InlineData("private readonly System.Collections.Generic.Dictionary<int, object> _newActors = new();")]
    [InlineData("private static void TickRac1NewEnemy() {}")]
    [InlineData("private void ConfigureRac2NewEncounter() {}")]
    [InlineData("private static float RecoveredRac1Damage(float damage) => damage * .5f;")]
    [InlineData("public object NativeState => new object();")]
    [InlineData("private sealed class NewSmoke { public int Read(OBPGame game) => game._old; }")]
    [InlineData("private enum NewNativeStates { Idle, Attack }")]
    public void NewGameplayStateNativeFormulasAndPrivilegedScenariosCannotGrowExistingPartials(string member)
    {
        var original = Compile((PathName, Fixture));
        var changed = Compile((PathName, Fixture.Insert(Fixture.LastIndexOf('}'), member)));
        Assert.Contains(RootBoundaryAudit.BoundaryViolations(changed, FixturePolicy(original)), error => error.Contains("New privileged root member", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("game/scripts/OBPGame.NewEnemy.cs")]
    [InlineData("game/diagnostics/HiddenScenario.cs")]
    [InlineData("game/elsewhere/InnocentName.cs")]
    public void RootDeclarationsAreGuardedIndependentlyOfTheirFilename(string path)
    {
        var original = Compile((PathName, Fixture));
        var changed = Compile((PathName, Fixture), (path, "namespace OBP.Game; public partial class OBPGame { private void ConfigureRac3Enemy() {} }"));
        Assert.Contains(RootBoundaryAudit.BoundaryViolations(changed, FixturePolicy(original)), error => error.Contains("New root partial/scope", StringComparison.Ordinal));
    }

    [Fact]
    public void EscapedNamespaceIdentifiersCannotHideAnotherRootPartial()
    {
        var original = Compile((PathName, Fixture));
        var changed = Compile((PathName, Fixture), ("game/diagnostics/Escaped.cs",
            "namespace @OBP.@Game; public partial class @OBPGame { private int _escapedState; }"));
        Assert.Contains(RootBoundaryAudit.BoundaryViolations(changed, FixturePolicy(original)), error => error.Contains("_escapedState", StringComparison.Ordinal));
    }

    [Fact]
    public void ConditionalMembersAreAuditedAcrossBothBuildConfigurations()
    {
        const string source = "namespace OBP.Game; public partial class OBPGame {\n#if DEBUG\nprivate int _debugState;\n#else\nprivate int _releaseState;\n#endif\n}";
        var debug = RootBoundaryAudit.Compile([CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithPreprocessorSymbols("DEBUG"), PathName)]);
        var release = RootBoundaryAudit.Compile([CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithPreprocessorSymbols("RELEASE"), PathName)]);
        var original = Compile((PathName, "namespace OBP.Game; public partial class OBPGame {}"));
        var errors = RootBoundaryAudit.BoundaryViolations(new[] { debug, release }, FixturePolicy(original));
        Assert.Contains(errors, error => error.Contains("_debugState", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("_releaseState", StringComparison.Ordinal));
        var members = RootBoundaryAudit.Members(new[] { debug, release });
        var accepted = new RootBoundaryPolicy(1, RootBoundaryAudit.BaselineCommit,
            [new(PathName, "#376", "Conditional fixture", members.Select(member => member.Key).ToArray())], [], [], [], []);
        Assert.Empty(RootBoundaryAudit.BoundaryViolations(new[] { debug, release }, accepted));
    }

    [Fact]
    public void RealBuildSymbolsCoverSupportedDesktopTargetsAndConfigurations()
    {
        Assert.Equal(new[] { "Debug/linuxbsd", "Debug/windows", "Release/linuxbsd", "Release/windows" }, Host.Value.Select(build => build.Name).Order(StringComparer.Ordinal));
        foreach (var build in Host.Value)
        {
            Assert.Contains("NET8_0", build.Symbols); Assert.Contains("GODOT", build.Symbols);
            Assert.Contains(build.Name.StartsWith("Debug", StringComparison.Ordinal) ? "DEBUG" : "RELEASE", build.Symbols);
            Assert.Contains(build.Name.EndsWith("windows", StringComparison.Ordinal) ? "GODOT_WINDOWS" : "GODOT_LINUXBSD", build.Symbols);
        }
    }

    [Fact]
    public void SameNamedConsumersInDifferentNamespacesCannotShareAnAccessException()
    {
        const string path = "game/diagnostics/Consumers.cs";
        const string allowed = "namespace Allowed { class Consumer { int Read(OBP.Game.OBPGame root) => root.Read(); } }";
        const string unexpected = "namespace Unexpected { class Consumer { int Read(OBP.Game.OBPGame root) => root.Read(); } }";
        var original = Compile((PathName, Fixture), (path, allowed));
        var policy = FixturePolicy(original) with
        {
            LegacyAccess = RootBoundaryAudit.References(original).Where(reference => reference.External)
                .Select(reference => new RootAccessPolicy(reference.Key, "#337", "Existing fixture consumer")).ToArray()
        };
        Assert.Empty(RootBoundaryAudit.BoundaryViolations(original, policy));
        var changed = Compile((PathName, Fixture), (path, allowed + unexpected));
        Assert.Contains(RootBoundaryAudit.BoundaryViolations(changed, policy), error => error.Contains("Unexpected.Consumer", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("class NewScenario : OBPGame {}")]
    [InlineData("class NewScenario { private OBPGame _host; }")]
    [InlineData("class NewScenario { int Inspect(OBPGame game) => game.Read(); }")]
    [InlineData("using Host = OBP.Game.OBPGame; class NewScenario { Host _host; }")]
    public void ExternalScenariosCannotInheritOrAcquireTheWholeRoot(string declaration)
    {
        var original = Compile((PathName, Fixture));
        var changed = Compile((PathName, Fixture), ("game/diagnostics/NewScenario.cs", "namespace OBP.Game; " + declaration));
        Assert.Contains(RootBoundaryAudit.BoundaryViolations(changed, FixturePolicy(original)), error => error.Contains("Unapproved external root access", StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingBodiesCanBeFixedWithoutFreezingTheirImplementation()
    {
        var original = Compile((PathName, Fixture));
        var changed = Compile((PathName, Fixture.Replace("=> _old", "=> Math.Max(0, _old)", StringComparison.Ordinal)));
        Assert.Empty(RootBoundaryAudit.BoundaryViolations(changed, FixturePolicy(original)));
    }

    [Theory]
    [InlineData("private int _old;", "[Godot.Export] private int _old;")]
    [InlineData("public partial class OBPGame", "[Godot.Tool] public partial class OBPGame")]
    [InlineData("public int Read()", "[System.Obsolete] public int Read()")]
    public void AttributesCannotChangeExistingExposureOrLifecycle(string before, string after)
    {
        var original = Compile((PathName, Fixture));
        var changed = Compile((PathName, Fixture.Replace(before, after, StringComparison.Ordinal)));
        Assert.Contains(RootBoundaryAudit.BoundaryViolations(changed, FixturePolicy(original)),
            error => error.Contains("New privileged root member", StringComparison.Ordinal));
    }

    [Fact]
    public void HeadlineCensusIncludesFilesAndMembersFromEveryBuild()
    {
        const string conditional = "namespace OBP.Game; public partial class OBPGame {\n#if GODOT_LINUXBSD\nprivate int _linux;\n#else\nprivate int _windows;\n#endif\n}";
        const string releaseOnly = "#if RELEASE\nnamespace OBP.Game; public partial class OBPGame { private void ReleaseOnly() {} }\n#endif";
        var windows = RootBoundaryAudit.Compile([CSharpSyntaxTree.ParseText(conditional, CSharpParseOptions.Default.WithPreprocessorSymbols("GODOT_WINDOWS"), PathName),
            CSharpSyntaxTree.ParseText(releaseOnly, CSharpParseOptions.Default.WithPreprocessorSymbols("DEBUG"), "game/scripts/Release.cs")]);
        var linux = RootBoundaryAudit.Compile([CSharpSyntaxTree.ParseText(conditional, CSharpParseOptions.Default.WithPreprocessorSymbols("GODOT_LINUXBSD"), PathName),
            CSharpSyntaxTree.ParseText(releaseOnly, CSharpParseOptions.Default.WithPreprocessorSymbols("RELEASE"), "game/scripts/Release.cs")]);
        Assert.Equal(1, RootBoundaryAudit.Census(windows).Files);
        var census = RootBoundaryAudit.Census(new[] { windows, linux });
        Assert.Equal(2, census.Files); Assert.Equal(2, census.Fields); Assert.Equal(1, census.Methods);
        Assert.Equal(conditional.Count(character => character == '\n') + 1, census.ByFile.Single(file => file.Path == PathName).Lines);
    }

    [Fact]
    public void AnExistingReadOnlyObservationCannotAcquireASetter()
    {
        var original = Compile((PathName, Fixture.Insert(Fixture.LastIndexOf('}'), "public int State => _old;")));
        var changed = Compile((PathName, Fixture.Insert(Fixture.LastIndexOf('}'), "public int State { get => _old; set => _old = value; }")));
        Assert.Contains(RootBoundaryAudit.BoundaryViolations(changed, FixturePolicy(original)), error => error.Contains("New privileged root member", StringComparison.Ordinal));
    }

    [Fact]
    public void CompositionExceptionsAreExactOwnedAndExpireAfterMigration()
    {
        var original = Compile((PathName, Fixture));
        var changed = Compile((PathName, Fixture.Insert(Fixture.LastIndexOf('}'), "private object ReadSnapshot() => new object();")));
        var member = Assert.Single(RootBoundaryAudit.Members(changed).ExceptBy(RootBoundaryAudit.Members(original).Select(item => item.Key), item => item.Key));
        var exception = new RootCompositionException(member.Key, "#335", "Temporary diagnostic snapshot bridge", "#337", member.Hash);
        var policy = FixturePolicy(original) with { Composition = [exception] };
        Assert.Empty(RootBoundaryAudit.BoundaryViolations(changed, policy));
        var enlarged = Compile((PathName, changed.SyntaxTrees.First().GetText().ToString().Replace("new object()", "new int[100]", StringComparison.Ordinal)));
        Assert.Contains(RootBoundaryAudit.BoundaryViolations(enlarged, policy), error => error.Contains("Composition exception changed", StringComparison.Ordinal));
        Assert.Contains(RootBoundaryAudit.BoundaryViolations(original, policy), error => error.Contains("Stale composition exception", StringComparison.Ordinal));
    }

    [Fact]
    public void RemovedMembersMustBeRetiredAndCannotBeReintroduced()
    {
        var original = Compile((PathName, Fixture));
        var changed = Compile((PathName, Fixture.Replace("private int _old;", "", StringComparison.Ordinal).Replace("=> _old", "=> 0", StringComparison.Ordinal)));
        var policy = FixturePolicy(original);
        var field = RootBoundaryAudit.Members(original).Single(member => member.Kind == "field");
        Assert.Contains(RootBoundaryAudit.BoundaryViolations(changed, policy), error => error.Contains("must be marked retired", StringComparison.Ordinal));
        var retired = policy with { RetiredMembers = [field.Key] };
        Assert.Empty(RootBoundaryAudit.BoundaryViolations(changed, retired));
        Assert.Contains(RootBoundaryAudit.BoundaryViolations(original, retired), error => error.Contains("New privileged root member", StringComparison.Ordinal));
    }

    [Fact]
    public void CensusSeparatesDirectRootStateFromNestedHelpersAndBindsCrossFileReferences()
    {
        var compilation = Compile((PathName, Fixture), ("game/diagnostics/OtherName.cs",
            "namespace OBP.Game; public partial class OBPGame { private int Inspect() => _old; private class Nested { int _nested; } }"));
        var census = RootBoundaryAudit.Census(compilation);
        Assert.Equal(2, census.Files); Assert.Equal(2, census.Methods); Assert.Equal(1, census.Fields);
        Assert.Equal(1, census.MutableFields); Assert.Equal(1, census.ExposedMembers);
        Assert.Contains(census.CrossPartialReferences, reference => reference.From.EndsWith("OtherName.cs", StringComparison.Ordinal) && reference.Member.EndsWith("._old", StringComparison.Ordinal) && !reference.External);
    }
}
