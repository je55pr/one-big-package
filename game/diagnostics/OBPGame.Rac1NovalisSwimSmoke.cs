using Godot;
using OBP.Godot;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>
    /// Novalis-only host integration on original water-tagged collision, without
    /// synthetic floors, manual water state injection or PCSX2. The entry point
    /// stages a player position but all subsequent motion uses input actions.
    /// </summary>
    private async Task RunRac1NovalisSwimSmokeAsync()
    {
        try
        {
            Require(_world is { Game: "rac1", LevelId: 1 } && _player is not null &&
                _rac1NovalisWater is not null,
                "Novalis swimming smoke requires decoded LEVEL1");
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            await PhysicsFramesAsync(3);

            var player = _player!;
            const float nativeX = 115, nativeZ = 115;
            float surface = SampleRac1NovalisWaterHeight(new(-nativeX, 0, nativeZ))
                ?? throw new InvalidDataException("Expected authored water surface missing");
            Require(surface > 55 && surface < 57,
                $"Expected local waterworks height 56; actual {surface:F3}");
            player.StageDevelopmentWaterPosition(new(-nativeX, surface + .12f, nativeZ));
            await PhysicsFramesAsync(6);
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
            GD.Print($"[novalis-swim-smoke] dive playerY={player.GlobalPosition.Y:F3} oxygen={underwaterOxygen}");
            Vector3 beforeSwim = player.GlobalPosition;
            SetAnalogueSmokeInput(1f, 0f);
            await PhysicsFramesAsync(24);
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
            await PhysicsFramesAsync(50);
            Input.ActionRelease(RawGamepadInput.Jump);
            await PhysicsFramesAsync(3);
            var surfaced = player.Rac1SwimState ??
                throw new InvalidDataException("Surfacing lost swimming state");
            Require(surfaced.Water == OBP.RAC1.Player.Rac1WaterDomain.Surface &&
                surfaced.Oxygen == OBP.RAC1.Player.Rac1TraversalEquipmentWater.MaximumOxygen,
                $"Surfacing failed to refill: {surfaced} pos={player.GlobalPosition}");
            GD.Print($"[novalis-swim-smoke] surfaced playerY={player.GlobalPosition.Y:F3}, full oxygen");
            ApplicationLifecycle.RequestQuit(this, "rac1-novalis-swim-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[novalis-swim-smoke] FAIL {ex}");
            ApplicationLifecycle.RequestQuit(this, "rac1-novalis-swim-smoke-fail", 1);
        }
        finally
        {
            ClearMovementSmokeInput();
        }
    }
}
