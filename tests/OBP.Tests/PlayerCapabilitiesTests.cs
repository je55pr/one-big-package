using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC2;
using OBP.RAC2.Gameplay;
using OBP.RAC3;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OneBigPackage;

namespace OBP.Tests;

public sealed class PlayerCapabilitiesTests
{
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>
    {
        ["rac1"] = Rac1Authority.Primary.BuildId,
        ["rac2"] = Rac2Authority.Primary.BuildId,
        ["rac3"] = Rac3Authority.Primary.BuildId,
    };
    private static PlayerSessionNativeState Native() => new(Rac1WeaponInventory.CreateOpeningLoadout(), 42, 1440);
    private static PlayerEquipmentCatalog Catalog => PlayerEquipmentRegistrations.Catalog;
    private static EquipmentLiveState Ready(EquipmentDefinition entry) => new(true, false, false, EquipmentResourceStatus.Available, EquipmentResourceStatus.Unavailable);

    [Fact]
    public void OwningAGadgetDoesNotCreateAnUnimplementedWeaponController()
    {
        var native = Native(); native.Inventory.AcquireNativeItem((int)Rac1TraversalEquipmentId.Swingshot);
        Assert.False(native.Weapons.Select(new("rac1", (int)Rac1TraversalEquipmentId.Swingshot)));
        Assert.Equal(FusionWeaponSession.BombGlove, native.Weapons.Selected);
    }

    [Fact]
    public void PlayerRestoreKeepsCurrentWorldAttackReceiptsAndLifeGeneration()
    {
        var native = Native(); native.Life.BeginWorld("world", 1); var actor = Actor("rac2"); Register(native, actor);
        var before = native.Capture(); native.Life.Apply(Attack(actor)); native.Life.Respawn();
        native.Restore(before);
        Assert.Equal(0, native.Life.LifeGeneration); Assert.Equal(4, native.Life.Capture().Current);
        Assert.Equal(PlayerIntakeStatus.Duplicate, native.Life.Apply(Attack(actor)).Status);
        Assert.True(native.Life.Apply(Attack(actor, 2)).Applied);
    }

    [Fact]
    public void CatalogEnumeratesRealControllersAndDoesNotAdvertiseUnrecoveredSkinsOrBoots()
    {
        var entries = Catalog.Read(Sources, Ready);
        Assert.Equal(18, entries.Count(entry => entry.Definition.Category == EquipmentCategory.Weapon && entry.Usable));
        Assert.Equal(5, entries.Count(entry => entry.Definition.Category == EquipmentCategory.Gadget && entry.Usable));
        Assert.Single(entries, entry => entry.Definition.Category == EquipmentCategory.Appearance && entry.Usable);
        foreach (string id in new[] { "rac2/appearance/ratchet", "rac3/appearance/ratchet", "rac1/item/27", "rac1/item/28", "rac1/item/29" })
        {
            var entry = entries.Single(entry => entry.Definition.Id == id);
            Assert.Equal(EquipmentUsability.Unimplemented, entry.Usability); Assert.NotEmpty(entry.Reason!); Assert.False(entry.Selectable);
        }
    }

    [Fact]
    public void EqualNativeItemNumbersAcrossGamesNeverAlias()
    {
        var entries = new[] { "rac1", "rac2", "rac3" }.Select(game => new EquipmentDefinition(game + "/item/10", game, game, Sources[game],
            EquipmentCategory.Weapon, EquipmentImplementation.Implemented, new(game, 10))).ToArray();
        var catalog = new PlayerEquipmentCatalog(entries);
        Assert.Equal(3, catalog.Read(Sources, Ready).Count);
        Assert.Equal("rac2/item/10", catalog.Find(new SourceWeaponKey("rac2", 10))!.Id);
        Assert.Throws<ArgumentException>(() => new PlayerEquipmentCatalog(entries.Append(entries[0] with { Id = "alias" })));
        Assert.Throws<ArgumentException>(() => new PlayerEquipmentCatalog([entries[0] with { Item = new("rac2", 10) }]));
    }

