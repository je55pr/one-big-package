using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.PS2;
using OBP.Runtime.Player;
using OBP.Runtime;

namespace OBP.Game.Diagnostics;

internal static class DiagnosticWorldFailures
{
    internal const string Destination = "test:failed-preparation";
    internal static RuntimeWorld Load() => throw new InvalidDataException("Intentional staged-load failure");
}

internal sealed class DiagnosticSourceAttachmentFixture(ObpSourceLibrary sources, Action refresh) : IDiagnosticSourceAttachmentFixture
{
    public IDisposable Detach(ObpSourceGame game, bool refreshOnDetach)
    {
        var source = sources.Get(game) ?? throw new DiagnosticUnavailableException("The fixture source is not attached: " + game);
        if (!sources.Remove(game)) throw new DiagnosticUnavailableException("The fixture source could not be detached.");
        var lease = new AttachmentLease(() =>
        {
            using var reader = new FileRandomAccessReader(source.Path);
            sources.Restore(new(source.Game, source.Identity.BuildId, source.Path), reader);
            refresh();
        });
        try { if (refreshOnDetach) refresh(); }
        catch { lease.Dispose(); throw; }
        return lease;
    }
    private sealed class AttachmentLease(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}

internal sealed class GodotPlayerObservations(Func<PlayerHost?> player, Func<PlayerAvatarIdentity?> avatar,
    Func<long> nativeTick, Func<double?> health) : IDiagnosticPlayerObservations
{
    public DiagnosticPlayerSnapshot ReadPlayer()
    {
        var actor = player();
        if (actor is null || !GodotObject.IsInstanceValid(actor)) throw new DiagnosticUnavailableException("No live player observation is available.");
        var position = actor.GlobalPosition; var velocity = actor.Velocity;
        return new(avatar(), new(position.X, position.Y, position.Z), new(velocity.X, velocity.Y, velocity.Z),
            actor.IsOnFloor(), nativeTick(), null, health());
    }
}

internal sealed class GodotWorldLifecycleFixture(Node owner, WorldSceneLifetime<GodotWorldResources> worlds,
    Func<bool> enterLab, Func<bool> failedPreparation) : IDiagnosticWorldLifecycleFixture
{
    public bool TryFailedPreparation() => failedPreparation();
    public IDiagnosticDeferredNavigation ScheduleLabAfterNextFrame(CancellationToken lifetime) => new Deferred(owner, worlds, enterLab, lifetime);
    private sealed class Deferred : IDiagnosticDeferredNavigation
    {
        private readonly WorldSceneLifetime<GodotWorldResources> _worlds;
        private readonly CancellationTokenRegistration _cancellation;
        public Task Completion { get; }
        public bool EntryRan { get; private set; }
        public bool FailureReported { get; private set; }
        public Deferred(Node owner, WorldSceneLifetime<GodotWorldResources> worlds, Func<bool> enterLab, CancellationToken lifetime)
        {
            lifetime.ThrowIfCancellationRequested();
            _worlds = worlds;
            worlds.PrepareLater(async token =>
            {
                await GodotAsyncWait.SignalAsync(owner.GetTree(), SceneTree.SignalName.ProcessFrame, token);
                EntryRan = true; enterLab();
            }, (_, _) => FailureReported = true);
            Completion = worlds.PendingPreparation;
            _cancellation = lifetime.Register(CancelOwned);
        }
        private void CancelOwned() { if (ReferenceEquals(_worlds.PendingPreparation, Completion) && !Completion.IsCompleted) _worlds.CancelPreparation(); }
        public async ValueTask DisposeAsync()
        {
            _cancellation.Dispose(); CancelOwned();
            await Completion;
        }
    }
}
