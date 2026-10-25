using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public class Rac2PlayerAttackDamageTests
{
    [Fact]
    public void State20MatchesTheRetailOozlaDamageTuple()
    {
        var damage = Rac2PlayerAttackDamage.State20;

        Assert.Equal(20, Rac2PlayerAttackDamage.State20Id);
        Assert.Equal(0x00010000u, damage.DamageFlags);
        Assert.Equal((byte)0, damage.DamageClass);
        Assert.Equal((byte)1, damage.DamageStrength);
        Assert.Equal((ushort)71, damage.DamageIndex);
        Assert.Equal(2.0f, damage.DamageHp);
        Assert.Equal(1u, damage.Flags);
    }

    [Fact]
    public void State20PlayerDamageSatisfiesTheRetailClass500BreakPredicate()
    {
        Assert.True(Rac2CrateInteraction.ShouldBreakClass500(Rac2PlayerAttackDamage.State20));
    }
}
