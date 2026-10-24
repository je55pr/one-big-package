using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OneBigPackage;

/// <summary>
/// Source-controller composition adapter owned for the player's lifetime. No
/// Godot objects or world/campaign state. Native laws remain in the source modules.
/// </summary>
public sealed class PlayerSessionNativeState : IPlayerSessionStateAdapter
{
    private readonly int _seed;
    private PreviewInventory? _previewReturn;
    public Rac1WeaponInventory Inventory { get; private set; }
    public Rac1RatchetNanotechSession Health { get; } = new();
    public Rac1BoltWallet Wallet { get; private set; }
    public Rac1HeldItemSession Held { get; } = new();
    public FusionWeaponSession Weapons { get; }

    public PlayerSessionNativeState(Rac1WeaponInventory initial, int bolts, int seed)
    {
        _seed = seed;
        Inventory = new(initial.CapturePersistentState(), initial.CurrentItemId);
        Wallet = new(bolts);
        Weapons = new(seed, Inventory, Held.Hand);
    }

    public sealed record State(Rac1WeaponPersistentState Inventory, int CurrentItem,
        Rac1RatchetNanotechSession.State Health, int Bolts, SourceWeaponKey Selected, SourceWeaponKey? Hand,
        int LancerAmmo, int CannonAmmo, PreviewInventory? PreviewReturn = null) : IPlayerSessionComponentSnapshot
    {
        public string ComponentId => "obp/native-player/v1";
        public int Version => 1;
    }

    public sealed record PreviewInventory(Rac1WeaponPersistentState Inventory, int CurrentItem);

    /// <summary>The existing temporary arsenal remains a diagnostic owned by this player.</summary>
    public void SetEquipmentPreview(bool enabled)
    {
        if (enabled)
        {
            if (_previewReturn is not null) return;
            _previewReturn = new(Inventory.CapturePersistentState(), Inventory.CurrentItemId);
            Inventory = new(_previewReturn.Inventory, _previewReturn.CurrentItem);
        }
        else
        {
            if (_previewReturn is null) return;
            Inventory = new(_previewReturn.Inventory, _previewReturn.CurrentItem);
            _previewReturn = null;
        }
        Weapons.BindRac1Inventory(Inventory);
        if (Weapons.Selected.SourceGame == "rac1" && !Inventory.OwnsNativeItem(Weapons.Selected.NativeItemId))
            Weapons.Select(new("rac1", Inventory.CurrentItemId));
        if (Held.Hand.Equipment is { SourceGame: "rac1" } hand && !Inventory.OwnsNativeItem(hand.NativeItemId)) Held.HoldWrench();
        Weapons.ClearTransientState();
    }

    public PlayerGameplaySnapshot Capture()
    {
        var health = Health.Probe();
        var state = new State(Inventory.CapturePersistentState(), Inventory.CurrentItemId, Health.CaptureState(),
            Wallet.Balance, Weapons.Selected, Held.Hand.Equipment, Weapons.LancerSession.Ammo, Weapons.CannonSession.Ammo, _previewReturn);
        return new(new(Weapons.Selected, Held.Hand.Equipment, FusionWeaponSession.DefaultQuickSelect(Inventory).Cast<SourceWeaponKey?>().ToArray()),
            new(health.Nanotech, health.RespawnNanotech, !health.IsDead), new Dictionary<string, long> { ["obp/bolts"] = Wallet.Balance }, [state]);
    }

    public void ValidateRestore(PlayerGameplaySnapshot snapshot)
    {
        var state = RequiredState(snapshot);
        var inventory = new Rac1WeaponInventory(state.Inventory, state.CurrentItem);
        if (state.PreviewReturn is { } preview) _ = new Rac1WeaponInventory(preview.Inventory, preview.CurrentItem);
        var health = new Rac1RatchetNanotechSession(); health.RestoreState(state.Health);
        var wallet = new Rac1BoltWallet(state.Bolts);
        var weapons = new FusionWeaponSession(_seed, inventory);
        weapons.LancerSession.ResetWorld(state.LancerAmmo); weapons.CannonSession.Reset(state.CannonAmmo);
        if (!weapons.Select(state.Selected) || snapshot.Loadout.SelectedWeapon != state.Selected ||
            snapshot.Loadout.HandEquipment != state.Hand || snapshot.Health.Current != state.Health.Health.Nanotech ||
            snapshot.Health.Capacity != state.Health.Health.RespawnNanotech || snapshot.Health.Alive == state.Health.Health.IsDead ||
            snapshot.Currencies.Count != 1 || snapshot.Currencies.GetValueOrDefault("obp/bolts") != wallet.Balance ||
            !snapshot.Loadout.QuickSelect.SequenceEqual(FusionWeaponSession.DefaultQuickSelect(inventory).Cast<SourceWeaponKey?>()))
            throw new ArgumentException("Player projections do not match their source-owned state.");
        state.Hand?.Validate();
        if (state.Hand is { SourceGame: not "rac1" } foreign && foreign != FusionWeaponSession.Lancer && foreign != FusionWeaponSession.ShockCannon)
            throw new ArgumentException("The held equipment has no admitted native controller.");
        if (state.Hand is { SourceGame: "rac1" } held) new Rac1HeldItemSession().Restore(held.NativeItemId, inventory);
    }

    public void Restore(PlayerGameplaySnapshot snapshot)
    {
        ValidateRestore(snapshot);
        var state = RequiredState(snapshot);
        Inventory = new(state.Inventory, state.CurrentItem);
        _previewReturn = state.PreviewReturn;
        Wallet = new(state.Bolts); Health.RestoreState(state.Health);
        Weapons.BindRac1Inventory(Inventory);
        Weapons.LancerSession.ResetWorld(state.LancerAmmo); Weapons.CannonSession.Reset(state.CannonAmmo);
        Weapons.Select(state.Selected);
        RestoreHand(state.Hand);
        Weapons.ClearTransientState();
    }

    public void EndWorld()
    {
        var hand = Held.Hand.Equipment;
        Held.ResetForLevelEntry(); RestoreHand(hand);
        Weapons.ClearTransientState();
    }

    private void RestoreHand(SourceWeaponKey? hand)
    {
        if (hand is null) Held.HoldWrench();
        else if (hand.Value.SourceGame == "rac1") Held.Restore(hand.Value.NativeItemId, Inventory);
        else { Held.HoldWrench(); Held.Hand.HoldEquipment(hand.Value); }
    }

    private static State RequiredState(PlayerGameplaySnapshot snapshot) =>
        snapshot.Components.OfType<State>().SingleOrDefault() is { Version: 1 } state ? state :
        throw new ArgumentException("The native player state component is missing or unsupported.");
}
