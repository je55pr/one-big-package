using System.Text.Json;
using OBP.Core;
using OBP.Fusion;
using OBP.Game;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC2;
using OBP.RAC3;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class PlayerEquipmentFacadeTests
{
    [Theory]
    [InlineData(EquipmentUsability.MissingSource)]
    [InlineData(EquipmentUsability.UnsupportedBuild)]
    [InlineData(EquipmentUsability.MissingModel)]
    [InlineData(EquipmentUsability.Unowned)]
    [InlineData(EquipmentUsability.ContextBlocked)]
    [InlineData(EquipmentUsability.PresentationBlocked)]
    public void AvailabilitySeparatesSourcePreparationOwnershipAndContext(EquipmentUsability expected)
    {
        using var fixture = new Fixture();
        if (expected == EquipmentUsability.MissingSource) fixture.Sources.Remove(ObpSourceGame.Rac2);
        if (expected == EquipmentUsability.UnsupportedBuild) fixture.Sources[ObpSourceGame.Rac2] = "unsupported";
        if (expected == EquipmentUsability.MissingModel) fixture.Presentation = new(EquipmentResourceStatus.Unavailable, EquipmentResourceStatus.Unavailable);
        if (expected == EquipmentUsability.Unowned) fixture.Native = new(Rac1WeaponInventory.CreateOpeningLoadout(), 42, 1440, PlayerLoadoutPolicy.RetailCampaign);
        if (expected == EquipmentUsability.ContextBlocked) fixture.Presentation = fixture.Presentation with { ContextReason = "No measured hand socket." };
        if (expected == EquipmentUsability.PresentationBlocked) fixture.Presentation = fixture.Presentation with { BindingReady = false, BindingReason = "No measured hand socket." };
        var entry = fixture.Entry("rac2/item/30");
        Assert.Equal(expected, entry.Usability);
        Assert.False(entry.Usable);
        Assert.False(string.IsNullOrWhiteSpace(entry.Reason));
        Assert.Equal(expected == EquipmentUsability.ContextBlocked, entry.Selectable);
        if (expected == EquipmentUsability.PresentationBlocked) Assert.Equal(EquipmentResourceStatus.Available, entry.State.Model);
        if (expected != EquipmentUsability.MissingSource)
            Assert.DoesNotContain("Attach", entry.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisabledItemsStayHonestAfterTrilogySetupAndReadModelsAreFrozenCopies()
    {
        using var fixture = new Fixture();
        var entries = fixture.Equipment.Read();
        var slots = fixture.Equipment.ReadQuickSlots();
        var sources = fixture.Equipment.Sources;
        foreach (string id in new[] { "rac1/item/27", "rac1/item/28", "rac1/item/29", "rac2/appearance/ratchet", "rac3/appearance/ratchet" })
        {
            var entry = entries.Single(entry => entry.Definition.Id == id);
            Assert.Equal(EquipmentUsability.Unimplemented, entry.Usability);
            Assert.True(entry.SourceAvailable);
            Assert.False(entry.Selectable);
            Assert.NotEmpty(entry.Reason!);
            Assert.DoesNotContain("Attach", entry.Reason!, StringComparison.OrdinalIgnoreCase);
            Assert.False(fixture.Equipment.Select(id).Accepted);
        }
        Assert.True(fixture.Equipment.AssignQuickSelect(7, "rac2/item/30").Accepted);
        Assert.NotEqual(FusionWeaponSession.Lancer, slots[7]);
        Assert.False(entries.Single(entry => entry.Definition.Id == "rac2/item/30").State.Selected);
        fixture.Sources.Clear();
        Assert.Equal(3, sources.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<SourceWeaponKey?>)slots)[0] = null);
        Assert.Throws<NotSupportedException>(() => ((IList<EquipmentCatalogEntry>)entries).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<ObpSourceGame, string>)sources).Clear());
    }

    [Theory]
    [InlineData(-1, "rac2/item/30")]
    [InlineData(8, null)]
    [InlineData(0, "unknown")]
    [InlineData(0, "rac1/item/2")]
    [InlineData(0, "rac1/appearance/ratchet")]
    public void RejectedAssignmentsHaveReasonsAndDoNotChangeOrRecordState(int slot, string? id)
    {
        using var fixture = new Fixture();
        string before = fixture.Equipment.ConfigurationIdentity!;
        var result = fixture.Equipment.AssignQuickSelect(slot, id);
        Assert.False(result.Accepted);
        Assert.NotEmpty(result.Reason!);
        Assert.Equal(before, fixture.Equipment.ConfigurationIdentity);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public void WheelRetainsEmptySlotsAndUnavailableAssignmentsWithoutAliasingNativeIds()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Equipment.AssignQuickSelect(0, null).Accepted);
        Assert.True(fixture.Equipment.AssignQuickSelect(7, "rac2/item/30").Accepted);
        fixture.Sources.Remove(ObpSourceGame.Rac2);
        var wheel = fixture.Equipment.QuickSelectOrder();
        Assert.Equal(fixture.Equipment.ReadQuickSlots(), wheel.Take(PlayerEquipmentAssignments.SlotCount));
        Assert.Null(wheel[0]);
        Assert.Equal(FusionWeaponSession.Lancer, wheel[7]);
        Assert.Equal(1, wheel.Count(key => key == FusionWeaponSession.Lancer));
        Assert.DoesNotContain(PlayerEquipmentRegistrations.Wrench, wheel);
        Assert.False(fixture.Entry("rac2/item/30").Selectable);
        string before = fixture.Equipment.ConfigurationIdentity!;
        Assert.False(fixture.Equipment.Select("rac2/item/10").Accepted);
        Assert.Equal(before, fixture.Equipment.ConfigurationIdentity);
    }

    [Fact]
    public void ContextBlockedGadgetsMayBeEnabledAndDisabledWithoutInventingWorldCapabilities()
    {
        using var fixture = new Fixture();
        fixture.Context = new(false, false);
        foreach (string id in new[] { "rac1/item/4", "rac1/item/6", "rac1/item/12" })
        {
            Assert.Equal(EquipmentUsability.ContextBlocked, fixture.Entry(id).Usability);
            Assert.True(fixture.Equipment.SetGadgetEnabled(id, true).Accepted);
            var enabled = fixture.Entry(id);
            Assert.True(enabled.State.Selected);
            Assert.False(enabled.State.Equipped);
            Assert.NotEmpty(enabled.Reason!);
        }
        fixture.Context = new(true, true);
        Assert.True(fixture.Entry("rac1/item/12").State.Equipped);
        Assert.True(fixture.Entry("rac1/item/6").State.Equipped);
        fixture.Sources.Remove(ObpSourceGame.Rac1);
        Assert.True(fixture.Equipment.SetGadgetEnabled("rac1/item/12", false).Accepted);
        Assert.False(fixture.Native!.GadgetEnabled(Rac1TraversalEquipmentId.Swingshot));
        Assert.False(fixture.Movement.Swingshot);
        Assert.False(fixture.Movement.HydroPack);
    }

    [Fact]
    public void BackpackConflictsUseTheSessionAssignmentsAndUnequipRetainsSelectionAndAmmo()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Equipment.SetGadgetEnabled("rac1/item/3", true).Accepted);
        Assert.True(fixture.Native!.GadgetEnabled(Rac1TraversalEquipmentId.ThrusterPack));
        Assert.False(fixture.Native.GadgetEnabled(Rac1TraversalEquipmentId.HeliPack));
        Assert.Equal(Rac1TraversalEquipmentId.ThrusterPack, fixture.Backpack);
        Assert.True(fixture.Equipment.Select("rac2/item/30").Accepted);
        fixture.Native.Held.Hand.HoldEquipment(FusionWeaponSession.Lancer);
        var before = fixture.Native.Weapons.Status(FusionWeaponSession.Lancer);
        Assert.True(fixture.Equipment.Unequip().Accepted);
        Assert.Equal(FusionWeaponSession.Lancer, fixture.Native.Weapons.Selected);
        Assert.Equal(before.Ammo, fixture.Native.Weapons.Status(FusionWeaponSession.Lancer).Ammo);
        Assert.True(fixture.Native.Held.Hand.WrenchHeld);
        Assert.Equal(1, fixture.PresentationResets);
        Assert.Equal(new PlaytestCommand("unequip", ""), fixture.Commands.Last());
    }

    [Fact]
    public void SourceQualifiedCommandsReplayThroughTheSameFacadeAndNativeState()
    {
        using var original = new Fixture();
        using var replay = new Fixture();
        Assert.True(original.Equipment.AssignQuickSelect(7, "rac2/item/30").Accepted);
        Assert.True(original.Equipment.AssignQuickSelect(6, "rac3/item/43").Accepted);
        Assert.True(original.Equipment.AssignQuickSelect(0, null).Accepted);
        Assert.True(original.Equipment.Select("rac1/item/15").Accepted);
        Assert.True(original.Equipment.Select("rac3/item/43").Accepted);
        Assert.True(original.Equipment.SetGadgetEnabled("rac1/item/3", true).Accepted);
        Assert.True(original.Equipment.SetGadgetEnabled("rac1/item/12", false).Accepted);
        Assert.True(original.Equipment.Unequip().Accepted);
        var commands = JsonSerializer.Deserialize<PlaytestCommand[]>(JsonSerializer.Serialize(original.Commands))!;
        Assert.Contains(new PlaytestCommand("quick-slot", "", 0), commands);
        Assert.Contains(new PlaytestCommand("equip", "rac3/item/43"), commands);
        foreach (var command in commands) Assert.True(replay.Equipment.ApplyCommand(command).Accepted, command.ToString());
        Assert.Equal(original.Equipment.ConfigurationIdentity, replay.Equipment.ConfigurationIdentity);
        Assert.Equal(State(original.Native!), State(replay.Native!));
        Assert.Equal(commands, replay.Commands);
        Assert.Null(replay.Equipment.ReadQuickSlots()[0]);
        Assert.Equal(FusionWeaponSession.Lancer, replay.Equipment.ReadQuickSlots()[7]);
        Assert.Equal(FusionWeaponSession.ShockCannon, replay.Equipment.ReadQuickSlots()[6]);
    }

    [Fact]
    public void SourceLossAndWorldRetirementKeepDesiredSelectionSlotsAndNativeAmmo()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Equipment.Select("rac2/item/30").Accepted);
        fixture.Native!.Held.Hand.HoldEquipment(FusionWeaponSession.Lancer);
        var slots = fixture.Equipment.ReadQuickSlots();
        var inventory = fixture.Native.Inventory;
        var weapons = fixture.Native.Weapons;
        int ammo = weapons.Status(FusionWeaponSession.Lancer).Ammo;
        fixture.Sources.Remove(ObpSourceGame.Rac2);
        fixture.Equipment.RefreshAvailability();
        Assert.True(fixture.Native.Held.Hand.WrenchHeld);
        Assert.Equal(FusionWeaponSession.Lancer, weapons.Selected);
        fixture.Native.EndWorld();
        fixture.Sources[ObpSourceGame.Rac2] = Rac2Authority.Primary.BuildId;
        fixture.Equipment.RefreshAvailability();
        Assert.True(fixture.Native.Held.Hand.WrenchHeld);
        Assert.Equal(slots, fixture.Equipment.ReadQuickSlots());
        Assert.Same(inventory, fixture.Native.Inventory);
        Assert.Same(weapons, fixture.Native.Weapons);
        Assert.Equal(ammo, weapons.Status(FusionWeaponSession.Lancer).Ammo);
        Assert.True(fixture.Entry("rac2/item/30").Usable);
    }

    [Fact]
    public void ParityUsesItsExistingInventoryAndWeaponOrderWithoutCreatingAnObpLoadout()
    {
        using var fixture = new Fixture(ordinary: false);
        Assert.Empty(fixture.Equipment.ReadQuickSlots());
        Assert.Null(fixture.Equipment.ConfigurationIdentity);
        Assert.Equal(EquipmentUsability.Unowned, fixture.Entry("rac1/item/15").Usability);
        Assert.Equal(FusionWeaponSession.DefaultQuickSelect(fixture.Inventory), fixture.Equipment.WeaponOrder());
        Assert.True(fixture.Equipment.Select("rac1/item/8").Accepted);
        Assert.Equal((int)Rac1WeaponId.Wrench, fixture.Inventory.CurrentItemId);
        foreach (var result in new[] { fixture.Equipment.AssignQuickSelect(0, "rac1/item/10"), fixture.Equipment.Unequip(),
                     fixture.Equipment.SetGadgetEnabled("rac1/item/2", true) })
        {
            Assert.False(result.Accepted);
            Assert.NotEmpty(result.Reason!);
        }
        fixture.ParityWeapons = new(1440, fixture.Inventory, fixture.Held.Hand);
        fixture.FusionTest = true;
        Assert.Equal(FusionWeaponSession.Weapons, fixture.Equipment.WeaponOrder());
        Assert.True(fixture.Equipment.Select("rac2/item/30").Accepted);
    }

    [Fact]
    public void RejectedNativeSelectionIsExplainedAndAppearanceRequiresTheSelectedProfile()
    {
        using var fixture = new Fixture();
        fixture.AcceptSelection = false;
        var rejected = fixture.Equipment.Select("rac2/item/30");
        Assert.False(rejected.Accepted);
        Assert.NotEmpty(rejected.Reason!);
        Assert.Empty(fixture.Commands);
        var avatar = fixture.SelectedAvatar;
        fixture.SelectedAvatar = null;
        rejected = fixture.Equipment.Select("rac1/appearance/ratchet");
        Assert.False(rejected.Accepted);
        Assert.Contains("profile", rejected.Reason!);
        fixture.SelectedAvatar = avatar;
        Assert.True(fixture.Equipment.Select("rac1/appearance/ratchet").Accepted);
    }

    [Fact]
    public void PlayerOwnerRetainsOneFacadeAndDisposalOnlyReleasesItsPorts()
    {
        using var fixture = new Fixture();
        var owner = new PlayerSessionHost();
        owner.BindEquipment(fixture.Ports);
        var facade = owner.Equipment;
        Assert.Throws<InvalidOperationException>(() => owner.BindEquipment(fixture.Ports));
        string before = State(fixture.Native!);
        owner.RetireEquipment();
        owner.RetireEquipment();
        Assert.Same(facade, owner.Equipment);
        Assert.Throws<ObjectDisposedException>(() => facade.Read());
        Assert.Throws<ObjectDisposedException>(() => facade.AssignQuickSelect(0, null));
        Assert.Equal(before, State(fixture.Native!));
        Assert.Equal(0, fixture.PresentationResets);
        Assert.Empty(fixture.Commands);
    }

    private static string State(PlayerSessionNativeState native) =>
        JsonSerializer.Serialize((PlayerSessionNativeState.State)native.Capture().Components.Single());

    private sealed class Fixture : IDisposable
    {
        internal readonly Dictionary<ObpSourceGame, string> Sources = new()
        {
            [ObpSourceGame.Rac1] = Rac1Authority.Primary.BuildId,
            [ObpSourceGame.Rac2] = Rac2Authority.Primary.BuildId,
            [ObpSourceGame.Rac3] = Rac3Authority.Primary.BuildId,
        };
        internal PlayerSessionNativeState? Native;
        private readonly Rac1WeaponInventory _parityInventory = Rac1WeaponInventory.CreateOpeningLoadout();
        private readonly Rac1HeldItemSession _parityHeld = new();
        internal FusionWeaponSession? ParityWeapons;
        internal Rac1WeaponInventory Inventory => Native?.Inventory ?? _parityInventory;
        internal Rac1HeldItemSession Held => Native?.Held ?? _parityHeld;
        private FusionWeaponSession? Weapons => Native?.Weapons ?? ParityWeapons;
        private SourceWeaponKey CurrentWeapon => Weapons?.Selected ?? new(ObpSourceGame.Rac1, Inventory.CurrentItemId);
        internal bool FusionTest, AcceptSelection = true;
        internal PlayerAvatarIdentity? SelectedAvatar = PlayerEquipmentRegistrations.Catalog.Find("rac1/appearance/ratchet")!.Appearance;
        internal PlayerEquipmentPresentationState Presentation = new(EquipmentResourceStatus.Available, EquipmentResourceStatus.Unavailable);
        internal PlayerEquipmentContext Context = new(true, true);
        internal Rac1MovementCapabilities Movement;
        internal Rac1TraversalEquipmentId? Backpack;
        internal int PresentationResets;
        internal readonly List<PlaytestCommand> Commands = [];
        internal PlayerEquipmentPorts Ports { get; }
        internal PlayerEquipmentFacade Equipment { get; }

        internal Fixture(bool ordinary = true)
        {
            if (ordinary)
            {
                Native = new(_parityInventory, 42, 1440);
                Native.RefreshDevelopmentSources(Sources);
            }
            var presentedAvatar = SelectedAvatar;
            Ports = new(() => Native, () => Inventory, () => Held, () => Weapons, () => CurrentWeapon, () => FusionTest,
                () => Sources, () => SelectedAvatar, () => presentedAvatar, _ => Presentation, () => Context,
                key =>
                {
                    if (!AcceptSelection) return;
                    if (Weapons is { } weapons) weapons.Select(key);
                    else if (key.SourceGame == ObpSourceGame.Rac1) Inventory.TryEquip((Rac1WeaponId)key.NativeItemId);
                },
                () => PresentationResets++, (movement, backpack) => { Movement = movement; Backpack = backpack; }, Commands.Add);
            Equipment = new(Ports);
        }

        internal EquipmentCatalogEntry Entry(string id) => Equipment.Read().Single(entry => entry.Definition.Id == id);
        public void Dispose() => Equipment.Dispose();
    }
}
