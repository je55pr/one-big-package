namespace OBP.Tests;

/// <summary>
/// Source-free structural parity checks for the #366 world-lease migration.
/// Native damage/fuse/reward contracts remain covered by the dedicated session tests.
/// </summary>
public sealed class Rac1BreakableOwnershipTests
{
    private static readonly string Scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
    private static readonly string Diagnostics = Path.Combine(RepoPaths.Root, "game", "diagnostics");
    private static string Script(string name) => File.ReadAllText(Path.Combine(Scripts, name));

    [Fact]
    public void RootPartialsRetiredAndWorldLeaseOwnsBothBreakableAdapters()
    {
        Assert.False(File.Exists(Path.Combine(Scripts, "OBPGame.Rac1Explosives.cs")));
        Assert.False(File.Exists(Path.Combine(Scripts, "OBPGame.Rac1NovalisBreakables.cs")));
        string host = Script("Rac1BreakableHost.cs");
        string module = Script("Rac1WorldModule.cs");
        Assert.DoesNotContain("partial class OBPGame", host, StringComparison.Ordinal);
        Assert.Contains("Rac1ExplosiveCrateAdapter _explosives", host, StringComparison.Ordinal);
        Assert.Contains("Rac1NovalisBreakableAdapter _novalis", host, StringComparison.Ordinal);
        Assert.Contains("Breakables = new(world, Context, Presentation, Loot, Enemies, () => breakables.Current(this), breakables);", module, StringComparison.Ordinal);
        Assert.Contains("Breakables.ConfigureExplosives(world, result);", module, StringComparison.Ordinal);
        Assert.Contains("Breakables.ConfigureNovalis(world, result);", module, StringComparison.Ordinal);
        Assert.Contains("Breakables.TickExplosives(delta);", module, StringComparison.Ordinal);
        Assert.Contains("Breakables.Dispose();", module, StringComparison.Ordinal);
        Assert.DoesNotContain("_reset.Explosives(", module, StringComparison.Ordinal);
        Assert.DoesNotContain("_actors.Breakables(", module, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceNativeAndContactAdaptersRemainSpecificAndRootDoesNotOwnState()
    {
        string explosive = Script("Rac1ExplosiveCrateAdapter.cs");
        string novalis = Script("Rac1NovalisBreakableAdapter.cs");
        string root = Script("OBPGame.cs");
        Assert.Contains("source.SourceGame != ObpSourceGame.Rac1", explosive, StringComparison.Ordinal);
        Assert.Contains("new(context.MobyRuntime, ports.ChooseFrames)", explosive, StringComparison.Ordinal);
        Assert.Contains("context.MobyRuntime.CanDispatchDamage(damage)", explosive, StringComparison.Ordinal);
        Assert.Contains("Rac1ExplosiveCrateBlast.ToMoby", explosive, StringComparison.Ordinal);
        Assert.Contains("Rac1ExplosiveCrateBlast.ToPlayer", explosive, StringComparison.Ordinal);
        Assert.Contains("world.LevelId != Rac1NovalisBreakableSession.NovalisLevelId", novalis, StringComparison.Ordinal);
        Assert.Contains("source.SourceGame != ObpSourceGame.Rac1", novalis, StringComparison.Ordinal);
        Assert.Contains("Rac1NovalisBreakableSession.PlantBoltRewardAdmitted", novalis, StringComparison.Ordinal);
        Assert.Contains("(node, damage) => owner.Breakables.TryTriggerExplosiveDamage(node, damage)", root, StringComparison.Ordinal);
        Assert.Contains("owner => ReferenceEquals(_worldScenes?.Current?.Resources.Rac1Gameplay, owner)", root, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1ExplosiveNodes", root, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1NovalisBreakableNodes", root, StringComparison.Ordinal);
        Assert.DoesNotContain("TryTriggerRac1ExplosiveDamage", root, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetRac1ExplosiveCrates", root, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetRac1NovalisBreakables", root, StringComparison.Ordinal);
    }

    [Fact]
    public void RetirementRestoresBorrowedNodesAndReleasesContactBodies()
    {
        string explosive = Script("Rac1ExplosiveCrateAdapter.cs");
        string novalis = Script("Rac1NovalisBreakableAdapter.cs");
        string loot = Script("Rac1LootPresentation.Pickups.cs");
        string module = Script("Rac1WorldModule.cs");
        Assert.Contains("_originalStates.Add(source.InstanceIndex, (node.State, node.Root.Visible))", explosive, StringComparison.Ordinal);
        Assert.Contains("_originalStates.Add(source.InstanceIndex, (node.State, node.Root.Visible))", novalis, StringComparison.Ordinal);
        Assert.Contains("node.ApplyState(original.State);", explosive, StringComparison.Ordinal);
        Assert.Contains("node.ApplyState(original.State);", novalis, StringComparison.Ordinal);
        Assert.Contains("node.Root.Visible = original.Visible;", explosive, StringComparison.Ordinal);
        Assert.Contains("node.Root.Visible = original.Visible;", novalis, StringComparison.Ordinal);
        Assert.Contains("loot.DisableRac1CrateCollider(id);", explosive, StringComparison.Ordinal);
        Assert.Contains("flash.Dispose();", explosive, StringComparison.Ordinal);
        Assert.Contains("foreach (int instance in _rac1CrateBodies.Keys.ToArray()) DisableRac1CrateCollider(instance);", loot, StringComparison.Ordinal);
        Assert.Contains("if (_configured) throw new InvalidOperationException", module, StringComparison.Ordinal);
        Assert.Contains("ResetBindings(retiring: true);", module, StringComparison.Ordinal);
    }

    [Fact]
    public void RewardPresentationAndCrateFixtureUseBoundedPorts()
    {
        string explosive = Script("Rac1ExplosiveCrateAdapter.cs");
        string novalis = Script("Rac1NovalisBreakableAdapter.cs");
        string loot = Script("Rac1LootPresentation.cs");
        string scenario = File.ReadAllText(Path.Combine(Diagnostics, "Rac1CrateScenario.cs"));
        string fixtures = File.ReadAllText(Path.Combine(Diagnostics, "GodotRac1LootDiagnosticFixture.cs"));
        Assert.DoesNotContain("loot.CrateBodies", explosive, StringComparison.Ordinal);
        Assert.DoesNotContain("loot.CrateBodies", novalis, StringComparison.Ordinal);
        Assert.Contains("PresentRac1ExplosiveDetonation", loot, StringComparison.Ordinal);
        Assert.Contains("TryGetRac1CrateContact", loot, StringComparison.Ordinal);
        Assert.Contains("TouchExplosiveForDiagnostics", Script("Rac1BreakableHost.cs"), StringComparison.Ordinal);
        Assert.Contains("GodotRac1BreakableDiagnosticFixture", fixtures, StringComparison.Ordinal);
        Assert.Contains("ports.Breakables", scenario, StringComparison.Ordinal);
        Assert.Contains("Rac1DiagnosticActorScope.RequireCurrent(actor, worlds.ReadWorld())", fixtures, StringComparison.Ordinal);
    }
}