    [Theory]
    [InlineData(EquipmentUsability.MissingSource)]
    [InlineData(EquipmentUsability.UnsupportedBuild)]
    [InlineData(EquipmentUsability.MissingModel)]
    [InlineData(EquipmentUsability.Unowned)]
    [InlineData(EquipmentUsability.ContextBlocked)]
    public void CatalogKeepsSourceOwnershipResourcesAndContextSeparate(EquipmentUsability expected)
    {
        var sources = Sources.ToDictionary();
        if (expected == EquipmentUsability.MissingSource) sources.Remove("rac2");
        if (expected == EquipmentUsability.UnsupportedBuild) sources["rac2"] = "unsupported";
        var entry = Catalog.Read(sources, definition => Ready(definition) with
        {
            Owned = expected != EquipmentUsability.Unowned,
            Model = expected == EquipmentUsability.MissingModel ? EquipmentResourceStatus.Unavailable : EquipmentResourceStatus.Available,
            ContextAllowed = expected != EquipmentUsability.ContextBlocked,
            ContextReason = expected == EquipmentUsability.ContextBlocked ? "Source target context absent." : null,
        }).Single(entry => entry.Definition.Item == FusionWeaponSession.Lancer);
        Assert.Equal(expected, entry.Usability); Assert.NotEmpty(entry.Reason!); Assert.False(entry.Usable);
        Assert.Equal(expected == EquipmentUsability.ContextBlocked, entry.Selectable);
    }

    [Fact]
    public void DevelopmentInventoryIsIndependentAcquiredOnceAndKeepsNativeAmmoOnReentry()
    {
        var campaign = Rac1WeaponInventory.CreateOpeningLoadout();
        var native = new PlayerSessionNativeState(campaign, 42, 1440);
        native.RefreshDevelopmentSources(Sources);
        Assert.False(campaign.Owns(Rac1WeaponId.Blaster)); Assert.True(native.Inventory.Owns(Rac1WeaponId.Blaster));
        int initial = native.Inventory.GetAmmo((int)Rac1WeaponId.Blaster);
        Assert.True(native.Inventory.TryConsumeAmmo((int)Rac1WeaponId.Blaster, 1));
        foreach (string world in new[] { "rac1", "rac2", "rac3", "fusion" }) { native.EndWorld(); native.RefreshDevelopmentSources(Sources); }
        Assert.Equal(initial - 1, native.Inventory.GetAmmo((int)Rac1WeaponId.Blaster));
        Assert.False(native.Inventory.OwnsNativeItem(28)); Assert.False(native.Inventory.OwnsNativeItem(29));
        var parity = new PlayerSessionNativeState(campaign, 42, 1440, PlayerLoadoutPolicy.RetailCampaign);
        parity.RefreshDevelopmentSources(Sources); Assert.False(parity.Inventory.Owns(Rac1WeaponId.Blaster));
        var missing = Native(); missing.RefreshDevelopmentSources(new Dictionary<string, string>()); Assert.False(missing.Inventory.Owns(Rac1WeaponId.Blaster));
    }

    [Fact]
    public void SlotsPacksHandAndSelectionRoundTripWithoutCreatingAnotherWeaponOwner()
    {
        var native = Native(); native.RefreshDevelopmentSources(Sources);
        Assert.True(native.Weapons.Select(FusionWeaponSession.Lancer));
        native.Assignments.Assign(7, FusionWeaponSession.Lancer);
        Assert.Null(native.Assignments.Capture().QuickSelect[1]);
        var wheel = native.Assignments.WithOverflow(Catalog.Definitions.Where(entry => entry.Category == EquipmentCategory.Weapon).Select(entry => entry.Item!.Value));
        Assert.Null(wheel[1]); Assert.Equal(FusionWeaponSession.Lancer, wheel[7]);
        Assert.Equal(1, wheel.Count(item => item == FusionWeaponSession.Lancer));
        native.Assignments.Enable(Catalog.Find(new SourceWeaponKey("rac1", 3))!, true, Catalog);
        Assert.False(native.GadgetEnabled(Rac1TraversalEquipmentId.HeliPack)); Assert.True(native.GadgetEnabled(Rac1TraversalEquipmentId.ThrusterPack));
        native.Held.HoldWrench();
        var before = native.Capture();
        native.Assignments.Assign(7, FusionWeaponSession.ShockCannon); native.Wallet.Credit(5);
        native.Restore(before);
        Assert.Equal(FusionWeaponSession.Lancer, native.Weapons.Selected); Assert.True(native.Held.Hand.WrenchHeld);
        Assert.Equal(before.Loadout.QuickSelect, native.Capture().Loadout.QuickSelect); Assert.Equal(42, native.Wallet.Balance);
        Assert.Same(native.Inventory, native.Weapons.Rac1Inventory);
        var invalid = native.Assignments.Capture() with { EnabledGadgets = [new("rac1", 2), new("rac1", 3)] };
        Assert.Throws<ArgumentException>(() => native.Assignments.Restore(invalid, Catalog));
        Assert.Throws<ArgumentException>(() => native.Assignments.Enable(Catalog.Find(new SourceWeaponKey("rac1", 28))!, true, Catalog));
    }

