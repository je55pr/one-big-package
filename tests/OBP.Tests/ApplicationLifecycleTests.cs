using OBP.Godot;

namespace OBP.Tests;

public sealed class ApplicationLifecycleTests
{
    [Fact]
    public void InteractiveEscapeFallbackStaysAlive()
    {
        Assert.Equal(
            ApplicationLifecycle.EscapeFallback.StayAlive,
            ApplicationLifecycle.ResolveEscapeFallback(interactiveSurface: true));
    }

    [Fact]
    public void NonInteractiveEscapeFallbackCanQuit()
    {
        Assert.Equal(
            ApplicationLifecycle.EscapeFallback.Quit,
            ApplicationLifecycle.ResolveEscapeFallback(interactiveSurface: false));
    }
}
