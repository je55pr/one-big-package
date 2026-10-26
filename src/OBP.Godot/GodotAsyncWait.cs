using Godot;

namespace OBP.Godot;

/// <summary>Host-thread waits for parameterless Godot signals, with subscription cleanup on cancellation.</summary>
public static class GodotAsyncWait
{
    public static async Task SignalAsync(GodotObject source, StringName signal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Godot emits on the host thread. Preserve the signal's callback phase,
        // especially FramePostDraw; posting a later continuation can pair an old image with newer metadata.
        var completion = new TaskCompletionSource();
        var callback = Callable.From(() => { completion.TrySetResult(); });
        if (source.Connect(signal, callback) != Error.Ok)
            throw new InvalidOperationException("Could not subscribe to the requested Godot signal.");
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try { await completion.Task; }
        finally
        {
            if (GodotObject.IsInstanceValid(source) && source.IsConnected(signal, callback))
                source.Disconnect(signal, callback);
        }
    }
}
