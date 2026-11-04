using System.Numerics;
using System.Buffers.Binary;
using OBP.Core;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public sealed class Rac1NovalisSwimScenario(MovementDiagnosticContext context, IDiagnosticWaterFixture water) : MovementScenarioSupport(context)
{
    /// <summary>
    /// Novalis-only host integration on original water-tagged collision, without
    /// synthetic floors, manual water state injection or PCSX2. The entry point
    /// stages a player position but all subsequent motion uses input actions.
    /// </summary>
    public async Task RunAsync()
    {
        var middleTrace = new List<string>();
        void TraceMiddle(string stage)
        {
            var p = Player!;
            middleTrace.Add($"{stage}: pos={p.Read().GlobalPosition} velocity={p.Read().Velocity} " +
                $"yaw={p.Read().Rac1CurrentYaw:F6} control={p.Read().MovementReproControlHeading:F6} " +
                $"action={p.Read().Rac1TraversalAction} phase={p.Read().Rac1MovementPhase} " +
                $"grounded={p.Read().Rac1RecoveredSupportGrounded} hanging={p.Read().Rac1Hanging} " +
                $"water={p.Read().Rac1SwimState} nanotech={water.ReadNanotech()}");
        }
        try
        {
            Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 1, WaterPresent: true },
                "Novalis swimming smoke requires decoded LEVEL1");
            using var frameRate = World.LimitFrameRate(60);
            ClearMovementSmokeInput();
            // Isolate every water fixture from combat, not only drowning. A
            // live Peckbot can reach this staged pool and interrupt the ledge
            // jump with contact damage. This is not an encounter/playthrough.
            using var combatIsolation = water.IsolateCombat();
            await PhysicsFramesAsync(3);

            var player = Player!;
            const float nativeX = 115, nativeZ = 115;
            float surface = water.SampleHeight(new(-nativeX, 0, nativeZ))
                ?? throw new InvalidDataException("Expected authored water surface missing");
            Require(surface > 55 && surface < 57,
                $"Expected local waterworks height 56; actual {surface:F3}");
            player.StageWater(new(-nativeX, surface + .12f, nativeZ));
            await PhysicsFramesAsync(30);
            Require(player.Read().Rac1SwimState is { Water: OBP.RAC1.Player.Rac1WaterDomain.Surface },
                $"Surface entry failed: state={player.Read().Rac1SwimState} pos={player.Read().GlobalPosition}");
            Log($"[novalis-swim-smoke] surface entry waterY={surface:F3} playerY={player.Read().GlobalPosition.Y:F3}");

            Driver.SetAction(MovementDiagnosticDriver.Action, true);
            await PhysicsFramesAsync(48);
            Driver.SetAction(MovementDiagnosticDriver.Action, false);
            await PhysicsFramesAsync(3);
            var submerged = player.Read().Rac1SwimState ??
                throw new InvalidDataException("Dive lost swimming state");
            Require(submerged.Water == OBP.RAC1.Player.Rac1WaterDomain.Underwater,
                $"Dive never reached underwater domain: {submerged} pos={player.Read().GlobalPosition}");
            int underwaterOxygen = submerged.Oxygen;
            Require(underwaterOxygen < OBP.RAC1.Player.Rac1TraversalEquipmentWater.MaximumOxygen,
                "Underwater did not drain oxygen");
            Require(water.AirMeterVisible,
                "Underwater air meter is missing from the normal HUD");
            Log($"[novalis-swim-smoke] dive playerY={player.Read().GlobalPosition.Y:F3} oxygen={underwaterOxygen}");
            Vector3 beforeSwim = player.Read().GlobalPosition;
            SetAnalogueSmokeInput(1f, 0f);
            await PhysicsFramesAsync(60);
            SetAnalogueSmokeInput(0f, 0f);
            Vector3 afterSwim = player.Read().GlobalPosition;
            float horizontal = new Vector2(afterSwim.X - beforeSwim.X,
                afterSwim.Z - beforeSwim.Z).Length();
            Require(horizontal > .35f &&
                player.Read().Rac1SwimState is { Water: OBP.RAC1.Player.Rac1WaterDomain.Underwater },
                $"Underwater steering blocked or lost water: distance={horizontal:F3}, state={player.Read().Rac1SwimState}");
            underwaterOxygen = player.Read().Rac1SwimState!.Value.Oxygen;
            Log($"[novalis-swim-smoke] underwater horizontal steering={horizontal:F3} units");

            // Holding ascend while still too deep MUST NOT reset breath.
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            await PhysicsFramesAsync(6);
            var ascending = player.Read().Rac1SwimState ??
                throw new InvalidDataException("Ascend lost swimming state");
            Require(ascending.Water == OBP.RAC1.Player.Rac1WaterDomain.Underwater &&
                ascending.Oxygen < underwaterOxygen,
                $"Ascend prematurely refilled oxygen: {ascending} pos={player.Read().GlobalPosition}");
            for (int i = 0; i < 240 && player.Read().Rac1SwimState?.Water != OBP.RAC1.Player.Rac1WaterDomain.Surface; i++)
                await PhysicsFramesAsync(1);
            Driver.SetAction(MovementDiagnosticDriver.Jump, false);
            await PhysicsFramesAsync(3);
            var surfaced = player.Read().Rac1SwimState ??
                throw new InvalidDataException("Surfacing lost swimming state");
            Require(surfaced.Water == OBP.RAC1.Player.Rac1WaterDomain.Surface &&
                surfaced.Oxygen == OBP.RAC1.Player.Rac1TraversalEquipmentWater.MaximumOxygen,
                $"Surfacing failed to refill: {surfaced} pos={player.Read().GlobalPosition}");
            Require(!water.AirMeterVisible,
                "Surface state retained the underwater air meter");
            Log($"[novalis-swim-smoke] surfaced playerY={player.Read().GlobalPosition.Y:F3}, full oxygen");

            // Exact pool marked by the fourth human run. Staging is explicit;
            // entry, paddling, Cross exit and re-entry use the live input path.
            Vector3 reportedPool = new(-295.03513f, 82.85f, 149.39476f);
            float reportedSurface = water.SampleHeight(reportedPool)
                ?? throw new InvalidDataException("Reported pool surface is missing");
            Require(Math.Abs(reportedSurface - 82.25f) < .01f, "Reported pool height changed");
            ClearMovementSmokeInput();
            player.StageWater(reportedPool);
            await PhysicsFramesAsync(90);
            Require(player.Read().Rac1SwimState is { NativeAction: 0x37, NativeSequence: 58 },
                $"Reported pool still freezes in an airborne pose: {player.Read().Rac1SwimState} pos={player.Read().GlobalPosition}");
            Require(Math.Abs(player.Read().GlobalPosition.Y - (reportedSurface - .12f)) < .15f,
                $"Surface bob did not settle: {player.Read().GlobalPosition}");
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            await PhysicsFramesAsync(8);
            Require(!player.Read().Rac1SwimmingActive && player.Read().Rac1TraversalAction == 0x12,
                $"Surface Cross did not select native water exit: action={player.Read().Rac1TraversalAction}");
            float exitPeak = player.Read().GlobalPosition.Y;
            for (int i = 0; i < 55; i++)
            {
                await PhysicsFramesAsync(1);
                exitPeak = Math.Max(exitPeak, player.Read().GlobalPosition.Y);
            }
            Driver.SetAction(MovementDiagnosticDriver.Jump, false);
            Require(exitPeak > reportedSurface + .8f,
                $"Native water exit never cleared the surface: peak={exitPeak:F3}");
            await PhysicsFramesAsync(90);
            Require(player.Read().Rac1SwimState is { NativeSequence: 58 },
                $"Returning to water lost surface animation: {player.Read().Rac1SwimState}");
            Vector3 paddleStart = player.Read().GlobalPosition;
            SetAnalogueSmokeInput(.65f, 0);
            await PhysicsFramesAsync(60);
            Require(player.Read().Rac1SwimState is { NativeSequence: 101 },
                $"Surface steering did not select paddling: {player.Read().Rac1SwimState}");
            Require(player.Read().GlobalPosition.DistanceTo(paddleStart) > .05f,
                $"Surface paddle did not generate motion: start={paddleStart} end={player.Read().GlobalPosition} velocity={player.Read().Velocity} clock={player.Read().Rac1WaterAnimationFacts}");
            ClearMovementSmokeInput();
            Log($"[novalis-swim-smoke] human pool: surface58, Cross/action12 peak={exitPeak:F3}, re-entry58, paddle101 PASS");

            // Fifth human recording: the middle section has an authored
            // 83.5-unit step above this 82.25 water plane. Stage only the
            // approach; Cross, ledge admission and climbing use normal input.
            const double middleHeading = .943;
            player.SetYaw(middleHeading);
            player.StageWater(new(-294.6f, 82.85f, 148.9f));
            await PhysicsFramesAsync(90);
            TraceMiddle("staged surface");
            for (int i = 0; i < 75; i++)
            {
                double stickYaw = player.Read().MovementReproControlHeading - middleHeading;
                SetRac1SmokeConditionedInput(Math.Sin(stickYaw), Math.Cos(stickYaw));
                await PhysicsFramesAsync(1);
            }
            ClearMovementSmokeInput();
            await PhysicsFramesAsync(90);
            TraceMiddle("approach settled");
            Require(player.Read().Rac1SwimState is { NativeSequence: 58 },
                $"Middle-section approach did not settle to surface idle: pos={player.Read().GlobalPosition} action={player.Read().Rac1TraversalAction}");
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            for (int i = 0; i < 90 && !player.Read().Rac1Hanging; i++)
            {
                await PhysicsFramesAsync(1);
            }
            ClearMovementSmokeInput();
            Require(player.Read().Rac1Hanging,
                $"Cross from water did not grab the middle step: pos={player.Read().GlobalPosition} action={player.Read().Rac1TraversalAction}");
            await PhysicsFramesAsync(60);
            TraceMiddle("hang settled, Cross released");
            Require(player.Read().Rac1Hanging && player.Read().Rac1TraversalAction == 0x19,
                $"Middle step did not retain its ledge hang: pos={player.Read().GlobalPosition} action={player.Read().Rac1TraversalAction}");
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            bool landedMiddle = false;
            for (int i = 0; i < 120; i++)
            {
                // A distinct Cross edge starts the native ledge jump. Release
                // it after admission, rather than holding it through re-entry.
                if (i == 2)
                {
                    Driver.SetAction(MovementDiagnosticDriver.Jump, false);
                    Require(!player.Read().Rac1Hanging && player.Read().Rac1TraversalAction == 0x1c,
                        "Cross did not admit the native middle-section ledge jump");
                }
                double stickYaw = player.Read().MovementReproControlHeading - middleHeading;
                if (i < 18) { SetRac1SmokeConditionedInput(Math.Sin(stickYaw), Math.Cos(stickYaw)); }
                else SetAnalogueSmokeInput(0, 0);
                await PhysicsFramesAsync(1);
                if (i < 3 || i % 10 == 9) { TraceMiddle($"ledge jump tick {i + 1}"); }
                if (!player.Read().Rac1Hanging && player.Read().Rac1RecoveredSupportGrounded && player.Read().GlobalPosition.Y > 83.49f)
                {
                    landedMiddle = true;
                    break;
                }
            }
            ClearMovementSmokeInput();
            TraceMiddle("ledge jump completed");
            Require(landedMiddle && !player.Read().Rac1SwimmingActive,
                $"Cross from the hang did not reach the middle section: pos={player.Read().GlobalPosition} action={player.Read().Rac1TraversalAction}");
            Log($"[novalis-swim-smoke] human middle section: surface, Cross, grab, hang, Cross and solid landing PASS pos={player.Read().GlobalPosition}");

            // The reported opening puddle has a 0x60 water face only 0.1875
            // above solid 0x1f terrain. It cannot own surface-swim buoyancy.
            // This stages the authored location; movement/jumping use input.
            Vector3 puddle = new(-179.8f, 59.9f, 129.5f);
            float puddleHeight = water.SampleHeight(puddle)
                ?? throw new InvalidDataException("Reported shallow water is missing");
            player.StageWater(puddle);
            await PhysicsFramesAsync(30);
            Require(!player.Read().Rac1SwimmingActive && player.Read().Rac1RecoveredSupportGrounded &&
                player.Read().Rac1MovementPhase == OBP.RAC1.Player.Rac1RatchetMovementPhase.Grounded,
                $"Shallow water stole grounded motion: swim={player.Read().Rac1SwimmingActive}, phase={player.Read().Rac1MovementPhase}, pos={player.Read().GlobalPosition}");
            float bottomY = player.Read().GlobalPosition.Y;
            Require(puddleHeight - bottomY is > .1f and < .25f,
                $"Expected the reported thin puddle, water={puddleHeight:F4}, feet={bottomY:F4}");
            Vector3 walkStart = player.Read().GlobalPosition;
            SetAnalogueSmokeInput(1f, 0f);
            await PhysicsFramesAsync(18);
            float walked = new Vector2(player.Read().GlobalPosition.X - walkStart.X,
                player.Read().GlobalPosition.Z - walkStart.Z).Length();
            Require(walked > .1f && player.Read().AnimationState is
                OBP.Runtime.Player.PlayerAnimationState.Walk or OBP.Runtime.Player.PlayerAnimationState.Run,
                $"Puddle walking/animation failed: distance={walked:F4}, animation={player.Read().AnimationState}");
            SetAnalogueSmokeInput(0f, 0f);
            await PhysicsFramesAsync(6);
            Require(!player.Read().Rac1SwimmingActive, "Walking through the puddle entered swimming");
            Driver.SetAction(MovementDiagnosticDriver.Jump, true);
            await PhysicsFramesAsync(14);
            Driver.SetAction(MovementDiagnosticDriver.Jump, false);
            Require(!player.Read().Rac1SwimmingActive && player.Read().GlobalPosition.Y > bottomY + .4f,
                $"Cross failed from shallow water: phase={player.Read().Rac1MovementPhase}, pos={player.Read().GlobalPosition}");
            await PhysicsFramesAsync(90);
            Require(!player.Read().Rac1SwimmingActive && player.Read().Rac1RecoveredSupportGrounded,
                "Landing back in shallow water did not restore ordinary support");
            Log("[novalis-swim-smoke] reported shallow puddle: walk, Cross jump and landing PASS");

            // No oxygen/state injection: let the source counter exhaust and
            // require the actual sequence95 clock to deliver the death event.
            ClearMovementSmokeInput();
            player.StageWater(reportedPool);
            await PhysicsFramesAsync(90);
            Driver.SetAction(MovementDiagnosticDriver.Action, true);
            await PhysicsFramesAsync(100);
            Driver.SetAction(MovementDiagnosticDriver.Action, false);
            bool drowned = false, sawDrowning = false;
            using var drowning = player.SubscribeDrowned(() => drowned = true);
            for (int i = 0; i < 1250 && !drowned; i++)
            {
                await PhysicsFramesAsync(1);
                if (player.Read().Rac1SwimState is { Drowning: true, NativeSequence: 95 }) { sawDrowning = true; }
            }
            Require(sawDrowning && drowned,
                $"Native drowning sequence did not reach the host event: pose={sawDrowning}, event={drowned}, state={player.Read().Rac1SwimState}");
            Log("[novalis-swim-smoke] oxygen exhaustion, sequence95, completed death event PASS");
        }
        catch (OperationCanceledException error) when (error.CancellationToken == Context.Driver.Lifetime && Context.Driver.Lifetime.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            foreach (string sample in middleTrace)
                LogError($"[novalis-swim-smoke] middle trace {sample}");
            LogError($"[novalis-swim-smoke] FAIL {ex}");
            throw;
        }
        finally
        {
            CleanupStep(() => ClearMovementSmokeInput());
        }
    }

}
