using Godot;

namespace OBP.Game.Diagnostics;

internal sealed class GodotFusionLabTraversalFixture(PlayerHost player, Func<long> processFrame,
    IDiagnosticWorldObservations worlds, Action selector, Func<bool> enterLab, Func<bool> hasPlayer) : IFusionLabTraversalFixture
{
    public FusionLabTraversalPose Read()
    {
        if (!GodotObject.IsInstanceValid(player) || !player.IsInsideTree() || worlds.ReadWorld().DestinationId != "obp:fusion-lab")
            throw new DiagnosticUnavailableException("The Lab traversal player/world was retired.");
        return new(player.Position.X, player.Position.Z, processFrame());
    }
    public IDisposable Drive(Func<FusionLabTraversalPose, System.Numerics.Vector2> input) => new InputLease(player, () => input(Read()));
    public async Task<bool> ReloadDeferredAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>();
        using var cancellation = token.Register(() => completion.TrySetCanceled(token));
        Callable.From(() =>
        {
            if (token.IsCancellationRequested) return;
            try
            {
                selector();
                completion.TrySetResult(enterLab() && worlds.ReadWorld().DestinationId == "obp:fusion-lab" && hasPlayer());
            }
            catch (Exception error) { completion.TrySetException(error); }
        }).CallDeferred();
        return await completion.Task;
    }
    private sealed class InputLease : IDisposable
    {
        private readonly PlayerHost _player;
        private readonly Func<double, (Vector2 Move, bool Jump)>? _previous;
        private readonly Func<double, (Vector2 Move, bool Jump)> _input;
        private bool _disposed;
        public InputLease(PlayerHost player, Func<System.Numerics.Vector2> read)
        {
            _player = player;
            _previous = player.ScriptedInputProvider;
            _input = _ => { var axes = read(); return (new(axes.X, axes.Y), false); };
            player.ScriptedInputProvider = _input;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (GodotObject.IsInstanceValid(_player) && ReferenceEquals(_player.ScriptedInputProvider, _input))
                _player.ScriptedInputProvider = _previous;
        }
    }
}
