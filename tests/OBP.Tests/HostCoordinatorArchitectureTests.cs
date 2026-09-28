namespace OBP.Tests;

public class HostCoordinatorArchitectureTests
{
    [Fact]
    public void Rac1GameplayCoordinatorStaysSplitByHostResponsibility()
    {
        string scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        string corePath = Path.Combine(scripts, "OBPGame.Rac1Gameplay.cs");
        string core = File.ReadAllText(corePath);

        Assert.True(File.ReadLines(corePath).Count() < 400);
        Assert.DoesNotContain("private void TickRac1Swing(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private void TickRac1Projectiles(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private void TickRac1Hostiles(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private void TickRac1Pickups(", core, StringComparison.Ordinal);

        Assert.Contains("private void TickRac1Swing(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac1Combat.cs")), StringComparison.Ordinal);
        Assert.Contains("private void TickRac1Projectiles(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac1Projectiles.cs")), StringComparison.Ordinal);
        Assert.Contains("private void TickRac1Hostiles(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac1Hostiles.cs")), StringComparison.Ordinal);
        Assert.Contains("private void TickRac1Pickups(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac1Pickups.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void GcGameplayCoordinatorKeepsAranosAndDebugHarnessSeparate()
    {
        string scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        string corePath = Path.Combine(scripts, "OBPGame.GcGameplay.cs");
        string core = File.ReadAllText(corePath);

        Assert.False(File.Exists(Path.Combine(scripts, "OBPGame.Crates.cs")));
        Assert.True(File.ReadLines(corePath).Count() < 400);
        Assert.DoesNotContain("private void TickGcOpeningMsr1NativeTick(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private CrateDebugSnapshot? GetCrateDebugSnapshot(", core, StringComparison.Ordinal);

        Assert.Contains("private void TickGcOpeningMsr1NativeTick(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.GcAranosOpeningHost.cs")), StringComparison.Ordinal);
        Assert.Contains("private CrateDebugSnapshot? GetCrateDebugSnapshot(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.GcCrateDebugHarness.cs")), StringComparison.Ordinal);

        Assert.Contains("private void ResetGcGameplayHost(", core, StringComparison.Ordinal);
        Assert.Contains("private void ConfigureGcGameplayHost(", core, StringComparison.Ordinal);
        Assert.Contains("private void ArmGcGameplayHost(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("CrateDebugHarness", core, StringComparison.Ordinal);
    }
}
