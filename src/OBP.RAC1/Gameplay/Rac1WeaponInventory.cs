namespace OBP.RAC1.Gameplay;

/// <summary>
/// Retail item ids already named by retained R&C1 evidence.
/// Other native ids remain numeric until their gameplay contracts are recovered.
/// </summary>
public enum Rac1WeaponId
{
    Wrench = 8,
    FirstRanged = 10,
}

/// <summary>
/// Save-backed R&C1 inventory layout recovered from SCUS-97199.
/// These are source-game fields, not a trilogy-wide inventory abstraction.
/// </summary>
public static class Rac1NativeInventoryLayout
{
    public const int ItemCount = 37;
    public const int QuickSelectCapacity = 8;
    public const int EquippedGadgetCapacity = 7;

    public const int AmmoSaveBlockId = 9;
    public const int ItemsSaveBlockId = 10;
    public const int UnlockFlagsSaveBlockId = 11;
    public const int QuickSelectSaveBlockId = 13;
    public const int LastEquippedGadgetSaveBlockId = 21;
    public const int EquippedGadgetsSaveBlockId = 32;
}

/// <summary>
/// The generic ammo fields whose descriptor semantics are proven for every
/// R&C1 item id: +0x12 provides the first-acquisition ammo floor when the descriptor's
/// +0x08 gate is nonzero, and +0x0e is the clamp/query capacity.
/// </summary>
public readonly record struct Rac1ItemAmmoDescriptor(
    int NativeItemId,
    int FirstAcquisitionAmmoGate,
    int FirstAcquisitionAmmoFloor,
    int MaxAmmo)
{
    public bool AppliesFirstAcquisitionAmmoFloor => FirstAcquisitionAmmoGate != 0;
    public bool UsesAmmo => MaxAmmo > 0;
}

/// <summary>
/// Observable consequences of the generic native acquisition path before its
/// separately-conditional quick-select insertion branch.
/// </summary>
public readonly record struct Rac1ItemAcquisitionResult(
    int NativeItemId,
    bool WasPersistentlyOwned,
    int AmmoBefore,
    int AmmoAfter)
{
    public bool IsFirstAcquisition => !WasPersistentlyOwned;
    public int AmmoGranted => AmmoAfter - AmmoBefore;
}

public static class Rac1ItemAmmoDescriptors
{
    private static readonly int[] FirstAcquisitionAmmoGate =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        5, 50, 0, 100, 0, 1, 1, 5, 0, 1,
        40, 0, 0, 20, 40, 10, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0,
    ];

    private static readonly int[] FirstAcquisitionAmmoFloor =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        10, 10, 0, 10, 0, 100, 120, 25, 0, 120,
        3, 0, 0, 25, 3, 10, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0,
    ];

    private static readonly int[] MaxAmmo =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        40, 20, 0, 20, 0, 200, 240, 50, 0, 240,
        10, 0, 0, 50, 10, 20, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0,
    ];

    public static Rac1ItemAmmoDescriptor Get(int nativeItemId)
    {
        ValidateItemId(nativeItemId);
        return new Rac1ItemAmmoDescriptor(
            nativeItemId,
            FirstAcquisitionAmmoGate[nativeItemId],
            FirstAcquisitionAmmoFloor[nativeItemId],
            MaxAmmo[nativeItemId]);
    }

    private static void ValidateItemId(int nativeItemId)
    {
        if ((uint)nativeItemId >= Rac1NativeInventoryLayout.ItemCount)
            throw new ArgumentOutOfRangeException(nameof(nativeItemId));
    }
}

/// <summary>
/// Exact save-backed inventory blocks recovered from retail. The currently-held
/// item is deliberately absent: live equip mirrors are session state and can
/// differ from the persisted equipped-gadget slots (notably while holding the wrench).
/// </summary>
public sealed record Rac1WeaponPersistentState(
    IReadOnlyList<int> Ammo,
    IReadOnlyList<byte> Items,
    IReadOnlyList<byte> UnlockFlags,
    IReadOnlyList<int> QuickSelect,
    int LastEquippedGadget,
    IReadOnlyList<int> EquippedGadgets);

/// <summary>
/// R&C1-owned item/inventory state. All 37 native item slots are retained, while
/// weapon-use behaviour remains admitted only for the separately recovered wrench
/// and item-10 Bomb Glove slice.
/// </summary>
public sealed class Rac1WeaponInventory
{
    private readonly int[] _ammo;
    private readonly byte[] _items;
    private readonly byte[] _unlockFlags;
    private readonly int[] _quickSelect;
    private readonly int[] _equippedGadgets;

