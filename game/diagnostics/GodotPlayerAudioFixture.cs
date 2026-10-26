using System.Collections.Immutable;
using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OBP.Game.Diagnostics;

internal sealed record GodotPlayerAudioBindings(Func<PlayerHost?> Player, Func<DiagnosticAudioHostState> Host,
    Action ClearInput, Action<float, float> Analogue, Action<double, double> Conditioned,
    Func<bool> Back, Action<Rac1WeaponId> SelectWeapon, Action<bool> Preview,
    Func<ImmutableArray<RuntimeWorldScene.DynamicObjectNode>> Crates, Func<RuntimeWorldScene.DynamicObjectNode, bool> ActiveCrate,
    Action<RuntimeWorldScene.DynamicObjectNode, Vector3> StageCrate, Action<Rac1GameplayDamageEvent> ApplyContact);

/// <summary>Veldin's explicit synthetic audio floor and playback observations. No game/controller objects escape the fixture.</summary>
internal sealed class GodotPlayerAudioFixture(Node owner, GodotPlayerAudioBindings bindings) : IDiagnosticPlayerAudioFixture
{
    private Node3D? _stage;
    private bool _ownsPause;
    private PlayerHost Player => bindings.Player() is { } player && GodotObject.IsInstanceValid(player) ? player
        : throw new DiagnosticUnavailableException("The audio fixture's player is unavailable.");
    public DiagnosticAudioPlayerState Read()
    {
        var player = Player;
        return new(true, player.IsOnFloor(), player.Rac1MovementPhase == Rac1RatchetMovementPhase.Grounded, player.Rac1AudioSurfaceEffectMode, player.Rac1TraversalAction, player.Rac1DevelopmentPackPreview,
            player.Rac1WrenchFrame.Active, player.Rac1WrenchAnimationBlending, player.Rac1TraversalAuthoredAnimationTime,
            owner.GetTree().Paused, [.. player.GetChildren().OfType<AudioStreamPlayer>().Where(node => !node.IsQueuedForDeletion()).Select(node =>
                new DiagnosticAudioVoice(node.Name, node.Stream is AudioStreamWav,
                    node.Stream is AudioStreamWav { LoopMode: AudioStreamWav.LoopModeEnum.Forward }, node.Playing, node.StreamPaused, node.CanProcess()))], bindings.Host());
    }
    public IDisposable BeginStage()
    {
        if (_stage is not null) throw new InvalidOperationException("An audio stage is already active.");
        var player = Player;
        var stage = _stage = new Node3D { Name = "PlayerAudioSyntheticFloor", Position = new(2000, 64, 2600) };
        owner.AddChild(stage);
        GodotRac1DiagnosticSurface.Add(stage, "SoundCategory0", 11,
            (new(-80, 0, -80), new(80, 0, -80), new(80, 0, 80)), (new(-80, 0, -80), new(80, 0, 80), new(-80, 0, 80)));
        GodotRac1DiagnosticSurface.Add(stage, "SoundCategory1", 43,
            (new(100, 0, -80), new(200, 0, -80), new(200, 0, 80)), (new(100, 0, -80), new(200, 0, 80), new(100, 0, 80)));
        int previousFps = Engine.MaxFps; bool previousMute = AudioServer.IsBusMute(0);
        bool previousScripted = player.Scripted, previousIgnoreJoypad = Input.IgnoreJoypadOnUnfocusedApplication;
        Engine.MaxFps = 60; player.Scripted = false; Input.IgnoreJoypadOnUnfocusedApplication = false; AudioServer.SetBusMute(0, true);
        return new StageLease(() =>
        {
            try { bindings.ClearInput(); if (_ownsPause && owner.GetTree().Paused) bindings.Back(); }
            finally
            {
                try
                {
                    if (GodotObject.IsInstanceValid(stage)) { stage.QueueFree(); }
                    if (GodotObject.IsInstanceValid(player)) { player.Scripted = previousScripted; }
                }
                finally
                {
                    _stage = null; _ownsPause = false;
                    Engine.MaxFps = previousFps; AudioServer.SetBusMute(0, previousMute); Input.IgnoreJoypadOnUnfocusedApplication = previousIgnoreJoypad;
                }
            }
        });
    }
    public void ResetPosition(System.Numerics.Vector3 offset) => Player.ApplyDevelopmentReset(
        (_stage ?? throw new DiagnosticUnavailableException("No audio stage is active.")).GlobalPosition + new Vector3(offset.X, offset.Y, offset.Z) + Vector3.Up * .08f);
    public void CyclePack() => Player.CycleRac1DevelopmentPack();
    public void SetAction(DiagnosticPlayerButton button, bool pressed)
    {
        var action = button switch
        {
            DiagnosticPlayerButton.Jump => RawGamepadInput.Jump,
            DiagnosticPlayerButton.Crouch => RawGamepadInput.Crouch,
            DiagnosticPlayerButton.Wrench => RawGamepadInput.Action,
            DiagnosticPlayerButton.Traversal => RawGamepadInput.Traversal,
            DiagnosticPlayerButton.MoveRight => RawGamepadInput.MoveRight,
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };
        if (pressed) Input.ActionPress(action); else Input.ActionRelease(action);
    }
    public void SetAnalogue(float right, float forward) => bindings.Analogue(right, forward);
    public void SetConditioned(double right, double forward) => bindings.Conditioned(right, forward);
    public void ClearInput() => bindings.ClearInput();
    public bool Back()
    {
        bool result = bindings.Back(); _ownsPause = result && owner.GetTree().Paused;
        return result;
    }
    public void SelectWeapon(Rac1WeaponId weapon) => bindings.SelectWeapon(weapon);
    public void SetEquipmentPreview(bool disable) => bindings.Preview(disable);
    public void StageRewardCrateInFront()
    {
        var crate = bindings.Crates().First(node => bindings.ActiveCrate(node) && Rac1BoltCrate.ReadAuthored(node.Source)?.RewardCentre == 10);
        var player = Player;
        Vector3 forward = new(-(float)Math.Cos(player.Rac1CurrentYaw), 0, (float)Math.Sin(player.Rac1CurrentYaw));
        bindings.StageCrate(crate, player.GlobalPosition + forward * .8f);
    }
    public void ApplyContactHit() => bindings.ApplyContact(new(Rac1GameplayEntityRef.Moby(new(749, 143)), Rac1GameplayEntityRef.Player,
        nativeDamage: 1, nativeMarker: Rac1Class749Hostile.AttackMarker));
    private sealed class StageLease(Action cleanup) : IDisposable
    {
        private Action? _cleanup = cleanup;
        public void Dispose() => Interlocked.Exchange(ref _cleanup, null)?.Invoke();
    }
}
