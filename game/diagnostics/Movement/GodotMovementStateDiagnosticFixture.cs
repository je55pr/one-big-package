using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

internal sealed class GodotMovementStateDiagnosticBindings
{
    internal required Func<PlayerHost?> Player { get; init; }
    internal required Func<ImmutableArray<RuntimeWorldScene.DynamicObjectNode>> Enemies { get; init; }
    internal required Func<ImmutableArray<RuntimeWorldScene.DynamicObjectNode>> Crates { get; init; }
    internal required Func<RuntimeWorldScene.DynamicObjectNode, bool> Active { get; init; }
    internal required Func<RuntimeWorldScene.DynamicObjectNode, Rac1GameplayDamageEvent, bool> Damage { get; init; }
    internal required Func<RuntimeWorldScene.DynamicObjectNode, bool> Strike { get; init; }
    internal required Func<MovementStateWorldDiagnostic> Read { get; init; }
    internal required Func<bool> Consume { get; init; }
    internal required Action<int> Credit { get; init; }
    internal required Func<bool> Loading { get; init; }
    internal required Func<string?> Override { get; init; }
    internal required Action<string?> SetOverride { get; init; }
    internal required Func<string> Path { get; init; }
    internal required Func<string, int?, CancellationToken, Task<ImmutableArray<MovementStateReplayDiagnostic>>> Load { get; init; }
    internal required Func<int, CancellationToken, Task<ImmutableArray<MovementStateReplayDiagnostic>>> Forward { get; init; }
}
internal sealed class GodotMovementStateDiagnosticFixture(GodotMovementStateDiagnosticBindings bindings) : IMovementStateDiagnosticFixture
{
    private PlayerHost Player => bindings.Player() ?? throw new DiagnosticUnavailableException("Movement state smoke requires a player.");
    public bool SavedFileExists => File.Exists(bindings.Path());
    public bool Loading => bindings.Loading();
    public string EvidenceLabel => bindings.Path();
    public IDisposable Begin()
    {
        int frames = Engine.MaxFps; string? previous = bindings.Override();
        string directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "captures", "repros", "smoke-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory); bindings.SetOverride(System.IO.Path.Combine(directory, "smoke-state.json")); Engine.MaxFps = 60;
        return new Restore(() => { try { bindings.SetOverride(previous); } finally { Engine.MaxFps = frames; } });
    }
    private static WeaponTargetKey Target(RuntimeWorldScene.DynamicObjectNode node) => new(ObpSourceGame.Rac1, node.Source.NativeClassId, node.Source.InstanceIndex);
    public ImmutableArray<WeaponTargetKey> StageLethalReactions()
    {
        var selected = bindings.Enemies().GroupBy(node => node.Source.NativeClassId).Select(group => group.First()).ToArray();
        foreach (var enemy in selected)
        {
            if (!bindings.Damage(enemy, Rac1DamageRuntime.FromBombGlove(new(100000, enemy.Source.NativeClassId, enemy.Source.InstanceIndex,
                    Rac1BombGlove.NativeDamage, Rac1BombGlove.NativeDamageFlags)))) throw new InvalidOperationException("movement save fixture rejected lethal damage");
            var state = bindings.Read().Enemies.Single(item => item.Target == Target(enemy));
            if (!state.RewardEmitted || !state.Active) throw new InvalidOperationException("fixture did not capture the pending lethal reaction");
        }
        return [.. selected.Select(Target)];
    }
    public WeaponTargetKey BreakCrate()
    {
        var crate = bindings.Crates().First(node => node.Source.NativeClassId == 500 && bindings.Active(node));
        if (!bindings.Strike(crate)) { throw new InvalidOperationException("movement save fixture rejected crate break"); }
        return Target(crate);
    }
    public bool SelectAndConsumeBomb() => bindings.Consume();
    public void Credit(int bolts) => bindings.Credit(bolts);
    public MovementStateWorldDiagnostic ReadWorld() => bindings.Read();
    public IMovementStateSaveWitness ReadSaved() => new Saved(this, bindings.Path(), MovementReproStateFile.Load(bindings.Path()));
    public Task<ImmutableArray<MovementStateReplayDiagnostic>> ForwardAsync(int frames, CancellationToken token) => bindings.Forward(frames, token);
    public Task<ImmutableArray<MovementStateReplayDiagnostic>> LoadAsync(IMovementStateSaveWitness save, int? frames, CancellationToken token)
    {
        var captured = save as Saved ?? throw new ArgumentException("The movement save belongs to another fixture.", nameof(save));
        if (!ReferenceEquals(captured.Owner, this)) throw new ArgumentException("The movement save belongs to another fixture.", nameof(save));
        return bindings.Load(captured.Path, frames, token);
    }
    private sealed class Saved(GodotMovementStateDiagnosticFixture owner, string path, MovementReproState state) : IMovementStateSaveWitness
    {
        internal GodotMovementStateDiagnosticFixture Owner => owner;
        internal string Path => path;
        public MovementStateSaveDiagnostic Read() => new(state.World is not null, state.World?.InactiveObjects.Count ?? 0,
            state.Player.Position, state.Player.Camera?.ControlHeadingRadians ?? throw new DiagnosticUnavailableException("The saved camera/control heading is unavailable."));
        public IMovementStateSaveWitness WriteFallback()
        {
            var fallback = state with
            {
                Player = state.Player with
                {
                    ForceHostCamera = true,
                    HostCameraYawRotation = new(0, .7, 0),
                    HostCameraPitchRotation = new(-1.1, 0, 0),
                    HostCameraLocalPosition = new(0, 0, state.Player.CameraDistance),
                }
            };
            string target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "smoke-fallback-state.json");
            MovementReproStateFile.Save(target, fallback); return new Saved(owner, target, fallback);
        }
        public void RestorePlayer(double verticalOffset = 0) => owner.Player.RestoreMovementRepro(state.Player with
        { Position = state.Player.Position with { Y = state.Player.Position.Y + verticalOffset } });
    }
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}
