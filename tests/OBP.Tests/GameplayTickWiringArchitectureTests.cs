namespace OBP.Tests;

/// <summary>Guards the Godot boundary without requiring a live game scene in portable CI.</summary>
public sealed class GameplayTickWiringArchitectureTests
{
    private static string Script(string name) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", name));

    [Fact]
    public void RootPhysicsProcessAndRenderProcessDispatchToOneWorldLeaseSchedule()
    {
        string root = Script("OBPGame.cs");
        string physics = Between(root, "public override void _PhysicsProcess", "public override void _Process");
        string frame = Between(root, "public override void _Process", "public override void _UnhandledInput");
        Assert.Contains("GameplayTicks?.Run(GameplayTickPhase.Physics, delta)", physics);
        Assert.Contains("GameplayTicks?.Run(GameplayTickPhase.Prepare, delta)", frame);
        Assert.Contains("GameplayTicks?.RunWorldFrame(delta)", frame);
        Assert.DoesNotContain("Rac1Gameplay?.Tick(", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac2Gameplay?.Tick(", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1Gameplay?.PhysicsStep(", physics, StringComparison.Ordinal);
        Assert.Contains("TickSharedProfile(delta)", frame);
        Assert.Contains("TickRac1CampaignSaves(delta)", frame);
        Assert.Contains("_sourceVerification?.Tick(Time.GetTicksMsec())", frame);
    }

    [Fact]
    public void WorldActivationRegistersOnceAndRetirementStopsCallbacksBeforeTeardown()
    {
        string entry = Between(Script("OBPGame.WorldEntry.cs"), "private void ActivateOwnedWorld",
            "private void ReportWorldPreparationFailure");
        string root = Script("OBPGame.cs");
        string retire = Between(root, "private void RetireWorldBindings", "private void SetupOverlay");
        Assert.Contains("WorldGameplayTickBindings.Activate(entry", entry);
        Assert.Contains("entry.Resources.BindPlayer(_player)", entry);
        Assert.True(entry.IndexOf("BindPlayer(_player)", StringComparison.Ordinal)
            < entry.IndexOf("WorldGameplayTickBindings.Activate(", StringComparison.Ordinal));
        Assert.Contains("RetireGameplayTicks()", retire);
        Assert.True(retire.IndexOf("RetireGameplayTicks()", StringComparison.Ordinal)
            < retire.IndexOf("ActorHost.Reset()", StringComparison.Ordinal));
        Assert.Contains("GameplayTicks?.Dispose()", Script("GodotWorldResources.cs"));
        Assert.Contains("_lifetime.Cancel()", Script("WorldSceneLifetime.cs"));
    }

    [Fact]
    public void OnlySourceModuleRegistersItsNativeWorldTickAndWorldInput()
    {
        string one = Script("Rac1WorldModule.cs");
        string two = Script("Rac2WorldModule.cs");
        string binder = Script("WorldGameplayTickBindings.cs");
        string rac1Registrations = Between(one, "internal void RegisterTicks(", "internal void PhysicsStep(");
        int effects = rac1Registrations.IndexOf("\"rac1-dead-bomb-effects\"", StringComparison.Ordinal);
        int sourceGate = rac1Registrations.IndexOf("if (_world.Game != ObpSourceGame.Rac1) return", StringComparison.Ordinal);
        int native = rac1Registrations.IndexOf("\"rac1-native\"", StringComparison.Ordinal);
        Assert.True(effects >= 0 && sourceGate > effects && native > sourceGate,
            "Shared post-death Bomb Glove effects must tick in all worlds, but R&C1 native clocks must not.");
        Assert.Contains("if (playerDead()) Effects.StepBombEffectsPhysics()", rac1Registrations);
        Assert.Contains("ticks.RegisterInput(\"rac1-taxi-input\"", one);
        Assert.Contains("GameplayTickPhase.NativeWorld, \"rac1-native\"", one);
        Assert.Contains("if (_world.Game == ObpSourceGame.Rac2)", two);
        Assert.Contains("GameplayTickPhase.NativeWorld, \"rac2-native\"", two);
        Assert.Contains("resources.Rac1Gameplay?.RegisterTicks", binder);
        Assert.Contains("resources.Rac2Gameplay?.RegisterTicks", binder);
        Assert.DoesNotContain("Rac3Gameplay?.RegisterTicks", binder, StringComparison.Ordinal);
        Assert.Contains("GameplayTicks?.TryHandleInput(@event)", Script("OBPGame.cs"));
    }

    private static string Between(string text, string begin, string end)
    {
        int start = text.IndexOf(begin, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing {begin}");
        int stop = text.IndexOf(end, start + begin.Length, StringComparison.Ordinal);
        Assert.True(stop > start, $"Missing {end} following {begin}");
        return text[start..stop];
    }
}
