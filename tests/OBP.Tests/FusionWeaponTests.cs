using System.Numerics;
using System.Text.Json;
using OneBigPackage;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class FusionWeaponTests
{
    private static readonly WeaponUseInput Held = new(true, true, Vector3.Zero, Vector3.UnitX);

    [Fact]
    public void DefaultQuickSelectKeepsOwnedNativeOrderAndAddsBothForeignWeaponsWithoutGrantingNativeItems()
    {
        var inventory = Rac1WeaponInventory.CreateOpeningLoadout();
        inventory.AcquireNativeItem((int)Rac1WeaponId.Blaster);
        inventory.TryAssignQuickSelect(0, (int)Rac1WeaponId.Blaster);
        string before = JsonSerializer.Serialize(inventory.CapturePersistentState());
        var keys = FusionWeaponSession.DefaultQuickSelect(inventory);
        Assert.Equal(new SourceWeaponKey("rac1", 15), keys[0]);
        Assert.Contains(FusionWeaponSession.BombGlove, keys);
        Assert.Equal(new[] { FusionWeaponSession.Lancer, FusionWeaponSession.ShockCannon }, keys.TakeLast(2));
        var fusion = new FusionWeaponSession(0, inventory);
        fusion.Select(FusionWeaponSession.Lancer);
        for (int i = 0; i < 20; i++) Step(fusion, Held);
        Assert.Equal(before, JsonSerializer.Serialize(inventory.CapturePersistentState()));
        Assert.True(fusion.Select(new("rac1", 15)));
        Assert.Equal(Rac1WeaponId.Blaster, inventory.Equipped);
    }

    [Fact]
    public void SameNativeNumberInThreeGamesCannotAliasOrAcquireCampaignGear()
    {
        var keys = new HashSet<SourceWeaponKey> { new("rac1", 30), new("rac2", 30), new("rac3", 30) };
        Assert.Equal(3, keys.Count);
        foreach (var key in keys) Assert.Equal(key, JsonSerializer.Deserialize<SourceWeaponKey>(JsonSerializer.Serialize(key)));
        Assert.Throws<ArgumentException>(() => default(SourceWeaponKey).Validate());
        Assert.Throws<ArgumentException>(() => new SourceWeaponKey("gc", 30).Validate());
        var campaign = Rac1WeaponInventory.CreateOpeningLoadout();
        string before = JsonSerializer.Serialize(campaign.CapturePersistentState());
        var fusion = new FusionWeaponSession(42);
        Assert.True(fusion.Select(FusionWeaponSession.Lancer));
        for (int i = 0; i < 90; i++) Step(fusion, Held);
        Assert.InRange(fusion.LancerSession.Ammo, 1, 39);
        Assert.Equal(20, fusion.CannonSession.Ammo);
        Assert.True(fusion.Rac1Inventory.TryConsumeAmmo(10, 1));
        Assert.Equal(before, JsonSerializer.Serialize(campaign.CapturePersistentState()));
        Assert.Equal(new[] { 10 }, Enumerable.Range(0, 37).Where(id => fusion.Rac1Inventory.Items[id] != 0));
        Assert.False(fusion.Select(new("rac1", 30)));
        Assert.False(fusion.Select(new("rac3", 30)));
    }

    [Fact]
    public void LancerContactKeepsOriginalHalfDamageAndConsumesOnlyTheFirstBlockingContact()
    {
        var fusion = new FusionWeaponSession(0); fusion.Select(FusionWeaponSession.Lancer);
        WeaponFrame frame;
        do { frame = fusion.Advance(Held); if (frame.Queries.Count == 0) fusion.ResolveContacts(frame, []); } while (frame.Queries.Count == 0);
        var key = new WeaponTargetKey("rac1", 1440, 189);
        var intents = fusion.ResolveContacts(frame, [new(0, key, .7f, .7f), new(0, key, .2f, .2f)]);
        var intent = Assert.Single(intents);
        Assert.Equal(.5f, intent.SourceDamage);
        Assert.Equal(FusionWeaponSession.Lancer, intent.Shot.Weapon);
        Assert.Empty(fusion.LancerSession.ActiveShots);
        Assert.Throws<InvalidOperationException>(() => fusion.ResolveContacts(frame, []));

        fusion.ClearTransientState();
        do { frame = fusion.Advance(Held); if (frame.Queries.Count == 0) fusion.ResolveContacts(frame, []); } while (frame.Queries.Count == 0);
        Assert.Empty(fusion.ResolveContacts(frame, [new(0, null, .1f, .1f), new(0, key, .2f, .2f)]));
    }

    [Fact]
    public void ShockQueriesCoalesceAndPreserveOriginalSpreadAndChargedDamage()
    {
        var fusion = new FusionWeaponSession(123); fusion.Select(FusionWeaponSession.ShockCannon);
        var target = new WeaponTargetKey("rac1", 1440, 189);
        var intents = new List<WeaponDamageIntent>();
        for (int i = 0; i < 85; i++)
        {
            var frame = fusion.Advance(Held);
            var contacts = frame.Queries.Select(q => new WeaponContact(q.Index, target, .25f, 2)).ToArray();
            var damage = fusion.ResolveContacts(frame, contacts);
            Assert.True(damage.Count <= 1);
            intents.AddRange(damage);
        }
        Assert.Contains(intents, d => d.SourceDamage == 5 && d.Kind == "spread");
        Assert.Contains(intents, d => d.SourceDamage == 50 && d.Kind == "beam");
        Assert.InRange(fusion.CannonSession.Ammo, 0, 9);
        fusion.Select(FusionWeaponSession.BombGlove);
        Assert.Null(fusion.CannonFrame);
        Assert.Empty(Step(fusion, Held).Queries);
    }

    [Fact]
    public void ResetRejectsStaleFramesAndDiscardsFlightsWithoutRefillingAmmo()
    {
        var fusion = new FusionWeaponSession(0); fusion.Select(FusionWeaponSession.Lancer);
        for (int i = 0; i < 9; i++) Step(fusion, Held);
        Assert.NotEmpty(fusion.LancerSession.ActiveShots);
        int ammo = fusion.LancerSession.Ammo;
        var old = fusion.Advance(Held);
        fusion.ClearTransientState();
        Assert.Empty(fusion.LancerSession.ActiveShots);
        Assert.Equal(ammo, fusion.LancerSession.Ammo);
        Assert.Throws<InvalidOperationException>(() => fusion.ResolveContacts(old, []));
        Assert.Empty(Step(fusion, Held).Queries);
    }

    [Fact]
    public void ForeignDamageUsesExplicitTargetAdmissionWithoutAliasingNativeClasses()
    {
        var target = new WeaponTargetKey("rac1", 1440, 189);
        var intent = new WeaponDamageIntent(new(FusionWeaponSession.Lancer, 1, 1), target, .5f, 0x10000, "projectile");
        var damage = Assert.IsType<Rac1GameplayDamageEvent>(FusionDamagePolicy.Translate(intent));
        Assert.Equal(.5, damage.NativeDamage);
        Assert.Same(intent, damage.HostWeaponIntent);
        Assert.True(Rac1EnemyDamage.IsPlayerWeapon(damage));
        Assert.False(Rac1EnemyDamage.IsBlaster(damage));
        Assert.Equal(.5f, Rac1EnemyDamage.ResolveNativeDamage(damage, []));
        Assert.Equal(Rac1GameplayEntityRef.Player, damage.Source);
        Assert.Null(FusionDamagePolicy.Translate(intent with { Target = target with { SourceGame = "rac2" } }));
        Assert.Null(FusionDamagePolicy.Translate(intent with { Target = target with { NativeClassId = 502 } }));
        Assert.Null(FusionDamagePolicy.Translate(intent with { Target = target with { NativeClassId = 749 } }));
        Assert.False(Rac1EnemyDamage.IsPlayerWeapon(new(damage.Source, damage.Target, .5, 0x10000)));

        // The actual R&C1 robot consumer retains fractional health and cooldown.
        var source = Rac1VeldinEnemyTests.Robot(189);
        var runtime = new Rac1MobyRuntimeSession();
        var robot = new Rac1Class1440Session(runtime, []);
        robot.Register(source, RuntimeEntityState.FromAuthored(source));
        Assert.True(robot.CanApplyDamage(source, damage));
        var result = robot.ApplyDamage(source, damage);
        Assert.Equal(1.5f, result.Probe.Health);
        Assert.False(result.Lethal);
        Assert.False(robot.CanApplyDamage(source, damage));
    }

    [Fact]
    public void ReplayConfigurationRejectsChangedSourcesAndPolicyEvenWithTheSameDestination()
    {
        var config = new PlaytestWeaponConfiguration(FusionWeaponSession.LoadoutId, FusionDamagePolicy.Id,
            new Dictionary<string, string> { ["rac1"] = "a", ["rac2"] = "b", ["rac3"] = "c" });
        config.Validate();
        Assert.True(config.Matches(JsonSerializer.Deserialize<PlaytestWeaponConfiguration>(JsonSerializer.Serialize(config))!));
        Assert.False(config.Matches(config with { Sources = new Dictionary<string, string>(config.Sources) { ["rac2"] = "different" } }));
        Assert.False(config.Matches(config with { DamagePolicy = "other" }));
        Assert.Throws<InvalidDataException>(() => (config with { Sources = new Dictionary<string, string> { ["rac1"] = "a" } }).Validate());
    }

    private static WeaponFrame Step(FusionWeaponSession fusion, WeaponUseInput input)
    {
        var frame = fusion.Advance(input); fusion.ResolveContacts(frame, []); return frame;
    }
}
