using OBP.Core.Math;
using OBP.RAC1.Gameplay;
using System.Collections.Immutable;

namespace OBP.Game.Diagnostics;

/// <summary>Original save/load fixture through owned files and immutable native-removal/inventory witnesses.</summary>
public sealed class MovementStateScenario(CaptureDiagnosticContext context, MovementDiagnosticContext movement,
    IMovementStateDiagnosticFixture states) : CaptureScenarioSupport(context)
{
    public async Task RunAsync()
    {
        using var scope = states.Begin();
        movement.Driver.Clear();
        await WaitGroundedAsync(); await Physics(8);
        var defeated = states.StageLethalReactions(); var broken = states.BreakCrate();
        Require(states.SelectAndConsumeBomb(), "movement save fixture could not consume ammo"); states.Credit(37);
        var expected = states.ReadWorld();
        Key(CaptureKey.F12, true); Key(CaptureKey.F12, false);
        await WaitFileAsync(() => states.SavedFileExists, 30, "F12 movement save");
        var saved = states.ReadSaved(); var persisted = saved.Read(); var position = movement.Ports.Player.Read().GlobalPosition;
        Require(persisted.HasWorld && persisted.InactiveObjects >= defeated.Length + 1, "world removals were not saved");
        await states.LoadAsync(saved, null, Lifetime); Lifetime.ThrowIfCancellationRequested(); VerifyWorld();
        var first = RequireReplaySamples(await states.ForwardAsync(25, Lifetime), 25, "initial forward route");
        Require(Math.Sqrt(Math.Pow(first[^1].Position.X - position.X, 2) + Math.Pow(first[^1].Position.Z - position.Z, 2)) > .25f, "saved movement setup produced no forward travel");
        Key(CaptureKey.F12, true, shift: true); Key(CaptureKey.F12, false, shift: true);
        await WaitFileAsync(() => !states.Loading && System.Numerics.Vector3.Distance(movement.Ports.Player.Read().GlobalPosition, position) < .0001f, 60, "Shift+F12 movement load");
        VerifyWorld(); Require(Math.Abs(first[0].ControlHeading - persisted.ControlHeading) < .000001, "movement load changed the camera/control heading");
        var second = RequireReplaySamples(await states.LoadAsync(saved, 25, Lifetime), 25, "reloaded forward route"); Lifetime.ThrowIfCancellationRequested(); VerifyWorld();
        float error = (float)first.Zip(second).Max(pair => Distance(pair.First.Position, pair.Second.Position));
        Require(error < .03f, $"reloaded forward route diverged by {error:F6} units");
        await VerifyFallbackAndExactPoseAsync(saved);
        Log("[movement-state-smoke] defeated toad/robot, broken crate, no duplicate rewards, equipped item/ammo/bolts PASS");
        Log($"[movement-state-smoke] PASS; hotkey save/load, persistent roundtrip, exact pose, native/fallback camera/control, no spawn snap, forward replay error={error:F6}; state={states.EvidenceLabel}");

        void VerifyWorld()
        {
            var current = states.ReadWorld();
            Require(defeated.All(key => current.Enemies.Single(enemy => enemy.Target == key) is { Active: false, Visible: false, CollisionLayer: 0, RewardEmitted: true }),
                "movement load resurrected a defeated enemy or its collision body");
            Require(current.Crates.Single(crate => crate.Target == broken) is { Active: false, HasBody: false } && current.DestroyedCrates == expected.DestroyedCrates,
                "movement load resurrected a broken crate or lost its UID state");
            Require(current.Equipped == Rac1WeaponId.FirstRanged && current.Ammo == expected.Ammo && current.Bolts == expected.Bolts && !current.EmittedPickups,
                "movement load changed inventory/currency or re-emitted death rewards");
        }
    }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "S1244", Justification = "The original fallback-camera regression requires bit-exact replay samples; a tolerance would weaken this parity check.")]
    private async Task VerifyFallbackAndExactPoseAsync(IMovementStateSaveWitness saved)
    {
        var fallback = saved.WriteFallback();
        var first = RequireReplaySamples(await states.LoadAsync(fallback, 15, Lifetime), 15, "first fallback route"); Lifetime.ThrowIfCancellationRequested();
        var second = RequireReplaySamples(await states.LoadAsync(fallback, 15, Lifetime), 15, "second fallback route"); Lifetime.ThrowIfCancellationRequested();
        Require(!movement.Ports.Player.Read().HasActiveRecoveredCamera && first.Zip(second).All(pair => pair.First.Position == pair.Second.Position && pair.First.ControlHeading == pair.Second.ControlHeading),
            "fallback camera/control replay changed after reload");
        saved.RestorePlayer(-.5);
        Require(Math.Abs(movement.Ports.Player.Read().GlobalPosition.Y - (saved.Read().Position.Y - .5)) < .00001, "movement restore resnapped the saved coordinate");
        saved.RestorePlayer();
    }
    private async Task WaitGroundedAsync()
    {
        int supported = 0;
        for (int frame = 0; frame < 180; frame++)
        {
            await Physics(1); await Frames(); var state = movement.Ports.Player.Read();
            supported = state.Grounded && state.Rac1MovementPhase == OBP.RAC1.Player.Rac1RatchetMovementPhase.Grounded ? supported + 1 : 0;
            if (supported >= 3) { return; }
        }
        throw new InvalidOperationException("player did not reach Godot floor contact within smoke timeout");
    }
    private async Task WaitFileAsync(Func<bool> ready, int frames, string phase)
    {
        for (int i = 0; i < frames; i++) { if (ready()) { return; } await Frames(); }
        throw new TimeoutException("Timed out waiting for RAC1 smoke phase: " + phase + ".");
    }
    private static double Distance(Vec3 a, Vec3 b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
    internal static ImmutableArray<MovementStateReplayDiagnostic> RequireReplaySamples(ImmutableArray<MovementStateReplayDiagnostic> samples, int frames, string phase)
    {
        Require(!samples.IsDefault && samples.Length == frames + 1, phase + " did not produce its initial sample and every requested physics update.");
        return samples;
    }
}
