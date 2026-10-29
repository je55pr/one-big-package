using OBP.Game;

namespace OBP.Tests;

public sealed class ReplayInputLeaseTests
{
    [Fact]
    public void RetiredWorldCannotConsumeReplayInputBeforeOrAfterReplacementBinds()
    {
        object first = new(), second = new();
        object? current = first;
        var observed = new List<int>();
        Func<int, int>? installed = null;
        using var old = new ReplayInputLease<int>(() => ReferenceEquals(current, first), input =>
        {
            observed.Add(input);
            return input + 10;
        }, filter => { if (installed == filter) installed = null; });
        installed = old.Filter;
        var retained = installed;
        Assert.Equal(11, retained(1));
        current = null;
        Assert.Equal(0, retained(2));
        current = second;
        using var next = new ReplayInputLease<int>(() => ReferenceEquals(current, second), input =>
        {
            observed.Add(input);
            return input + 20;
        }, filter => { if (installed == filter) installed = null; });
        installed = next.Filter;
        old.Dispose();
        Assert.Equal(0, retained(3));
        Assert.Equal(24, installed!(4));
        Assert.Equal(new[] { 1, 4 }, observed);
    }

    [Fact]
    public void OwnerStopDetachesOnceAndRetainedCallbackCannotInspectRetiredScene()
    {
        int detachments = 0, inspections = 0;
        var lease = new ReplayInputLease<int>(() => { inspections++; return true; }, input => input,
            _ => detachments++);
        Func<int, int> retained = lease.Filter;
        Assert.Equal(7, retained(7));
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(0, retained(8));
        Assert.Equal(1, inspections);
        Assert.Equal(1, detachments);
    }
}
