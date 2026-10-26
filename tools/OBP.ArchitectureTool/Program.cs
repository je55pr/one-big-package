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
    var compilation = RootBoundaryAudit.Read(root);
    if (args[0] == "census")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { Census = RootBoundaryAudit.Census(compilation), Members = RootBoundaryAudit.Members(compilation) }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    var policy = JsonSerializer.Deserialize<RootBoundaryPolicy>(File.ReadAllText(Path.Combine(root, "tests", "OBP.Tests", "root-boundary-policy.json")))
        ?? throw new InvalidDataException("Missing root boundary policy.");
    var errors = RootBoundaryAudit.Violations(compilation, policy);
    foreach (string error in errors) Console.Error.WriteLine(error);
    Console.WriteLine($"Root boundary: {errors.Length} violation(s).");
    return errors.Length == 0 ? 0 : 1;
}
catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
{
    Console.Error.WriteLine("Root boundary audit failed: " + error.Message);
    return 2;
}
