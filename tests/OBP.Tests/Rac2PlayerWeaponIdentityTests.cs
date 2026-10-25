using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public class Rac2PlayerWeaponIdentityTests
{
    [Fact]
    public void State21MatchesTheRetailWeaponObjectIdentity()
    {
        Assert.Equal(21, Rac2PlayerWeaponIdentity.State21Id);
        Assert.Equal(10, Rac2PlayerWeaponIdentity.State21EffectiveWeaponIndex);
        Assert.Equal(71, Rac2PlayerWeaponIdentity.State21MobyClass);
    }
}
