using Godot;
using OBP.RAC3;
using OBP.RAC3.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Captures only the admitted world/pose facts, never the persistent player session.</summary>
internal sealed class GodotRac3RatchetSeedFixture(Rac3WorldModule? world, Func<PlayerHost?> player)
{
    internal Rac3RatchetSeedSnapshot Sample()
    {
        PlayerHost? current = player();
        if (world is null || !world.Current || current is null ||
            !GodotObject.IsInstanceValid(current) || !world.HasPlayer(current))
            throw new DiagnosticUnavailableException("The R&C3 world/pose audit lease has retired.");
        Vector3 position = current.GlobalPosition;
        // Native R&C3 coordinates: horizontal X is negated, horizontal Y is host Z.
        Rac3NativePoint ratchet = new(-position.X, position.Z, position.Y);
        return Rac3RatchetSeedSampler.Sample(world.World, ratchet);
    }
}