    [Fact]
    public void ConfigurationIdentityIsOrderedPathFreeAndChangesForSlotsPolicyAndSourceBuild()
    {
        var native = Native(); var slots = native.Assignments.Capture();
        string Identity(PlayerLoadoutPolicy policy, IReadOnlyDictionary<string, string> sources, PlayerEquipmentAssignmentsSnapshot assignments) => Catalog.ConfigurationIdentity(policy, sources, assignments);
        string before = Identity(native.LoadoutPolicy, Sources, slots);
        Assert.Equal(before, new PlayerEquipmentCatalog(Catalog.Definitions.Reverse()).ConfigurationIdentity(native.LoadoutPolicy, Sources.Reverse().ToDictionary(), slots));
        native.Assignments.Assign(6, FusionWeaponSession.Lancer);
        Assert.NotEqual(before, Identity(native.LoadoutPolicy, Sources, native.Assignments.Capture()));
        Assert.NotEqual(before, Identity(PlayerLoadoutPolicy.RetailCampaign, Sources, slots));
        Assert.NotEqual(before, Identity(native.LoadoutPolicy, Sources.Append(new("extra", "build")).ToDictionary(), slots));
        Assert.Equal(64, before.Length);
        Assert.NotEqual(before, Catalog.ConfigurationIdentity(native.LoadoutPolicy, Sources, slots, FusionWeaponSession.Lancer));
    }

    private static ActorInstanceKey Actor(string game, int index = 1, long lease = 1) => new("world", lease, new(game, 749, index));
    private static void Register(PlayerSessionNativeState native, ActorInstanceKey actor, Func<bool>? alive = null)
    {
        string adapter = actor.Target.SourceGame == "rac1" ? PlayerSessionNativeState.Rac1DamageAdapter : PlayerSessionNativeState.GcDamageAdapter;
        string descriptor = actor.Target.SourceGame == "rac1" ? PlayerSessionNativeState.Rac1DamageDescriptor : PlayerSessionNativeState.GcDamageDescriptor;
        native.Life.RegisterActor(new(actor, new(actor.Target.SourceGame, Sources[actor.Target.SourceGame], "moby:749"), adapter, null,
            [new(descriptor, CapabilityAdmission.Ready)]), alive ?? (() => true));
    }
    private static PlayerAttackIntent Attack(ActorInstanceKey actor, long sequence = 1, double amount = 1) => new(actor, Sources[actor.Target.SourceGame],
        actor.Target.SourceGame == "rac1" ? PlayerSessionNativeState.Rac1DamageAdapter : PlayerSessionNativeState.GcDamageAdapter,
        actor.Target.SourceGame == "rac1" ? PlayerSessionNativeState.Rac1DamageDescriptor : PlayerSessionNativeState.GcDamageDescriptor, sequence, amount, 1);

