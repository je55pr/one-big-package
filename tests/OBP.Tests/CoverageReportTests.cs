using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using OBP.CoverageTool;

namespace OBP.Tests;

public sealed class CoverageReportTests
{
    [Fact]
    public void ReportsAreSourceRelativeAndARealZeroRemainsMeasuredZero()
    {
        using var fixture = new Fixture();
        fixture.CreateReports(zeroModule: "OBP.IO");
        var result = fixture.Write();
        Assert.Equal(8, result.CoveredLines);
        Assert.Equal(9, result.TotalLines);
        Assert.Equal(0, result.Modules.Single(module => module.Name == "OBP.IO").CoveredLines);
        Assert.Equal("measured", result.Status);
        foreach (string name in new[] { "coverage.cobertura.xml", "coverage.opencover.xml", "summary.json", "summary.md", "index.html" })
        {
            string text = File.ReadAllText(Path.Combine(fixture.Output, name));
            Assert.DoesNotContain(fixture.Root, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw.githubusercontent.com", text, StringComparison.Ordinal);
        }
        Assert.Contains("src/OBP.Core/Fixture.cs", File.ReadAllText(Path.Combine(fixture.Output, "summary.json")), StringComparison.Ordinal);
        Assert.Contains("0.0%", File.ReadAllText(Path.Combine(fixture.Output, "summary.md")), StringComparison.Ordinal);
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Output, "summary.json")));
        Assert.Equal(1, summary.RootElement.GetProperty("Version").GetInt32());
    }

    [Theory]
    [InlineData("OBP.Tests", "src/OBP.Core/Fixture.cs")]
    [InlineData("OBP.Core", "game/scripts/Fixture.cs")]
    [InlineData("OBP.Runtime", "src/OBP.Core/Fixture.cs")]
    public void CrossAssemblySourceAttributionIsRejected(string module, string falseSource)
    {
        using var fixture = new Fixture();
        fixture.CreateReports();
        var cobertura = XDocument.Load(fixture.Cobertura);
        cobertura.Descendants("package").Single(item => (string?)item.Attribute("name") == module)
            .Descendants("class").Single().SetAttributeValue("filename", falseSource);
        cobertura.Save(fixture.Cobertura);
        var open = XDocument.Load(fixture.OpenCover);
        open.Descendants("Module").Single(item => item.Element("ModuleName")?.Value == module)
            .Descendants("File").Single().SetAttributeValue("fullPath", falseSource);
        open.Save(fixture.OpenCover);
        Assert.Throws<InvalidDataException>(fixture.Write);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public void UnlinkedGameScriptCannotBeClaimedAsPortableExecution()
    {
        using var fixture = new Fixture();
        fixture.CreateReports();
        string unlinked = "game/scripts/OBPGame.cs";
        File.WriteAllText(Path.Combine(fixture.Root, unlinked), "public class OBPGame {}");
        var cobertura = XDocument.Load(fixture.Cobertura);
        cobertura.Descendants("package").Single(item => (string?)item.Attribute("name") == "OBP.Tests")
            .Descendants("class").Single().SetAttributeValue("filename", unlinked);
        cobertura.Save(fixture.Cobertura);
        var open = XDocument.Load(fixture.OpenCover);
        open.Descendants("Module").Single(item => item.Element("ModuleName")?.Value == "OBP.Tests")
            .Descendants("File").Single().SetAttributeValue("fullPath", unlinked);
        open.Save(fixture.OpenCover);
        Assert.Contains("outside the declared repository source scope",
            Assert.Throws<InvalidDataException>(fixture.Write).Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Theory]
    [InlineData("OBP.Tests", "src/OBP.Core/Fixture.cs")]
    [InlineData("OBP.Core", "game/scripts/Fixture.cs")]
    [InlineData("OBP.Runtime", "src/OBP.Core/Fixture.cs")]
    public void OpenCoverRejectsWrongModuleSourceIndependently(string module, string falseSource)
    {
        using var fixture = new Fixture();
        fixture.CreateReports();
        var open = XDocument.Load(fixture.OpenCover);
        open.Descendants("Module").Single(item => item.Element("ModuleName")?.Value == module)
            .Descendants("File").Single().SetAttributeValue("fullPath", falseSource);
        open.Save(fixture.OpenCover);
        Assert.Contains("outside the declared repository source scope",
            Assert.Throws<InvalidDataException>(fixture.Write).Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public void MissingCollectionFailsInsteadOfPublishingAZeroReport()
    {
        using var fixture = new Fixture();
        var error = Assert.Throws<InvalidDataException>(fixture.Write);
        Assert.Contains("fresh collector report is missing", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public void MissingExpectedAssemblyAndAnEmptyMeasurementBothFail()
    {
        using var fixture = new Fixture();
        fixture.CreateReports(modules: ["OBP.Core"]);
        Assert.Throws<InvalidDataException>(fixture.Write);
        fixture.CreateReports(zeroModule: "all");
        Assert.Throws<InvalidDataException>(fixture.Write);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public void SonarAndHumanReportCannotDisagreeAboutExecution()
    {
        using var fixture = new Fixture();
        fixture.CreateReports();
        var report = XDocument.Load(fixture.OpenCover);
        report.Descendants("SequencePoint").First().SetAttributeValue("vc", "0");
        report.Save(fixture.OpenCover);
        Assert.Throws<InvalidDataException>(fixture.Write);
    }

    [Fact]
    public void EqualCountsCannotHideDisagreementAboutUncoveredSourceLines()
    {
        using var fixture = new Fixture();
        fixture.CreateReports(zeroModule: "OBP.IO");
        var report = XDocument.Load(fixture.OpenCover);
        report.Descendants("Module").Single(module => module.Element("ModuleName")!.Value == "OBP.IO")
            .Descendants("SequencePoint").Single().SetAttributeValue("sl", "2");
        report.Save(fixture.OpenCover);
        Assert.Throws<InvalidDataException>(fixture.Write);
    }

    [Theory]
    [InlineData("OBP.Core")]
    [InlineData("OBP.IO")]
    [InlineData("OBP.Runtime")]
    [InlineData("OBP.Fusion")]
    [InlineData("OBP.PS2")]
    [InlineData("OBP.RAC1")]
    [InlineData("OBP.RAC2")]
    [InlineData("OBP.RAC3")]
    [InlineData("OBP.Tests")]
    public void EveryDeclaredAssemblyMustProduceAReport(string missing)
    {
        using var fixture = new Fixture();
        fixture.CreateReports();
        foreach (string path in new[] { fixture.Cobertura, fixture.OpenCover })
        {
            var report = XDocument.Load(path);
            if (path == fixture.Cobertura) report.Descendants("package").Single(module => (string?)module.Attribute("name") == missing).Remove();
            else report.Descendants("Module").Single(module => module.Element("ModuleName")!.Value == missing).Remove();
            report.Save(path);
        }
        Assert.Throws<InvalidDataException>(fixture.Write);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Theory]
    [InlineData("../../captures/source.iso")]
    [InlineData("tests/OBP.Tests/Private.cs")]
    [InlineData("src/OBP.Core/../../Private.cs")]
    public void FilesOutsideTheDeclaredProductionScopeAreRejected(string path)
    {
        using var fixture = new Fixture();
        fixture.CreateReports();
        var report = XDocument.Load(fixture.Cobertura);
        report.Descendants("class").First().SetAttributeValue("filename", path);
        report.Save(fixture.Cobertura);
        Assert.Throws<InvalidDataException>(fixture.Write);
    }

    [Fact]
    public void AbsolutePathsOutsideTheCheckoutAreRejected()
    {
        using var fixture = new Fixture();
        fixture.CreateReports();
        var report = XDocument.Load(fixture.Cobertura);
        report.Descendants("class").First().SetAttributeValue("filename", Path.Combine(fixture.Container, "private.cs"));
        report.Save(fixture.Cobertura);
        Assert.Throws<InvalidDataException>(fixture.Write);
    }

    [Fact]
    public void DtdEntitiesAreNotReadFromCollectorOutput()
    {
        using var fixture = new Fixture();
        fixture.CreateReports();
        File.WriteAllText(fixture.Cobertura, "<!DOCTYPE coverage [<!ENTITY extra SYSTEM 'file:///private'>]><coverage>&extra;</coverage>");
        Assert.Throws<XmlException>(fixture.Write);
    }

    [Fact]
    public void InvalidAndNegativeCountsAreNotConvertedToZero()
    {
        using var fixture = new Fixture();
        fixture.CreateReports();
        var report = XDocument.Load(fixture.Cobertura);
        report.Descendants("line").First().SetAttributeValue("hits", "-1");
        report.Save(fixture.Cobertura);
        Assert.Throws<InvalidDataException>(fixture.Write);
    }

    [Fact]
    public void SourceNamesAreEscapedInTheHumanReport()
    {
        using var fixture = new Fixture();
        fixture.CreateReports(fileName: "A&B.cs");
        fixture.Write();
        string html = File.ReadAllText(Path.Combine(fixture.Output, "index.html"));
        Assert.Contains("A&amp;B.cs", html, StringComparison.Ordinal);
        Assert.DoesNotContain("A&B.cs", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitDiagnosticCoveragePermissionsMatchThePortableProjectLinks()
    {
        var project = XDocument.Load(Path.Combine(RepoPaths.Root, "tests", "OBP.Tests", "OBP.Tests.csproj"));
        var links = project.Descendants("Compile").Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .Where(path => path?.StartsWith("../../game/diagnostics/", StringComparison.Ordinal) == true)
            .Select(path => path![6..]).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var permissions = LinkedHostPaths().Select(row => (string)row[1])
            .Where(path => path.StartsWith("game/diagnostics/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.Equal(permissions, links);
    }

    public static IEnumerable<object[]> LinkedHostPaths()
    {
        foreach (string form in new[] { "relative", "deterministic", "source-link" })
            foreach (string path in new[] { "game/scripts/Fixture.cs", "game/diagnostics/DiagnosticContracts.cs",
                "game/diagnostics/DiagnosticInputActions.cs", "game/diagnostics/DiagnosticScenarioRunner.cs", "game/diagnostics/SourceFreeStartupScenario.cs",
                "game/diagnostics/AcceptanceEvidence.cs", "game/diagnostics/DiagnosticScenarioRegistry.cs", "game/diagnostics/Rac3RatchetSeedScenario.cs", "game/diagnostics/LegacyDiagnosticScenario.cs",
                "game/diagnostics/ExistingDiagnosticWorld.cs", "game/diagnostics/WorldSmokeHarness.cs", "game/diagnostics/UiDiagnosticPorts.cs", "game/diagnostics/OrdinaryUiScenario.cs", "game/diagnostics/SourceSetupScenario.cs", "game/diagnostics/StartupVisibilityScenario.cs", "game/diagnostics/Rac1VeldinRetailPauseScenario.cs", "game/diagnostics/Rac1RetailNavigationScenario.cs", "game/diagnostics/DiagnosticProcessScenario.cs", "game/diagnostics/UiDiagnosticHarness.cs", "game/diagnostics/PlayerDiagnosticPorts.cs", "game/diagnostics/PersistentPlayerScenario.cs", "game/diagnostics/PlayerProfileDiagnosticPorts.cs", "game/diagnostics/SharedProfileScenario.cs", "game/diagnostics/PlayerAudioDiagnosticPorts.cs", "game/diagnostics/Rac1PlayerAudioScenario.cs", "game/diagnostics/DiagnosticPlayerAudioCollector.cs", "game/diagnostics/PlayerCapabilityDiagnosticPorts.cs", "game/diagnostics/PlayerCapabilitiesScenario.cs", "game/diagnostics/PlayerCapabilityTrial.cs", "game/diagnostics/PlayerSessionDiagnosticHarness.cs", "game/diagnostics/PlayerProfileDiagnosticFixtures.cs", "game/diagnostics/Rac1DiagnosticCollision.cs", "game/diagnostics/MovementDiagnosticPorts.cs", "game/diagnostics/MovementDiagnosticDriver.cs", "game/diagnostics/MovementScenarioSupport.cs", "game/diagnostics/DiagnosticPhysicalInput.cs", "game/diagnostics/MovementDiagnosticHarness.cs", "game/diagnostics/MovementScenario.cs", "game/diagnostics/Rac1AirborneRegression.cs", "game/diagnostics/Rac1EnvironmentalDeathRegression.cs", "game/diagnostics/Rac1LandingRegression.cs", "game/diagnostics/Rac1NovalisSwimScenario.cs", "game/diagnostics/Rac1SlopeJumpRegression.cs", "game/diagnostics/Rac1SmallStepRegression.cs", "game/diagnostics/Rac1SwingshotRegression.cs", "game/diagnostics/Rac1TraversalContactRegression.cs", "game/diagnostics/ControllerWeaponInputRegression.cs",
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
        "game/diagnostics/Rac1CampaignDiagnosticPorts.cs" })
                yield return new object[] { form, path };
    }

    [Theory]
    [MemberData(nameof(LinkedHostPaths))]
    public void LinkedHostSourceAcceptsRepositoryRelativeDeterministicAndForkSourceLinkPaths(string form, string relative)
    {
        using var fixture = new Fixture("test-owner/fork");
        fixture.CreateReports();
        string path = Path.Combine(fixture.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "public class Fixture {}\n");
        string reported = form switch
        {
            "deterministic" => "/_/" + relative,
            "source-link" => "https://raw.githubusercontent.com/TEST-OWNER/FORK/commit/" + relative,
            _ => relative
        };
        var cobertura = XDocument.Load(fixture.Cobertura);
        cobertura.Descendants("package").Single(module => (string?)module.Attribute("name") == "OBP.Tests")
            .Descendants("class").Single().SetAttributeValue("filename", reported);
        cobertura.Save(fixture.Cobertura);
        var open = XDocument.Load(fixture.OpenCover);
        open.Descendants("Module").Single(module => module.Element("ModuleName")!.Value == "OBP.Tests")
            .Descendants("File").Single().SetAttributeValue("fullPath", reported);
        open.Save(fixture.OpenCover);
        Assert.Contains(fixture.Write().Files, file => file.Module == "OBP.Tests" && file.File == relative);
    }

    [Theory]
    [InlineData("GodotDiagnosticAdapters.cs")]
    [InlineData("SourceFreeStartupProbe.cs")]
    [InlineData("UnrelatedScenario.cs")]
    public void UnlinkedDiagnosticFilesRemainOutsideTheCoverageScope(string name)
    {
        using var fixture = new Fixture();
        fixture.CreateReports();
        string relative = "game/diagnostics/" + name;
        string path = Path.Combine(fixture.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "public class Fixture {}\n");
        var report = XDocument.Load(fixture.Cobertura);
        report.Descendants("class").First().SetAttributeValue("filename", relative);
        report.Save(fixture.Cobertura);
        Assert.Throws<InvalidDataException>(fixture.Write);
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("assembly")]
    [InlineData("duplicate-uid")]
    [InlineData("unknown-fileid")]
    [InlineData("zero-line")]
    [InlineData("schema")]
    public void MalformedCollectorIdentitySchemaAndLineReferencesFailBeforePublishing(string defect)
    {
        using var fixture = new Fixture();
        fixture.CreateReports();
        var cobertura = XDocument.Load(fixture.Cobertura);
        var open = XDocument.Load(fixture.OpenCover);
        switch (defect)
        {
            case "repository":
                cobertura.Descendants("class").First().SetAttributeValue("filename",
                "https://raw.githubusercontent.com/unexpected/repository/commit/src/OBP.Core/Fixture.cs"); break;
            case "assembly": cobertura.Descendants("package").First().SetAttributeValue("name", "Undeclared"); break;
            case "duplicate-uid": open.Descendants("Files").First().Add(new XElement(open.Descendants("File").First())); break;
            case "unknown-fileid": open.Descendants("SequencePoint").First().SetAttributeValue("fileid", "999"); break;
            case "zero-line": cobertura.Descendants("line").First().SetAttributeValue("number", "0"); break;
            case "schema": open.Root!.Name = "Unexpected"; break;
        }
        cobertura.Save(fixture.Cobertura); open.Save(fixture.OpenCover);
        Assert.Throws<InvalidDataException>(fixture.Write);
        Assert.False(Directory.Exists(fixture.Output));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _repository;
        public string Container { get; } = Path.Combine(Path.GetTempPath(), "obp-coverage-tests-" + Guid.NewGuid().ToString("N"));
        public string Root => Path.Combine(Container, "repo");
        public string Output => Path.Combine(Container, "output");
        public string Cobertura => Path.Combine(Container, "input.cobertura.xml");
        public string OpenCover => Path.Combine(Container, "input.opencover.xml");
        public Fixture(string repository = "je55pr/one-big-package")
        {
            _repository = repository;
            Directory.CreateDirectory(Root);
        }
        public CoverageSummary Write() => CoverageReport.Write(Root, Cobertura, OpenCover, Output, _repository);

        public void CreateReports(string? zeroModule = null, string[]? modules = null, string fileName = "Fixture.cs")
        {
            modules ??= ["OBP.Core", "OBP.IO", "OBP.Runtime", "OBP.Fusion", "OBP.PS2", "OBP.RAC1", "OBP.RAC2", "OBP.RAC3", "OBP.Tests"];
            string projectDirectory = Path.Combine(Root, "tests", "OBP.Tests");
            Directory.CreateDirectory(projectDirectory);
            new XDocument(new XElement("Project", new XElement("ItemGroup",
                new XElement("Compile", new XAttribute("Include", "../../game/scripts/" + fileName),
                    new XAttribute("Link", "Game/" + fileName)))))
                .Save(Path.Combine(projectDirectory, "OBP.Tests.csproj"));
            var packages = new XElement("packages"); var openModules = new XElement("Modules");
            foreach (string module in modules)
            {
                string file = module == "OBP.Tests" ? "game/scripts/" + fileName : "src/" + module + "/" + fileName;
                string path = Path.Combine(Root, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "public class Fixture {}\n");
                int hits = zeroModule == "all" || zeroModule == module ? 0 : 1;
                string reported = module == "OBP.Runtime" ? "https://raw.githubusercontent.com/" + _repository + "/commit/" + file : path;
                packages.Add(new XElement("package", new XAttribute("name", module), new XElement("classes",
                    new XElement("class", new XAttribute("filename", reported), new XElement("lines",
                        new XElement("line", new XAttribute("number", "1"), new XAttribute("hits", hits)))))));
                openModules.Add(new XElement("Module", new XElement("ModuleName", module),
                    new XElement("ModulePath", Path.Combine(Root, "bin", module + ".dll")),
                    new XElement("Files", new XElement("File", new XAttribute("uid", "1"), new XAttribute("fullPath", reported))),
                    new XElement("Classes", new XElement("SequencePoint", new XAttribute("fileid", "1"),
                        new XAttribute("sl", "1"), new XAttribute("vc", hits)))));
            }
            new XDocument(new XElement("coverage", new XElement("sources", new XElement("source", Root)), packages)).Save(Cobertura);
            new XDocument(new XElement("CoverageSession", openModules)).Save(OpenCover);
        }

        public void Dispose()
        {
            string absolute = Path.GetFullPath(Container);
            var directory = new DirectoryInfo(absolute);
            if (directory.Parent?.FullName != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) ||
                !directory.Name.StartsWith("obp-coverage-tests-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(directory.Name[19..], "N", out _))
                throw new InvalidOperationException("Refusing cleanup outside the owned temporary coverage directory.");
            Directory.Delete(absolute, recursive: true);
        }
    }
}
