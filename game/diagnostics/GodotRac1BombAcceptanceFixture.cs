using Godot;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed class GodotWeaponDiagnosticTransientFixture(IDiagnosticWorldObservations worlds, Action clear) : IWeaponDiagnosticTransientFixture
{
    public IDisposable BeginCleanup()
    {
        var world = worlds.ReadWorld();
        return new MovementFixtureLease(() =>
        {
            var live = worlds.ReadWorld();
            // World teardown already owns retired entities; never clear a replacement world's weapons.
            if (!string.IsNullOrWhiteSpace(world.WorldInstanceId) && live.WorldInstanceId == world.WorldInstanceId && live.Generation == world.Generation)
                clear();
        });
    }
}

internal sealed record GodotRac1BombAcceptanceBindings(Func<Rac1BombViewDiagnostic> View, Func<Rac1BombEffectsDiagnostic> Effects,
    Func<IRac1BombAcceptanceWitness> CaptureShot, Func<Vector3, Vector3, bool> LineOfSight,
    Func<Vector3, Vector3, Vector3?> Contact, Func<int, bool> ConsumeAmmo, Func<PlayerHost?> Player);
internal sealed class GodotRac1BombAcceptanceFixture(GodotRac1BombAcceptanceBindings bindings) : IRac1BombAcceptanceFixture
{
    public Rac1BombViewDiagnostic ReadView() => bindings.View();
    public Rac1BombEffectsDiagnostic ReadEffects() => bindings.Effects();
    public IRac1BombAcceptanceWitness CaptureSingleShot() => bindings.CaptureShot();
    public bool HasLineOfSight(NVector3 from, NVector3 to) => bindings.LineOfSight(GodotMovementPlayer.Engine(from), GodotMovementPlayer.Engine(to));
    public NVector3? ProbeWorldContact(NVector3 from, NVector3 to) => bindings.Contact(GodotMovementPlayer.Engine(from), GodotMovementPlayer.Engine(to)) is { } hit ? GodotMovementPlayer.Copy(hit) : null;
    public bool ConsumeAmmo(int amount) => bindings.ConsumeAmmo(amount);
    public string? SavedReproductionPath => System.Environment.GetEnvironmentVariable("OBP_BOMB_REPRO_STATE");
    public string DescribeRay(NVector3 from, NVector3 to)
    {
        var player = bindings.Player() ?? throw new DiagnosticUnavailableException("The bomb contact-query player is unavailable.");
        using var ray = PhysicsRayQueryParameters3D.Create(GodotMovementPlayer.Engine(from), GodotMovementPlayer.Engine(to));
        ray.Exclude = [player.GetRid()];
        return player.GetWorld3D().DirectSpaceState.IntersectRay(ray).ToString() ?? "";
    }
}
internal sealed class GodotRac1BombAcceptanceWitness(GodotRac1DiagnosticWorldScope scope, Func<Rac1BombAcceptanceShot> read) : IRac1BombAcceptanceWitness
{
    public Rac1BombAcceptanceShot Read() { scope.RequireCurrent(); return read(); }
}
internal sealed class GodotWeaponMovementStateFixture(Func<bool> loading, Func<MovementReproState> capture,
    Func<string, CancellationToken, Task> load, Func<string, string> reproPath, IWeaponDiagnosticTransientFixture transients) : IWeaponMovementStateFixture
{
    public bool Loading => loading();
    public WeaponMovementStateDiagnostic CaptureBoundary()
    {
        var state = capture(); return new(state.World is not null, state.World?.CapturedMobies?.Count ?? 0, state.World?.WeaponPreview ?? false,
            state.World?.MorphedMobies is { } morphed ? [.. morphed.Select(key => new OBP.Runtime.Gameplay.WeaponTargetKey(OBP.Core.ObpSourceGame.Rac1, key.NativeClassId, key.InstanceIndex))] : []);
    }
    public async Task<IDisposable> LoadAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); await load(path, token);
        var cleanup = transients.BeginCleanup();
        try { token.ThrowIfCancellationRequested(); return cleanup; }
        catch (Exception error)
        {
            try { cleanup.Dispose(); }
            catch (Exception failure) { throw new AggregateException("Reload cancellation and transient cleanup failed.", error, failure); }
            throw;
        }
    }
    public async Task<IDisposable> SaveAndReloadAsync(string fileName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var path = reproPath(fileName);
        MovementReproStateFile.Save(path, capture()); return await LoadAsync(path, token);
    }
}
