using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Player;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

internal sealed record GodotCapabilitySampleBindings(Func<PlayerHost?> Player, Func<PlayerNativeClock?> Clock,
    Func<long> ServiceTick, Func<IPlayerAnimationPresentationController?> Animation, Func<int> ShotsFired);
internal sealed class GodotCapabilitySamples(GodotCapabilitySampleBindings bindings) : IDiagnosticCapabilitySamples
{
    private PlayerHost Player => bindings.Player() ?? throw new DiagnosticUnavailableException("Capability sampling needs a live player.");
    public DiagnosticCapabilityFrame Read()
    {
        var player = Player;
        var clock = bindings.Clock() ?? throw new DiagnosticUnavailableException("The selected native player clock is missing.");
        var controller = bindings.Animation() as Rac1PlayerAnimationPresentationController
            ?? throw new DiagnosticUnavailableException("The selected source animation/attachment sampler is unavailable.");
        var pose = controller.Current;
        var position = player.GlobalPosition; var camera = player.Camera.GlobalTransform.Origin;
        return new(clock.Tick, bindings.ServiceTick(), clock.Seconds, pose.SourceSequenceKey, pose.SampledPose?.FrameIndex,
            [.. pose.SampledPose?.LocalPositions ?? []], JsonSerializer.Serialize(controller.CurrentAttachmentPose(Rac1WrenchAttachment.SocketKey)),
            player.Rac1CurrentYaw, new(position.X, position.Y, position.Z), new(camera.X, camera.Y, camera.Z), bindings.ShotsFired(), player.Rac1WrenchFrame.Generation);
    }
    public IDisposable SubscribeAfterPhysics(Action sample)
    {
        var player = Player; player.DevelopmentPhysicsCompleted += sample;
        return new Subscription(() => player.DevelopmentPhysicsCompleted -= sample);
    }
    private sealed class Subscription(Action remove) : IDisposable
    {
        private Action? _remove = remove;
        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }
}

internal sealed class GodotCommonContactFixture(Func<Node3D?> scene, Func<PlayerHost?> player) : IDiagnosticCommonContactFixture
{
    private PlayerHost Player => player() ?? throw new DiagnosticUnavailableException("The common-contact fixture needs a player.");
    public IDisposable CreateDeck()
    {
        var parent = scene() ?? throw new DiagnosticUnavailableException("The common-contact fixture needs a live scene.");
        var deck = new StaticBody3D { Name = "ExplicitCommonContactFixture" };
        deck.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 2, 200) } });
        parent.AddChild(deck); deck.GlobalPosition = new(4096, 4095, 4096);
        return new DeckLease(deck);
    }
    public void ResetPlayerOnDeck() { var player = Player; player.Rotation = Vector3.Zero; player.Rac1CurrentYaw = 0; player.ApplyDevelopmentReset(new(4096, 4097, 4096)); }
    public MovementReproPlayer CaptureMovement() => Player.CaptureMovementRepro();
    public void RestoreMovement(MovementReproPlayer state) => Player.RestoreMovementRepro(state);
    private sealed class DeckLease(StaticBody3D deck) : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (_disposed) return; _disposed = true; if (GodotObject.IsInstanceValid(deck)) deck.QueueFree(); }
    }
}

internal delegate PlayerIntakeResult DiagnosticDamageIntake(RuntimeWorldScene.DynamicObjectNode node, string build,
    string adapter, string descriptor, long sequence, int amount, ulong flags);
internal sealed class DiagnosticRetiredActorProbe(Func<ImmutableArray<RuntimeWorldScene.DynamicObjectNode>> actors,
    DiagnosticDamageIntake damage) : IDiagnosticRetiredActorProbe
{
    private RuntimeWorldScene.DynamicObjectNode? _retired;
    public PlayerIntakeStatus? TryDamageRetiredActor() => _retired is null ? null : damage(_retired,
        OBP.RAC1.Rac1Authority.Primary.BuildId, PlayerSessionNativeState.Rac1DamageAdapter, PlayerSessionNativeState.Rac1DamageDescriptor, 999999, 1, 0).Status;
    public void RememberCurrentActor() => _retired = actors().FirstOrDefault();
}

internal sealed record GodotCapabilityHealthBindings(Func<PlayerSessionNativeState?> Native, Func<PlayerHost?> Player,
    Func<int> SourceRac2Nanotech, Func<HudHealth?> Hud, Func<ImmutableArray<(int Instance, Node3D Root)>> Rac2Attackers, Func<bool> Respawn);
internal sealed class GodotCapabilityHealthFixture(GodotCapabilityHealthBindings bindings) : IDiagnosticCapabilityHealthFixture
{
    private PlayerSessionNativeState Native => bindings.Native() ?? throw new DiagnosticUnavailableException("No selected native health adapter is bound.");
    public DiagnosticCapabilityHealth Read()
    {
        var native = Native; var hud = bindings.Hud();
        return new(native.Life.Capture().Current, bindings.SourceRac2Nanotech(), hud?.Current,
            native.Life.Alive, bindings.Player()?.Rac1GameplayAlive == true, hud?.LifeState, native.Wallet.Balance);
    }
    public bool StageFirstRac2HostileContact()
    {
        var actors = bindings.Rac2Attackers();
        if (actors.Length == 0) return false;
        var actor = actors.OrderBy(actor => actor.Instance).First();
        (bindings.Player() ?? throw new DiagnosticUnavailableException("No player for the source combat fixture.")).ApplyDevelopmentReset(actor.Root.GlobalPosition + new Vector3(1, 0, 0));
        return true;
    }
    public void DefeatFromWorldHazard() => Native.Life.DefeatFromWorldHazard();
    public bool Respawn() => bindings.Respawn();
}
