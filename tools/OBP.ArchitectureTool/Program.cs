using System.Text.Json;
using OBP.ArchitectureTool;

if (args.Length != 2 || args[0] is not ("census" or "check"))
{
    Console.Error.WriteLine("Usage: OBP.ArchitectureTool census|check REPOSITORY_ROOT");
    return 2;
}
try
{
    string root = Path.GetFullPath(args[1]);
    var builds = RootBoundaryAudit.ReadBuilds(root);
    var compilations = builds.Select(build => build.Compilation).ToArray();
    if (args[0] == "census")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { Census = RootBoundaryAudit.Census(compilations[0]),
            Configurations = builds.Select(build => new { build.Name, build.Symbols, Census = RootBoundaryAudit.Census(build.Compilation) }),
            Members = RootBoundaryAudit.Members(compilations), References = RootBoundaryAudit.References(compilations) }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    var policy = JsonSerializer.Deserialize<RootBoundaryPolicy>(File.ReadAllText(Path.Combine(root, "tests", "OBP.Tests", "root-boundary-policy.json")))
        ?? throw new InvalidDataException("Missing root boundary policy.");
    var errors = RootBoundaryAudit.Violations(compilations, policy);
    foreach (string error in errors) Console.Error.WriteLine(error);
    Console.WriteLine($"Root boundary: {errors.Length} violation(s).");
    return errors.Length == 0 ? 0 : 1;
}
catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
{
    Console.Error.WriteLine("Root boundary audit failed: " + error.Message);
    return 2;
}
