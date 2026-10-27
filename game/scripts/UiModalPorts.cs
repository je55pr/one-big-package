using Godot;
using OBP.Core;
using OBP.Runtime.Gameplay;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

/// <summary>
/// Typed equipment boundary for modal views. Admission, assignment, and selection
/// remain with the player equipment session, not the UI lifetime coordinator.
/// </summary>
internal sealed record ModalEquipmentPorts(
    Func<IReadOnlyList<EquipmentCatalogEntry>> Catalog,
    Func<IReadOnlyList<SourceWeaponKey?>> QuickSlots,
    Func<string, EquipmentChangeResult> Equip,
    Func<EquipmentChangeResult> Unequip,
    Func<string, bool, EquipmentChangeResult> SetGadget,
    Func<int, string?, EquipmentChangeResult> AssignSlot,
    Func<int, int, bool> AssignNativeSlot,
    Action<Rac1WeaponId> SelectNativeWeapon,
    Func<SourceWeaponKey> CurrentWeapon,
    Action<SourceWeaponKey> SelectWeapon,
    Func<SourceWeaponKey, bool> CanRepeatWeapon,
    Func<WeaponQuickSelectUi.Entry[]> WheelEntries);

/// <summary>
/// Scene and navigation actions for the one modal lifetime owner.
/// No direct access to the host's mutable player/equipment internals.
/// </summary>
internal sealed record UiModalPorts(
    Node3D Host,
    Func<bool> CanPause,
    Func<bool> CanQuickSelect,
    Func<PauseMenuUi> BuildPauseMenu,
    Func<PlayerHost?> Player,
    Func<bool> HasSourceView,
    Action<bool> SetSourceVisible,
    Action<bool> SetSelectorVisible,
    Action<bool> SetPlayerAudioPaused,
    Func<InputEvent, bool> TryMissionInteraction,
    Func<InputEvent, bool> TryTaxiInteraction,
    ModalEquipmentPorts Equipment);