    [Fact]
    public void SameNumberedAttackersUseOneVictimAndNativeProtectionThenTrueDeathAndRespawn()
    {
        var native = Native(); native.Weapons.Select(FusionWeaponSession.Lancer);
        native.Life.BeginWorld("world", 1); var rac1 = Actor("rac1"); var gc = Actor("rac2"); Register(native, rac1); Register(native, gc);
        Assert.True(native.Life.Apply(Attack(rac1)).Applied); Assert.Equal(3, native.Life.Capture().Current);
        Assert.Equal(PlayerIntakeStatus.Duplicate, native.Life.Apply(Attack(rac1)).Status);
        Assert.Equal(PlayerIntakeStatus.Protected, native.Life.Apply(Attack(gc)).Status);
        for (int i = 0; i < Rac1RatchetNanotechSession.EnemyHitProtectionTicks; i++) native.Life.TickNative();
        Assert.True(native.Life.Apply(Attack(gc, 2, 3)).Applied); Assert.False(native.Life.Alive); Assert.Equal(0, native.Life.Capture().Current);
        Assert.Equal(PlayerIntakeStatus.DeadPlayer, native.Life.Apply(Attack(rac1, 2)).Status);
        native.Life.Heal(1); Assert.False(native.Life.Alive);
        native.Life.Respawn(); Assert.True(native.Life.Alive); Assert.Equal(4, native.Life.Capture().Current); Assert.Equal(1, native.Life.LifeGeneration);
        Assert.Equal(42, native.Wallet.Balance); Assert.Equal(FusionWeaponSession.Lancer, native.Weapons.Selected);
        native.Life.BeginWorld("next", 2); Assert.Equal(PlayerIntakeStatus.StaleLease, native.Life.Apply(Attack(gc, 3)).Status);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(.5)]
    [InlineData(2147483648d)]
    public void SharedVictimRejectsInvalidDamageWithoutChangingHealth(double value)
    {
        var native = Native(); native.Life.BeginWorld("world", 1); var actor = Actor("rac2"); Register(native, actor);
        Assert.Equal(PlayerIntakeStatus.Invalid, native.Life.Apply(Attack(actor, amount: value)).Status);
        Assert.Equal(4, native.Life.Capture().Current);
    }

    [Fact]
    public void RetiredActorCannotAttackOrRetainItsOldDamageReceipt()
    {
        var native = Native(); native.Life.BeginWorld("world", 1);
        var old = Actor("rac2"); Register(native, old);
        Assert.True(native.Life.Apply(Attack(old)).Applied);
        native.Life.RetireActor(old);
        Assert.Equal(PlayerIntakeStatus.UnregisteredActor, native.Life.Apply(Attack(old, 2)).Status);
        var replacement = Actor("rac2", index: 2); Register(native, replacement);
        for (int tick = 0; tick < Rac1RatchetNanotechSession.EnemyHitProtectionTicks; tick++) native.Life.TickNative();
        Assert.True(native.Life.Apply(Attack(replacement)).Applied);
        Assert.Equal(2, native.Life.Capture().Current);
    }

    [Fact]
    public void DeadUnknownWrongBuildAndUnregisteredConversionsCannotDamageThePlayer()
    {
        var native = Native(); native.Life.BeginWorld("world", 1); var actor = Actor("rac2"); bool alive = true; Register(native, actor, () => alive);
        Assert.Equal(PlayerIntakeStatus.UnregisteredActor, native.Life.Apply(Attack(Actor("rac2", 2))).Status);
        Assert.Equal(PlayerIntakeStatus.UnregisteredActor, native.Life.Apply(Attack(actor) with { SourceBuildId = "wrong" }).Status);
        Assert.Equal(PlayerIntakeStatus.Unsupported, native.Life.Apply(Attack(actor) with { DescriptorId = "invented" }).Status);
        alive = false; Assert.Equal(PlayerIntakeStatus.DeadActor, native.Life.Apply(Attack(actor)).Status);
        Assert.Equal(4, native.Life.Capture().Current);
    }

    [Fact]
    public void NativeGcDamageRoundingHappensInTheSourceBeforeSharedAdmission()
    {
        Assert.Equal(0, GcRatchetNanotechSession.RoundDamageHpLikeRetail(.5f));
        Assert.Equal(2, GcRatchetNanotechSession.RoundDamageHpLikeRetail(1.5f));
        var native = Native(); native.Life.BeginWorld("world", 1); var actor = Actor("rac2"); Register(native, actor);
        Assert.True(native.Life.Apply(Attack(actor, amount: GcRatchetNanotechSession.RoundDamageHpLikeRetail(1.5f))).Applied);
        Assert.Equal(2, native.Life.Capture().Current);
    }

