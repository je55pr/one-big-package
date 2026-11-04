namespace OBP.Game.Diagnostics;

/// <summary>A non-owning initial-world view for legacy application-scoped diagnostics that can travel/respawn.</summary>
public sealed class ExistingDiagnosticWorld(Func<DiagnosticWorldSnapshot> read, CancellationToken lifetime = default) : IDiagnosticWorldSetup
{
    public Task<IDiagnosticWorldLease> PrepareAsync(string destinationId, bool fresh, DiagnosticSourceAccess sourceAccess, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var world = read();
        if (fresh || world.DestinationId != destinationId || world.Content is null || string.IsNullOrWhiteSpace(world.WorldInstanceId))
            throw new DiagnosticUnavailableException("The requested initial diagnostic world is unavailable.");
        return Task.FromResult<IDiagnosticWorldLease>(new View(lifetime));
    }
    private sealed class View(CancellationToken lifetime) : IDiagnosticWorldLease
    {
        public CancellationToken Lifetime => lifetime;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
