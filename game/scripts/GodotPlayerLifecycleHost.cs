using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Presentation;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game;

internal sealed record GodotPlayerLifecyclePorts(Func<PlayerSessionNativeState?> Native, Func<ObpPlayerSession?> Session,
    Func<bool> ParityAlive, Func<bool> EnterWorld, Func<CanvasLayer> Ui, Func<double?> DeathHeight,
    Func<RuntimeWorldScene.DynamicObjectNode, bool> ActorAlive, Func<bool> NativeRetryOpen,
    Action Defeated, Action<int, int> Hurt, Action RefreshHud, HudStateAdapter Hud,
    Func<HudPresentationState> NativeHud, Func<HudPresentationState, HudPresentationState> EquipmentHud);

/// <summary>Owns player-service subscriptions and ordinary retry UI, separately from native parity restart.</summary>
internal sealed class GodotPlayerLifecycleHost : IDisposable
{
    private readonly GodotPlayerLifecyclePorts _ports;
    private readonly PlayerLifecycleBridge _life;
    private WorldBinding? _world;
    private PlayerHost? _player;
    private Action? _tick;
    private PanelContainer? _retry;

    internal GodotPlayerLifecycleHost(GodotPlayerLifecyclePorts ports)
    {
        _ports = ports;
        _life = new(new(ports.Native, ports.Session, ports.ParityAlive, ports.EnterWorld, Present));
    }
    internal bool Alive => _life.Alive;
    internal string? WorldId => _life.WorldId;
    internal long Generation => _life.Generation;
    internal long ServiceTick => _life.ServiceTick;
    internal PanelContainer? RetryPanel => _retry;
    internal bool Contains(ActorInstanceKey key) => _life.Contains(key);
    internal void RetireActor(ActorInstanceKey key) => _life.RetireActor(key);
    internal bool Respawn() => _life.Respawn();

    internal IDisposable BindWorld(string id, long generation, Node3D scene)
    {
        RetireWorld();
        return _world = new(this, scene, _life.BeginWorld(id, generation));
    }

    internal void BindPlayer(PlayerHost player)
    {
        UnbindPlayer();
        _player = player;
        var world = _world ?? throw new InvalidOperationException("Player health requires its scene lease.");
        _tick = () =>
        {
            if (!ReferenceEquals(_world, world) || !ReferenceEquals(_player, player)) return;
            _life.Tick(true, player.GlobalPosition.Y, _ports.DeathHeight());
        };
        player.PlayerServicesPhysicsUpdated += _tick;
        if (_ports.Native() is { } native) player.PlayerHealth = native.Life.Capture();
    }

    internal PlayerIntakeResult ApplySourceDamage(RuntimeWorldScene.DynamicObjectNode node, string build, string adapter,
        string descriptor, long sequence, int amount, ulong flags)
    {
        bool belongs = _world is { } world && GodotObject.IsInstanceValid(node.Root) && node.Root.IsInsideTree() && world.Scene.IsAncestorOf(node.Root);
        var source = node.Source;
        var key = new ActorInstanceKey(WorldId ?? "retired", Generation,
            new WeaponTargetKey(source.SourceGame, source.NativeClassId, source.InstanceIndex));
        return _life.Apply(new(key, new(source.SourceGame, build, $"moby:{source.NativeClassId}"), adapter,
            source.InteractionId, [new(descriptor, CapabilityAdmission.Ready)]), new(key, build, adapter, descriptor, sequence, amount, flags),
            () => GodotObject.IsInstanceValid(node.Root) && _ports.ActorAlive(node), belongs);
    }

    internal void PublishHud(params HudFeedbackDraft[] feedback)
    {
        var state = (_ports.Session() ?? throw new InvalidOperationException("Persistent HUD requires a player session.")).Capture();
        var health = state.Health;
        var projection = _ports.NativeHud() with
        {
            Health = new((int)health.Current, (int)health.Capacity, "nanotech", health.Alive ? HudLifeState.Alive : HudLifeState.Dead),
            Bolts = new(Rac1HudProjection.BoltsResourceKey, (int)state.Currencies.GetValueOrDefault("obp/bolts"))
        };
        if (_ports.Hud.Current.Epoch == 0) _ports.Hud.BeginSession(_ports.EquipmentHud(projection));
        else _ports.Hud.Publish(_ports.EquipmentHud(projection), feedback);
    }

    private void Present(PlayerLifecycleEvent change)
    {
        if (_player is { } player && _ports.Native() is { } native)
        {
            player.PlayerHealth = change.Health;
            player.Rac1GameplayState = native.Health.Probe();
        }
        switch (change.Kind)
        {
            case PlayerLifecycleEventKind.Hurt:
                _ports.Hurt((int)change.Before.Current, (int)change.Health.Current);
                _ports.RefreshHud();
                break;
            case PlayerLifecycleEventKind.Sample:
                if (_ports.Hud.Current.Health is not { } displayed || displayed.Current != (int)change.Health.Current || displayed.Capacity != (int)change.Health.Capacity ||
                    displayed.LifeState != (change.Health.Alive ? HudLifeState.Alive : HudLifeState.Dead)) _ports.RefreshHud();
                if (_player is { } current && _ports.Hud.Current.Air != Rac1HudProjection.CaptureAir(current.Rac1SwimState, current.Rac1MovementEquipment.OxygenMask)) _ports.RefreshHud();
                break;
            case PlayerLifecycleEventKind.DamageSample:
                // No admitted damage: the health snapshot was already copied above.
                break;
            case PlayerLifecycleEventKind.Defeated:
                if (_retry is not null || _ports.NativeRetryOpen() || _world is null) break;
                _ports.Defeated();
                ShowRetry();
                break;
            case PlayerLifecycleEventKind.RespawnRejected:
                _ports.RefreshHud();
                break;
            case PlayerLifecycleEventKind.Respawned:
                ResetRetry();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change.Kind, "Unsupported player lifecycle event.");
        }
    }

    private void ShowRetry()
    {
        var world = _world!;
        var panel = new PanelContainer { Name = "PlayerRetry", Size = new(300, 110) };
        var rows = new VBoxContainer();
        rows.AddChild(new Label { Text = "Defeated", HorizontalAlignment = HorizontalAlignment.Center });
        var retry = new Button { Text = "Respawn" };
        retry.Pressed += () => { if (ReferenceEquals(_world, world) && ReferenceEquals(_retry, panel)) _life.Respawn(); };
        rows.AddChild(retry); panel.AddChild(rows); _ports.Ui().AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center); retry.GrabFocus();
        ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;
        _retry = panel;
    }

    internal void ResetRetry()
    {
        if (_retry is { } panel && GodotObject.IsInstanceValid(panel)) panel.QueueFree();
        _retry = null;
    }

    private void UnbindPlayer()
    {
        if (_player is { } player && GodotObject.IsInstanceValid(player) && _tick is not null) player.PlayerServicesPhysicsUpdated -= _tick;
        _tick = null;
        _player = null;
    }

    internal void RetireWorld() => _world?.Dispose();
    public void Dispose() => RetireWorld();

    private sealed class WorldBinding(GodotPlayerLifecycleHost owner, Node3D scene, IDisposable life) : IDisposable
    {
        private bool _disposed;
        internal Node3D Scene { get; } = scene;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!ReferenceEquals(owner._world, this)) { life.Dispose(); return; }
            try { owner.UnbindPlayer(); }
            finally
            {
                try { owner.ResetRetry(); }
                finally { life.Dispose(); owner._world = null; }
            }
        }
    }
}
