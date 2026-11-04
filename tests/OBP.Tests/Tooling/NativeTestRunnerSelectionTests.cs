using System.Reflection;
using System.Text.RegularExpressions;

namespace OBP.Tests;

public sealed class NativeTestRunnerSelectionTests
{
    [Theory]
    [InlineData("tools/uya-shock-cannon-native-trace.py")]
    [InlineData("tools/trilogy-skeleton-native-trace.py")]
    [InlineData("tools/rac1-veldin-spawn-probe.ps1")]
    public void FocusedRunnerFiltersResolveToClassesWithDiscoveredTestMethods(string path)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RepoPaths.Root)) + Path.DirectorySeparatorChar;
        string scriptPath = Path.GetFullPath(Path.Combine(root, path));
        Assert.StartsWith(root, scriptPath, StringComparison.Ordinal);
        string script = File.ReadAllText(scriptPath);
        var filter = Assert.Single(Regex.Matches(script, @"FullyQualifiedName~(?<type>[A-Za-z0-9_.]+)",
            RegexOptions.None, TimeSpan.FromSeconds(1)).Cast<Match>());
        string name = filter.Groups["type"].Value;
        var selected = Assert.Single(Assembly.GetExecutingAssembly().GetTypes(), type => type.Name == name || type.FullName == name);
        Assert.Contains(selected.GetMethods(), method => method.GetCustomAttributesData()
            .Any(attribute => typeof(FactAttribute).IsAssignableFrom(attribute.AttributeType)));
    }
}
