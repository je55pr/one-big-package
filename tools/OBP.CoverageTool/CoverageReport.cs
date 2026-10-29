using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace OBP.CoverageTool;

public sealed record FileCoverage(string Module, string File, int CoveredLines, int TotalLines);
public sealed record ModuleCoverage(string Name, string Scope, int CoveredLines, int TotalLines);
public sealed record CoverageSummary(string Measurement, IReadOnlyList<ModuleCoverage> Modules, IReadOnlyList<FileCoverage> Files)
{
    public int Version => 1;
    public string Status => "measured";
    public int CoveredLines => Files.Sum(file => file.CoveredLines);
    public int TotalLines => Files.Sum(file => file.TotalLines);
}

/// <summary>Validate collector output and publish only source-relative, payload-free coverage artifacts.</summary>
public static class CoverageReport
{
    private static readonly HashSet<string> AllowedModules = new(StringComparer.Ordinal)
    { "OBP.Core", "OBP.IO", "OBP.Runtime", "OBP.Fusion", "OBP.PS2", "OBP.RAC1", "OBP.RAC2", "OBP.RAC3", "OBP.Tests" };
    // Only the engine-free diagnostic sources linked into OBP.Tests are measured.
    private static readonly HashSet<string> LinkedDiagnosticSources = new(StringComparer.Ordinal)
    { "game/diagnostics/DiagnosticContracts.cs", "game/diagnostics/DiagnosticInputActions.cs",
        "game/diagnostics/DiagnosticScenarioRunner.cs", "game/diagnostics/SourceFreeStartupScenario.cs",
        "game/diagnostics/AcceptanceEvidence.cs", "game/diagnostics/DiagnosticScenarioRegistry.cs", "game/diagnostics/Rac3RatchetSeedScenario.cs",
        "game/diagnostics/LegacyDiagnosticScenario.cs", "game/diagnostics/ExistingDiagnosticWorld.cs", "game/diagnostics/WorldSmokeHarness.cs", "game/diagnostics/UiDiagnosticPorts.cs", "game/diagnostics/OrdinaryUiScenario.cs", "game/diagnostics/SourceSetupScenario.cs", "game/diagnostics/StartupVisibilityScenario.cs", "game/diagnostics/Rac1VeldinRetailPauseScenario.cs", "game/diagnostics/Rac1RetailNavigationScenario.cs", "game/diagnostics/DiagnosticProcessScenario.cs", "game/diagnostics/UiDiagnosticHarness.cs", "game/diagnostics/PlayerDiagnosticPorts.cs", "game/diagnostics/PersistentPlayerScenario.cs", "game/diagnostics/PlayerProfileDiagnosticPorts.cs", "game/diagnostics/SharedProfileScenario.cs", "game/diagnostics/PlayerAudioDiagnosticPorts.cs", "game/diagnostics/Rac1PlayerAudioScenario.cs", "game/diagnostics/DiagnosticPlayerAudioCollector.cs", "game/diagnostics/PlayerCapabilityDiagnosticPorts.cs", "game/diagnostics/PlayerCapabilitiesScenario.cs", "game/diagnostics/PlayerCapabilityTrial.cs", "game/diagnostics/PlayerSessionDiagnosticHarness.cs", "game/diagnostics/PlayerProfileDiagnosticFixtures.cs", "game/diagnostics/Rac1DiagnosticCollision.cs", "game/diagnostics/MovementDiagnosticPorts.cs", "game/diagnostics/MovementDiagnosticDriver.cs", "game/diagnostics/MovementScenarioSupport.cs", "game/diagnostics/DiagnosticPhysicalInput.cs", "game/diagnostics/MovementDiagnosticHarness.cs", "game/diagnostics/MovementScenario.cs", "game/diagnostics/Rac1AirborneRegression.cs", "game/diagnostics/Rac1EnvironmentalDeathRegression.cs", "game/diagnostics/Rac1LandingRegression.cs", "game/diagnostics/Rac1NovalisSwimScenario.cs", "game/diagnostics/Rac1SlopeJumpRegression.cs", "game/diagnostics/Rac1SmallStepRegression.cs", "game/diagnostics/Rac1SwingshotRegression.cs", "game/diagnostics/Rac1TraversalContactRegression.cs", "game/diagnostics/ControllerWeaponInputRegression.cs",
        "game/diagnostics/Rac1EnemyDiagnosticPorts.cs",
        "game/diagnostics/Rac1AridiaDiagnosticPorts.cs",
        "game/diagnostics/Rac1AridiaScenario.cs",
        "game/diagnostics/Rac1FrogDiagnosticPorts.cs",
        "game/diagnostics/Rac1FrogScenario.cs",
        "game/diagnostics/Rac1AlienQueenScenario.cs",
        "game/diagnostics/Rac1CombatDiagnosticPorts.cs",
        "game/diagnostics/Rac1CombatAssertions.cs",
        "game/diagnostics/Rac1CombatScenarioSupport.cs",
        "game/diagnostics/Rac1CombatContractScenario.cs",
        "game/diagnostics/Rac1VeldinScenario.cs",
        "game/diagnostics/Rac1NovalisEnemyScenarios.cs",
        "game/diagnostics/Rac1EncounterScenarioSupport.cs",
        "game/diagnostics/Rac1EnemyScenario.cs",
        "game/diagnostics/Rac1EncounterInput.cs",
        "game/diagnostics/Rac1WrenchApproachScenario.cs", "game/diagnostics/DiagnosticCleanup.cs",
        "game/diagnostics/Rac1EncounterDiagnosticHarness.cs",
        "game/diagnostics/Rac1BombAcceptancePorts.cs",
        "game/diagnostics/Rac1BombGloveScenario.cs",
        "game/diagnostics/Rac1WeaponDiagnosticPorts.cs",
        "game/diagnostics/Rac1WrenchScenario.cs",
        "game/diagnostics/WeaponDiagnosticHarness.cs",
        "game/diagnostics/Rac1AdditionalWeaponDiagnosticPorts.cs",
        "game/diagnostics/Rac1WeaponScenarioSupport.cs",
        "game/diagnostics/Rac1ArsenalScenario.cs",
        "game/diagnostics/Rac1WeaponsScenario.cs",
        "game/diagnostics/FusionWeaponDiagnosticPorts.cs",
        "game/diagnostics/FusionWeaponDiagnosticHarness.cs",
        "game/diagnostics/FusionWeaponScenarioSupport.cs",
        "game/diagnostics/FusionWeaponsScenario.cs",
        "game/diagnostics/FusionWeaponRecordingScenario.cs",
        "game/diagnostics/Rac1LootDiagnosticHarness.cs",
        "game/diagnostics/Rac1PlacedBoltScenario.cs",
        "game/diagnostics/Rac1CrateScenario.cs",
        "game/diagnostics/Rac1LootDiagnosticPorts.cs",
        "game/diagnostics/Rac1CampaignDiagnosticHarness.cs",
        "game/diagnostics/Rac1WorldRouteDiagnosticHarness.cs",
        "game/diagnostics/TrilogyGameplayScenario.cs",
        "game/diagnostics/TrilogyGameplayDiagnosticPorts.cs",
        "game/diagnostics/MovementStateScenario.cs",
        "game/diagnostics/MovementStateDiagnosticPorts.cs",
        "game/diagnostics/BugReporterScenario.cs",
        "game/diagnostics/BugReporterDiagnosticPorts.cs",
        "game/diagnostics/ReconciliationCaptureScenario.cs",
        "game/diagnostics/PlaytestRecordingScenario.cs",
        "game/diagnostics/CaptureDiagnosticHarness.cs",
        "game/diagnostics/CaptureDiagnosticPorts.cs",
        "game/diagnostics/FusionLabTraversalScenario.cs",
        "game/diagnostics/Rac2CrateDiagnosticProbe.cs",
        "game/diagnostics/ShotListCliService.cs",
        "game/diagnostics/StressSwitchCliService.cs",
        "game/diagnostics/Rac1MetropolisTaxiScenario.cs",
        "game/diagnostics/Rac1NovalisRouteScenario.cs",
        "game/diagnostics/Rac1NovalisMusicScenario.cs",
        "game/diagnostics/Rac1WorldRouteLifetimeScenario.cs",
        "game/diagnostics/Rac1TaxiDiagnosticPorts.cs",
        "game/diagnostics/Rac1NovalisDiagnosticPorts.cs",
        "game/diagnostics/Rac1MissionPlayScenario.cs",
        "game/diagnostics/Rac1CampaignScenario.cs",
        "game/diagnostics/Rac1CampaignScenarioAssertions.cs",
        "game/diagnostics/Rac1CampaignSaveDiagnostics.cs",
        "game/diagnostics/Rac1CampaignDiagnosticPorts.cs" };
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static CoverageSummary Write(string root, string coberturaPath, string openCoverPath, string output,
        string sourceRepository = "je55pr/one-big-package")
    {
        root = Path.GetFullPath(root);
        var cobertura = Read(coberturaPath);
        var openCover = Read(openCoverPath);
        if (cobertura.Root?.Name != "coverage" || openCover.Root?.Name != "CoverageSession")
            throw new InvalidDataException("Collector output has an unsupported coverage schema.");
        var lines = new Dictionary<(string Module, string File), Dictionary<int, bool>>();
        var measuredModules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in cobertura.Descendants("package"))
        {
            string module = Required(package, "name");
            if (!AllowedModules.Contains(module)) throw new InvalidDataException("Coverage contains an assembly outside the declared scope.");
            measuredModules.Add(module);
            foreach (var type in package.Descendants("class"))
            {
                string file = SourcePath(root, Required(type, "filename"), sourceRepository);
                type.SetAttributeValue("filename", file);
                if (!lines.TryGetValue((module, file), out var fileLines)) lines[(module, file)] = fileLines = [];
                foreach (var line in type.Element("lines")?.Elements("line") ?? [])
                {
                    int number = NonNegative(Required(line, "number"));
                    if (number == 0) throw new InvalidDataException("Coverage contains an invalid source line.");
                    bool covered = NonNegative(Required(line, "hits")) > 0;
                    fileLines[number] = fileLines.GetValueOrDefault(number) || covered;
                }
            }
        }
        var openModules = openCover.Descendants("ModuleName").Select(node => node.Value).ToHashSet(StringComparer.Ordinal);
        if (!measuredModules.SetEquals(openModules) || !measuredModules.SetEquals(AllowedModules))
            throw new InvalidDataException("Coverage instrumentation is missing expected assemblies or report formats disagree.");
        var openLines = new Dictionary<(string Module, string File), Dictionary<int, bool>>();
        foreach (var module in openCover.Descendants("Module"))
        {
            string name = module.Element("ModuleName")?.Value ?? throw new InvalidDataException("Coverage is missing a module name.");
            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in module.Descendants("File"))
            {
                string path = SourcePath(root, Required(file, "fullPath"), sourceRepository);
                file.SetAttributeValue("fullPath", path);
                if (!paths.TryAdd(Required(file, "uid"), path)) throw new InvalidDataException("Coverage has duplicate source identifiers.");
            }
            foreach (var point in module.Descendants("SequencePoint"))
            {
                if (!paths.TryGetValue(Required(point, "fileid"), out var path)) throw new InvalidDataException("Coverage refers to an unknown source file.");
                int line = NonNegative(Required(point, "sl"));
                if (!openLines.TryGetValue((name, path), out var hits)) openLines[(name, path)] = hits = [];
                hits[line] = hits.GetValueOrDefault(line) || NonNegative(Required(point, "vc")) > 0;
            }
        }
        if (lines.Count != openLines.Count || lines.Any(file => !openLines.TryGetValue(file.Key, out var points) ||
            file.Value.Count != points.Count || file.Value.Any(line => !points.TryGetValue(line.Key, out bool hit) || hit != line.Value)))
            throw new InvalidDataException("Coverage report formats disagree about source execution.");
        foreach (var module in openCover.Descendants("ModulePath")) module.Value = Path.GetFileName(module.Value.Replace('\\', '/'));
        foreach (var source in cobertura.Descendants("source")) source.Value = ".";
        cobertura.Root.Attribute("timestamp")?.Remove();

        var files = lines.OrderBy(pair => pair.Key.Module, StringComparer.Ordinal).ThenBy(pair => pair.Key.File, StringComparer.Ordinal)
            .Select(pair => new FileCoverage(pair.Key.Module, pair.Key.File, pair.Value.Count(line => line.Value), pair.Value.Count)).ToArray();
        var modules = measuredModules.Order(StringComparer.Ordinal).Select(module => new ModuleCoverage(module, Scope(module),
            files.Where(file => file.Module == module).Sum(file => file.CoveredLines),
            files.Where(file => file.Module == module).Sum(file => file.TotalLines))).ToArray();
        if (modules.Any(module => module.TotalLines == 0) || files.Sum(file => file.CoveredLines) == 0)
            throw new InvalidDataException("Coverage instrumentation produced an empty/unexecuted measurement.");
        var summary = new CoverageSummary("Portable xUnit execution only; retail assets/oracles and Godot engine integration are excluded.", modules, files);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "coverage.cobertura.xml"), cobertura.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "coverage.opencover.xml"), openCover.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "summary.md"), Markdown(summary));
        File.WriteAllText(Path.Combine(output, "index.html"), Html(summary));
        return summary;
    }

    private static XDocument Read(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException("A fresh collector report is missing; test success alone does not prove coverage collection.");
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 100_000_000 });
        return XDocument.Load(reader);
    }

    private static string SourcePath(string root, string reported, string sourceRepository)
    {
        string candidate = reported.Replace('\\', '/');
        string prefix = root.Replace('\\', '/').TrimEnd('/') + '/';
        if (candidate.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            candidate = candidate[prefix.Length..];
        else if (candidate.StartsWith("/_/", StringComparison.Ordinal)) candidate = candidate[3..];
        else if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "raw.githubusercontent.com")
        {
            string[] parts = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Split('/');
            if (parts.Length < 5 || !string.Equals(parts[0] + "/" + parts[1], sourceRepository, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Coverage has an unexpected source repository.");
            candidate = string.Join('/', parts.Skip(3));
        }
        string[] segments = candidate.Split('/');
        if (segments.Any(segment => segment is "" or "." or "..") || candidate.Contains(':') ||
            !(candidate.StartsWith("src/OBP.", StringComparison.Ordinal) || candidate.StartsWith("game/scripts/", StringComparison.Ordinal) || LinkedDiagnosticSources.Contains(candidate)) ||
            !candidate.EndsWith(".cs", StringComparison.Ordinal) || !File.Exists(Path.Combine(root, candidate)))
            throw new InvalidDataException("Coverage has a file outside the declared repository source scope.");
        return candidate;
    }

    private static string Required(XElement node, string name) => node.Attribute(name)?.Value is { Length: > 0 } value
        ? value : throw new InvalidDataException("Coverage is missing a required field.");
    private static int NonNegative(string value) => int.TryParse(value, NumberStyles.None, Invariant, out int number) && number >= 0
        ? number : throw new InvalidDataException("Coverage has an invalid count.");
    private static string Scope(string module) => module switch
    {
        "OBP.Tests" => "Portable host/scenario source compiled into the test assembly; not full app/engine coverage",
        "OBP.RAC1" or "OBP.RAC2" or "OBP.RAC3" => "Source-library synthetic/portable fixtures; not retail equivalence",
        _ => "Engine-neutral library"
    };
    private static string Rate(int covered, int total) => total == 0 ? "n/a" : (100d * covered / total).ToString("0.0", Invariant) + "%";
    private static string Markdown(CoverageSummary summary)
    {
        var text = new StringBuilder("## Portable managed-code coverage\n\n" + summary.Measurement + "\n\n");
        text.AppendLine("| Module | Covered lines | Coverage | Scope |");
        text.AppendLine("|---|---:|---:|---|");
        foreach (var module in summary.Modules)
            text.AppendLine($"| {module.Name} | {module.CoveredLines}/{module.TotalLines} | {Rate(module.CoveredLines, module.TotalLines)} | {module.Scope} |");
        text.AppendLine("\nA measured zero is shown as zero. Missing reports or assemblies fail the command. No arbitrary percentage threshold is imposed.");
        text.AppendLine("\nThe OBP.Game binary, Godot scene/physics/render/audio execution, tool code, xUnit test implementations and generated code are outside this report. Host rows measure only the linked host/scenario source exercised by portable tests.");
        text.AppendLine("\nSonar coverage import is unavailable under the current automatic analysis. Its missing coverage must not be read as measured zero; use this validated report.");
        return text.ToString();
    }
    private static string Html(CoverageSummary summary)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        var text = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>OBP portable coverage</title><style>body{font:16px system-ui;max-width:1100px;margin:40px auto;padding:0 20px}table{border-collapse:collapse;width:100%;margin:24px 0}th,td{padding:8px;text-align:left;border-bottom:1px solid #ccc}td:first-child{font-family:monospace}p{line-height:1.6}</style><h1>Portable managed-code coverage</h1>");
        text.Append("<p>" + E(summary.Measurement) + "</p><p>Missing instrumentation fails collection. No arbitrary coverage target is imposed. Full Godot engine execution and retail equivalence are not measured.</p><p>Sonar coverage import is unavailable under the current automatic analysis. Missing Sonar coverage is not a measured zero; use this report.</p>");
        text.Append("<table><tr><th>Module</th><th>Covered lines</th><th>Coverage</th><th>Scope</th></tr>");
        foreach (var module in summary.Modules)
            text.Append($"<tr><td>{E(module.Name)}</td><td>{module.CoveredLines}/{module.TotalLines}</td><td>{Rate(module.CoveredLines, module.TotalLines)}</td><td>{E(module.Scope)}</td></tr>");
        text.Append("</table><h2>Source files</h2><table><tr><th>File</th><th>Module</th><th>Covered lines</th><th>Coverage</th></tr>");
        foreach (var file in summary.Files)
            text.Append($"<tr><td>{E(file.File)}</td><td>{E(file.Module)}</td><td>{file.CoveredLines}/{file.TotalLines}</td><td>{Rate(file.CoveredLines, file.TotalLines)}</td></tr>");
        return text.Append("</table></html>").ToString();
    }
}