    public Rac1WeaponInventory(
        bool ownsFirstRanged,
        Rac1WeaponId equipped = Rac1WeaponId.Wrench,
        int firstRangedAmmo = 0)
        : this(CreateEarlySliceState(ownsFirstRanged, firstRangedAmmo), (int)equipped)
    {
    }

    public Rac1WeaponInventory(
        Rac1WeaponPersistentState persistentState,
        int currentItemId = (int)Rac1WeaponId.Wrench)
    {
        ArgumentNullException.ThrowIfNull(persistentState);
        ValidateItemId(currentItemId);
        ValidateCount(persistentState.Ammo, Rac1NativeInventoryLayout.ItemCount, nameof(persistentState.Ammo));
        ValidateCount(persistentState.Items, Rac1NativeInventoryLayout.ItemCount, nameof(persistentState.Items));
        ValidateCount(persistentState.UnlockFlags, Rac1NativeInventoryLayout.ItemCount, nameof(persistentState.UnlockFlags));
        ValidateCount(persistentState.QuickSelect, Rac1NativeInventoryLayout.QuickSelectCapacity, nameof(persistentState.QuickSelect));
        ValidateCount(persistentState.EquippedGadgets, Rac1NativeInventoryLayout.EquippedGadgetCapacity, nameof(persistentState.EquippedGadgets));

        _ammo = persistentState.Ammo.ToArray();
        _items = persistentState.Items.ToArray();
        _unlockFlags = persistentState.UnlockFlags.ToArray();
        _quickSelect = persistentState.QuickSelect.ToArray();
        _equippedGadgets = persistentState.EquippedGadgets.ToArray();
        LastEquippedGadget = persistentState.LastEquippedGadget;
        CurrentItemId = currentItemId;

        for (int itemId = 0; itemId < _ammo.Length; itemId++)
        {
            if (_ammo[itemId] < 0)
                throw new ArgumentOutOfRangeException(nameof(persistentState), $"Ammo slot {itemId} is negative.");

            int maxAmmo = Rac1ItemAmmoDescriptors.Get(itemId).MaxAmmo;
            if (maxAmmo > 0 && _ammo[itemId] > maxAmmo)
                throw new ArgumentOutOfRangeException(nameof(persistentState), $"Ammo slot {itemId} exceeds retail capacity {maxAmmo}.");
        }

        if (CurrentItemId != (int)Rac1WeaponId.Wrench && !OwnsNativeItem(CurrentItemId))
            throw new ArgumentException("The currently-held R&C1 item must be owned.", nameof(currentItemId));
    }

    public int CurrentItemId { get; private set; }
    public int LastEquippedGadget { get; }
    public IReadOnlyList<int> Ammo => Array.AsReadOnly(_ammo);
    public IReadOnlyList<byte> Items => Array.AsReadOnly(_items);
    public IReadOnlyList<byte> UnlockFlags => Array.AsReadOnly(_unlockFlags);
    public IReadOnlyList<int> QuickSelect => Array.AsReadOnly(_quickSelect);
    public IReadOnlyList<int> EquippedGadgets => Array.AsReadOnly(_equippedGadgets);

    public bool OwnsFirstRanged => OwnsNativeItem((int)Rac1WeaponId.FirstRanged);
    public int FirstRangedAmmo => GetAmmo((int)Rac1WeaponId.FirstRanged);

    /// <summary>
    /// Compatibility view for the currently implemented weapon-use slice.
    /// Callers that need arbitrary retail ids should use CurrentItemId.
    /// </summary>
    public Rac1WeaponId Equipped =>
        CurrentItemId is (int)Rac1WeaponId.Wrench or (int)Rac1WeaponId.FirstRanged
            ? (Rac1WeaponId)CurrentItemId
            : throw new InvalidOperationException($"R&C1 item {CurrentItemId} has no implemented weapon-use controller.");

    public bool Owns(Rac1WeaponId weapon) => OwnsNativeItem((int)weapon);

    public bool OwnsNativeItem(int nativeItemId)
    {
        ValidateItemId(nativeItemId);
        return nativeItemId == (int)Rac1WeaponId.Wrench || _items[nativeItemId] != 0;
    }

    public bool HasUnlockFlag(int nativeItemId)
    {
        ValidateItemId(nativeItemId);
        return nativeItemId == (int)Rac1WeaponId.Wrench || _unlockFlags[nativeItemId] != 0;
    }

    public int GetAmmo(int nativeItemId)
    {
        ValidateItemId(nativeItemId);
        return _ammo[nativeItemId];
    }

