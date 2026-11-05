using System.Collections.Immutable;
using System.Numerics;
using OBP.Game.Diagnostics;
using OBP.Game;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed partial class CaptureDiagnosticTests
{
    [Theory]
    [InlineData("both", true)]
    [InlineData("none", false)]
    [InlineData("toad-only", false)]
    [InlineData("robot-only", false)]
    [InlineData("foreign-source", false)]
    [InlineData("duplicate-toad", false)]
    [InlineData("unexpected-class", false)]
    public async Task MovementSaveCannotClaimEnemyRestorationWithoutBothSourceQualifiedDeaths(string cohort, bool accepted)
    {
        // Synthetic scenario ports isolate admission of the death cohort; they
        // do not model retail save bytes, damage, input delivery or movement.
        WeaponTargetKey[] targets = [new(ObpSourceGame.Rac1, Rac1ClassIds.VeldinToad, 149), new(ObpSourceGame.Rac1, Rac1ClassIds.VeldinFlameRobot, 287)];
        targets = cohort switch
        {
            "both" => targets,
            "none" => [],
            "toad-only" => [targets[0]],
            "robot-only" => [targets[1]],
            "foreign-source" => targets.Select(target => target with { SourceGame = ObpSourceGame.Rac2 }).ToArray(),
            "duplicate-toad" => [targets[0], targets[1] with { NativeClassId = Rac1ClassIds.VeldinToad }],
            "unexpected-class" => [targets[0], targets[1] with { NativeClassId = Rac1ClassIds.WoodenCrate }],
            _ => throw new ArgumentOutOfRangeException(nameof(cohort)),
        };
        var clock = new Fixture();
        using var run = new DiagnosticRunContext(clock, CancellationToken.None, 1000);
        var player = new MovementCohortPlayer();
        var actions = new DiagnosticInputActions(_ => true, (_, _) => { });
        var physical = new DiagnosticPhysicalInput((_, _) => { }, (_, _) => { });
        using var driver = new MovementDiagnosticDriver(run, actions, physical);
        var logs = new List<string>();
        var context = clock.Context(run) with { Log = logs.Add };
        var movement = new MovementDiagnosticContext(new(player, null!, null!, null!, actions, physical, clock, logs.Add, logs.Add), driver);
        var states = new MovementCohortStates(player, [.. targets]);
        var error = await Record.ExceptionAsync(() => new MovementStateScenario(context, movement, states).RunAsync());
        if (accepted)
        {
            Assert.Null(error);
            Assert.Equal(1, states.CrateBreaks);
            Assert.Contains(logs, message => message.StartsWith("[movement-state-smoke] PASS;", StringComparison.Ordinal));
        }
        else
        {
            Assert.True(error is InvalidOperationException && error.Message.Contains("death cohort", StringComparison.Ordinal),
                "Missing death cohort was accepted: " + string.Join("\n", logs) + "\n" + error);
            Assert.DoesNotContain(logs, message => message.Contains("PASS", StringComparison.Ordinal));
            Assert.Equal(0, states.CrateBreaks);
            Assert.Equal(0, states.AmmoSelections);
            Assert.Equal(0, states.Credits);
            Assert.Equal(0, states.SaveReads);
            Assert.Equal(0, states.Loads);
        }
    }

    private sealed class MovementCohortStates(MovementCohortPlayer player, ImmutableArray<WeaponTargetKey> targets) : IMovementStateDiagnosticFixture
    {
        private static readonly WeaponTargetKey Crate = new(ObpSourceGame.Rac1, Rac1ClassIds.WoodenCrate, 17);
        private bool _loaded;
        public int CrateBreaks { get; private set; }
        public int AmmoSelections { get; private set; }
        public int Credits { get; private set; }
        public int SaveReads { get; private set; }
        public int Loads { get; private set; }
        public IDisposable Begin() => new Lease();
        private sealed class Lease : IDisposable { public void Dispose() { /* Synthetic admission owns no resources. */ } }
        public bool SavedFileExists => true;
        public bool Loading => false;
        public string EvidenceLabel => "synthetic death-cohort ports";
        public ImmutableArray<WeaponTargetKey> StageLethalReactions() => targets;
        public WeaponTargetKey BreakCrate() { CrateBreaks++; return Crate; }
        public bool SelectAndConsumeBomb() { AmmoSelections++; return true; }
        public void Credit(int bolts) { Assert.Equal(37, bolts); Credits++; }
        public MovementStateWorldDiagnostic ReadWorld() => new(
            [.. targets.Select(target => new MovementStateEnemyDiagnostic(target, !_loaded, !_loaded, 0, true))],
            [new(Crate, false, false)], 1, Rac1WeaponId.FirstRanged, 5, 37, false);
        public IMovementStateSaveWitness ReadSaved() { SaveReads++; return new Saved(player); }
        public Task<ImmutableArray<MovementStateReplayDiagnostic>> ForwardAsync(int frames, CancellationToken _) =>
            Task.FromResult(Enumerable.Range(0, frames + 1).Select(tick => new MovementStateReplayDiagnostic(new((double)tick / frames, 0, 0), 0)).ToImmutableArray());
        public Task<ImmutableArray<MovementStateReplayDiagnostic>> LoadAsync(IMovementStateSaveWitness _, int? frames, CancellationToken token)
        {
            Loads++;
            _loaded = true;
            return frames is { } count ? ForwardAsync(count, token) : Task.FromResult(ImmutableArray<MovementStateReplayDiagnostic>.Empty);
        }
        private sealed class Saved(MovementCohortPlayer player) : IMovementStateSaveWitness
        {
            public MovementStateSaveDiagnostic Read() => new(true, 3, new(0, 0, 0), 0);
            public IMovementStateSaveWitness WriteFallback() => this;
            public void RestorePlayer(double verticalOffset = 0) => player.SetPosition(new(0, (float)verticalOffset, 0));
        }
    }

    private sealed class MovementCohortPlayer : IDiagnosticMovementPlayer
    {
        private Vector3 _position;
        public DiagnosticMovementState Read() => new() { GlobalPosition = _position, Grounded = true, Rac1MovementPhase = Rac1RatchetMovementPhase.Grounded };
        public void SetPosition(Vector3 position) => _position = position;
        public IDisposable PreserveDevelopmentSettings() => throw new NotSupportedException();
        public void ResetCollisionDiagnostics() => throw new NotSupportedException();
        public void ResetToSpawn() => throw new NotSupportedException();
        public void ResetAt(Vector3 _) => throw new NotSupportedException();
        public void SetRotation(Vector3 _) => throw new NotSupportedException();
        public void SetYaw(double _) => throw new NotSupportedException();
        public void SetBackpack(Rac1TraversalEquipmentId? _) => throw new NotSupportedException();
        public void SetClank(bool _) => throw new NotSupportedException();
        public void CyclePack() => throw new NotSupportedException();
        public void ToggleSwingshot() => throw new NotSupportedException();
        public void SetSnapReach(float _) => throw new NotSupportedException();
        public void StageWater(Vector3 _) => throw new NotSupportedException();
        public void ConfigureTargets(ImmutableArray<RuntimeDynamicObject> _) => throw new NotSupportedException();
        public void RestoreWorldTargets() => throw new NotSupportedException();
        public IDisposable SubscribeDrowned(Action _) => throw new NotSupportedException();
    }

}
