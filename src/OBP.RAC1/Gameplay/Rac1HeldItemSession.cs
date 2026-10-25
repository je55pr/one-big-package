using OBP.Core;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// R&C1 draw/use timing over shared hand ownership, independently of the
/// equipment selected for Circle. Native inventory blocks and the complete
/// retail equipment-spawn state machine remain separate.
/// </summary>
public sealed class Rac1HeldItemSession
{
    // Veldin 0x210024 requests the switch after hero_item_use. The next slot
    // update closes it (0x210854); respawn needs frame > closeFrame+2 (0x20ea80).
    public const int CircleSwapSpawnDelayTicks = 4;
    public const int NativeSwitchItemAge = 3;
    public const int ThrowHeldAgeExclusiveMinimum = 8;
    public const int ThrowHeldAgeExclusiveMaximum = 17;
    public const int BlasterHeldAgeExclusiveMinimum = 5;

    private int? _pendingItem;
    private int _swapTicksRemaining;
    private int _itemAge;
    private int _pressAge = 1000;
    private bool _awaitingFirstUse;

    public PlayerHeldEquipmentSession Hand { get; }
    // A foreign item has no R&C1 item number. Never alias its id to native gear.
    public int? NativeItemId => Hand.Equipment is { } equipment
        ? equipment.SourceGame == ObpSourceGame.Rac1 ? equipment.NativeItemId : null : (int)Rac1WeaponId.Wrench;
    public bool Switching => _pendingItem.HasValue;

    public Rac1HeldItemSession(PlayerHeldEquipmentSession? hand = null) => Hand = hand ?? new();

    public readonly record struct CircleFrame(bool HandChanged, bool UseRequested);

    public void ResetForLevelEntry() => HoldWrench();

    public void HoldWrench()
    {
        Hand.HoldWrench();
        ResetUseInput();
    }

    public void HoldSelected(Rac1WeaponInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        HoldNativeItem(inventory.CurrentItemId);
        ResetUseInput();
    }

    /// <summary>
    /// One NTSC update of the Circle draw/use boundary. The switch press is not
    /// forwarded as a shot. Source weapon controllers still own ammo, cadence,
    /// startup animation and projectile admission after this boundary.
    /// </summary>
    public CircleFrame StepCircle(Rac1WeaponInventory inventory, bool pressed, bool held, bool admitted = true)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        _pressAge = pressed ? 0 : Math.Min(1000, _pressAge + 1);
        if (!admitted) return default;
        if (_pendingItem is { } pending && pending != inventory.CurrentItemId)
            ResetUseInput();

        if (_pendingItem is { } target)
        {
            if (--_swapTicksRemaining > 0) return default;
            HoldNativeItem(target);
            _pendingItem = null;
            // hero_slots_update increments +0x10b0 after spawning, while item
            // use runs earlier in the update. No use on the spawn update itself.
            _itemAge = NativeSwitchItemAge + 1;
            return new(true, false);
        }

        if (NativeItemId == (int)Rac1WeaponId.Wrench || NativeItemId != inventory.CurrentItemId)
        {
            if (pressed && inventory.CurrentItemId != (int)Rac1WeaponId.Wrench)
            {
                _pendingItem = inventory.CurrentItemId;
                _swapTicksRemaining = CircleSwapSpawnDelayTicks;
                _itemAge = NativeSwitchItemAge;
                _awaitingFirstUse = true;
            }
            return default;
        }

        bool use = pressed || held;
        if (_awaitingFirstUse)
        {
            use = inventory.Equipped switch
            {
                // 0x228870: recent press in min(age,7), or held while 8<age<17.
                Rac1WeaponId.FirstRanged or Rac1WeaponId.MineGlove or Rac1WeaponId.GloveOfDoom or
                    Rac1WeaponId.DroneDevice or Rac1WeaponId.DecoyGlove =>
                    _pressAge < Math.Min(_itemAge, 7) ||
                    held && _itemAge > ThrowHeldAgeExclusiveMinimum && _itemAge < ThrowHeldAgeExclusiveMaximum,
                // 0x228724: held after age5, or a fresh edge. The original
                // switch edge has already passed while the old hand slot closed.
                Rac1WeaponId.Blaster => pressed || held && _itemAge > BlasterHeldAgeExclusiveMinimum,
                // 0x22898c and 0x2b30a8: these paths need another press.
                Rac1WeaponId.Walloper => _pressAge < Math.Min(_itemAge, 10),
                Rac1WeaponId.VisibombGun => pressed,
                // These weapons' own Moby controllers read held Circle. Their
                // existing startup/cadence handling follows the common slot swap.
                _ => pressed || held,
            };
            if (use) _awaitingFirstUse = false;
        }
        _itemAge = Math.Min(1000, _itemAge + 1);
        return new(false, use);
    }

    private void ResetUseInput()
    {
        _pendingItem = null;
        _swapTicksRemaining = 0;
        _itemAge = 0;
        _pressAge = 1000;
        _awaitingFirstUse = false;
    }

    public void Restore(int nativeItemId, Rac1WeaponInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if ((uint)nativeItemId >= Rac1NativeInventoryLayout.ItemCount || !inventory.OwnsNativeItem(nativeItemId))
            throw new ArgumentException("The held item must be an owned R&C1 item.", nameof(nativeItemId));
        HoldNativeItem(nativeItemId);
        ResetUseInput();
    }

    private void HoldNativeItem(int item)
    {
        if (item == (int)Rac1WeaponId.Wrench) Hand.HoldWrench();
        else Hand.HoldEquipment(new(ObpSourceGame.Rac1, item));
    }
}
