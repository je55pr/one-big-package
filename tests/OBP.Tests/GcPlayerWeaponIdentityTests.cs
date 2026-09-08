using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public class GcPlayerWeaponIdentityTests
{
    [Fact]
    public void State21MatchesTheRetailWeaponObjectIdentity()
    {
        Assert.Equal(21, GcPlayerWeaponIdentity.State21Id);
        Assert.Equal(10, GcPlayerWeaponIdentity.State21EffectiveWeaponIndex);
        Assert.Equal(71, GcPlayerWeaponIdentity.State21MobyClass);
    }
}
