using System.Collections.Immutable;
using Godot;
using OBP.Fusion;

namespace OBP.Game.Diagnostics;

internal sealed class GodotTrilogyPlayerDiagnosticBindings
{
    internal required Func<ObpPlayerSession> Session { get; init; }
    internal required Func<PlayerHost?> Player { get; init; }
    internal required Func<Node3D> Scene { get; init; }
    internal required Func<TrilogyPlayerDiagnostic> Read { get; init; }
    internal required Action ResetAudio { get; init; }
    internal required Func<ImmutableArray<TrilogyAudioDiagnostic>> Audio { get; init; }
    internal required Func<CancellationToken, Task> Trial { get; init; }
    internal required Func<bool> OpenPause { get; init; }
    internal required Action<string> PauseSection { get; init; }
    internal required Action ClosePause { get; init; }
    internal required Func<ImmutableArray<TrilogyEquipmentDiagnostic>> Equipment { get; init; }
    internal required Action Respawn { get; init; }
}
internal sealed class GodotTrilogyPlayerDiagnosticFixture(Node owner, GodotTrilogyPlayerDiagnosticBindings bindings) : ITrilogyPlayerDiagnosticFixture
{
    private PlayerHost Player => bindings.Player() ?? throw new DiagnosticUnavailableException("The trilogy player is unavailable.");
    public TrilogyPlayerDiagnostic Read() => bindings.Read();
    public ITrilogySessionBaseline CaptureSession()
    {
        var session = bindings.Session(); var state = session.Capture(); return new Baseline(() => session.Restore(state));
    }
    public IDisposable BeginVisit()
    {
        var player = Player; var filter = player.PlaytestInputFilter; player.PlaytestInputFilter = _ => default;
        return new Scope(() => { if (GodotObject.IsInstanceValid(player)) player.PlaytestInputFilter = filter; });
    }
    public void StageDeck()
    {
        var deck = new StaticBody3D { Name = "ExplicitEquipmentContactFixture" };
        deck.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 2, 200) } });
        bindings.Scene().AddChild(deck); deck.GlobalPosition = new(4096, 4095, 4096);
    }
    public void StageEncounterDeck() => Player.ApplyDevelopmentReset(new(4096, 4097, 4096));
    public ITrilogyPoseWitness CapturePose()
    {
        var saved = Player.CaptureMovementRepro(); return new Pose(() => Player.RestoreMovementRepro(saved));
    }
    public void ResetAudio() => bindings.ResetAudio();
    public Task CaptureTrialAsync(CancellationToken token) => bindings.Trial(token);
    public ImmutableArray<TrilogyAudioDiagnostic> ReadAudio() => bindings.Audio();
    public bool OpenPause() => bindings.OpenPause();
    public void ShowPauseSection(string section) => bindings.PauseSection(section);
    public void ClosePause() => bindings.ClosePause();
    public ImmutableArray<TrilogyEquipmentDiagnostic> ReadEquipment() => bindings.Equipment();
    public void HoldEquipped(bool held) => Player.PlaytestInputFilter = _ => new RawPlayerInputFrame { TraversalHeld = held };
    public void SetPaused(bool paused) => owner.GetTree().Paused = paused;
    public void StageEncounter() { Player.PlaytestInputFilter = _ => default; Player.ApplyDevelopmentReset(new(0, 1, 14)); }
    public void StageAttack(System.Numerics.Vector3 target)
    {
        bindings.Respawn(); Player.ApplyDevelopmentReset(GodotMovementPlayer.Engine(target) + new Vector3(5, 1, 0)); Player.Rac1CurrentYaw = 0;
    }
    private sealed class Baseline(Action restore) : ITrilogySessionBaseline { public void Restore() => restore(); }
    private sealed class Pose(Action restore) : ITrilogyPoseWitness { public void Restore() => restore(); }
    private sealed class Scope(Action restore) : IDisposable { public void Dispose() => restore(); }
}
