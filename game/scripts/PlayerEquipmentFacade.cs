using System.Collections.ObjectModel;
using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Game;

/// <summary>Copied scene observations; no nodes, models, or mutable asset caches cross this boundary.</summary>
internal sealed record PlayerEquipmentPresentationState(
    EquipmentResourceStatus Model, EquipmentResourceStatus Icon, string? ContextReason = null,
    bool BindingReady = true, string? BindingReason = null);

internal sealed record PlayerEquipmentContext(bool SwingshotTargets, bool Water);

/// <summary>Existing controller owners and focused scene/recording actions, resolved at use time.</summary>
internal sealed record PlayerEquipmentPorts(
    Func<PlayerSessionNativeState?> Native,
    Func<Rac1WeaponInventory> Inventory,
    Func<Rac1HeldItemSession> Held,
    Func<FusionWeaponSession?> Weapons,
    Func<SourceWeaponKey> CurrentWeapon,
    Func<bool> FusionTest,
    Func<IReadOnlyDictionary<ObpSourceGame, string>> Sources,
    Func<PlayerAvatarIdentity?> SelectedAvatar,
    Func<PlayerAvatarIdentity?> PresentedAvatar,
    Func<SourceWeaponKey, PlayerEquipmentPresentationState> Presentation,
    Func<PlayerEquipmentContext> Context,
    Action<SourceWeaponKey> SelectWeapon,
    Action ResetWeaponPresentation,
    Action<Rac1MovementCapabilities, Rac1TraversalEquipmentId?> ApplyMovementEquipment,
    Action<PlaytestCommand> Record);

/// <summary>
/// Player-lifetime catalogue/mutation facade. Inventories, ammunition, damage,
/// hand ownership and assignment rules stay with their existing session owners.
/// World changes supply fresh observations rather than replacing this facade.
/// </summary>
internal sealed class PlayerEquipmentFacade(PlayerEquipmentPorts ports) : IDisposable
{
    private PlayerEquipmentPorts? _ports = ports;
    private PlayerEquipmentPorts Ports => _ports ?? throw new ObjectDisposedException(nameof(PlayerEquipmentFacade));
    private static PlayerEquipmentCatalog Catalog => PlayerEquipmentRegistrations.Catalog;

    internal IReadOnlyDictionary<ObpSourceGame, string> Sources => new ReadOnlyDictionary<ObpSourceGame, string>(Ports.Sources().ToDictionary());
    internal IReadOnlyList<SourceWeaponKey?> ReadQuickSlots() => Ports.Native()?.Assignments.Capture().QuickSelect ?? Array.AsReadOnly(Array.Empty<SourceWeaponKey?>());

    internal IReadOnlyList<EquipmentCatalogEntry> Read()
    {
        var native = Ports.Native();
        var inventory = Ports.Inventory();
        var held = Ports.Held().Hand;
        var weapons = Ports.Weapons();
        var selected = Ports.CurrentWeapon();
        var assignments = native?.Assignments.Capture();
        var selectedAvatar = Ports.SelectedAvatar();
        var presentedAvatar = Ports.PresentedAvatar();
        var context = Ports.Context();
        return Catalog.Read(Sources, definition =>
        {
            var key = definition.Item;
            bool owned = definition.Category == EquipmentCategory.Appearance || key is { } item &&
                (item.SourceGame == ObpSourceGame.Rac1 ? inventory.OwnsNativeItem(item.NativeItemId) :
                    native?.LoadoutPolicy == PlayerLoadoutPolicy.DevelopmentAvailable || native is null && weapons is not null);
            if (definition.Category == EquipmentCategory.Appearance)
                return new(owned, selectedAvatar == definition.Appearance, presentedAvatar == definition.Appearance,
                    presentedAvatar == definition.Appearance ? EquipmentResourceStatus.Available : EquipmentResourceStatus.Unavailable,
                    EquipmentResourceStatus.Unavailable);
            if (definition.Category == EquipmentCategory.Gadget)
            {
                bool enabled = assignments?.EnabledGadgets.Contains(key!.Value) == true;
                string? reason = key switch
                {
                    { SourceGame: ObpSourceGame.Rac1, NativeItemId: (int)Rac1TraversalEquipmentId.Swingshot } when !context.SwingshotTargets =>
                        "No admitted source-native Swingshot target is present.",
                    { SourceGame: ObpSourceGame.Rac1, NativeItemId: (int)Rac1TraversalEquipmentId.HydroPack or (int)Rac1TraversalEquipmentId.OxygenMask } when !context.Water =>
                        "No admitted source-native water context is present.",
                    _ => null,
                };
                return new(owned, enabled, enabled && reason is null, EquipmentResourceStatus.NotRequired,
                    EquipmentResourceStatus.Unavailable, ContextAllowed: reason is null, ContextReason: reason);
            }
            var presentation = Ports.Presentation(key!.Value);
            var status = weapons?.Status(key.Value);
            return new(owned, selected == key, key == PlayerEquipmentRegistrations.Wrench ? held.WrenchHeld : held.IsHolding(key.Value),
                presentation.Model, presentation.Icon, status?.Capacity > 0 ? status.Ammo : null, status?.Capacity > 0 ? status.Capacity : null,
                ContextAllowed: presentation.ContextReason is null, ContextReason: presentation.ContextReason,
                PresentationReady: presentation.BindingReady, PresentationReason: presentation.BindingReason);
        });
    }

