using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>Staged *real Godot* class-827 source mesh, clock, physics,
    /// damage and cleanup fixture. Not an ordinary retail playthrough.</summary>
    private async Task RunRac1FrogSmokeAsync()
    {
        try
        {
            Require(_world is { Game: "rac1", LevelId: 6 or 10 } && _player is not null &&
                _rac1Frogs is not null,
                "Frog smoke requires retail LEVEL6 or LEVEL10");
            int expected = _world!.LevelId == 6 ? 169 : 24;
            int expectedAdmitted = _world.LevelId == 6 ? 108 : 24;
            int expectedPaths = _world.LevelId == 6 ? 26 : 0;
            Require(_rac1Frogs!.RegisteredCount == expected,
                "Frog smoke native level population differs from original");
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            var frogs = _rac1Frogs!;
            var population = _rac1Enemies.Values.Where(e =>
                e.Node.Source.NativeClassId == Rac1FrogAuthored.ClassId).ToArray();
            Require(population.Length == expected, "Frog host created an incorrect population");
            Require(frogs.PresentedCount == expectedAdmitted,
                "Frog carrier/hidden/switch admission differs from source census");
            Require(population.Where(e => !frogs.Get(e.Node.Source.InstanceIndex).Presented)
                .All(e => e.Body.CollisionLayer == 0), "Frog dormant forms still collide");
            Require(population.Count(e => frogs.Get(e.Node.Source.InstanceIndex).PathIndex >= 0) == expectedPaths,
                "Frog source paths were flattened");
            GD.Print($"[frog-smoke] LEVEL{_world.LevelId} {expected} registered, {frogs.PresentedCount} admitted, {expectedPaths} routed; dormant collider check PASS");
            await PhysicsFramesAsync(5);

            // Stage one *authored* ordinary frog into an isolated reachable
            // collision fixture. The physics/animation/gameplay loops below are
            // real; only the positions and supporting floor are synthetic.
            var candidate = population.First(e => frogs.Get(e.Node.Source.InstanceIndex) is
            { Presented: true, NativeState: 8, PathIndex: -1 });
            int instance = candidate.Node.Source.InstanceIndex;
            var fixture = new Node3D { Name = "FrogEncounterFixture", Position = new(2000, 64, 2600) };
            AddChild(fixture);
            AddSyntheticTraversalMesh(fixture, "FrogFloor", 11,
                (new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20)),
                (new(-20, 0, -20), new(20, 0, 20), new(-20, 0, 20)));
            await PhysicsFramesAsync(4);
            var origin = fixture.GlobalPosition;
            candidate.Node.Root.GlobalPosition = origin;
            _player!.ApplyDevelopmentReset(origin + new Vector3(2.8f, .05f, 0));
            await PhysicsFramesAsync(4);
            Require(candidate.Body.CollisionLayer != 0, "Ordinary Frog lacks a host contact capsule");
            Require(candidate.Node.AnimationSample is not null, "Native Frog sequence was not decoded");
            var initialHealth = _rac1Nanotech.Probe().Nanotech;
            bool approached = false, attack = false, playerHit = false, windowTouch = false;
            float maxAttackFrame = 0, closestPlayer = float.PositiveInfinity, closestSocket = float.PositiveInfinity;
            for (int tick = 0; tick < 500; tick++)
            {
                await PhysicsFramesAsync(1);
                var state = frogs.Get(instance);
                approached |= state.NativeState == 9;
                attack |= state.NativeState == 12;
                if (state.NativeState == 12)
                {
                    maxAttackFrame = Math.Max(maxAttackFrame, candidate.Animation.Position);
                    closestPlayer = Math.Min(closestPlayer, candidate.Node.Root.GlobalPosition.DistanceTo(_player.GlobalPosition));
                    closestSocket = Math.Min(closestSocket, EnemySocket(candidate).DistanceTo(_player.GlobalPosition));
                    windowTouch |= candidate.Animation.Position is >= 10 and <= 15 && EnemyMeleeTouchesPlayer(candidate);
                }
                playerHit |= _rac1Nanotech.Probe().Nanotech < initialHealth;
                if (attack && playerHit) break;
            }
            GD.Print($"[frog-smoke] i{instance} attack window maxAnim={maxAttackFrame:F2} closestRoot={closestPlayer:F2} closestSocket={closestSocket:F2} touch={windowTouch} end={frogs.Get(instance).NativeState}");
            Require(approached, "Frog never entered native state9 approach");
            Require(attack, "Frog never entered native state12 attack");
            Require(playerHit, "Frog animation contact never damaged Ratchet");
            GD.Print($"[frog-smoke] i{instance} state9 approach, state12 animation-window attack, Nanotech damage PASS");
            Require(TryStrikeRac1Hostile(candidate.Node), "Frog rejected real host wrench damage descriptor");
            Require(frogs.Get(instance).Terminal, "Health-1 Frog did not terminate on lethal wrench contact");
            await PhysicsFramesAsync(3);
            Require(candidate.Body.CollisionLayer == 0 && !candidate.Node.Root.Visible,
                "Defeated Frog retained collider or visible mesh");
            Require(frogs.RegisteredCount == expected, "Source Frog census grew through over-spawn");
            GD.Print($"[frog-smoke] i{instance} one-hit defeat, collision/mesh cleanup and fixed population PASS");
            fixture.QueueFree();
            ApplicationLifecycle.RequestQuit(this, "rac1-frog-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PushError($"[frog-smoke] FAIL {ex}");
            ApplicationLifecycle.RequestQuit(this, "rac1-frog-smoke-fail", 1);
        }
    }
}
