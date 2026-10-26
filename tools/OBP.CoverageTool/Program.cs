using OBP.CoverageTool;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: OBP.CoverageTool ROOT COBERTURA OPENCOVER OUTPUT");
    return 2;
}
try
{
    var report = CoverageReport.Write(args[0], args[1], args[2], args[3],
        Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? "je55pr/one-big-package");
    Console.WriteLine($"Portable coverage: {report.CoveredLines}/{report.TotalLines} source lines across {report.Modules.Count} measured modules.");
    return 0;
}
catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or System.Xml.XmlException)
{
    Console.Error.WriteLine("Coverage reporting failed: " + error.Message);
    return 1;
}