    internal EquipmentChangeResult Select(string catalogId)
    {
        var entry = Read().SingleOrDefault(entry => entry.Definition.Id == catalogId);
        if (entry is null) return new(false, "Unknown catalog entry.");
        if (!entry.Selectable) return new(false, entry.Reason);
        if (entry.Definition.Category == EquipmentCategory.Gadget) return SetGadgetEnabled(catalogId, true);
        if (entry.Definition.Appearance is { } avatar)
        {
            if (Ports.SelectedAvatar() != avatar)
                return new(false, "This appearance requires an admitted controller/pose profile before it can be selected.");
            Ports.Record(new("equip", catalogId));
            return new(true);
        }
        Ports.SelectWeapon(entry.Definition.Item!.Value);
        if (Ports.CurrentWeapon() != entry.Definition.Item)
            return new(false, entry.Reason ?? "The source-native controller did not accept the equipment selection.");
        Ports.Record(new("equip", catalogId));
        return new(true, entry.Reason);
    }

    internal EquipmentChangeResult AssignQuickSelect(int slot, string? catalogId)
    {
        if (Ports.Native() is not { } native) return new(false, "This explicit parity/diagnostic session has no OBP loadout.");
        if (slot < 0 || slot >= PlayerEquipmentAssignments.SlotCount)
            return new(false, "Quick Select slot must be between 0 and 7.");
        var entry = catalogId is null ? null : Read().SingleOrDefault(entry => entry.Definition.Id == catalogId);
        if (catalogId is not null && (entry is null || entry.Definition.Category != EquipmentCategory.Weapon))
            return new(false, "Quick Select requires an implemented weapon.");
        if (entry is { Selectable: false }) return new(false, entry.Reason);
        native.Assignments.Assign(slot, entry?.Definition.Item);
        Ports.Record(new("quick-slot", catalogId ?? "", slot));
        return new(true);
    }

    internal EquipmentChangeResult Unequip()
    {
        if (Ports.Native() is not { } native) return new(false, "This session has no OBP loadout.");
        native.Held.HoldWrench();
        Ports.ResetWeaponPresentation();
        Ports.Record(new("unequip", ""));
        return new(true, "Wrench held. Your selected weapon and ammunition are retained.");
    }

    internal EquipmentChangeResult SetGadgetEnabled(string catalogId, bool enabled)
    {
        var entry = Read().SingleOrDefault(entry => entry.Definition.Id == catalogId);
        if (Ports.Native() is not { } native) return new(false, "This session has no OBP loadout.");
        if (entry?.Definition.Category != EquipmentCategory.Gadget)
            return new(false, "No supported gadget in this player loadout.");
        if (entry.Definition.Implementation != EquipmentImplementation.Implemented || enabled && !entry.Selectable)
            return new(false, entry.Reason);
        native.Assignments.Enable(entry.Definition, enabled, Catalog);
        RefreshMovementEquipment();
        Ports.Record(new("gadget", catalogId, Enabled: enabled));
        return new(true, enabled ? entry.Reason : null);
    }

