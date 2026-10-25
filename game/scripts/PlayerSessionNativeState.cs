using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.Fusion;
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
    public FusionWeaponSession Weapons { get; private set; }
    public PlayerHealthSession Life { get; }
    public PlayerEquipmentAssignments Assignments { get; }
    public PlayerLoadoutPolicy LoadoutPolicy { get; }
    public const string Rac1DamageAdapter = "rac1/player-contact/v1", Rac1DamageDescriptor = "rac1/recovered-player-damage/v1",
        GcDamageAdapter = "rac2/msr1-player-contact/v1", GcDamageDescriptor = "rac2/admitted-integer-nanotech/v1",
        AridiaDamageAdapter = "obp/aridia-provisional-contact/v1", AridiaDamageDescriptor = "obp/provisional-damage-one/v1";

    public PlayerSessionNativeState(Rac1WeaponInventory initial, int bolts, int seed,
        PlayerLoadoutPolicy loadoutPolicy = PlayerLoadoutPolicy.DevelopmentAvailable)
    {
        _seed = seed;
        Inventory = new(initial.CapturePersistentState(), initial.CurrentItemId);
        if (!Enum.IsDefined((Rac1WeaponId)Inventory.CurrentItemId)) Inventory.TryEquip(Rac1WeaponId.Wrench);
        Wallet = new(bolts);
        Weapons = new(seed, Inventory, Held.Hand);
        LoadoutPolicy = Enum.IsDefined(loadoutPolicy) ? loadoutPolicy : throw new ArgumentOutOfRangeException(nameof(loadoutPolicy));
        var defaultSlots = FusionWeaponSession.DefaultQuickSelect(Inventory).Where(key => loadoutPolicy == PlayerLoadoutPolicy.DevelopmentAvailable || key.SourceGame == ObpSourceGame.Rac1);
        // Desired default slots describe implemented items, not ownership grants.
        // Availability still comes from the attached-source development policy.
        if (loadoutPolicy == PlayerLoadoutPolicy.DevelopmentAvailable)
            defaultSlots = defaultSlots.Concat(PlayerEquipmentRegistrations.Catalog.Definitions.Where(entry =>
                entry.Category == EquipmentCategory.Weapon && entry.Implementation == EquipmentImplementation.Implemented && entry.Item != PlayerEquipmentRegistrations.Wrench)
                .Select(entry => entry.Item!.Value));
        Assignments = new(defaultSlots);
        var imported = Rac1MovementCapabilities.FromInventory(Inventory);
        foreach (var gadget in new Rac1TraversalEquipmentId?[] { imported.RememberedBackpack(Inventory),
            imported.OxygenMask ? Rac1TraversalEquipmentId.OxygenMask : null, imported.Swingshot ? Rac1TraversalEquipmentId.Swingshot : null }.OfType<Rac1TraversalEquipmentId>())
            Assignments.Enable(PlayerEquipmentRegistrations.Catalog.Find(new SourceWeaponKey(ObpSourceGame.Rac1, (int)gadget))!, true, PlayerEquipmentRegistrations.Catalog);
        Life = new(new("obp/rac1-derived-health/v1", [new(ObpSourceGame.Rac1, OBP.RAC1.Rac1Authority.Primary.BuildId, Rac1DamageAdapter, Rac1DamageDescriptor),
            new(ObpSourceGame.Rac2, OBP.RAC2.Rac2Authority.Primary.BuildId, GcDamageAdapter, GcDamageDescriptor),
            new(ObpSourceGame.Rac1, OBP.RAC1.Rac1Authority.Primary.BuildId, AridiaDamageAdapter, AridiaDamageDescriptor)],
            [new(ObpSourceGame.Rac2, OBP.RAC2.Rac2Authority.Primary.BuildId, "rac2/collected-class500-bolt/v1", "obp/bolts")]), new VictimController(Health));
    }

    public sealed record State(Rac1WeaponPersistentState Inventory, int CurrentItem,
        Rac1RatchetNanotechSession.State Health, int Bolts, SourceWeaponKey Selected, SourceWeaponKey? Hand,
        int LancerAmmo, int CannonAmmo, PreviewInventory? PreviewReturn = null,
        PlayerEquipmentAssignmentsSnapshot? Assignments = null, PlayerLoadoutPolicy LoadoutPolicy = PlayerLoadoutPolicy.DevelopmentAvailable,
        long LifeGeneration = 0) : IPlayerSessionComponentSnapshot
    {
        public string ComponentId => "obp/native-player/v2";
        public int Version => 2;
    }

    public sealed record PreviewInventory(Rac1WeaponPersistentState Inventory, int CurrentItem);

    /// <summary>OBP development policy, applied only to this independent inventory, never source saves.</summary>
    public void RefreshDevelopmentSources(IReadOnlyDictionary<ObpSourceGame, string> sources)
    {
        if (LoadoutPolicy != PlayerLoadoutPolicy.DevelopmentAvailable ||
            sources.GetValueOrDefault(ObpSourceGame.Rac1) != OBP.RAC1.Rac1Authority.Primary.BuildId) return;
        bool firstPack = !Inventory.OwnsNativeItem((int)Rac1TraversalEquipmentId.HeliPack);
        foreach (var entry in PlayerEquipmentRegistrations.Catalog.Definitions.Where(entry =>
            entry.SourceGame == ObpSourceGame.Rac1 && entry.Item.HasValue && entry.Implementation == EquipmentImplementation.Implemented))
            if (!Inventory.OwnsNativeItem(entry.Item!.Value.NativeItemId)) Inventory.AcquireNativeItem(entry.Item.Value.NativeItemId);
        if (firstPack)
            foreach (int id in new[] { 2, 6, 12 }) Assignments.Enable(PlayerEquipmentRegistrations.Catalog.Find(new SourceWeaponKey(ObpSourceGame.Rac1, id))!, true, PlayerEquipmentRegistrations.Catalog);
    }

    public bool GadgetEnabled(Rac1TraversalEquipmentId item) => Inventory.OwnsNativeItem((int)item) &&
        Assignments.Capture().EnabledGadgets.Contains(new SourceWeaponKey(ObpSourceGame.Rac1, (int)item));

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
        if (Weapons.Selected.SourceGame == ObpSourceGame.Rac1 && !Inventory.OwnsNativeItem(Weapons.Selected.NativeItemId))
            Weapons.Select(new(ObpSourceGame.Rac1, Inventory.CurrentItemId));
        if (Held.Hand.Equipment is { SourceGame: ObpSourceGame.Rac1 } hand && !Inventory.OwnsNativeItem(hand.NativeItemId)) Held.HoldWrench();
        Weapons.ClearTransientState();
    }

    public PlayerGameplaySnapshot Capture()
    {
        var health = Health.Probe();
        var state = new State(Inventory.CapturePersistentState(), Inventory.CurrentItemId, Health.CaptureState(),
            Wallet.Balance, Weapons.Selected, Held.Hand.Equipment, Weapons.LancerSession.Ammo, Weapons.CannonSession.Ammo, _previewReturn,
            Assignments.Capture(), LoadoutPolicy, Life.LifeGeneration);
        return new(new(Weapons.Selected, Held.Hand.Equipment, state.Assignments!.QuickSelect),
            new(health.Nanotech, health.RespawnNanotech, !health.IsDead), new Dictionary<string, long> { ["obp/bolts"] = Wallet.Balance }, [state]);
    }

    public void ValidateRestore(PlayerGameplaySnapshot snapshot)
    {
        var state = RequiredState(snapshot);
        if (state.LoadoutPolicy != LoadoutPolicy || state.LifeGeneration < 0 || state.Assignments is null || state.Inventory is null || state.Health?.Health is null)
            throw new ArgumentException("Missing or incompatible loadout/life state.");
        PlayerEquipmentRegistrations.Catalog.ValidateAssignments(state.Assignments);
        var inventory = new Rac1WeaponInventory(state.Inventory, state.CurrentItem);
        if (state.Assignments.EnabledGadgets.Any(key => key.SourceGame == ObpSourceGame.Rac1 && !inventory.OwnsNativeItem(key.NativeItemId)))
            throw new ArgumentException("An enabled gadget must be owned by the persistent player.");
        if (state.PreviewReturn is { } preview) _ = new Rac1WeaponInventory(preview.Inventory, preview.CurrentItem);
        var health = new Rac1RatchetNanotechSession(); health.RestoreState(state.Health);
        var wallet = new Rac1BoltWallet(state.Bolts);
        var weapons = new FusionWeaponSession(_seed, inventory);
        weapons.LancerSession.ResetWorld(state.LancerAmmo); weapons.CannonSession.Reset(state.CannonAmmo);
        if (!weapons.Select(state.Selected) || snapshot.Loadout.SelectedWeapon != state.Selected ||
            snapshot.Loadout.HandEquipment != state.Hand || snapshot.Health.Current != state.Health.Health.Nanotech ||
            snapshot.Health.Capacity != state.Health.Health.RespawnNanotech || snapshot.Health.Alive == state.Health.Health.IsDead ||
            snapshot.Currencies.Count != 1 || snapshot.Currencies.GetValueOrDefault("obp/bolts") != wallet.Balance ||
            !snapshot.Loadout.QuickSelect.SequenceEqual(state.Assignments.QuickSelect))
            throw new ArgumentException("Player projections do not match their source-owned state.");
        state.Hand?.Validate();
        if (state.Hand is { SourceGame: not ObpSourceGame.Rac1 } foreign && foreign != FusionWeaponSession.Lancer && foreign != FusionWeaponSession.ShockCannon)
            throw new ArgumentException("The held equipment has no admitted native controller.");
        if (state.Hand is { SourceGame: ObpSourceGame.Rac1 } held) new Rac1HeldItemSession().Restore(held.NativeItemId, inventory);
    }

    public void Restore(PlayerGameplaySnapshot snapshot)
    {
        ValidateRestore(snapshot);
        var state = RequiredState(snapshot);
        Inventory = new(state.Inventory, state.CurrentItem);
        _previewReturn = state.PreviewReturn;
        Wallet = new(state.Bolts); Health.RestoreState(state.Health);
        Life.RestoreGeneration(state.LifeGeneration);
        Assignments.Restore(state.Assignments!, PlayerEquipmentRegistrations.Catalog);
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

    public void ResetReplayControllers()
    {
        var state = Capture();
        Weapons = new(_seed, Inventory, Held.Hand);
        Restore(state);
    }

    private void RestoreHand(SourceWeaponKey? hand)
    {
        if (hand is null) Held.HoldWrench();
        else if (hand.Value.SourceGame == ObpSourceGame.Rac1) Held.Restore(hand.Value.NativeItemId, Inventory);
        else { Held.HoldWrench(); Held.Hand.HoldEquipment(hand.Value); }
    }

    private static State RequiredState(PlayerGameplaySnapshot snapshot) =>
        snapshot.Components.Count == 1 && snapshot.Components[0] is State { Version: 2 } state ? state :
        throw new ArgumentException("The native player state component is missing or unsupported.");

    private sealed class VictimController(Rac1RatchetNanotechSession health) : IPlayerHealthController
    {
        public PlayerHealthSnapshot Capture() { var state = health.Probe(); return new(state.Nanotech, state.RespawnNanotech, !state.IsDead); }
        public void ApplyDamageUnits(int amount) => health.ApplyAdmittedDamageUnits(amount);
        public void HealUnits(int amount) { for (int i = 0; i < amount && health.CollectCrateNanotech(); i++) { } }
        public void TickNative() { health.UpdateNative(); health.Actions.Update(); }
        // Explicit OBP respawn policy, not a claim of a recovered combat checkpoint restart.
        public void ResetForObpRespawn() => health.RestoreState(new Rac1RatchetNanotechSession().CaptureState());
        public void DefeatFromWorldHazard() => health.ApplyEnvironmentalDeathReset();
    }
}
