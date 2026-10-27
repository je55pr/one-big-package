using Godot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OBP.Game;

namespace OBP.Tests;

public sealed class AutomationDesktopSafetyTests
{
    [Theory]
    [InlineData("rac1-campaign-smoke.ps1")]
    [InlineData("rac1-crate-smoke.ps1")]
    [InlineData("rac1-placed-bolt-smoke.ps1")]
    public void CampaignAndLootWrappersRequireAnExplicitRenderedOptIn(string name)
    {
        string script = File.ReadAllText(Path.Combine(RepoPaths.Root, "tools", name));
        Assert.Contains("[switch]$Render", script, StringComparison.Ordinal);
        Assert.Contains("if ($Render) { @(Get-ObpWindowArguments) } else { @('--headless') }", script, StringComparison.Ordinal);
        Assert.Contains("'--no-profile'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void HumanMissionWrapperRejectsUnattendedLaunchBeforeBuildingOrOpeningAWindow()
    {
        string script = File.ReadAllText(Path.Combine(RepoPaths.Root, "tools", "rac1-mission-play-smoke.ps1"));
        int guard = script.IndexOf("if (-not $Interactive)", StringComparison.Ordinal);
        Assert.True(guard > 0);
        Assert.True(guard < script.IndexOf("build.ps1", StringComparison.Ordinal));
        Assert.True(guard < script.IndexOf("'--windowed'", StringComparison.Ordinal));
        Assert.Contains("'--no-profile'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceSetupCheckIsAutomatedWhileRetainingOrdinarySourceRequirements()
    {
        string[] arguments = ["--source-setup-check"];
        Assert.True(AutomationRunPolicy.IsAutomated(arguments, []));
        Assert.False(SourceSetupPolicy.IsDiagnostic(CommandLineArgs.Parse(arguments), arguments, []));
    }

    [Fact]
    public void EveryCurrentAndFutureSmokeFlagIsIsolated()
    {
        var flags = typeof(CommandLineArgs).GetProperties().Where(property => property.PropertyType == typeof(bool) && property.Name.EndsWith("Smoke", StringComparison.Ordinal))
            .Select(property => "--" + System.Text.RegularExpressions.Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant()).ToArray();
        Assert.Contains("--player-session-smoke", flags);
        Assert.Contains("--player-capabilities-smoke", flags);
        Assert.Contains("--default-weapons-smoke", flags);
        Assert.All(flags.Append("--future-unknown-smoke").Append("--smoke").Append("--automation"),
            flag => Assert.True(AutomationRunPolicy.IsAutomated([flag], []), flag));
    }

    [Theory]
    [InlineData("--capture-frame", "120")]
    [InlineData("--shots", "shots.json")]
    [InlineData("--stress-switch", "1,3")]
    [InlineData("--replay-forward", "120")]
    [InlineData("--replay-playtest", "run.jsonl.gz")]
    [InlineData("--replay-bug-report", "report.json")]
    [InlineData("--test-scene", "controller-smoke")]
    public void BatchAndReplayEntrypointsNeverAcquireDesktopInput(string flag, string value) =>
        Assert.True(AutomationRunPolicy.IsAutomated([flag, value], []));

    [Theory]
    [InlineData("--headless")]
    [InlineData("--quit-after")]
    [InlineData("res://scenes/ControllerSmoke.tscn")]
    [InlineData("res://tests/ci_bootstrap_smoke.gd")]
    [InlineData("res://tests/bug_inspect_smoke.gd")]
    public void DirectDiagnosticScenesAndHeadlessRunsAreSafeWithoutUserFlags(string engineArg) =>
        Assert.True(AutomationRunPolicy.IsAutomated([], [engineArg]));

    [Fact]
    public void InteractivePlayRecordingAndMarkerInspectionRetainDesktopInput()
    {
        Assert.False(AutomationRunPolicy.IsAutomated([], []));
        Assert.False(AutomationRunPolicy.IsAutomated(["--destination", "rac1:LEVEL0"], []));
        Assert.False(AutomationRunPolicy.IsAutomated(["--record-playtest", "manual.jsonl.gz"], []));
        Assert.False(AutomationRunPolicy.IsAutomated(["--replay-bug-report", "report.json", "--playtest-inspect-marker"], []));
        Assert.True(AutomationRunPolicy.IsAutomated(["--replay-bug-report", "report.json", "--playtest-inspect-marker", "--bug-report-smoke"], []));
    }

    [Fact]
    public void AutomatedMouseRequestsNeverReachTheDesktopWhileInteractiveModesArePreserved()
    {
        foreach (var mode in Enum.GetValues<Input.MouseModeEnum>())
        {
            Assert.Equal(Input.MouseModeEnum.Visible, ApplicationPointer.ResolveDesktopMode(mode, automated: true));
            Assert.Equal(mode, ApplicationPointer.ResolveDesktopMode(mode, automated: false));
        }
    }

    [Fact]
    public void HostAndDiagnosticsCannotBypassThePointerBoundary()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        foreach (var tree in compilation.SyntaxTrees.Where(tree => tree.FilePath.StartsWith("game/", StringComparison.Ordinal) &&
            tree.FilePath != "game/scripts/ApplicationPointer.cs"))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var assignment in tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                var symbol = model.GetSymbolInfo(assignment.Left).Symbol as IPropertySymbol;
                Assert.False(symbol is { Name: "MouseMode" } && symbol.ContainingType.ToDisplayString() is "Godot.Input" or "Godot.InputInstance",
                    $"{tree.FilePath}: desktop mouse writes must go through ApplicationPointer.");
            }
            foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var method = model.GetSymbolInfo(call).Symbol as IMethodSymbol;
                Assert.False(method is { Name: "MouseSetMode" } && method.ContainingType.ToDisplayString() == "Godot.DisplayServer",
                    $"{tree.FilePath}: direct DisplayServer mouse capture bypasses test isolation.");
            }
        }
    }
}
