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
        Assert.DoesNotContain("private void StepRac1BombProjectilesPhysics(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private void TickRac1Hostiles(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private void TickRac1Pickups(", core, StringComparison.Ordinal);

        Assert.Contains("private void TickRac1Swing(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac1Combat.cs")), StringComparison.Ordinal);
        Assert.Contains("private void StepRac1BombProjectilesPhysics(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac1Projectiles.cs")), StringComparison.Ordinal);
        Assert.Contains("private void TickRac1Hostiles(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac1Enemies.cs")), StringComparison.Ordinal);
        Assert.Contains("private void TickRac1Pickups(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac1Pickups.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Rac2GameplayCoordinatorKeepsAranosAndDebugHarnessSeparate()
    {
        string scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        string corePath = Path.Combine(scripts, "OBPGame.Rac2Gameplay.cs");
        string core = File.ReadAllText(corePath);

        Assert.False(File.Exists(Path.Combine(scripts, "OBPGame.Crates.cs")));
        Assert.True(File.ReadLines(corePath).Count() < 400);
        Assert.DoesNotContain("private void TickRac2OpeningMsr1NativeTick(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private CrateDebugSnapshot? GetCrateDebugSnapshot(", core, StringComparison.Ordinal);

        Assert.Contains("private void TickRac2OpeningMsr1NativeTick(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac2AranosOpeningHost.cs")), StringComparison.Ordinal);
        Assert.Contains("private CrateDebugSnapshot? GetCrateDebugSnapshot(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac2CrateDebugHarness.cs")), StringComparison.Ordinal);

        Assert.Contains("private void ResetRac2GameplayHost(", core, StringComparison.Ordinal);
        Assert.Contains("private void ConfigureRac2GameplayHost(", core, StringComparison.Ordinal);
        Assert.Contains("private void ArmRac2GameplayHost(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("CrateDebugHarness", core, StringComparison.Ordinal);
    }
}
