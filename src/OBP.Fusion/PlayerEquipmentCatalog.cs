using System.Security.Cryptography;
using System.Text;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Fusion;

public enum PlayerLoadoutPolicy { RetailCampaign, DevelopmentAvailable }

/// <summary>World-independent catalog and admission read model. It never decodes native assets or mutates inventories.</summary>
public sealed class PlayerEquipmentCatalog
{
    private readonly EquipmentDefinition[] _definitions;
    private readonly Dictionary<string, EquipmentDefinition> _byId;
    public IReadOnlyList<EquipmentDefinition> Definitions => Array.AsReadOnly(_definitions);

    public PlayerEquipmentCatalog(IEnumerable<EquipmentDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions.ToArray();
        foreach (var item in _definitions)
        {
            item.Item?.Validate();
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.DisplayName) || string.IsNullOrWhiteSpace(item.BuildId) ||
                !Enum.IsDefined(item.Category) || !Enum.IsDefined(item.Implementation) || !Enum.IsDefined(item.Usage) ||
                item.SourceGame is not ("rac1" or "rac2" or "rac3") || item.Item is { } key && key.SourceGame != item.SourceGame ||
                item.Category == EquipmentCategory.Appearance && (item.Appearance is null || item.Item is not null) ||
                item.Category != EquipmentCategory.Appearance && (item.Item is null || item.Appearance is not null) ||
                item.Appearance is { } appearance && (appearance.SourceGame.ToString().ToLowerInvariant() != item.SourceGame ||
                    appearance.BuildId != item.BuildId || string.IsNullOrWhiteSpace(appearance.AvatarId) || string.IsNullOrWhiteSpace(appearance.ModelId)) ||
                item.Implementation == EquipmentImplementation.Unimplemented && string.IsNullOrWhiteSpace(item.UnsupportedReason))
                throw new ArgumentException("Invalid or misleading source-qualified equipment registration.");
        }
        if (_definitions.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != _definitions.Length ||
            _definitions.Where(item => item.Item.HasValue).Select(item => item.Item).Distinct().Count() != _definitions.Count(item => item.Item.HasValue))
            throw new ArgumentException("Duplicate catalog identity or source-qualified item.");
        _byId = _definitions.ToDictionary(item => item.Id, StringComparer.Ordinal);
    }

    public EquipmentDefinition? Find(string id) => _byId.GetValueOrDefault(id);
    public EquipmentDefinition? Find(SourceWeaponKey item) => _definitions.SingleOrDefault(entry => entry.Item == item);

    public IReadOnlyList<EquipmentCatalogEntry> Read(IReadOnlyDictionary<string, string> sources,
        Func<EquipmentDefinition, EquipmentLiveState> observe)
    {
        return Array.AsReadOnly(_definitions.Select(item =>
        {
            var state = observe(item);
            if (!Enum.IsDefined(state.Model) || !Enum.IsDefined(state.Icon) || state.Ammo < 0 || state.Capacity < 0 ||
                state.Ammo > state.Capacity || !state.ContextAllowed && string.IsNullOrWhiteSpace(state.ContextReason))
                throw new ArgumentException("Invalid equipment resource, ammo or context projection.");
            bool present = sources.TryGetValue(item.SourceGame, out var build);
            var status = item.Implementation != EquipmentImplementation.Implemented ? EquipmentUsability.Unimplemented :
                !present ? EquipmentUsability.MissingSource : build != item.BuildId ? EquipmentUsability.UnsupportedBuild :
                state.Model == EquipmentResourceStatus.Unavailable ? EquipmentUsability.MissingModel :
                !state.Owned ? EquipmentUsability.Unowned : !state.ContextAllowed ? EquipmentUsability.ContextBlocked : EquipmentUsability.Ready;
            string? reason = status switch
            {
                EquipmentUsability.Unimplemented => item.UnsupportedReason,
                EquipmentUsability.MissingSource => $"Attach the {item.SourceGame} source for {item.DisplayName}.",
                EquipmentUsability.UnsupportedBuild => $"{item.DisplayName} requires {item.BuildId}.",
                EquipmentUsability.MissingModel => state.ContextReason ?? $"The genuine {item.DisplayName} model is unavailable.",
                EquipmentUsability.Unowned => "This loadout does not own the item.",
                EquipmentUsability.ContextBlocked => state.ContextReason ?? "The required runtime context is unavailable.",
                _ => null,
            };
            return new EquipmentCatalogEntry(item, state, present, status, reason);
        }).ToArray());
    }

    /// <summary>Payload/path-free deterministic identity for configuration/replay admission.</summary>
    public string ConfigurationIdentity(PlayerLoadoutPolicy policy, IReadOnlyDictionary<string, string> sources,
        PlayerEquipmentAssignmentsSnapshot assignments, SourceWeaponKey? selectedWeapon = null, PlayerAvatarIdentity? appearance = null)
    {
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        ValidateAssignments(assignments);
        if (selectedWeapon is { } selected && Find(selected) is not { Category: EquipmentCategory.Weapon, Implementation: EquipmentImplementation.Implemented } ||
            appearance is not null && !_definitions.Any(item => item.Appearance == appearance && item.Implementation == EquipmentImplementation.Implemented))
            throw new ArgumentException("The selected equipment/appearance has no implemented catalog registration.");
        // JSON length/escaping prevents ambiguous delimiter collisions in user-visible IDs.
        string text = System.Text.Json.JsonSerializer.Serialize(new
        {
            Version = 1,
            Policy = policy,
            Definitions = _definitions.OrderBy(item => item.Id, StringComparer.Ordinal),
            Sources = sources.OrderBy(item => item.Key, StringComparer.Ordinal),
            assignments.QuickSelect,
            SelectedWeapon = selectedWeapon,
            Appearance = appearance,
            EnabledGadgets = assignments.EnabledGadgets.OrderBy(item => item.ToString(), StringComparer.Ordinal),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public void ValidateAssignments(PlayerEquipmentAssignmentsSnapshot snapshot)
    {
        if (snapshot.QuickSelect.Count != PlayerEquipmentAssignments.SlotCount ||
            snapshot.QuickSelect.OfType<SourceWeaponKey>().Distinct().Count() != snapshot.QuickSelect.Count(item => item.HasValue) ||
            snapshot.EnabledGadgets.Distinct().Count() != snapshot.EnabledGadgets.Count)
            throw new ArgumentException("Invalid or duplicate equipment assignment.");
        foreach (var key in snapshot.QuickSelect.OfType<SourceWeaponKey>())
            if (Find(key) is not { Category: EquipmentCategory.Weapon, Implementation: EquipmentImplementation.Implemented })
                throw new ArgumentException("Quick Select requires registered implemented weapons.");
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in snapshot.EnabledGadgets)
        {
            var definition = Find(key);
            if (definition is not { Category: EquipmentCategory.Gadget, Implementation: EquipmentImplementation.Implemented } ||
                definition.ConflictGroup is { } group && !conflicts.Add(group))
                throw new ArgumentException("Unknown, unimplemented or conflicting enabled gadget.");
        }
    }
}

/// <summary>Slot/gadget assignments only; ammo, selection and hand ownership stay with their existing controller owner.</summary>
public sealed class PlayerEquipmentAssignments
{
    public const int SlotCount = 8;
    private readonly SourceWeaponKey?[] _slots = new SourceWeaponKey?[SlotCount];
    private readonly List<SourceWeaponKey> _gadgets = [];
    public PlayerEquipmentAssignments(IEnumerable<SourceWeaponKey> initial)
    {
        int index = 0;
        foreach (var item in initial.Distinct().Take(SlotCount)) _slots[index++] = item.Validate();
    }
    public PlayerEquipmentAssignmentsSnapshot Capture() => new(Array.AsReadOnly((SourceWeaponKey?[])_slots.Clone()), Array.AsReadOnly(_gadgets.ToArray()));
    public IReadOnlyList<SourceWeaponKey?> WithOverflow(IEnumerable<SourceWeaponKey> available) =>
        Array.AsReadOnly(_slots.Concat(available.Distinct().Where(item => !_slots.Contains(item)).Cast<SourceWeaponKey?>()).ToArray());
    public void Assign(int slot, SourceWeaponKey? item)
    {
        if (slot < 0 || slot >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
        item?.Validate();
        if (item.HasValue)
            for (int i = 0; i < SlotCount; i++) if (_slots[i] == item) _slots[i] = null;
        _slots[slot] = item;
    }
    public void Enable(EquipmentDefinition definition, bool enabled, PlayerEquipmentCatalog catalog)
    {
        if (catalog.Find(definition.Id) != definition || definition.Category != EquipmentCategory.Gadget ||
            definition.Implementation != EquipmentImplementation.Implemented || definition.Item is not { } item)
            throw new ArgumentException("Only implemented gadgets can be enabled.");
        if (!enabled) { _gadgets.Remove(item); return; }
        _gadgets.RemoveAll(existing => catalog.Find(existing)?.ConflictGroup is { } group && group == definition.ConflictGroup);
        if (!_gadgets.Contains(item)) _gadgets.Add(item);
    }
    public void Restore(PlayerEquipmentAssignmentsSnapshot snapshot, PlayerEquipmentCatalog catalog)
    {
        catalog.ValidateAssignments(snapshot);
        for (int i = 0; i < SlotCount; i++) _slots[i] = snapshot.QuickSelect[i];
        _gadgets.Clear(); _gadgets.AddRange(snapshot.EnabledGadgets);
    }
}
