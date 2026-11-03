using OBP.Game.Diagnostics;

namespace OBP.Tests;

public sealed class DiagnosticReproPathTests
{
    private static readonly string Captures = Path.Combine(Path.GetTempPath(), "obp-captures");

    [Fact]
    public void APlainNameResolvesInsideReproDirectory() =>
        Assert.Equal(Path.GetFullPath(Path.Combine(Captures, "repros", "state.json")),
            DiagnosticReproPath.Resolve(Captures, "state.json"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../escape.json")]
    [InlineData("..\\escape.json")]
    [InlineData("nested/state.json")]
    [InlineData("C:\\absolute.json")]
    public void NamesThatAreNotPlainFileNamesAreRejected(string name) =>
        Assert.Throws<ArgumentException>(() => DiagnosticReproPath.Resolve(Captures, name));
}
