namespace OBP.Runtime.Gameplay;

/// <summary>
/// Shared hand ownership, separate from the equipment selected for use. A
/// wrench attack keeps the wrench in hand until an equipment draw takes over.
/// Source adapters retain their own draw timing and weapon admission rules.
/// </summary>
public sealed class PlayerHeldEquipmentSession
{
    public SourceWeaponKey? Equipment { get; private set; }
    public bool WrenchHeld => Equipment is null;
    public bool IsHolding(SourceWeaponKey equipment) => Equipment == equipment;
    public void HoldWrench() => Equipment = null;
    public void HoldEquipment(SourceWeaponKey equipment) => Equipment = equipment.Validate();
}
