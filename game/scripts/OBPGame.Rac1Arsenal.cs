using Godot;

namespace OBP.Game;

public partial class OBPGame
{
    private void ResetRac1Arsenal()
    {
        Rac1World.Weapons.Arsenal.Reset();
        if (_player is not null) { _player.Rac1EquipmentActionActive = false; _player.Rac1EquipmentLungeVelocity = Vector3.Zero; }
    }

    private void StepRac1ArsenalWorld()
    {
        if (_player is null) return;
        Rac1World.Weapons.Arsenal.Tick();
    }
}