    internal EquipmentChangeResult ApplyCommand(PlaytestCommand command) => command.Kind switch
    {
        "equip" => Select(command.Value),
        "gadget" => SetGadgetEnabled(command.Value, command.Enabled),
        "quick-slot" => AssignQuickSelect(command.Slot, command.Value.Length == 0 ? null : command.Value),
        "unequip" => Unequip(),
        _ => new(false, "Unknown equipment command."),
    };

    internal string? ConfigurationIdentity => Ports.Native() is not { } native ? null :
        Catalog.ConfigurationIdentity(native.LoadoutPolicy, Sources, native.Assignments.Capture(), native.Weapons.Selected, Ports.SelectedAvatar());

    internal IReadOnlyList<SourceWeaponKey> WeaponOrder()
    {
        IEnumerable<SourceWeaponKey> order = Ports.Native() is null
            ? Ports.FusionTest() ? FusionWeaponSession.Weapons : FusionWeaponSession.DefaultQuickSelect(Ports.Inventory())
            : QuickSelectOrder().OfType<SourceWeaponKey>();
        return Array.AsReadOnly(order.ToArray());
    }

    /// <summary>
    /// Choose the next source-qualified wheel item without requiring any owned
    /// weapons. If a source/loadout change removed the current selection, start
    /// from the corresponding end of the remaining order.
    /// </summary>
    internal static SourceWeaponKey? CycleTarget(
        IReadOnlyList<SourceWeaponKey> order, SourceWeaponKey selected, int direction)
    {
        if (direction == 0 || order.Count == 0) return null;
        int current = -1;
        for (int i = 0; i < order.Count; i++)
            if (order[i] == selected) { current = i; break; }
        if (current < 0) return order[direction > 0 ? 0 : order.Count - 1];
        return order[(current + (direction > 0 ? 1 : order.Count - 1)) % order.Count];
    }

    internal IReadOnlyList<SourceWeaponKey?> QuickSelectOrder() => Ports.Native()?.Assignments.WithOverflow(Read()
        .Where(entry => entry.Definition.Category == EquipmentCategory.Weapon && entry.Definition.Implementation == EquipmentImplementation.Implemented && entry.State.Owned)
        .Select(entry => entry.Definition.Item!.Value).Where(key => key != PlayerEquipmentRegistrations.Wrench)) ?? ReadQuickSlots();

    internal void RefreshAvailability()
    {
        if (Ports.Native() is not { } native) return;
        var selected = Read().Single(entry => entry.Definition.Item == native.Weapons.Selected);
        if (!selected.Usable) native.Held.HoldWrench(); // Remember selection; draw only after explicit input/admission.
        RefreshMovementEquipment();
    }

    internal void RefreshMovementEquipment()
    {
        if (Ports.Native() is not { } native) return;
        var sources = Sources;
        bool Enabled(Rac1TraversalEquipmentId id) => native.GadgetEnabled(id) &&
            sources.GetValueOrDefault(ObpSourceGame.Rac1) == OBP.RAC1.Rac1Authority.Primary.BuildId;
        var movement = new Rac1MovementCapabilities(Enabled(Rac1TraversalEquipmentId.HeliPack), Enabled(Rac1TraversalEquipmentId.ThrusterPack),
            Enabled(Rac1TraversalEquipmentId.HydroPack), Enabled(Rac1TraversalEquipmentId.OxygenMask), Enabled(Rac1TraversalEquipmentId.Swingshot), false, false);
        var backpack = new[] { Rac1TraversalEquipmentId.HeliPack, Rac1TraversalEquipmentId.ThrusterPack, Rac1TraversalEquipmentId.HydroPack }
            .Cast<Rac1TraversalEquipmentId?>().FirstOrDefault(item => item.HasValue && Enabled(item.Value));
        Ports.ApplyMovementEquipment(movement, backpack);
    }

    // No subscriptions/resources or inventory lifetime to retire. Release scene callbacks only.
    public void Dispose() => _ports = null;
}