    /// <summary>
    /// Apply the recovered common item-acquisition prefix: set the secondary
    /// acquisition flag, set the persistent ownership byte on first acquisition,
    /// and raise ammo to the descriptor floor when its +0x08 gate is nonzero.
    /// The later quick-select insertion is intentionally not modeled here because
    /// its complete admission predicate has not been recovered.
    /// </summary>
    public Rac1ItemAcquisitionResult AcquireNativeItem(int nativeItemId)
    {
        ValidateItemId(nativeItemId);

        bool wasPersistentlyOwned = _items[nativeItemId] != 0;
        int ammoBefore = _ammo[nativeItemId];
        _unlockFlags[nativeItemId] = 1;

        if (!wasPersistentlyOwned)
        {
            _items[nativeItemId] = 1;
            var descriptor = Rac1ItemAmmoDescriptors.Get(nativeItemId);
            if (descriptor.AppliesFirstAcquisitionAmmoFloor &&
                _ammo[nativeItemId] < descriptor.FirstAcquisitionAmmoFloor)
            {
                _ammo[nativeItemId] = descriptor.FirstAcquisitionAmmoFloor;
            }
        }

        return new Rac1ItemAcquisitionResult(
            nativeItemId,
            wasPersistentlyOwned,
            ammoBefore,
            _ammo[nativeItemId]);
    }

    public bool TryEquip(Rac1WeaponId weapon)
    {
        int itemId = (int)weapon;
        if (!OwnsNativeItem(itemId))
            return false;

        CurrentItemId = itemId;
        return true;
    }

    public bool TryUseEquipped()
    {
        if (CurrentItemId == (int)Rac1WeaponId.Wrench)
            return true;
        if (CurrentItemId != (int)Rac1WeaponId.FirstRanged)
            return false;

        return TryConsumeAmmo(CurrentItemId, 1);
    }

    public bool TryConsumeAmmo(int nativeItemId, int amount)
    {
        ValidateItemId(nativeItemId);
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (!Rac1ItemAmmoDescriptors.Get(nativeItemId).UsesAmmo)
            return true;
        if (_ammo[nativeItemId] < amount)
            return false;

        _ammo[nativeItemId] -= amount;
        return true;
    }

    public int AddAmmoClamped(int nativeItemId, int amount)
    {
        ValidateItemId(nativeItemId);
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        int maxAmmo = Rac1ItemAmmoDescriptors.Get(nativeItemId).MaxAmmo;
        long increased = (long)_ammo[nativeItemId] + amount;
        _ammo[nativeItemId] = maxAmmo == 0
            ? checked((int)increased)
            : (int)Math.Min(increased, maxAmmo);
        return _ammo[nativeItemId];
    }

    public Rac1WeaponPersistentState CapturePersistentState() =>
        new(
            Array.AsReadOnly((int[])_ammo.Clone()),
            Array.AsReadOnly((byte[])_items.Clone()),
            Array.AsReadOnly((byte[])_unlockFlags.Clone()),
            Array.AsReadOnly((int[])_quickSelect.Clone()),
            LastEquippedGadget,
            Array.AsReadOnly((int[])_equippedGadgets.Clone()));

    private static Rac1WeaponPersistentState CreateEarlySliceState(bool ownsFirstRanged, int firstRangedAmmo)
    {
        if (firstRangedAmmo < 0 || firstRangedAmmo > Rac1ItemAmmoDescriptors.Get((int)Rac1WeaponId.FirstRanged).MaxAmmo)
            throw new ArgumentOutOfRangeException(nameof(firstRangedAmmo));

        var ammo = new int[Rac1NativeInventoryLayout.ItemCount];
        var items = new byte[Rac1NativeInventoryLayout.ItemCount];
        var unlocks = new byte[Rac1NativeInventoryLayout.ItemCount];
        var quickSelect = new int[Rac1NativeInventoryLayout.QuickSelectCapacity];
        var equippedGadgets = new int[Rac1NativeInventoryLayout.EquippedGadgetCapacity];

        int itemId = (int)Rac1WeaponId.FirstRanged;
        ammo[itemId] = firstRangedAmmo;
        if (ownsFirstRanged)
        {
            items[itemId] = 1;
            unlocks[itemId] = 1;
            quickSelect[0] = itemId;
            equippedGadgets[0] = itemId;
        }

        return new Rac1WeaponPersistentState(ammo, items, unlocks, quickSelect, 0, equippedGadgets);
    }

    private static void ValidateItemId(int nativeItemId)
    {
        if ((uint)nativeItemId >= Rac1NativeInventoryLayout.ItemCount)
            throw new ArgumentOutOfRangeException(nameof(nativeItemId));
    }

    private static void ValidateCount<T>(IReadOnlyList<T>? values, int expected, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count != expected)
            throw new ArgumentException($"R&C1 native table must contain exactly {expected} entries.", parameterName);
    }
}
