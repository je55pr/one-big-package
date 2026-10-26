using OBP.Core;
using Godot;
using OBP.Godot;

namespace OBP.Game;

public partial class OBPGame
{
    /// <summary>
    /// Novalis-only host integration on original water-tagged collision, without
    /// synthetic floors, manual water state injection or PCSX2. The entry point
    /// stages a player position but all subsequent motion uses input actions.
    /// </summary>
    private async Task RunRac1NovalisSwimSmokeAsync()
    {
        var middleTrace = new List<string>();
        void TraceMiddle(string stage)
        {
            var p = _player!;
            middleTrace.Add($"{stage}: pos={p.GlobalPosition} velocity={p.Velocity} " +
                $"yaw={p.Rac1CurrentYaw:F6} control={p.MovementReproControlHeading:F6} " +
                $"action={p.Rac1TraversalAction} phase={p.Rac1MovementPhase} " +
                $"grounded={p.Rac1RecoveredSupportGrounded} hanging={p.Rac1Hanging} " +
                $"water={p.Rac1SwimState} nanotech={_rac1Nanotech.Probe()}");
        }
        try
        {
            Require(_world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 1 } && _player is not null &&
                _rac1NovalisWater is not null,
                "Novalis swimming smoke requires decoded LEVEL1");
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            // Isolate every water fixture from combat, not only drowning. A
            // live Peckbot can reach this staged pool and interrupt the ledge
            // jump with contact damage. This is not an encounter/playthrough.
            foreach (var enemy in _rac1Enemies.Values)
            {
                enemy.Node.Root.GlobalPosition = new(2000, 100, 2000);
                enemy.Body.GlobalPosition = enemy.Node.Root.GlobalPosition;
            }
            await PhysicsFramesAsync(3);

            var player = _player!;
            const float nativeX = 115, nativeZ = 115;
            float surface = SampleRac1NovalisWaterHeight(new(-nativeX, 0, nativeZ))
                ?? throw new InvalidDataException("Expected authored water surface missing");
            Require(surface > 55 && surface < 57,
                $"Expected local waterworks height 56; actual {surface:F3}");
            player.StageDevelopmentWaterPosition(new(-nativeX, surface + .12f, nativeZ));
            await PhysicsFramesAsync(30);
            Require(player.Rac1SwimState is { Water: OBP.RAC1.Player.Rac1WaterDomain.Surface },
                $"Surface entry failed: state={player.Rac1SwimState} pos={player.GlobalPosition}");
            GD.Print($"[novalis-swim-smoke] surface entry waterY={surface:F3} playerY={player.GlobalPosition.Y:F3}");

            Input.ActionPress(RawGamepadInput.Action);
            await PhysicsFramesAsync(48);
            Input.ActionRelease(RawGamepadInput.Action);
            await PhysicsFramesAsync(3);
            var submerged = player.Rac1SwimState ??
                throw new InvalidDataException("Dive lost swimming state");
            Require(submerged.Water == OBP.RAC1.Player.Rac1WaterDomain.Underwater,
                $"Dive never reached underwater domain: {submerged} pos={player.GlobalPosition}");
            int underwaterOxygen = submerged.Oxygen;
            Require(underwaterOxygen < OBP.RAC1.Player.Rac1TraversalEquipmentWater.MaximumOxygen,
                "Underwater did not drain oxygen");
            Require(_playerHud?.FindChild("Air", true, false) is Control { Visible: true },
                "Underwater air meter is missing from the normal HUD");
            GD.Print($"[novalis-swim-smoke] dive playerY={player.GlobalPosition.Y:F3} oxygen={underwaterOxygen}");
            Vector3 beforeSwim = player.GlobalPosition;
            SetAnalogueSmokeInput(1f, 0f);
            await PhysicsFramesAsync(60);
            SetAnalogueSmokeInput(0f, 0f);
            Vector3 afterSwim = player.GlobalPosition;
            float horizontal = new Vector2(afterSwim.X - beforeSwim.X,
                afterSwim.Z - beforeSwim.Z).Length();
            Require(horizontal > .35f &&
                player.Rac1SwimState is { Water: OBP.RAC1.Player.Rac1WaterDomain.Underwater },
                $"Underwater steering blocked or lost water: distance={horizontal:F3}, state={player.Rac1SwimState}");
            underwaterOxygen = player.Rac1SwimState!.Value.Oxygen;
            GD.Print($"[novalis-swim-smoke] underwater horizontal steering={horizontal:F3} units");

            // Holding ascend while still too deep MUST NOT reset breath.
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(6);
            var ascending = player.Rac1SwimState ??
                throw new InvalidDataException("Ascend lost swimming state");
            Require(ascending.Water == OBP.RAC1.Player.Rac1WaterDomain.Underwater &&
                ascending.Oxygen < underwaterOxygen,
                $"Ascend prematurely refilled oxygen: {ascending} pos={player.GlobalPosition}");
            for (int i = 0; i < 240 && player.Rac1SwimState?.Water != OBP.RAC1.Player.Rac1WaterDomain.Surface; i++)
                await PhysicsFramesAsync(1);
            Input.ActionRelease(RawGamepadInput.Jump);
            await PhysicsFramesAsync(3);
            var surfaced = player.Rac1SwimState ??
                throw new InvalidDataException("Surfacing lost swimming state");
            Require(surfaced.Water == OBP.RAC1.Player.Rac1WaterDomain.Surface &&
                surfaced.Oxygen == OBP.RAC1.Player.Rac1TraversalEquipmentWater.MaximumOxygen,
                $"Surfacing failed to refill: {surfaced} pos={player.GlobalPosition}");
            Require(_playerHud?.FindChild("Air", true, false) is Control { Visible: false },
                "Surface state retained the underwater air meter");
            GD.Print($"[novalis-swim-smoke] surfaced playerY={player.GlobalPosition.Y:F3}, full oxygen");

            // Exact pool marked by the fourth human run. Staging is explicit;
            // entry, paddling, Cross exit and re-entry use the live input path.
            Vector3 reportedPool = new(-295.03513f, 82.85f, 149.39476f);
            float reportedSurface = SampleRac1NovalisWaterHeight(reportedPool)
                ?? throw new InvalidDataException("Reported pool surface is missing");
            Require(Math.Abs(reportedSurface - 82.25f) < .01f, "Reported pool height changed");
            ClearMovementSmokeInput();
            player.StageDevelopmentWaterPosition(reportedPool);
            await PhysicsFramesAsync(90);
            Require(player.Rac1SwimState is { NativeAction: 0x37, NativeSequence: 58 },
                $"Reported pool still freezes in an airborne pose: {player.Rac1SwimState} pos={player.GlobalPosition}");
            Require(Math.Abs(player.GlobalPosition.Y - (reportedSurface - .12f)) < .15f,
                $"Surface bob did not settle: {player.GlobalPosition}");
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(8);
            Require(!player.Rac1SwimmingActive && player.Rac1TraversalAction == 0x12,
                $"Surface Cross did not select native water exit: action={player.Rac1TraversalAction}");
            float exitPeak = player.GlobalPosition.Y;
            for (int i = 0; i < 55; i++)
            {
                await PhysicsFramesAsync(1);
                exitPeak = Math.Max(exitPeak, player.GlobalPosition.Y);
            }
            Input.ActionRelease(RawGamepadInput.Jump);
            Require(exitPeak > reportedSurface + .8f,
                $"Native water exit never cleared the surface: peak={exitPeak:F3}");
            await PhysicsFramesAsync(90);
            Require(player.Rac1SwimState is { NativeSequence: 58 },
                $"Returning to water lost surface animation: {player.Rac1SwimState}");
            Vector3 paddleStart = player.GlobalPosition;
            SetAnalogueSmokeInput(.65f, 0);
            await PhysicsFramesAsync(60);
            Require(player.Rac1SwimState is { NativeSequence: 101 },
                $"Surface steering did not select paddling: {player.Rac1SwimState}");
            Require(player.GlobalPosition.DistanceTo(paddleStart) > .05f,
                $"Surface paddle did not generate motion: start={paddleStart} end={player.GlobalPosition} velocity={player.Velocity} clock={player.Rac1WaterAnimationFacts}");
            ClearMovementSmokeInput();
            GD.Print($"[novalis-swim-smoke] human pool: surface58, Cross/action12 peak={exitPeak:F3}, re-entry58, paddle101 PASS");

            // Fifth human recording: the middle section has an authored
            // 83.5-unit step above this 82.25 water plane. Stage only the
            // approach; Cross, ledge admission and climbing use normal input.
            const double middleHeading = .943;
            player.Rac1CurrentYaw = middleHeading;
            player.StageDevelopmentWaterPosition(new(-294.6f, 82.85f, 148.9f));
            await PhysicsFramesAsync(90);
            TraceMiddle("staged surface");
            for (int i = 0; i < 75; i++)
            {
                double stickYaw = player.MovementReproControlHeading - middleHeading;
                SetRac1SmokeConditionedInput(Math.Sin(stickYaw), Math.Cos(stickYaw));
                await PhysicsFramesAsync(1);
            }
            ClearMovementSmokeInput();
            await PhysicsFramesAsync(90);
            TraceMiddle("approach settled");
            Require(player.Rac1SwimState is { NativeSequence: 58 },
                $"Middle-section approach did not settle to surface idle: pos={player.GlobalPosition} action={player.Rac1TraversalAction}");
            Input.ActionPress(RawGamepadInput.Jump);
            for (int i = 0; i < 90 && !player.Rac1Hanging; i++)
            {
                await PhysicsFramesAsync(1);
            }
            ClearMovementSmokeInput();
            Require(player.Rac1Hanging,
                $"Cross from water did not grab the middle step: pos={player.GlobalPosition} action={player.Rac1TraversalAction}");
            await PhysicsFramesAsync(60);
            TraceMiddle("hang settled, Cross released");
            Require(player.Rac1Hanging && player.Rac1TraversalAction == 0x19,
                $"Middle step did not retain its ledge hang: pos={player.GlobalPosition} action={player.Rac1TraversalAction}");
            Input.ActionPress(RawGamepadInput.Jump);
            bool landedMiddle = false;
            for (int i = 0; i < 120; i++)
            {
                // A distinct Cross edge starts the native ledge jump. Release
                // it after admission, rather than holding it through re-entry.
                if (i == 2)
                {
                    Input.ActionRelease(RawGamepadInput.Jump);
                    Require(!player.Rac1Hanging && player.Rac1TraversalAction == 0x1c,
                        "Cross did not admit the native middle-section ledge jump");
                }
                double stickYaw = player.MovementReproControlHeading - middleHeading;
                if (i < 18) SetRac1SmokeConditionedInput(Math.Sin(stickYaw), Math.Cos(stickYaw));
                else SetAnalogueSmokeInput(0, 0);
                await PhysicsFramesAsync(1);
                if (i < 3 || i % 10 == 9) TraceMiddle($"ledge jump tick {i + 1}");
                if (!player.Rac1Hanging && player.Rac1RecoveredSupportGrounded && player.GlobalPosition.Y > 83.49f)
                {
                    landedMiddle = true;
                    break;
                }
            }
            ClearMovementSmokeInput();
            TraceMiddle("ledge jump completed");
            Require(landedMiddle && !player.Rac1SwimmingActive,
                $"Cross from the hang did not reach the middle section: pos={player.GlobalPosition} action={player.Rac1TraversalAction}");
            GD.Print($"[novalis-swim-smoke] human middle section: surface, Cross, grab, hang, Cross and solid landing PASS pos={player.GlobalPosition}");

            // The reported opening puddle has a 0x60 water face only 0.1875
            // above solid 0x1f terrain. It cannot own surface-swim buoyancy.
            // This stages the authored location; movement/jumping use input.
            Vector3 puddle = new(-179.8f, 59.9f, 129.5f);
            float puddleHeight = SampleRac1NovalisWaterHeight(puddle)
                ?? throw new InvalidDataException("Reported shallow water is missing");
            player.StageDevelopmentWaterPosition(puddle);
            await PhysicsFramesAsync(30);
            Require(!player.Rac1SwimmingActive && player.Rac1RecoveredSupportGrounded &&
                player.Rac1MovementPhase == OBP.RAC1.Player.Rac1RatchetMovementPhase.Grounded,
                $"Shallow water stole grounded motion: swim={player.Rac1SwimmingActive}, phase={player.Rac1MovementPhase}, pos={player.GlobalPosition}");
            float bottomY = player.GlobalPosition.Y;
            Require(puddleHeight - bottomY is > .1f and < .25f,
                $"Expected the reported thin puddle, water={puddleHeight:F4}, feet={bottomY:F4}");
            Vector3 walkStart = player.GlobalPosition;
            SetAnalogueSmokeInput(1f, 0f);
            await PhysicsFramesAsync(18);
            float walked = new Vector2(player.GlobalPosition.X - walkStart.X,
                player.GlobalPosition.Z - walkStart.Z).Length();
            Require(walked > .1f && player.AnimationState is
                OBP.Runtime.Player.PlayerAnimationState.Walk or OBP.Runtime.Player.PlayerAnimationState.Run,
                $"Puddle walking/animation failed: distance={walked:F4}, animation={player.AnimationState}");
            SetAnalogueSmokeInput(0f, 0f);
            await PhysicsFramesAsync(6);
            Require(!player.Rac1SwimmingActive, "Walking through the puddle entered swimming");
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(14);
            Input.ActionRelease(RawGamepadInput.Jump);
            Require(!player.Rac1SwimmingActive && player.GlobalPosition.Y > bottomY + .4f,
                $"Cross failed from shallow water: phase={player.Rac1MovementPhase}, pos={player.GlobalPosition}");
            await PhysicsFramesAsync(90);
            Require(!player.Rac1SwimmingActive && player.Rac1RecoveredSupportGrounded,
                "Landing back in shallow water did not restore ordinary support");
            GD.Print("[novalis-swim-smoke] reported shallow puddle: walk, Cross jump and landing PASS");

            // No oxygen/state injection: let the source counter exhaust and
            // require the actual sequence95 clock to deliver the death event.
            ClearMovementSmokeInput();
            player.StageDevelopmentWaterPosition(reportedPool);
            await PhysicsFramesAsync(90);
            Input.ActionPress(RawGamepadInput.Action);
            await PhysicsFramesAsync(100);
            Input.ActionRelease(RawGamepadInput.Action);
            bool drowned = false, sawDrowning = false;
            player.Rac1NovalisDrowned += () => drowned = true;
            for (int i = 0; i < 1250 && !drowned; i++)
            {
                await PhysicsFramesAsync(1);
                if (player.Rac1SwimState is { Drowning: true, NativeSequence: 95 }) sawDrowning = true;
            }
            Require(sawDrowning && drowned,
                $"Native drowning sequence did not reach the host event: pose={sawDrowning}, event={drowned}, state={player.Rac1SwimState}");
            GD.Print("[novalis-swim-smoke] oxygen exhaustion, sequence95, completed death event PASS");
            ApplicationLifecycle.RequestQuit(this, "rac1-novalis-swim-smoke-pass", 0);
        }
        catch (OperationCanceledException error) when (Diagnostics.LegacyDiagnosticCancellation.IsOwned(error))
        {
            throw;
        }
        catch (Exception ex)
        {
            foreach (string sample in middleTrace)
                GD.PrintErr($"[novalis-swim-smoke] middle trace {sample}");
            GD.PrintErr($"[novalis-swim-smoke] FAIL {ex}");
            ApplicationLifecycle.RequestQuit(this, "rac1-novalis-swim-smoke-fail", 1);
        }
        finally
        {
            ClearMovementSmokeInput();
        }
    }
}
