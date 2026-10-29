using OBP.Core;
using OBP.Game;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1ClassIdsTests
{
    [Fact]
    public void NamedClassesRetainRecoveredRac1IdsAndLegacyAliases()
    {
        Assert.Equal(500, Rac1ClassIds.WoodenCrate);
        Assert.Equal(501, Rac1ClassIds.NanotechCrate);
        Assert.Equal(502, Rac1ClassIds.MetalCrate);
        Assert.Equal(511, Rac1ClassIds.AmmoCrate);
        Assert.Equal(749, Rac1ClassIds.VeldinToad);
        Assert.Equal(1440, Rac1ClassIds.VeldinFlameRobot);

        Assert.Equal(Rac1ClassIds.WoodenCrate, Rac1BoltCrate.NativeClassId);
        Assert.Equal(Rac1ClassIds.NanotechCrate, Rac1CrateSupply.NanotechCrateClass);
        Assert.Equal(Rac1ClassIds.VeldinToad, Rac1Class749Hostile.NativeClassId);
        Assert.Equal(Rac1ClassIds.VeldinFlameRobot, Rac1Class1440Session.ClassId);
        Assert.Equal(Rac1ClassIds.VeldinFlameRobot, Rac1Class1440ContactFamily.NativeClassId);
    }

    [Fact]
    public void FusionAdmissionRemainsNarrowerThanTheNativeCrateFamily()
    {
        foreach (int nativeClass in new[]
        {
            Rac1ClassIds.WoodenCrate, Rac1ClassIds.NanotechCrate, Rac1ClassIds.AmmoCrate,
            Rac1ClassIds.VeldinToad, Rac1ClassIds.VeldinFlameRobot,
        })
            Assert.True(FusionDamagePolicy.Admits(new(ObpSourceGame.Rac1, nativeClass, 0)));

        Assert.False(FusionDamagePolicy.Admits(new(ObpSourceGame.Rac1, Rac1ClassIds.MetalCrate, 0)));
        Assert.False(FusionDamagePolicy.Admits(new(ObpSourceGame.Rac2, Rac1ClassIds.WoodenCrate, 0)));
        Assert.False(FusionDamagePolicy.Admits(new(ObpSourceGame.Rac1, Rac1ClassIds.WoodenCrate, -1)));

        Assert.Equal(new[]
        {
            Rac1ClassIds.WoodenCrate, Rac1ClassIds.NanotechCrate,
            Rac1ClassIds.MetalCrate, Rac1ClassIds.AmmoCrate,
        }, Rac1BoltCrate.CrateClasses);
        Assert.True(Rac1BoltCrate.IsCrateClass(Rac1ClassIds.MetalCrate));
    }
}
