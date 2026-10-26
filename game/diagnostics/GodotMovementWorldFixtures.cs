using System.Collections.Immutable;
using Godot;
using OBP.RAC1.Player;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed class GodotMovementWorld(Func<RuntimeWorld?> world, Func<int> collisions, Func<bool> water) : IDiagnosticMovementWorld
{
    public DiagnosticMovementWorld Read()
    {
        var content = world();
        return new(content?.Game.SourceGame, content?.LevelId, content?.Environment?.DeathHeight, collisions(), water());
    }
    public IDisposable LimitFrameRate(int rate)
    {
        int previous = Engine.MaxFps; Engine.MaxFps = rate;
        return new MovementFixtureLease(() => Engine.MaxFps = previous);
    }
}
internal sealed class GodotMovementWater(Func<ImmutableArray<(Node3D Root, Node3D Body)>> enemies,
    Func<NVector3, float?> height, Func<bool?> airVisible, Func<Rac1RatchetNanotechSnapshot> nanotech) : IDiagnosticWaterFixture
{
    public IDisposable IsolateCombat()
    {
        var saved = enemies().Select(pair => (pair.Root, pair.Body, RootPosition: pair.Root.GlobalPosition, BodyPosition: pair.Body.GlobalPosition)).ToArray();
        foreach (var item in saved) { item.Root.GlobalPosition = new(2000, 100, 2000); item.Body.GlobalPosition = item.Root.GlobalPosition; }
        return new MovementFixtureLease(() =>
        {
            foreach (var item in saved)
            {
                if (GodotObject.IsInstanceValid(item.Root)) { item.Root.GlobalPosition = item.RootPosition; }
                if (GodotObject.IsInstanceValid(item.Body)) { item.Body.GlobalPosition = item.BodyPosition; }
            }
        });
    }
    public float? SampleHeight(NVector3 position) => height(position);
    public bool AirMeterVisible => airVisible() ?? throw new DiagnosticUnavailableException("The native water HUD air panel is unavailable.");
    public Rac1RatchetNanotechSnapshot ReadNanotech() => nanotech();
}
internal sealed class DiagnosticEnvironmentalDeathFixture(Func<DiagnosticEnvironmentalDeath> read) : IDiagnosticEnvironmentalDeath
{
    public DiagnosticEnvironmentalDeath Read() => read();
}
internal sealed class DiagnosticControllerEquipmentFixture(Func<DiagnosticControllerEquipment> read,
    Action<OBP.RAC1.Gameplay.Rac1WeaponId> select, Action<int> cycle) : IDiagnosticControllerEquipment
{
    public DiagnosticControllerEquipment Read() => read();
    public void Select(OBP.RAC1.Gameplay.Rac1WeaponId weapon) => select(weapon);
    public void Cycle(int direction) => cycle(direction);
}