    [Fact]
    public void SourceAdmittedPickupCreditsOnceEvenIfAnotherSequenceIsSentAndStaleRewardsFail()
    {
        var native = Native(); native.Life.BeginWorld("world", 1); var actor = Actor("rac2"); bool collected = false;
        native.Life.RegisterActor(new(actor, new("rac2", Sources["rac2"], "class500:17:bolt:1"), "rac2/collected-class500-bolt/v1", "uid:17", []), () => !collected);
        var reward = new PlayerRewardIntent(actor, Sources["rac2"], 1, "obp/bolts", 5);
        void Credit(long value) => native.Wallet.Credit(checked((int)value));
        Assert.Equal(PlayerIntakeStatus.LiveRewardActor, native.Life.Credit(reward, Credit).Status);
        collected = true;
        Assert.Throws<InvalidOperationException>(() => native.Life.Credit(reward, _ => throw new InvalidOperationException("failed credit")));
        Assert.True(native.Life.Credit(reward, Credit).Applied); Assert.Equal(47, native.Wallet.Balance);
        Assert.Equal(PlayerIntakeStatus.Duplicate, native.Life.Credit(reward with { Sequence = 2 }, Credit).Status);
        native.Life.BeginWorld("next", 2); Assert.Equal(PlayerIntakeStatus.StaleLease, native.Life.Credit(reward, Credit).Status);
    }

    [Fact]
    public void RegisteredProfileAndClockAdmitOnlyMeasuredAvatarsAndAvailableBindings()
    {
        var configuration = PlayerSessionLifetimeTests.Snapshot().Configuration with
        {
            Avatar = Catalog.Find("rac1/appearance/ratchet")!.Appearance!,
            Movement = PlayerHostProfiles.NativeIdentity,
            Animation = PlayerHostProfiles.NativeIdentity,
            Services = PlayerHostProfiles.Services,
        };
        var profile = PlayerHostProfiles.Registry.Resolve(configuration);
        Assert.Equal(PlayerHostProfiles.Native, profile.Control);
        Assert.True(profile.Control.Actions.HasFlag(PlayerActionCapabilities.Equipment));
        Assert.True(profile.Control.Interfaces.HasFlag(PlayerInterfaceCapabilities.QuickSelect));
        Assert.Throws<InvalidOperationException>(() => PlayerHostProfiles.Registry.Resolve(configuration with { Avatar = Catalog.Find("rac2/appearance/ratchet")!.Appearance! }));
        Assert.Throws<InvalidOperationException>(() => PlayerHostProfiles.Registry.Resolve(configuration with { NativeTicksPerSecond = 30 }));
        Assert.Throws<InvalidOperationException>(() => PlayerHostProfiles.Registry.Resolve(configuration with { Movement = configuration.Movement with { ProfileId = "missing" } }));
        Assert.Throws<InvalidOperationException>(() => PlayerHostProfiles.Registry.Resolve(configuration with { Services = configuration.Services with { HealthPolicyId = "missing" } }));
        var clock = new PlayerNativeClock(60); for (int i = 0; i < 120; i++) clock.Step();
        Assert.Equal(2, clock.Seconds); clock.Reset(); Assert.Equal(0, clock.Tick);
    }

    [Fact]
    public void ForeignAndUnclassifiedCollisionBytesCannotBecomeRac1WaterOrIce()
    {
        var geometry = new RuntimeCollisionBlob(1, [0, 0, 0, 1, 0, 0, 0, 0, 1], [0, 1, 2], [0]);
        foreach (var source in new ObpBuildIdentity?[] { null, Rac2Authority.Primary, Rac3Authority.Primary, Rac1Authority.Primary with { BuildId = "wrong" } })
        {
            var blob = geometry with { MaterialSource = source };
            Assert.Null(RuntimeWorldScene.MaterialIdForFace(blob, [0], 0, Rac1Authority.Primary));
            Assert.Equal(3, RuntimeWorldScene.BuildCollisionFacePlan(blob).Faces.Length); // Still physical geometry.
        }
        Assert.Equal(0, RuntimeWorldScene.MaterialIdForFace(geometry with { MaterialSource = Rac1Authority.Primary }, [0], 0, Rac1Authority.Primary));
    }
}
