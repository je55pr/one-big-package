using OBP.Game.Diagnostics;

namespace OBP.Tests;

public sealed class DiagnosticCleanupTests
{
    [Fact]
    public void EveryRestorationRunsAndEachFailureIsRetainedInOrder()
    {
        var completed = new List<int>();
        var first = new IOException("first restoration failed");
        var second = new InvalidOperationException("second restoration failed");
        var error = Assert.Throws<AggregateException>(() => DiagnosticCleanup.Run("fixture cleanup",
            () => { completed.Add(1); throw first; },
            () => { completed.Add(2); throw second; },
            () => completed.Add(3)));
        Assert.Equal(new[] { 1, 2, 3 }, completed);
        Assert.Equal(new Exception[] { first, second }, error.InnerExceptions);
    }
}
